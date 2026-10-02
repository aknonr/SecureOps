using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Report export payload.</summary>
public sealed record ReportExport(string FileName, string ContentType, byte[] Content);

public sealed partial class ServiceAccountService
{
    private static readonly JsonSerializerOptions _payloadJson = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Snapshot scope as stored: the caller's resolved scope plus explicit filters.</summary>
    private sealed record SnapshotScope(bool All, IReadOnlyList<Guid> Organizations, IReadOnlyList<Guid> Teams, Guid? OrganizationFilter, Guid? TeamFilter, string Label);

    /// <summary>Live weekly/manager report computed by the single metric implementation over the caller's scope.</summary>
    public Task<SaResult<ServiceAccountReport>> WeeklyReportAsync(ClaimsPrincipal principal, AccessOperationContext context, WeeklyReportQuery query,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Report, async caller =>
        {
            SaResult<(ServiceAccountReport Report, SnapshotScope Scope, string Watermark)> computed = await ComputeAsync(caller, query, cancellationToken);
            return computed.IsSuccess ? computed.Value.Report : SaResult<ServiceAccountReport>.Fail(computed.ErrorCode!, computed.Field);
        }, cancellationToken);

    /// <summary>Creates an immutable snapshot; later historical entries change live reports only.</summary>
    public Task<SaResult<SnapshotItem>> CreateSnapshotAsync(ClaimsPrincipal principal, AccessOperationContext context, CreateSnapshotRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Report, async caller =>
        {
            if (request.Kind is not ("Weekly" or "Manager") || !ValidText(request.Label, 200))
            {
                return SaResult<SnapshotItem>.Fail(SaErrors.Invalid, "kind");
            }

            SaResult<(ServiceAccountReport Report, SnapshotScope Scope, string Watermark)> computed = await ComputeAsync(caller,
                new WeeklyReportQuery(request.WeekStart, request.AsOf, request.OrganizationId, request.TeamId, request.Period, request.PeriodEnd), cancellationToken);
            if (!computed.IsSuccess)
            {
                return SaResult<SnapshotItem>.Fail(computed.ErrorCode!, computed.Field);
            }

            (ServiceAccountReport report, SnapshotScope scope, string watermark) = computed.Value;
            string payload = JsonSerializer.Serialize(report, _payloadJson);
            string scopeJson = JsonSerializer.Serialize(scope, _payloadJson);
            SnapshotRecord snapshot = new(Guid.NewGuid(), request.Kind, report.WeekStart, report.WeekEndExclusive.AddDays(-1), report.AsOf, scopeJson, Sha(scopeJson),
                report.MetricDefinitionVersion, watermark, payload, Sha(payload), ServiceAccountText.Clean(request.Label), clock.GetUtcNow(), caller.User.Id);
            await repository!.SaveSnapshotAsync(snapshot, caller.Actor, cancellationToken);
            return Item(snapshot, scope.Label);
        }, cancellationToken);

    /// <summary>Snapshots whose scope the caller fully covers.</summary>
    public Task<SaResult<IReadOnlyList<SnapshotItem>>> SnapshotsAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Report, async caller =>
        {
            IReadOnlyList<SnapshotRecord> snapshots = await repository!.SnapshotsAsync(200, cancellationToken);
            return new SaResult<IReadOnlyList<SnapshotItem>>([.. snapshots
                .Select(s => (Record: s, Scope: JsonSerializer.Deserialize<SnapshotScope>(s.ScopeJson, _payloadJson)!))
                .Where(s => Covers(caller, s.Scope))
                .Select(s => Item(s.Record, s.Scope.Label))]);
        }, cancellationToken);

    /// <summary>The stored (unchanged) payload of one snapshot.</summary>
    public Task<SaResult<ServiceAccountReport>> SnapshotAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Report, async caller =>
        {
            SaResult<(SnapshotRecord Record, ServiceAccountReport Report)> loaded = await LoadSnapshotAsync(caller, id, cancellationToken);
            return loaded.IsSuccess ? loaded.Value.Report : SaResult<ServiceAccountReport>.Fail(loaded.ErrorCode!);
        }, cancellationToken);

    /// <summary>XLSX or PDF rendered only from the stored snapshot payload (audited).</summary>
    public Task<SaResult<ReportExport>> ExportSnapshotAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, string format,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Report, async caller =>
        {
            if (format is not ("xlsx" or "pdf"))
            {
                return SaResult<ReportExport>.Fail(SaErrors.Invalid, "format");
            }

            SaResult<(SnapshotRecord Record, ServiceAccountReport Report)> loaded = await LoadSnapshotAsync(caller, id, cancellationToken);
            if (!loaded.IsSuccess)
            {
                return SaResult<ReportExport>.Fail(loaded.ErrorCode!);
            }

            (SnapshotRecord record, ServiceAccountReport report) = loaded.Value;
            string creator = await repository!.UserLabelAsync(record.CreatedBy, cancellationToken);
            var document = ReportDocument.From(report, record.Id, record.PayloadSha256, record.Label, record.CreatedAt, creator);
            string name = $"servis-hesaplari-{record.PeriodStart:yyyy-MM-dd}-{record.Id.ToString("N")[..8]}";
            await repository.AuditReadAsync("ReportExported", new { SnapshotId = id, Format = format, record.PayloadSha256 }, caller.Actor, cancellationToken);
            return format == "xlsx"
                ? new ReportExport(name + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ReportWorkbookWriter.Write(document))
                : new ReportExport(name + ".pdf", "application/pdf", ReportPdfWriter.Write(document));
        }, cancellationToken);

    /// <summary>Maximum rows in one account-list export; a larger selection must be narrowed with filters.</summary>
    public const int MaxExportRows = 5000;

    /// <summary>
    /// Exports the caller's filtered account list (same server-side scope and filters as the list) as XLSX for reporting.
    /// Capped, audited with the filter and row count, and never wider than the list the caller can already read.
    /// </summary>
    public Task<SaResult<ReportExport>> ExportAccountsAsync(ClaimsPrincipal principal, AccessOperationContext context, AccountListQuery query,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Report, async caller =>
        {
            if (!ValidText(query.Search, 100) || !SqlServiceAccountRepository.ValidListQuery(query))
            {
                return SaResult<ReportExport>.Fail(SaErrors.Invalid, "query");
            }

            AccountPage page = await repository!.ListAccountsAsync(caller.Scope, query with { Page = 1, PageSize = MaxExportRows + 1 }, Today, cancellationToken);
            if (page.Total > MaxExportRows)
            {
                return SaResult<ReportExport>.Fail(SaErrors.Invalid, "tooManyRows", page.Total);
            }

            DateTimeOffset now = clock.GetUtcNow();
            string scope = await ScopeLabelAsync(caller.Scope, query.OrganizationId, query.TeamId, cancellationToken);
            ReportDocument document = new("Servis Hesapları Listesi",
            [
                ("Kapsam", scope),
                ("Filtre", ExportFilter(query)),
                ("Kesim", ReportCalendar.LocalDate(now).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)),
                ("Satır", page.Total.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Not", "Sahip ekip/kişi yalnız teyitli sahipliktir; takipçi sahip değildir. Kimlik durumu dizin doğrulaması değildir.")
            ],
            [
                new ReportSection("Hesaplar", ["Hesap", "Domain", "Kimlik", "Rapor kurumu", "Sahip ekip", "Sahip kişi", "Takipçi (sahip değil)", "Açık iş",
                    "En yakın vade", "Durum", "Son görülme", "Son listede"],
                    [.. page.Items.Select(a => (IReadOnlyList<ReportCell>)[a.AccountName, a.Domain ?? "Bilinmiyor", a.IdentityState == "Provisional" ? "Geçici" : "Teyitli",
                        a.ReportOrganization?.Label ?? "Atanmadı", a.OwnerTeam?.Label ?? "Yok", a.OwnerPerson?.Label ?? "Yok", a.FollowupPerson?.Label ?? "",
                        a.OpenRequests, new ReportCell(Date: a.NearestDue), a.Status, new ReportCell(Date: a.LastObservedOn),
                        a.LastPresence == "NotPresent" ? "Yok" : a.LastPresence is null ? "" : "Var"])])
            ], now);
            await repository.AuditReadAsync("AccountsExported", new
            {
                Rows = page.Total,
                query.Status,
                query.OrganizationId,
                query.TeamId,
                query.MyTeam,
                Search = query.Search is not null
            }, caller.Actor, cancellationToken);
            return new ReportExport($"servis-hesaplari-liste-{ReportCalendar.LocalDate(now):yyyy-MM-dd}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ReportWorkbookWriter.Write(document));
        }, cancellationToken);

    private static string ExportFilter(AccountListQuery query) => string.Join(" · ", new[]
    {
        query.Status is null ? null : "durum: " + query.Status,
        query.MyTeam ? "yalnız ekiplerim" : null,
        query.Search is null ? null : "arama",
        query.Domain is null ? null : "domain: " + query.Domain,
        query.DueBefore is { } due ? "vade ≤ " + due.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) : null
    }.OfType<string>().DefaultIfEmpty("yok"));

    private async Task<SaResult<(SnapshotRecord Record, ServiceAccountReport Report)>> LoadSnapshotAsync(SaCaller caller, Guid id, CancellationToken cancellationToken)
    {
        SnapshotRecord? record = await repository!.SnapshotAsync(id, cancellationToken);
        if (record is null || !Covers(caller, JsonSerializer.Deserialize<SnapshotScope>(record.ScopeJson, _payloadJson)!))
        {
            return SaResult<(SnapshotRecord, ServiceAccountReport)>.Fail(SaErrors.NotFound);
        }

        if (!string.Equals(Sha(record.PayloadJson), record.PayloadSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Snapshot payload hash mismatch.");
        }

        return (record, JsonSerializer.Deserialize<ServiceAccountReport>(record.PayloadJson, _payloadJson)!);
    }

    private async Task<SaResult<(ServiceAccountReport Report, SnapshotScope Scope, string Watermark)>> ComputeAsync(SaCaller caller, WeeklyReportQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset asOf = query.AsOf ?? clock.GetUtcNow();
        if (asOf > clock.GetUtcNow().AddMinutes(1))
        {
            return SaResult<(ServiceAccountReport, SnapshotScope, string)>.Fail(SaErrors.Invalid, "asOf");
        }

        if (caller.Scope.IsEmpty
            || query.OrganizationId is { } org && !caller.Scope.All && !caller.Scope.Organizations.Contains(org)
            || query.TeamId is { } team && !caller.Scope.CoversTeam(team))
        {
            return SaResult<(ServiceAccountReport, SnapshotScope, string)>.Fail(SaErrors.Forbidden, "scope");
        }

        if (ReportPeriods.Resolve(query.Period, query.WeekStart, query.PeriodEnd) is not { } period)
        {
            return SaResult<(ServiceAccountReport, SnapshotScope, string)>.Fail(SaErrors.Invalid, "period");
        }

        string label = await ScopeLabelAsync(caller.Scope, query.OrganizationId, query.TeamId, cancellationToken);
        (ReportFacts facts, string watermark) = await repository!.ReportFactsAsync(caller.Scope, query.OrganizationId, query.TeamId, Thresholds, null,
            cancellationToken);
        ServiceAccountReport report = ServiceAccountMetrics.Compute(facts, period.Start, period.EndExclusive, asOf, label, query.Period ?? ReportPeriods.Week);
        SnapshotScope scope = new(caller.Scope.All, [.. caller.Scope.Organizations.Order()], [.. caller.Scope.Teams.Order()], query.OrganizationId, query.TeamId, label);
        return (report, scope, watermark);
    }

    private async Task<string> ScopeLabelAsync(ServiceAccountScope scope, Guid? organization, Guid? team, CancellationToken cancellationToken)
    {
        StringBuilder label = new(scope.All ? "Tüm modül" : string.Empty);
        if (!scope.All)
        {
            IReadOnlyList<SaRef> orgs = await repository!.OrganizationRefsAsync(scope.Organizations.Take(20), cancellationToken);
            IReadOnlyList<SaRef> teams = await repository.TeamRefsAsync(scope.DirectTeams.Take(20), cancellationToken);
            label.Append(string.Join(", ", orgs.Select(o => o.Label).Concat(teams.Select(t => t.Label + " (ekip)"))));
        }

        if (organization is { } org)
        {
            label.Append(" | Filtre: ").Append(string.Join(", ", (await repository!.OrganizationRefsAsync([org], cancellationToken)).Select(o => o.Label)));
        }

        if (team is { } t)
        {
            label.Append(" | Ekip filtresi: ").Append(string.Join(", ", (await repository!.TeamRefsAsync([t], cancellationToken)).Select(x => x.Label)));
        }

        return label.ToString();
    }

    private static bool Covers(SaCaller caller, SnapshotScope scope) =>
        caller.Scope.All || !scope.All && scope.Organizations.All(caller.Scope.Organizations.Contains) && scope.Teams.All(caller.Scope.Teams.Contains);

    private static SnapshotItem Item(SnapshotRecord record, string scopeLabel) => new(record.Id, record.Kind, record.PeriodStart, record.PeriodEnd, record.AsOf,
        scopeLabel, record.MetricDefinitionVersion, record.PayloadSha256, record.Label, record.CreatedAt);

    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
