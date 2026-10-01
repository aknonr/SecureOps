using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

public sealed class ServiceAccountReportSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];

    [ServiceAccountSqlFact]
    public async Task SentSnapshotNeverChanges_LiveReportPlacesLateActionInItsWeek_ExportsReconcile()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN RPT ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_RPT", null, org, "sentetik"), _token));
        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", ActualOn: new(2026, 9, 15), EvidenceNote: "kayıt"), _token));
        DateTimeOffset asOf = new(2026, 9, 20, 23, 0, 0, TimeSpan.FromHours(3));
        SnapshotItem sent = Ok(await fx.Service.CreateSnapshotAsync(coordinator.Principal, fx.Context,
            new CreateSnapshotRequest(new(2026, 9, 14), asOf, null, null, "Weekly", "Gönderilen nüsha"), _token));
        ServiceAccountReport before = Ok(await fx.Service.SnapshotAsync(coordinator.Principal, fx.Context, sent.Id, _token));
        before.Weekly.Actions.InPeriod.Should().Be(1);

        // A late entry for the same past week arrives after the snapshot was sent.
        Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("Review", "Performed", "Intermediate", ActualOn: new(2026, 9, 16), EvidenceNote: "geç girildi"), _token));
        ServiceAccountReport after = Ok(await fx.Service.SnapshotAsync(coordinator.Principal, fx.Context, sent.Id, _token));
        after.Should().BeEquivalentTo(before, "a sent snapshot is immutable");
        ServiceAccountReport live = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 14), asOf), _token));
        live.Weekly.Actions.InPeriod.Should().Be(2, "the live report places the late action in its real week");

        await using (SqlConnection connection = fx.Connection())
        {
            Func<Task> tamper = () => connection.ExecuteAsync("UPDATE svcacct.ReportSnapshots SET Label = N'x' WHERE Id = @Id", new { sent.Id });
            await tamper.Should().ThrowAsync<SqlException>();
        }

        ReportExport xlsx = Ok(await fx.Service.ExportSnapshotAsync(coordinator.Principal, fx.Context, sent.Id, "xlsx", _token));
        ReportExport pdf = Ok(await fx.Service.ExportSnapshotAsync(coordinator.Principal, fx.Context, sent.Id, "pdf", _token));
        SheetRow total = SpreadsheetReader.Read(xlsx.Content, new SpreadsheetLimits(), ["Özet"], out _).Single().Rows
            .Single(r => r.Cells["A"].Text == "Gerçekleşen işlem bildirimi (tümü)");
        total.Cells["B"].Number.Should().Be(1);
        ReportPdfWriter.ExtractLines(pdf.Content).Should().Contain(l => l.StartsWith("Gerçekleşen işlem bildirimi (tümü)", StringComparison.Ordinal)
            && l.TrimEnd().EndsWith("| 1", StringComparison.Ordinal));
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a AND Action = 'ServiceAccount.ReportExported'",
            new { a = coordinator.User.Id.ToString("D") })).Should().Be(2);
    }

    [ServiceAccountSqlFact]
    public async Task ReportFiltersAreBackendScoped_AndSnapshotsAreOnlyVisibleToCoveringScopes()
    {
        ServiceAccountSqlFixture fx = new();
        Guid mine = await fx.OrganizationAsync("SYN R1 " + fx.Suffix), theirs = await fx.OrganizationAsync("SYN R2 " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, mine);
        SynUser other = await fx.UserAsync(_all);
        await fx.GrantAsync(other, ScopeKind.Organization, theirs);
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_M1", null, mine, "s"), _token));
        Ok(await fx.Service.CreateAccountAsync(other.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_T1", null, theirs, "s"), _token));
        Ok(await fx.Service.CreateAccountAsync(other.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_T2", null, theirs, "s"), _token));

        ServiceAccountReport mineReport = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 14)), _token));
        mineReport.Summary.UniqueAccounts.Should().Be(1);
        (await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 14), OrganizationId: theirs), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, "a report filter cannot widen scope");
        SnapshotItem snapshot = Ok(await fx.Service.CreateSnapshotAsync(other.Principal, fx.Context, new CreateSnapshotRequest(new(2026, 9, 14), null, null, null, "Manager", null), _token));
        Ok(await fx.Service.SnapshotsAsync(coordinator.Principal, fx.Context, _token)).Should().NotContain(s => s.Id == snapshot.Id);
        (await fx.Service.ExportSnapshotAsync(coordinator.Principal, fx.Context, snapshot.Id, "pdf", _token)).ErrorCode.Should().Be(SaErrors.NotFound);

        SynUser noReport = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(noReport, ScopeKind.Organization, mine);
        (await fx.Service.WeeklyReportAsync(noReport.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 14)), _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    [ServiceAccountSqlFact]
    public async Task MonthlyReportAndSnapshot_KeepTheirPeriod_AndInvalidPeriodsAreRefused()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN AYLIK " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_AY", null, org, "sentetik"), _token));
        Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", ActualOn: new(2026, 9, 2), EvidenceNote: "ay başı"), _token));
        Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("Review", "Performed", "Intermediate", ActualOn: new(2026, 9, 25), EvidenceNote: "ay sonu"), _token));
        DateTimeOffset asOf = new(2026, 9, 27, 12, 0, 0, TimeSpan.FromHours(3));

        ServiceAccountReport month = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context,
            new WeeklyReportQuery(new(2026, 9, 17), asOf, Period: "Month"), _token));
        (month.WeekStart, month.WeekEndExclusive, month.Period).Should().Be((new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), "Month"));
        month.Weekly.Actions.InPeriod.Should().Be(2);
        ServiceAccountReport week = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 25), asOf), _token));
        week.Weekly.Actions.InPeriod.Should().Be(1, "the default stays the Monday–Sunday week");

        SnapshotItem snapshot = Ok(await fx.Service.CreateSnapshotAsync(coordinator.Principal, fx.Context,
            new CreateSnapshotRequest(month.WeekStart, month.AsOf, null, null, "Manager", "Aylık yönetici nüshası", "Month"), _token));
        (snapshot.PeriodStart, snapshot.PeriodEnd).Should().Be((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        Ok(await fx.Service.SnapshotAsync(coordinator.Principal, fx.Context, snapshot.Id, _token)).Should().BeEquivalentTo(month);

        (await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 9, 17), asOf, Period: "Custom"), _token))
            .Field.Should().Be("period", "a custom range needs an end date");
        (await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context,
            new WeeklyReportQuery(new(2025, 1, 1), asOf, Period: "Custom", PeriodEnd: new(2026, 9, 1)), _token)).Field.Should().Be("period");
    }

    [ServiceAccountSqlFact]
    public async Task AccountListExport_IsScopedFilteredAndAudited()
    {
        ServiceAccountSqlFixture fx = new();
        Guid mine = await fx.OrganizationAsync("SYN EXP A " + fx.Suffix), theirs = await fx.OrganizationAsync("SYN EXP B " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, mine);
        SynUser other = await fx.UserAsync(_all);
        await fx.GrantAsync(other, ScopeKind.Organization, theirs);
        AccountDetail first = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_E1", null, mine, "s"), _token));
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_E2", null, mine, "s"), _token));
        Ok(await fx.Service.CreateAccountAsync(other.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_X1", null, theirs, "s"), _token));
        Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, first.Summary.Id, new CreateWorkRequest("Review"), _token));

        ReportExport all = Ok(await fx.Service.ExportAccountsAsync(coordinator.Principal, fx.Context, new AccountListQuery(), _token));
        SheetRow[] rows = [.. SpreadsheetReader.Read(all.Content, new SpreadsheetLimits(), ["Hesaplar"], out _).Single().Rows];
        rows.Select(r => r.Cells["A"].Text).Should().Contain([$"SYN{fx.Suffix}_E1", $"SYN{fx.Suffix}_E2"])
            .And.NotContain($"SYN{fx.Suffix}_X1", "another organization's account never appears in an export");
        ReportExport open = Ok(await fx.Service.ExportAccountsAsync(coordinator.Principal, fx.Context, new AccountListQuery(Status: "open"), _token));
        SpreadsheetReader.Read(open.Content, new SpreadsheetLimits(), ["Hesaplar"], out _).Single().Rows
            .Count(r => r.Cells["A"].Text?.StartsWith("SYN" + fx.Suffix, StringComparison.Ordinal) == true).Should().Be(1, "the export uses the same filters as the list");
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a AND Action = 'ServiceAccount.AccountsExported'",
            new { a = coordinator.User.Id.ToString("D") })).Should().Be(2);

        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(viewer, ScopeKind.Organization, mine);
        (await fx.Service.ExportAccountsAsync(viewer.Principal, fx.Context, new AccountListQuery(), _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
