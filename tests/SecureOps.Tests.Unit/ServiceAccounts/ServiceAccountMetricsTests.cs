using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountMetricsTests
{
    private static readonly DateOnly _monday = new(2026, 9, 14);
    private static readonly DateTimeOffset _asOf = new(2026, 9, 19, 23, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void OneMailLinkedToTenAccounts_CountsOnce_DraftsAndUndatedSeparate()
    {
        AccountFact[] accounts = [.. Enumerable.Range(1, 10).Select(i => Account($"SYN_{i:00}"))];
        CommunicationFact[] mails =
        [
            new(Guid.NewGuid(), CommunicationDirection.Incoming, CommunicationKind.Reply, TimePrecision.DateOnly, new(2026, 9, 17), null, 10),
            new(Guid.NewGuid(), CommunicationDirection.Incoming, CommunicationKind.Information, TimePrecision.Unknown, null, null, 0),
            new(Guid.NewGuid(), CommunicationDirection.Outgoing, CommunicationKind.Draft, TimePrecision.DateOnly, new(2026, 9, 16), null, 1)
        ];
        ServiceAccountReport report = Compute(new(accounts, [], [], mails, [], [], [], Teams()));
        report.Summary.ValidMails.Should().Be(1);
        report.Summary.UndatedMails.Should().Be(1);
        report.Summary.DraftMailsExcluded.Should().Be(1);
        report.Weekly.Mails.InPeriod.Should().Be(1);
        report.Weekly.Mails.UnknownDate.Should().Be(1);
        report.Weekly.Mails.Reconciles.Should().BeTrue();
        report.Weekly.IncomingMailsInPeriod.Should().Be(1);
    }

    [Fact]
    public void PerformedThenVerified_IsOneAction_AndClosureIsSeparateDistinctAccount()
    {
        AccountFact account = Account("SYN_DEL");
        ActionFacts verifiedClosure = new(ServiceAccountActionType.Deletion, ServiceAccountActionResult.Verified, ServiceAccountRecordKind.Closure,
            new(2026, 9, 15), new(2026, 9, 16), true, true, true, false);
        ActionFact[] actions =
        [
            new(Guid.NewGuid(), account.Id, verifiedClosure, TimePrecision.DateOnly, null, null),
            new(Guid.NewGuid(), account.Id, verifiedClosure with { ActionType = ServiceAccountActionType.PasswordChange,
                RecordKind = ServiceAccountRecordKind.Intermediate, Result = ServiceAccountActionResult.Performed, VerifiedOn = null }, TimePrecision.Unknown, null, null),
            new(Guid.NewGuid(), account.Id, verifiedClosure with { Result = ServiceAccountActionResult.Planned }, TimePrecision.DateOnly, null, null)
        ];
        ServiceAccountReport report = Compute(new([account], [], actions, [], [], [], [], Teams()));
        report.Summary.PerformedActionReports.Should().Be(2);
        report.Summary.VerifiedClosureAccounts.Should().Be(1);
        report.Weekly.Actions.InPeriod.Should().Be(1);
        report.Weekly.Actions.UnknownDate.Should().Be(1, "undated reports are counted separately and never guessed into a week");
        report.Weekly.Actions.Reconciles.Should().BeTrue();
        report.Weekly.VerifiedClosures.InPeriod.Should().Be(1);
    }

    [Fact]
    public void LateHistoricalAction_GoesToItsOwnWeek_AndAfterCutoffIsExcluded()
    {
        AccountFact account = Account("SYN_LATE");
        ActionFacts performed = new(ServiceAccountActionType.PasswordChange, ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate,
            new(2026, 9, 2), null, false, false, false, false);
        ActionFact[] actions =
        [
            new(Guid.NewGuid(), account.Id, performed, TimePrecision.DateOnly, null, null),
            new(Guid.NewGuid(), account.Id, performed with { ActualOn = new(2026, 9, 20) }, TimePrecision.DateOnly, null, null)
        ];
        ServiceAccountReport report = Compute(new([account], [], actions, [], [], [], [], Teams()));
        report.Weekly.Actions.Earlier.Should().Be(1);
        report.Weekly.Actions.AfterCutoff.Should().Be(1);
        report.Weekly.Actions.InPeriod.Should().Be(0);
        ServiceAccountMetrics.Compute(new([account], [], actions, [], [], [], [], Teams()), new(2026, 9, 3), _asOf, "Kapsam")
            .Weekly.Actions.InPeriod.Should().Be(1, "the live report for that week includes the late-entered action");
    }

    [Fact]
    public void OwnershipMetrics_UseConfirmedPersonOnly_LegacyFollowupFallbackIsLabelledSeparately()
    {
        Guid personA = Guid.NewGuid(), personB = Guid.NewGuid(), followup = Guid.NewGuid(), proposed = Guid.NewGuid();
        AccountFact[] accounts =
        [
            Account("SYN_A") with { ConfirmedPersonId = personA },
            Account("SYN_B") with { ConfirmedPersonId = personA },
            Account("SYN_C") with { ConfirmedPersonId = personB },
            Account("SYN_D"),
            Account("SYN_E"),
            Account("SYN_F") with { NamedPersonId = proposed }
        ];
        RequestFact[] requests =
        [
            Request(accounts[3].Id, followup, "0001"),
            Request(accounts[0].Id, followup, "0002"),
            Request(accounts[4].Id, null, "0003")
        ];
        ServiceAccountReport report = Compute(new(accounts, requests, [], [], [], [], [], Teams()));
        report.Summary.AccountsWithAssignedPerson.Should().Be(3, "a proposed person is not an assignment");
        report.Summary.UniqueResponsiblePersons.Should().Be(2);
        report.Legacy.NamedAccounts.Should().Be(4, "the legacy view counts the person named in the inputs, proposed or confirmed");
        report.Legacy.FollowupFallbackAccounts.Should().Be(1);
        report.Legacy.CombinedAccounts.Should().Be(5);
        report.Legacy.CombinedPeople.Should().Be(4);
        report.Legacy.Label.Should().Contain("sahip değildir");
    }

    [Fact]
    public void Plans_AwaitingDates_Overdue_Handover_AndFindingsDoNotCountAsWork()
    {
        AccountFact a = Account("SYN_PLAN"), b = Account("SYN_GMSA");
        RequestFact dated = Request(a.Id, null, "1") with { ActionType = ServiceAccountActionType.PasswordChange, PlanStart = new(2026, 9, 30), PlanEnd = new(2026, 9, 30) };
        RequestFact overdue = dated with { Id = Guid.NewGuid(), PlanStart = new(2026, 9, 1), PlanEnd = new(2026, 9, 10) };
        RequestFact undated = Request(b.Id, null, "2") with { ActionType = ServiceAccountActionType.GmsaHandover };
        RequestFact evaluate = dated with { Id = Guid.NewGuid(), ActionType = ServiceAccountActionType.Evaluate };
        ReportFacts facts = new([a, b], [dated, overdue, undated, evaluate], [], [], [new(b.Id, HandoverStatus.Proposed, false)],
            [new(b.Id, GmsaSuitability.Unknown, false)], [new(a.Id, FindingStatus.Open), new(a.Id, FindingStatus.Closed)], Teams());
        ServiceAccountReport report = Compute(facts);
        report.Summary.DatedOpenPlanRequests.Should().Be(2);
        report.Summary.DatedOpenPlanAccounts.Should().Be(1);
        report.Summary.AwaitingDateRequests.Should().Be(2);
        report.Summary.OverdueRequests.Should().Be(1);
        report.Summary.OpenFindings.Should().Be(1);
        report.Summary.PerformedActionReports.Should().Be(0);
        report.Handover.Reported.Should().Be(1);
        report.Handover.Accepted.Should().Be(0);
        report.Handover.GmsaPending.Should().Be(1);
        report.DatedPlans.Should().HaveCount(2).And.Contain(p => p.Overdue);
    }

    [Fact]
    public void MonthAndCustomPeriods_UseTheSameRules_AndReconcile()
    {
        AccountFact account = Account("SYN_PERIOD");
        ActionFacts performed = new(ServiceAccountActionType.PasswordChange, ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate,
            new(2026, 9, 2), null, false, false, false, false);
        ActionFact[] actions =
        [
            new(Guid.NewGuid(), account.Id, performed, TimePrecision.DateOnly, null, null),
            new(Guid.NewGuid(), account.Id, performed with { ActualOn = new(2026, 9, 18) }, TimePrecision.DateOnly, null, null),
            new(Guid.NewGuid(), account.Id, performed with { ActualOn = new(2026, 8, 31) }, TimePrecision.DateOnly, null, null)
        ];
        ReportFacts facts = new([account], [], actions, [], [], [], [], Teams());

        (DateOnly start, DateOnly end) = ReportPeriods.Resolve(ReportPeriods.Month, new(2026, 9, 17), null)!.Value;
        (start, end).Should().Be((new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)));
        ServiceAccountReport month = ServiceAccountMetrics.Compute(facts, start, end, _asOf, "Sentetik kapsam", ReportPeriods.Month);
        month.Period.Should().Be(ReportPeriods.Month);
        month.Weekly.Actions.InPeriod.Should().Be(2, "both September actions up to the cut-off fall in the month");
        month.Weekly.Actions.Earlier.Should().Be(1);
        month.Weekly.Actions.Reconciles.Should().BeTrue();

        (start, end) = ReportPeriods.Resolve(ReportPeriods.Custom, new(2026, 8, 31), new(2026, 9, 2))!.Value;
        ServiceAccountReport custom = ServiceAccountMetrics.Compute(facts, start, end, _asOf, "Sentetik kapsam", ReportPeriods.Custom);
        custom.Weekly.Actions.InPeriod.Should().Be(2, "the inclusive end date is part of the range");
        custom.Weekly.Actions.LaterBeforeCutoff.Should().Be(1);

        ReportPeriods.Resolve(ReportPeriods.Custom, new(2026, 9, 2), new(2026, 9, 1)).Should().BeNull("an end before the start is invalid");
        ReportPeriods.Resolve(ReportPeriods.Custom, new(2025, 1, 1), new(2026, 9, 1)).Should().BeNull("a range longer than 366 days is refused");
        ReportPeriods.Resolve(ReportPeriods.Week, new(2026, 9, 17), new(2026, 9, 18)).Should().BeNull("a week has no explicit end");
        ReportPeriods.Resolve("Quarter", new(2026, 9, 17), null).Should().BeNull();
        ReportPeriods.Resolve(null, new(2026, 9, 17), null)!.Value.Start.Should().Be(_monday);
    }

    private static ServiceAccountReport Compute(ReportFacts facts) => ServiceAccountMetrics.Compute(facts, _monday, _asOf, "Sentetik kapsam");

    private static AccountFact Account(string label) => new(Guid.NewGuid(), label, null, null);

    private static RequestFact Request(Guid account, Guid? followup, string order) =>
        new(Guid.NewGuid(), account, ServiceAccountActionType.Review, ServiceAccountRequestStatus.Open, null, null, null, followup, order);

    private static Dictionary<Guid, string> Teams() => [];
}
