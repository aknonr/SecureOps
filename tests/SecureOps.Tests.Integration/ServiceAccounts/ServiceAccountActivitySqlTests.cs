using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Infrastructure.ServiceAccounts.Reporting;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>The report's first page against real tables: synthetic work in, the expected activity page out (no migration involved).</summary>
public sealed class ServiceAccountActivitySqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];

    [ServiceAccountSqlFact]
    public async Task ActivityPage_ListsDeletedHandedGmsaChangedAndPending_FromRealRecords_InTheReportAndBothExports()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN ACT ORG " + fx.Suffix), otherOrg = await fx.OrganizationAsync("SYN ACT BASKA " + fx.Suffix);
        Guid teamA = await fx.TeamAsync("SYN ACT EKIP A " + fx.Suffix, org), teamB = await fx.TeamAsync("SYN ACT EKIP B " + fx.Suffix, org);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        SynUser other = await fx.UserAsync(_all);
        await fx.GrantAsync(other, ScopeKind.Organization, otherOrg);
        DateOnly today = ReportCalendar.LocalDate(DateTimeOffset.UtcNow);
        string Name(string tag) => $"SYN{fx.Suffix}_{tag}";

        async Task<AccountDetail> NewAccountAsync(string tag, SynUser? owner = null, Guid? organization = null) => Ok(await fx.Service.CreateAccountAsync((owner ?? coordinator).Principal,
            fx.Context, new CreateAccountRequest(Name(tag), null, organization ?? org, "sentetik"), _token));
        async Task<AccountDetail> ReportAsync(AccountDetail account, string type, string result, string kind, int daysAgo, SynUser? by = null) => Ok(await fx.Service.ReportActionAsync(
            (by ?? coordinator).Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest(type, result, kind, ActualOn: today.AddDays(-daysAgo), PerformerTeamId: teamA, EvidenceNote: "sentetik kayıt"), _token));

        // Deleted: one verified closure (with OR), one only reported.
        AccountDetail delVerified = await ReportAsync(await NewAccountAsync("DELV"), "Deletion", "Performed", "Closure", 3);
        ActionView deletion = delVerified.Actions.Single();
        delVerified = Ok(await fx.Service.UpdateActionAsync(coordinator.Principal, fx.Context, deletion.Id,
            new UpdateActionRequest(deletion.Version, AddReferences: [new SaExternalRef("OR", "OR-SYN-" + fx.Suffix)]), _token));
        deletion = delVerified.Actions.Single();
        Ok(await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, deletion.Id,
            new VerifyActionRequest(deletion.Version, today.AddDays(-2), VerificationNote: "AD'de yok"), _token));
        await ReportAsync(await NewAccountAsync("DELR"), "Deletion", "Performed", "Intermediate", 2);
        // Moved to gMSA, a password change, and an older unverified report (before the 7-day period).
        await ReportAsync(await NewAccountAsync("GMSA"), "GmsaConversion", "Performed", "Intermediate", 1);
        await ReportAsync(await NewAccountAsync("PWD"), "PasswordChange", "Performed", "Intermediate", 4);
        await ReportAsync(await NewAccountAsync("OLD"), "Deletion", "Performed", "Intermediate", 40);
        // Another organization's work never appears.
        await ReportAsync(await NewAccountAsync("FOREIGN", other, otherOrg), "Deletion", "Performed", "Intermediate", 1, other);

        // Ownership moved from team A to team B in the app (a decision record), and an accepted handover.
        AccountDetail moved = await NewAccountAsync("OWN");
        moved = Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, moved.Summary.Id,
            new OwnershipChangeRequest(moved.Summary.Version, teamA, null, "Confirm", "ilk sahip"), _token));
        Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, moved.Summary.Id,
            new OwnershipChangeRequest(moved.Summary.Version, teamB, null, "Confirm", "devir kararı", EvidenceNote: "yazışma"), _token));
        AccountDetail handed = await NewAccountAsync("HAND");
        AccountDetail planned = await NewAccountAsync("PLAN");
        await using (SqlConnection connection = fx.Connection())
        {
            Guid user = coordinator.User.Id, plan = Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO svcacct.Handovers(Id, AccountId, SourceTeamId, TargetTeamId, Status, DecidedOn, DecidedBy, DecisionNote, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(NEWID(), @handed, @a, @b, 'Accepted', @on, @user, N'sentetik kabul', SYSUTCDATETIME(), @user, SYSUTCDATETIME(), @user);
                INSERT INTO svcacct.ChangePlans(Id, Kind, Title, Status, CurrentPreviewVersion, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
                VALUES(@plan, 'GmsaConversion', N'Sentetik tamamlanan plan', 'Completed', 1, @user, SYSUTCDATETIME(), @user, SYSUTCDATETIME());
                INSERT INTO svcacct.ChangePlanAccounts(PlanId, AccountId, Action, TargetGmsaName, ChangedBy, ChangedAt) VALUES(@plan, @planned, 'Added', N'gmsaSynAct', @user, SYSUTCDATETIME());
                INSERT INTO svcacct.ChangePlanEvents(PlanId, Event, FromStatus, ToStatus, Actor, At) VALUES(@plan, 'Completed', 'InProgress', 'Completed', @user, SYSUTCDATETIME());
                """, new { handed = handed.Summary.Id, planned = planned.Summary.Id, a = teamA, b = teamB, on = today.AddDays(-1), user, plan });
        }

        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        ServiceAccountReport report = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context,
            new WeeklyReportQuery(today.AddDays(-6), asOf, Period: ReportPeriods.Custom, PeriodEnd: today), _token));

        ServiceAccountActivity activity = report.Activity!;
        string[] Listed(string key) => [.. activity.Groups.Single(g => g.Key == key).Items.Select(i => i.Account)];
        Listed(ActivityGroupKeys.Deleted).Should().BeEquivalentTo(Name("DELV"), Name("DELR"));
        Listed(ActivityGroupKeys.Gmsa).Should().Equal(Name("GMSA"));
        Listed(ActivityGroupKeys.Handed).Should().BeEquivalentTo(Name("HAND"), Name("OWN"));
        activity.Groups.Single(g => g.Key == ActivityGroupKeys.Handed).Items.Select(i => i.Teams)
            .Should().OnlyContain(t => t == $"SYN ACT EKIP A {fx.Suffix} → SYN ACT EKIP B {fx.Suffix}");
        Listed(ActivityGroupKeys.Changed).Should().Contain([Name("PWD"), Name("PLAN"), Name("DELV")]);
        ActivityLine verified = activity.Groups.Single(g => g.Key == ActivityGroupKeys.Deleted).Items.Single(i => i.Account == Name("DELV"));
        verified.Status.Should().StartWith("Doğrulandı");
        verified.Evidence.Should().Be("Var · OR var");
        activity.Groups.Single(g => g.Key == ActivityGroupKeys.Deleted).Items.Single(i => i.Account == Name("DELR")).Status.Should().Be("Bildirildi · doğrulanmadı");
        Listed(ActivityGroupKeys.Pending).Should().BeEquivalentTo(Name("DELR"), Name("GMSA"), Name("PWD"), Name("OLD"));
        activity.Groups.SelectMany(g => g.Items).Select(i => i.Account).Should().NotContain(Name("FOREIGN"));
        activity.Cards.Should().HaveCount(5);
        activity.Cards.Single(c => c.Label == "Doğrulanmayı bekleyen").Value.Should().Be(4);

        // The saved copy and both exports carry the same first page.
        SnapshotItem snapshot = Ok(await fx.Service.CreateSnapshotAsync(coordinator.Principal, fx.Context,
            new CreateSnapshotRequest(report.WeekStart, report.AsOf, null, null, "Manager", "Sentetik", ReportPeriods.Custom, today), _token));
        Ok(await fx.Service.SnapshotAsync(coordinator.Principal, fx.Context, snapshot.Id, _token)).Activity.Should().BeEquivalentTo(activity);
        ReportExport xlsx = Ok(await fx.Service.ExportSnapshotAsync(coordinator.Principal, fx.Context, snapshot.Id, "xlsx", _token));
        ReportExport pdf = Ok(await fx.Service.ExportSnapshotAsync(coordinator.Principal, fx.Context, snapshot.Id, "pdf", _token));
        SheetData first = SpreadsheetReader.Read(xlsx.Content, new SpreadsheetLimits(), null, out _)[0];
        first.Name.Should().Be(ReportDocument.ActivityTitle);
        string[] cells = [.. first.Rows.SelectMany(r => r.Cells.Values).Select(c => c.Text ?? string.Empty)];
        cells.Should().Contain([Name("DELV"), Name("DELR"), Name("GMSA"), Name("HAND"), Name("OWN"), Name("PWD"), Name("PLAN"), Name("OLD")]);
        cells.Should().NotContain(Name("FOREIGN"));
        string pdfText = string.Join('\n', ReportPdfWriter.ExtractLines(pdf.Content));
        pdfText.Should().Contain("DÖNEM İÇİNDE YAPILAN İŞLEMLER").And.Contain(Name("DELV")).And.Contain(Name("OLD"));
        pdfText.IndexOf("DÖNEM İÇİNDE YAPILAN İŞLEMLER", StringComparison.Ordinal).Should().BeLessThan(pdfText.IndexOf("TANIMLAR VE NOTLAR", StringComparison.Ordinal));
    }

    [ServiceAccountSqlFact]
    public async Task WithoutAnyWork_TheActivityPageIsEmptyAndHonest()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN ACT BOS " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_BOS", null, org, "sentetik"), _token));
        DateOnly today = ReportCalendar.LocalDate(DateTimeOffset.UtcNow);

        ServiceAccountReport report = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context,
            new WeeklyReportQuery(today.AddDays(-29), null, Period: ReportPeriods.Custom, PeriodEnd: today), _token));

        report.Activity!.AccountsActed.Should().Be(0);
        report.Activity.Groups.Should().OnlyContain(g => g.Lines == 0 && g.Items.Count == 0);
        report.Activity.Cards.Single(c => c.Label == "Toplam hesap").Value.Should().Be(1);
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
