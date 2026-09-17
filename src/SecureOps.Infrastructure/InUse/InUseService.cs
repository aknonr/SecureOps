using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Authorized local In Use workflow. There is deliberately no external-write dependency.</summary>
public sealed partial class InUseService(IInUseRepository repository, IInUseSourceClient source,
    IApplicationAccessService access, IAccessRepository users, ICommandIdempotencyStore commands,
    ILogger<InUseService> logger, InUseReportArchive? archive = null, SqlInUseIdentities? identities = null, InUsePolicy? policy = null)
{
    /// <summary>Queries only persisted records.</summary>
    public Task<InUseResult<InUsePage>> QueryAsync(ClaimsPrincipal principal, AccessOperationContext context,
        InUseQuery query, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseView, async user =>
        {
            if (query.Page is < 1 or > 100000 || query.PageSize is < 1 or > 100 || query.Search?.Length > 100
                || query.View is not ("all" or "mine" or "unassigned") || query.Status is not (null or "Unreviewed" or "Draft" or "Stale"))
            { return InUseResult<InUsePage>.Fail("InUseInvalid"); }
            InUsePage page = await repository.QueryAsync(query, user.Id, token);
            IReadOnlyDictionary<Guid, string> labels = await LabelsAsync(page.Items, token);
            return new(page with { Items = page.Items.Select(r => Label(r, labels)).ToArray() });
        }, token);

    /// <summary>Reads local detail without querying corporate data.</summary>
    public Task<InUseResult<InUseRecord>> GetAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseView, async _ =>
        {
            InUseRecord? record = await repository.GetAsync(id, token);
            return record is null ? InUseResult<InUseRecord>.Fail("InUseNotFound")
                : new(Label(record, await LabelsAsync([record], token))
                    with
                { ArchivedVersions = archive?.Versions(id) ?? [], PolicyProposal = policy?.Propose(record) });
        }, token);

    /// <summary>Minimal approved-reviewer picker; never resolves source display names into users.</summary>
    public Task<InUseResult<IReadOnlyList<InUseAssignee>>> AssigneesAsync(ClaimsPrincipal principal,
        AccessOperationContext context, CancellationToken token, string? search = null) => RunAsync<IReadOnlyList<InUseAssignee>>(principal, context,
        Capabilities.InUseAssign, async _ =>
        {
            if (search?.Length > 100)
            { return InUseResult<IReadOnlyList<InUseAssignee>>.Fail("InUseInvalid"); }
            if (users is SqlAccessRepository && identities is not null)
            { return new(await identities.ReadAsync(null, search, token)); }
            ApplicationUser[] eligible = (await users.ListUsersAsync(token)).Where(Reviewer).ToArray();
            IReadOnlyDictionary<Guid, string> labels = InUseAssigneeLabels.Create(eligible);
            return new(eligible.Where(u => string.IsNullOrWhiteSpace(search) || labels[u.Id].Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(u => labels[u.Id], StringComparer.Ordinal).ThenBy(u => u.Id).Take(50)
                .Select(u => new InUseAssignee(u.Id, labels[u.Id])).ToArray());
        }, token);

    private static InUseRecord Label(InUseRecord record, IReadOnlyDictionary<Guid, string> labels) => record.AssigneeId is Guid id
        ? record with { AssigneeLabel = labels.GetValueOrDefault(id) ?? $"Kayıtlı inceleyici · {id:D}" } : record;

    /// <summary>Refreshes a bounded independent scope, with durable command tracking and no deletion.</summary>
    public Task<InUseResult<InUseRefreshState>> RefreshAsync(ClaimsPrincipal principal, AccessOperationContext context,
        RefreshInUseRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseRefresh, async user =>
        {
            if (request.CommandId == Guid.Empty)
            { return InUseResult<InUseRefreshState>.Fail("InUseInvalid"); }
            await using IAsyncDisposable? scope = await repository.TryAcquireRefreshAsync(token);
            if (scope is null)
            { return new(null, "InUseConflict", "Bu kapsam için kaynak okuması sürüyor. Tamamlandıktan sonra yeniden deneyin; kayıtlı veriler korunur."); }
            string key = request.CommandId.ToString("D");
            CommandBeginResult begin = await commands.TryBeginAsync("InUseRefresh", "4241:68", key,
                user.Id.ToString("D"), TimeSpan.FromMinutes(2), token);
            if (begin.Disposition == CommandBeginDisposition.Completed)
            { return new(await repository.StateAsync(token)); }
            if (begin.Disposition != CommandBeginDisposition.Acquired)
            { return InUseResult<InUseRefreshState>.Fail("InUseConflict"); }
            InUseRefreshState state = await repository.StateAsync(token);
            InUseBatch? batch = null;
            string? error = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                batch = await source.DiscoverAsync(timeout.Token);
                if (!InUseState.Valid(batch))
                { throw new InvalidDataException("Invalid bounded In Use batch."); }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { error = "SourceTimeout"; batch = null; }
            catch (Exception ex) when (ex is ExternalIntegrationException or HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException)
            { error = "SourceUnavailableOrMalformed"; batch = null; }
            bool saved = await repository.RefreshAsync(state.Version, batch, error,
                Audit(user, context, "Refresh", new { Count = batch?.Records.Count, batch?.Complete, Error = error ?? batch?.Issue }), token);
            if (!saved)
            {
                await commands.FailAsync("InUseRefresh", "4241:68", key, begin.ExecutionToken!.Value, "InUseConflict", token);
                return InUseResult<InUseRefreshState>.Fail("InUseConflict");
            }
            await commands.CompleteAsync("InUseRefresh", "4241:68", key, begin.ExecutionToken!.Value, token);
            return new(await repository.StateAsync(token));
        }, token);

    /// <summary>Manually assigns an approved reviewer with an audited reason and exact version.</summary>
    public Task<InUseResult<InUseRecord>> AssignAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, AssignInUseRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseAssign, async user =>
        {
            if (!Text(request.Reason, 500, required: true) || request.ExpectedVersion < 1 || request.AssigneeId == Guid.Empty)
            { return InUseResult<InUseRecord>.Fail("InUseInvalid"); }
            InUseRecord? old = await repository.GetAsync(id, token);
            if (old is null)
            { return InUseResult<InUseRecord>.Fail("InUseNotFound"); }
            if (old.Version != request.ExpectedVersion)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            ApplicationUser? assignee = request.AssigneeId is Guid target ? await users.GetUserAsync(target, token) : null;
            if (request.AssigneeId.HasValue && (assignee is null || !Reviewer(assignee)))
            { return InUseResult<InUseRecord>.Fail("InUseAssigneeUnavailable"); }
            IReadOnlyDictionary<Guid, string> labels = await LabelsAsync([old with { AssigneeId = assignee?.Id }], token);
            InUseRecord next = old with
            {
                Version = old.Version + 1,
                AssigneeId = assignee?.Id,
                AssigneeLabel = assignee is null ? null : labels.GetValueOrDefault(assignee.Id) ?? InUseAssigneeLabels.Create([assignee])[assignee.Id],
                AssignedBy = user.Id,
                AssignedByLabel = ActorLabel(user),
                AssignedAt = DateTimeOffset.UtcNow
            };
            return await SaveAsync(next, request.ExpectedVersion, Audit(user, context, "Assigned",
                new { id, PreviousAssignee = old.AssigneeId, next.AssigneeId, request.Reason, next.Version }, id, request.ExpectedVersion, assigneeId: next.AssigneeId), token);
        }, token);

    /// <summary>An approved review-capable actor can save a version-protected draft; assignment is optional.</summary>
    public Task<InUseResult<InUseRecord>> SaveDraftAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, SaveInUseDraftRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            InUseRecord? old = await repository.GetAsync(id, token);
            if (old is null)
            { return InUseResult<InUseRecord>.Fail("InUseNotFound"); }
            if (request.SourceVersion != old.SourceVersion || request.ExpectedVersion != old.Version)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            if (!ValidDraft(request, old))
            { return InUseResult<InUseRecord>.Fail("InUseInvalid"); }
            InUsePolicyProposal? accepted = old.Draft?.SourceVersion == old.SourceVersion ? old.Draft.Policy : null;
            if (request.ReviewedPolicyFingerprint is not null)
            {
                InUsePolicyProposal? current = policy?.Propose(old);
                if (current is null || current.Fingerprint != request.ReviewedPolicyFingerprint)
                { return InUseResult<InUseRecord>.Fail("InUsePolicyChanged"); }
                accepted = current;
            }
            InUseRecord next = old with
            {
                PolicyProposal = null,
                Version = old.Version + 1,
                Draft = new(old.SourceVersion,
                (old.Draft?.Answers ?? []).Where(a => !request.Answers.Any(n => n.ServerId == a.ServerId && n.Check == a.Check))
                    .Concat(request.Answers.Select(a => string.IsNullOrWhiteSpace(a.Evidence)
                        ? a with { Evidence = old.Draft?.Answers.FirstOrDefault(o => o.ServerId == a.ServerId && o.Check == a.Check)?.Evidence ?? "" } : a))
                    .OrderBy(a => a.ServerId, StringComparer.Ordinal).ThenBy(a => a.Check, StringComparer.Ordinal).ToArray(),
                string.IsNullOrWhiteSpace(request.Notes) ? old.Draft?.Notes ?? "" : request.Notes.Trim(), user.Id, DateTimeOffset.UtcNow)
                { ReviewedByLabel = ActorLabel(user), Policy = accepted }
            };
            return await SaveAsync(next, request.ExpectedVersion, Audit(user, context, "DraftSaved",
                new { id, next.Version, next.SourceVersion, AnswerCount = request.Answers.Count, PolicyFingerprint = accepted?.Fingerprint }, id, request.ExpectedVersion, assigneeId: old.AssigneeId), token);
        }, token);

    /// <summary>Prepares one version-bound text-only workbook and audits its hash before returning it.</summary>
    public Task<InUseResult<InUseReport>> ExportAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, ExportInUseRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            InUseRecord? record = await repository.GetAsync(id, token);
            if (record is null)
            { return InUseResult<InUseReport>.Fail("InUseNotFound"); }
            if (record.Version != request.ExpectedVersion)
            { return InUseResult<InUseReport>.Fail("InUseConflict"); }
            if (request.ArchivedVersion is long historical)
            {
                if (historical < 1 || request.Archive)
                { return InUseResult<InUseReport>.Fail("InUseInvalid"); }
                InUseReport? stored = await (archive ?? throw new InvalidOperationException("Archive unavailable.")).AccessAsync(id, historical, null,
                    report => repository.ExportAsync(id, request.ExpectedVersion, Audit(user, context, "ArchivedReportDownloadAuthorized",
                        new { id, report.Version, report.Sha256 }, id, request.ExpectedVersion), token), token);
                return stored is null ? InUseResult<InUseReport>.Fail("InUseConflict") : new(stored);
            }
            if (record.Version != request.ExpectedVersion || record.Draft is null || record.Draft.SourceVersion != record.SourceVersion)
            { return InUseResult<InUseReport>.Fail("InUseConflict"); }
            if (request.Archive && InUseChecks.Missing(record.Source, record.Draft.Answers) is { } missing)
            {
                string label = missing.Check switch { "InternetOut" => "Sunucudan internete erişim", "InternetIn" => "İnternetten sunucuya erişim", _ => "Mikrosegmentasyon" };
                string serverName = InUseDisplayText.Decode(record.Source.Servers.Single(s => s.Id == missing.ServerId).Fields.GetValueOrDefault("HOSTNAME")?.Value);
                return new(null, "InUseIncomplete", $"{serverName} ({missing.ServerId}): {label} için Evet veya Hayır seçin. Taslak kaydedilebilir; rapor hazır değil.");
            }
            if (request.Archive && !InUseChecks.RelationshipReady(record.Source))
            { return InUseResult<InUseReport>.Fail("InUseIncomplete"); }
            InUseReport report = InUseWorkbook.Create(record, user.Id, DateTimeOffset.UtcNow) with { PreparedByLabel = ActorLabel(user) };
            if (request.Archive)
            {
                InUseReport? stored = await (archive ?? throw new InvalidOperationException("Archive unavailable.")).AccessAsync(id, record.Version, report,
                    artifact => repository.ExportAsync(id, request.ExpectedVersion, Audit(user, context, "ReportArchiveAuthorized",
                        new { id, record.Version, record.SourceVersion, artifact.Sha256 }, id, request.ExpectedVersion), token), token);
                return stored is null ? InUseResult<InUseReport>.Fail("InUseConflict") : new(stored);
            }
            bool saved = await repository.ExportAsync(id, request.ExpectedVersion, Audit(user, context, "ReportPrepared",
                new { id, record.Version, record.SourceVersion, report.Sha256 }, id, request.ExpectedVersion), token);
            return saved ? new(report) : InUseResult<InUseReport>.Fail("InUseConflict");
        }, token);

    private async Task<InUseResult<InUseRecord>> SaveAsync(InUseRecord next, long expected, AuditEvent audit, CancellationToken token)
    {
        IReadOnlyDictionary<Guid, string> labels = await LabelsAsync([next], token);
        return await repository.SaveAsync(next with { PolicyProposal = null }, expected, audit, token)
            ? new(Label(next, labels) with { PolicyProposal = policy?.Propose(next) }) : InUseResult<InUseRecord>.Fail("InUseConflict");
    }

    /// <summary>Audited bounded diagnostic; no enrichment, assignment or source mutation.</summary>
    public Task<InUseResult<JsonElement>> DiagnoseAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, InUseDiagnosticRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseRefresh, async user =>
        {
            if (!user.Capabilities.Contains(Capabilities.OperationalRecordsViewDiagnostics))
            { return InUseResult<JsonElement>.Fail("AccessDenied"); }
            InUseRecord? record = await repository.GetAsync(id, token);
            if (record is null)
            { return InUseResult<JsonElement>.Fail("InUseNotFound"); }
            if (record.Source.Id != request.SourceId || record.Version != request.ExpectedVersion)
            { return InUseResult<JsonElement>.Fail("InUseConflict"); }
            if (!await repository.ExportAsync(id, request.ExpectedVersion, Audit(user, context, "RelationshipDiagnosticRequested", new { id }), token))
            { return InUseResult<JsonElement>.Fail("InUseConflict"); }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                JsonElement report = await source.DiagnoseAsync(request.SourceId, timeout.Token);
                return await repository.ExportAsync(id, request.ExpectedVersion, Audit(user, context, "RelationshipDiagnosticPrepared", new { id }), token)
                    ? new(report) : InUseResult<JsonElement>.Fail("InUseConflict");
            }
            catch (Exception ex) when (ex is ExternalIntegrationException or HttpRequestException or OperationCanceledException or TuruncuHatQueryResultException)
            { return InUseResult<JsonElement>.Fail("SourceUnavailableOrMalformed"); }
        }, token);

    private async Task<InUseResult<T>> RunAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, string capability,
        Func<ApplicationUser, Task<InUseResult<T>>> operation, CancellationToken token)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, token);
            if (!current.IsSuccess)
            { return InUseResult<T>.Fail(current.ErrorCode!); }
            ApplicationUser user = current.Value!.User;
            if (user.Id == Guid.Empty || user.Status != AccessStatus.Approved
                || !user.Capabilities.Contains(Capabilities.InUseView) || !user.Capabilities.Contains(capability))
            { return InUseResult<T>.Fail("AccessDenied"); }
            return await operation(user);
        }
        catch (InUseArchiveException ex)
        {
            logger.LogError("In Use archive failed. Code: {Code}. CorrelationId: {CorrelationId}", ex.Code, context.CorrelationId);
            return InUseResult<T>.Fail(ex.Code);
        }
        catch (Exception ex) when (ex is DbException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            logger.LogError("In Use local operation failed. FailureType: {FailureType}. CorrelationId: {CorrelationId}", ex.GetType().Name, context.CorrelationId);
            return InUseResult<T>.Fail("PersistenceUnavailable");
        }
    }

    private static bool Reviewer(ApplicationUser user) => user.Status == AccessStatus.Approved
        && user.Capabilities.Contains(Capabilities.InUseView) && user.Capabilities.Contains(Capabilities.InUseReview);
    private async Task<IReadOnlyDictionary<Guid, string>> LabelsAsync(IReadOnlyList<InUseRecord> records, CancellationToken token) =>
        users is SqlAccessRepository && identities is not null
            ? (await identities.ReadAsync(records.Where(r => r.AssigneeId.HasValue).Select(r => r.AssigneeId!.Value).Distinct().ToArray(), null, token)).ToDictionary(p => p.Id, p => p.Label)
            : InUseAssigneeLabels.Create(await users.ListUsersAsync(token));
    private static string ActorLabel(ApplicationUser user) => (string.IsNullOrWhiteSpace(user.DisplayName) ? user.LoginName ?? "Kayıtlı kullanıcı" : user.DisplayName)
        + " · " + user.Id.ToString("N")[..8];
    private static bool Text(string? value, int max, bool required = false) => value is not null && value.Length <= max
        && (!required || !string.IsNullOrWhiteSpace(value)) && !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
    private static bool ValidDraft(SaveInUseDraftRequest request, InUseRecord record) =>
        Text(request.Notes, 2000) && request.Answers is not null && request.Answers.Count <= 900
        && request.Answers.All(a => a is not null)
        && request.Answers.Select(a => (a.ServerId, a.Check)).Distinct().Count() == request.Answers.Count
        && request.Answers.All(a => record.Source.Servers.Any(s => s.Id == a.ServerId)
            && InUseChecks.Codes.Contains(a.Check) && InUseChecks.Values.Contains(a.Value)
            && Text(a.Evidence, 500));
    private static AuditEvent Audit(ApplicationUser user, AccessOperationContext context, string action, object details,
        Guid? recordId = null, long inputVersion = 0, Guid? commandId = null, Guid? assigneeId = null) => new()
        {
            Actor = user.Id.ToString("D"),
            Action = "InUse" + action,
            CorrelationId = context.CorrelationId,
            Details = details,
            Operation = recordId is null ? null : new(Guid.NewGuid(), commandId ?? Guid.NewGuid(), "InUse", recordId.Value.ToString("D"),
            "InUse" + action, inputVersion, DateTimeOffset.UtcNow, action switch
            { "CompletionIntentBlocked" => "Blocked", "ReportPrepared" => "Prepared", "ReportArchiveAuthorized" => "Archived", "CompletionResult" => "Recorded", _ => "Saved" },
            new(user.Id, "Human", user.DisplayName, user.LoginName, null),
            new("SecureOps.Api", Environment.MachineName + ":" + Environment.ProcessId, Environment.UserDomainName + "\\" + Environment.UserName),
            null, context.CorrelationId, AssigneeId: assigneeId)
        };
}
