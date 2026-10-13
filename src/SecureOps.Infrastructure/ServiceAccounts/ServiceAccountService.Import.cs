using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    private const int _maxDecisionsPerRequest = 5000;
    private const int _maxCoverageOrganizations = 20;

    /// <summary>Profiles that can carry coordination-list observations, the only source from which absence is inferred.</summary>
    private static readonly string[] _coverageProfiles =
        [ServiceAccountImportProfiles.CoordinationList, ServiceAccountImportProfiles.LegacyPackage, ServiceAccountImportProfiles.LegacyWorkbook];

    /// <summary>Stored stage parameters (re-parse input); the file bytes stay server-side.</summary>
    private sealed record StoredMapping(StageImportRequest Request, IReadOnlyList<ImportColumnMapping> Applied, IReadOnlyList<string> Warnings);

    /// <summary>Stages a file: validates, hashes, parses, plans and stores the preview. A committed replay returns the existing result.</summary>
    public Task<SaResult<ImportBatchView>> StageImportAsync(ClaimsPrincipal principal, AccessOperationContext context, StageImportRequest request,
        string fileName, string contentType, byte[] bytes, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Import, async caller =>
        {
            if (!caller.Scope.HasOrganizationLevel)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Forbidden, "scope");
            }

            if (Validate(request, fileName, bytes) is { } invalid)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Invalid, invalid);
            }

            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            StageImportRequest normalized = request with
            {
                SourceDateProvenance = request.SourceDateProvenance.Trim(),
                Coverage = CoverageOf(request),
                CoverageOrganizationIds = [.. (request.CoverageOrganizationIds ?? []).Distinct().Order()]
            };
            string replayKey = ReplayKey(normalized, sha);
            if (await repository!.FindCommittedReplayAsync(replayKey, cancellationToken) is { } committed)
            {
                return View(committed, replay: true);
            }

            StagedFile staged;
            try
            {
                staged = ImportParser.Parse(request.Profile, bytes, normalized, sha, Limits);
            }
            catch (ImportFileException rejected)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.ImportFile, rejected.Code);
            }

            ImportContext importContext = await repository.LoadImportContextAsync(caller.Scope, cancellationToken);
            if (!CoverageInScope(normalized, importContext))
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Invalid, "coverageOrganizationIds");
            }

            ImportPlanResult plan = ImportPlanner.Plan(PlanInput(normalized, fileName), staged, importContext, new Dictionary<int, ImportRowDecision>());
            DateTimeOffset now = clock.GetUtcNow();
            ImportBatchRecord batch = new(Guid.NewGuid(), request.Profile, SafeName(fileName), ContentTypeOf(bytes), sha, bytes, request.SourceReportDate,
                normalized.SourceDateProvenance, Clean(request.DeclaredScope, 200), Clean(request.DeclaredDomain, 128),
                JsonSerializer.Serialize(new StoredMapping(normalized, staged.Mapping, [.. staged.Warnings, .. plan.Warnings])), 1, 1, 1,
                staged.Rows.Count == 0 ? "Staged" : "Previewed", replayKey, JsonSerializer.Serialize(Summary(plan, staged)), null, null,
                caller.User.Id, now, null);
            await repository.StageAsync(batch, Records(plan, new Dictionary<int, ImportRowDecision>(), caller, now), caller.Actor, cancellationToken);
            return View(batch with { Content = null }, replay: false);
        }, cancellationToken);

    /// <summary>Reads a batch with its summary.</summary>
    public Task<SaResult<ImportBatchView>> ImportAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Import, async caller =>
            caller.Scope.HasOrganizationLevel && await repository!.GetBatchAsync(id, false, cancellationToken) is { } batch
                ? View(batch, replay: false) : SaResult<ImportBatchView>.Fail(SaErrors.NotFound), cancellationToken);

    /// <summary>Lists recent import batches.</summary>
    public Task<SaResult<IReadOnlyList<ImportHistoryItem>>> ImportsAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Import, async caller =>
        {
            if (!caller.Scope.HasOrganizationLevel)
            {
                return SaResult<IReadOnlyList<ImportHistoryItem>>.Fail(SaErrors.Forbidden, "scope");
            }

            IReadOnlyList<ImportBatchRecord> batches = await repository!.ListBatchesAsync(100, cancellationToken);
            return new SaResult<IReadOnlyList<ImportHistoryItem>>([.. batches.Select(b => new ImportHistoryItem(b.Id, b.Profile, b.FileName, b.SourceReportDate,
                b.Status, b.UploadedAt, b.CommittedAt, b.SummaryJson is null ? 0 : JsonSerializer.Deserialize<ImportSummary>(b.SummaryJson)!.TotalRows))]);
        }, cancellationToken);

    /// <summary>Pages preview rows.</summary>
    public Task<SaResult<ImportRowPage>> ImportRowsAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, string? classification,
        string? kind, bool decisionsOnly, int page, int pageSize, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Import, async caller =>
        {
            if (!caller.Scope.HasOrganizationLevel || page < 1 || pageSize is < 1 or > 200)
            {
                return SaResult<ImportRowPage>.Fail(caller.Scope.HasOrganizationLevel ? SaErrors.Invalid : SaErrors.Forbidden, "page");
            }

            (IReadOnlyList<ImportRowRecord> rows, int total) = await repository!.GetRowsAsync(id, classification, kind, decisionsOnly, page, pageSize, cancellationToken);
            return new ImportRowPage([.. rows.Select(RowView)], total, page, pageSize);
        }, cancellationToken);

    /// <summary>Records versioned row decisions (single or bulk) and re-plans the preview.</summary>
    public Task<SaResult<ImportBatchView>> DecideImportAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, ImportDecisionsRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Import, async caller =>
        {
            if (!caller.Scope.HasOrganizationLevel)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Forbidden, "scope");
            }

            if (await repository!.GetBatchAsync(id, true, cancellationToken) is not { Status: "Previewed" } batch)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.NotFound);
            }

            if (batch.DecisionVersion != request.ExpectedDecisionVersion)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Conflict, "decisionVersion", View(batch with { Content = null }, false).Value);
            }

            if (request.Decisions.Count > _maxDecisionsPerRequest || request.Decisions.Any(d => d.Note is { Length: > 1000 }))
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Invalid, "decisions");
            }

            Dictionary<int, ImportRowDecision> decisions = await repository.GetDecisionsAsync(id, cancellationToken);
            (StagedFile staged, StoredMapping stored) = Reparse(batch);
            ImportPlanResult current = ImportPlanner.Plan(PlanInput(stored.Request, batch.FileName), staged,
                await repository.LoadImportContextAsync(caller.Scope, cancellationToken), decisions);
            var rows = current.Rows.ToDictionary(r => r.RowKey);
            foreach (ImportRowDecision decision in request.Decisions)
            {
                if (!rows.TryGetValue(decision.RowKey, out PlannedRow? row) || !row.AllowedDecisions.Contains(decision.Decision, StringComparer.Ordinal))
                {
                    return SaResult<ImportBatchView>.Fail(SaErrors.Invalid, "decisions[" + decision.RowKey + "]");
                }

                decisions[decision.RowKey] = decision;
            }

            if (request.BulkDecision is { } bulk)
            {
                foreach (PlannedRow row in current.Rows.Where(r => (request.BulkClassification is null || r.Classification == request.BulkClassification)
                    && (request.BulkEntityKind is null || r.Row.Kind == request.BulkEntityKind) && r.AllowedDecisions.Contains(bulk, StringComparer.Ordinal)
                    && (r.Candidates.Count == 0 || bulk != ImportDecisions.Link)))
                {
                    decisions[row.RowKey] = new ImportRowDecision(row.RowKey, bulk, null, "Toplu karar");
                }
            }

            if (decisions.Values.Any(d => d.Decision == ImportDecisions.Confirm) && !caller.Can(ServiceAccountCapabilities.Assign))
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Forbidden, "confirmOwnership");
            }

            ImportPlanResult replanned = ImportPlanner.Plan(PlanInput(stored.Request, batch.FileName), staged,
                await repository.LoadImportContextAsync(caller.Scope, cancellationToken), decisions);
            bool saved = await repository.ReplacePlanAsync(id, batch.PreviewVersion, batch.DecisionVersion, Records(replanned, decisions, caller, clock.GetUtcNow()),
                JsonSerializer.Serialize(Summary(replanned, staged)), "Decided", caller.Actor, cancellationToken);
            return saved && await repository.GetBatchAsync(id, false, cancellationToken) is { } updated
                ? View(updated, false) : SaResult<ImportBatchView>.Fail(SaErrors.Conflict, "decisionVersion");
        }, cancellationToken);

    /// <summary>Re-runs the preview against current data (after a 409 or concurrent change).</summary>
    public Task<SaResult<ImportBatchView>> RefreshImportAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Import, async caller =>
        {
            if (!caller.Scope.HasOrganizationLevel || await repository!.GetBatchAsync(id, true, cancellationToken) is not { Status: "Previewed" } batch)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.NotFound);
            }

            Dictionary<int, ImportRowDecision> decisions = await repository.GetDecisionsAsync(id, cancellationToken);
            (StagedFile staged, StoredMapping stored) = Reparse(batch);
            ImportPlanResult plan = ImportPlanner.Plan(PlanInput(stored.Request, batch.FileName), staged,
                await repository.LoadImportContextAsync(caller.Scope, cancellationToken), decisions);
            bool saved = await repository.ReplacePlanAsync(id, batch.PreviewVersion, batch.DecisionVersion, Records(plan, decisions, caller, clock.GetUtcNow()),
                JsonSerializer.Serialize(Summary(plan, staged)), "Refreshed", caller.Actor, cancellationToken);
            return saved && await repository.GetBatchAsync(id, false, cancellationToken) is { } updated
                ? View(updated, false) : SaResult<ImportBatchView>.Fail(SaErrors.Conflict, "previewVersion");
        }, cancellationToken);

    /// <summary>Atomic, idempotent commit. The server re-plans inside the transaction and refuses stale previews.</summary>
    public Task<SaResult<ImportBatchView>> CommitImportAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, ImportCommitRequest request,
        string? idempotencyKey, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Import, async caller =>
        {
            if (idempotencyKey is not { Length: >= 8 and <= 128 } key || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.IdempotencyKey, "Idempotency-Key");
            }

            if (!caller.Scope.HasOrganizationLevel)
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Forbidden, "scope");
            }

            Dictionary<int, ImportRowDecision> decisions = await repository!.GetDecisionsAsync(id, cancellationToken);
            if (decisions.Values.Any(d => d.Decision == ImportDecisions.Confirm) && !caller.Can(ServiceAccountCapabilities.Assign))
            {
                return SaResult<ImportBatchView>.Fail(SaErrors.Forbidden, "confirmOwnership");
            }

            SaResult<ImportCommitOutcome> outcome = await repository.CommitAsync(id, request, key, caller.Actor, caller.Scope, (batch, importContext, stored) =>
            {
                (StagedFile staged, StoredMapping mapping) = Reparse(batch);
                return ImportPlanner.Plan(PlanInput(mapping.Request, batch.FileName), staged, importContext, stored);
            }, cancellationToken);
            if (!outcome.IsSuccess)
            {
                return SaResult<ImportBatchView>.Fail(outcome.ErrorCode!, outcome.Field, outcome.Current);
            }

            return await repository.GetBatchAsync(id, false, cancellationToken) is { } committed
                ? View(committed, outcome.Value!.Replay) : SaResult<ImportBatchView>.Fail(SaErrors.NotFound);
        }, cancellationToken);

    private SpreadsheetLimits Limits => new(MaxUploadBytes: _options.MaxImportBytes);

    private string? Validate(StageImportRequest request, string fileName, byte[] bytes)
    {
        if (!ServiceAccountImportProfiles.All.Contains(request.Profile, StringComparer.Ordinal))
        {
            return "profile";
        }

        if (bytes.Length == 0 || bytes.Length > _options.MaxImportBytes)
        {
            return "file";
        }

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        bool zip = Import.SpreadsheetReader.IsZip(bytes);
        bool expected = request.Profile == ServiceAccountImportProfiles.LegacyPackage
            ? extension == ".json" && bytes[0] is (byte)'{' or 0xEF
            : zip ? extension == ".xlsx" : extension == ".csv" && request.Profile != ServiceAccountImportProfiles.LegacyWorkbook;
        if (!expected)
        {
            return "file";
        }

        if (string.IsNullOrWhiteSpace(request.SourceDateProvenance) || request.SourceDateProvenance.Length > 400)
        {
            return "sourceDateProvenance";
        }

        if (request.SourceReportDate is { } date && date > Today)
        {
            return "sourceReportDate";
        }

        return !ValidText(request.DeclaredScope, 200) ? "declaredScope" : !ValidText(request.DeclaredDomain, 128) ? "declaredDomain"
            : !ValidText(request.Sheet, 64) ? "sheet" : !ValidText(request.TargetTeam, 200) ? "targetTeam"
            : request.Mapping is { Count: > 60 } ? "mapping" : ValidateCoverage(request);
    }

    /// <summary>
    /// A complete-list declaration names its population explicitly (1..20 organizations, optional domain), needs a
    /// dated source that carries coordination-list observations, and is the only way absence can be inferred. Partial/unknown lists carry no population.
    /// </summary>
    private static string? ValidateCoverage(StageImportRequest request)
    {
        string coverage = CoverageOf(request);
        int organizations = request.CoverageOrganizationIds?.Count ?? 0;
        if (!ServiceAccountImportCoverage.All.Contains(coverage, StringComparer.Ordinal))
        {
            return "coverage";
        }

        if (coverage != ServiceAccountImportCoverage.Complete)
        {
            return organizations == 0 ? null : "coverageOrganizationIds";
        }

        return !_coverageProfiles.Contains(request.Profile, StringComparer.Ordinal) ? "coverage"
            : request.SourceReportDate is null ? "sourceReportDate"
            : organizations is < 1 or > _maxCoverageOrganizations || request.CoverageOrganizationIds!.Contains(Guid.Empty) ? "coverageOrganizationIds"
            : null;
    }

    private static string CoverageOf(StageImportRequest request) =>
        string.IsNullOrWhiteSpace(request.Coverage) ? ServiceAccountImportCoverage.Unknown : request.Coverage.Trim();

    /// <summary>Every declared organization must exist and lie inside the importer's organization-level scope.</summary>
    private static bool CoverageInScope(StageImportRequest request, ImportContext context) =>
        (request.CoverageOrganizationIds ?? []).All(id => context.Organizations.Any(o => o.Id == id)
            && (context.Scope.All || context.Scope.Organizations.Contains(id)));

    private static string ReplayKey(StageImportRequest request, string sha) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.Profile,
            sha,
            request.SourceReportDate,
            Scope = Domain.ServiceAccounts.ServiceAccountText.LabelKey(request.DeclaredScope),
            Domain = Domain.ServiceAccounts.ServiceAccountText.DomainKey(request.DeclaredDomain),
            Sheet = request.Sheet,
            Target = Domain.ServiceAccounts.ServiceAccountText.LabelKey(request.TargetTeam),
            Mapping = request.Mapping?.Select(m => new { Header = Domain.ServiceAccounts.ServiceAccountText.LabelKey(m.SourceHeader), m.TargetField }),
            Coverage = CoverageOf(request),
            CoverageOrganizations = request.CoverageOrganizationIds?.Distinct().Order()
        })))).ToLowerInvariant();

    private (StagedFile Staged, StoredMapping Stored) Reparse(ImportBatchRecord batch)
    {
        StoredMapping stored = JsonSerializer.Deserialize<StoredMapping>(batch.MappingJson)!;
        byte[] bytes = batch.Content ?? throw new InvalidOperationException("Batch content missing.");
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), batch.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Staged bytes do not match their hash.");
        }

        return (ImportParser.Parse(batch.Profile, bytes, stored.Request, batch.Sha256, Limits), stored);
    }

    private static ImportPlanInput PlanInput(StageImportRequest request, string fileName) =>
        new(request.Profile, request.SourceReportDate, request.DeclaredScope, request.DeclaredDomain, request.TargetTeam, SafeName(fileName),
            CoverageOf(request), request.CoverageOrganizationIds ?? []);

    private static ImportSummary Summary(ImportPlanResult plan, StagedFile staged) => plan.Summary with
    {
        IgnoredHelperColumns = staged.IgnoredHelperColumns,
        FormulaCells = staged.FormulaCells
    };

    private static List<ImportRowRecord> Records(ImportPlanResult plan, IReadOnlyDictionary<int, ImportRowDecision> decisions, SaCaller caller, DateTimeOffset now) =>
        [.. plan.Rows.Select(row =>
        {
            ImportRowDecision? decision = decisions.TryGetValue(row.RowKey, out ImportRowDecision? chosen)
                && row.AllowedDecisions.Contains(chosen.Decision, StringComparer.Ordinal) ? chosen : null;
            return new ImportRowRecord(row.RowKey, row.Row.Sheet, row.Row.RowNumber, row.Row.Kind, JsonSerializer.Serialize(row.Row.Original),
                JsonSerializer.Serialize(row.Row.Fields), row.Classification, row.AccountId, JsonSerializer.Serialize(row.Candidates),
                JsonSerializer.Serialize(row.Errors), JsonSerializer.Serialize(new RowMeta(row.Fingerprint, row.Diff, row.AllowedDecisions, row.DefaultDecision,
                    row.AccountLabel, row.Warnings)), row.RequiresDecision, decision?.Decision,
                decision is null ? null : JsonSerializer.Serialize(new SqlServiceAccountRepository.DecisionNote(decision.TargetId, decision.Note)),
                decision is null ? null : caller.User.Id, decision is null ? null : now);
        })];

    private sealed record RowMeta(string Fingerprint, IReadOnlyList<ImportFieldDiff> Diff, IReadOnlyList<string> Allowed, string Default,
        string? Label, IReadOnlyList<string> Warnings);

    private static ImportRowView RowView(ImportRowRecord row)
    {
        RowMeta meta = JsonSerializer.Deserialize<RowMeta>(row.DiffJson ?? "{}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return new ImportRowView(row.RowKey, row.Sheet, row.RowNumber, row.EntityKind, row.Classification, meta.Label, meta.Diff,
            JsonSerializer.Deserialize<string[]>(row.ErrorsJson ?? "[]")!, meta.Warnings,
            JsonSerializer.Deserialize<ImportCandidate[]>(row.CandidatesJson ?? "[]", new JsonSerializerOptions(JsonSerializerDefaults.Web))!,
            row.RequiresDecision, row.Decision ?? (row.RequiresDecision ? null : meta.Default), meta.Allowed);
    }

    private static SaResult<ImportBatchView> View(ImportBatchRecord batch, bool replay)
    {
        StoredMapping? stored = JsonSerializer.Deserialize<StoredMapping>(batch.MappingJson);
        return new ImportBatchView(batch.Id, batch.Profile, batch.FileName, batch.Sha256, batch.SourceReportDate, batch.SourceDateProvenance,
            batch.DeclaredScope, batch.DeclaredDomain, batch.Status, batch.PreviewVersion, batch.DecisionVersion,
            batch.SummaryJson is null ? null : JsonSerializer.Deserialize<ImportSummary>(batch.SummaryJson),
            stored?.Applied ?? [], stored?.Warnings ?? [], replay, batch.UploadedAt, batch.CommittedAt,
            batch.ResultJson is null ? null : JsonSerializer.Deserialize<ImportResultView>(batch.ResultJson),
            stored is null ? ServiceAccountImportCoverage.Unknown : CoverageOf(stored.Request), stored?.Request.CoverageOrganizationIds ?? []);
    }

    private static string ContentTypeOf(byte[] bytes) => Import.SpreadsheetReader.IsZip(bytes)
        ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        : bytes.Length > 0 && bytes[0] == (byte)'{' ? "application/json" : "text/csv";

    private static string SafeName(string fileName) => Domain.ServiceAccounts.ServiceAccountText.FileName(fileName);

    private static string? Clean(string? value, int max) =>
        Domain.ServiceAccounts.ServiceAccountText.Clean(value) is { } clean ? clean.Length <= max ? clean : clean[..max] : null;
}
