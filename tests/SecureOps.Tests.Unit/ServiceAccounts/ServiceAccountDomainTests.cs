using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountDomainTests
{
    private static readonly DateOnly _today = new(2026, 9, 19);

    [Theory]
    [InlineData("Sentetik İnci Işıl", "SENTETİK İNCİ IŞIL")]
    [InlineData("Örnek Kişi Bir", "ÖRNEK KİŞİ BİR")]
    [InlineData(" Irmak  Işık ", "IRMAK IŞIK")]
    public void LabelKey_TurkishCaseAndWhitespaceVariants_AreTheSameExactKey(string left, string right) =>
        ServiceAccountText.LabelKey(left).Should().Be(ServiceAccountText.LabelKey(right));

    [Fact]
    public void LabelKey_AccentFoldedAsciiVariant_IsOnlyACandidate()
    {
        ServiceAccountText.LabelKey("SENTETIK INCI").Should().NotBe(ServiceAccountText.LabelKey("Sentetik İnci"));
        ServiceAccountText.CandidateKey("SENTETIK INCI").Should().Be(ServiceAccountText.CandidateKey("Sentetik İnci"));
        ServiceAccountText.CandidateKey("Örnek Şağçı").Should().Be("ORNEK SAGCI");
    }

    [Fact]
    public void AccountKey_NeverCorrectsSpellingDifferences()
    {
        ServiceAccountText.AccountKey("SYN_CRMSVC").Should().NotBe(ServiceAccountText.AccountKey("SYN_CRMSSVC"));
        ServiceAccountText.AccountKey(" syn_app ").Should().Be("SYN_APP");
    }

    [Fact]
    public void IdentityKey_SameNameInTwoDomains_AreDifferentAccounts()
    {
        string a = ServiceAccountText.IdentityKey("SVC_APP", "CORP-A");
        string b = ServiceAccountText.IdentityKey("SVC_APP", "CORP-B");
        string unknown = ServiceAccountText.IdentityKey("SVC_APP", null);
        new[] { a, b, unknown }.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Belirlenecek")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("belirlenecek")]
    public void Placeholder_IsNeverATeamOrPerson(string? label) => ServiceAccountText.IsPlaceholder(label).Should().BeTrue();

    [Fact]
    public void Verification_OnSameAction_KeepsPerformedCountAndRequiresEvidenceVerifierOrder()
    {
        ActionFacts performed = Facts(ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate, new(2026, 9, 10));
        ServiceAccountRules.IsPerformedReport(performed).Should().BeTrue();
        ServiceAccountRules.ValidateVerification(performed, new(2026, 9, 9), true, true, _today).Should().Be(ServiceAccountRules.Errors.VerificationBeforeAction);
        ServiceAccountRules.ValidateVerification(performed, new(2026, 9, 11), false, true, _today).Should().Be(ServiceAccountRules.Errors.VerifierRequired);
        ServiceAccountRules.ValidateVerification(performed, new(2026, 9, 11), true, false, _today).Should().Be(ServiceAccountRules.Errors.EvidenceRequired);
        ServiceAccountRules.ValidateVerification(performed, new(2026, 9, 11), true, true, _today).Should().BeNull();
        ActionFacts verified = performed with { Result = ServiceAccountActionResult.Verified, VerifiedOn = new(2026, 9, 11), HasVerifier = true, HasEvidence = true };
        new[] { verified }.Count(ServiceAccountRules.IsPerformedReport).Should().Be(1);
        ServiceAccountRules.IsVerifiedClosure(verified).Should().BeFalse("an intermediate step is never a closure");
    }

    [Fact]
    public void DeletionClosure_WithoutOr_IsNotVerifiedClosure()
    {
        ActionFacts deletion = Facts(ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Closure, new(2026, 9, 10)) with { ActionType = ServiceAccountActionType.Deletion };
        ServiceAccountRules.ValidateVerification(deletion, new(2026, 9, 12), true, true, _today).Should().Be(ServiceAccountRules.Errors.OrRequiredForDeletion);
        ActionFacts closed = deletion with { Result = ServiceAccountActionResult.Verified, VerifiedOn = new(2026, 9, 12), HasVerifier = true, HasEvidence = true };
        ServiceAccountRules.IsVerifiedClosure(closed).Should().BeFalse();
        ServiceAccountRules.IsVerifiedClosure(closed with { HasOrReference = true }).Should().BeTrue();
        ServiceAccountRules.IsVerifiedClosure(closed with { HasOrReference = true, Voided = true }).Should().BeFalse();
    }

    [Fact]
    public void ClosureRecordKind_AloneIsNotClosure_AndPlannedIsNotPerformed()
    {
        ActionFacts planned = Facts(ServiceAccountActionResult.Planned, ServiceAccountRecordKind.Closure, null);
        ServiceAccountRules.IsPerformedReport(planned).Should().BeFalse();
        ServiceAccountRules.IsVerifiedClosure(planned).Should().BeFalse();
        ServiceAccountRules.ValidateVerification(planned, _today, true, true, _today).Should().Be(ServiceAccountRules.Errors.NotPerformed);
    }

    [Fact]
    public void CloseRequest_IsExplicitAndValidatedAgainstItsOwnActions()
    {
        ActionFacts password = Facts(ServiceAccountActionResult.Performed, ServiceAccountRecordKind.Intermediate, new(2026, 9, 1));
        ServiceAccountRules.ValidateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.PasswordChange,
            ServiceAccountCloseOutcome.Completed, null, []).Should().Be(ServiceAccountRules.Errors.NoMatchingAction);
        ServiceAccountRules.ValidateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.PasswordChange,
            ServiceAccountCloseOutcome.Completed, null, [password]).Should().BeNull();
        ServiceAccountRules.ValidateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.Deletion,
            ServiceAccountCloseOutcome.Completed, null, [password with { ActionType = ServiceAccountActionType.Deletion }])
            .Should().Be(ServiceAccountRules.Errors.VerifiedClosureRequired);
        ServiceAccountRules.ValidateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.Deletion,
            ServiceAccountCloseOutcome.NotNeeded, " ", []).Should().Be(ServiceAccountRules.Errors.ReasonRequired);
        ServiceAccountRules.ValidateClose(ServiceAccountRequestStatus.Closed, ServiceAccountActionType.Deletion,
            ServiceAccountCloseOutcome.NotNeeded, "x", []).Should().Be(ServiceAccountRules.Errors.AlreadyClosed);
        ServiceAccountRules.ValidateClose(ServiceAccountRequestStatus.Open, ServiceAccountActionType.OwnershipConfirmation,
            ServiceAccountCloseOutcome.Completed, null, [password]).Should().Be(ServiceAccountRules.Errors.OwnershipNotConfirmed);
    }

    [Fact]
    public void WeekBoundaries_UseIstanbulMondayToMondayAndCutoff()
    {
        DateOnly monday = new(2026, 9, 14);
        DateTimeOffset asOf = new(2026, 9, 19, 23, 59, 59, TimeSpan.FromHours(3));
        ReportCalendar.WeekStart(new DateOnly(2026, 9, 20)).Should().Be(monday, "Sunday belongs to the previous Monday week");
        Place(new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.FromHours(3)), monday, asOf).Should().Be(WeekPlacement.InPeriod);
        Place(new DateTimeOffset(2026, 9, 13, 20, 59, 59, TimeSpan.Zero), monday, asOf).Should().Be(WeekPlacement.Earlier, "Sunday 23:59:59 Istanbul");
        Place(new DateTimeOffset(2026, 9, 13, 21, 0, 0, TimeSpan.Zero), monday, asOf).Should().Be(WeekPlacement.InPeriod, "Monday 00:00 Istanbul");
        Place(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.FromHours(3)), monday, asOf).Should().Be(WeekPlacement.AfterCutoff);
        ReportCalendar.Place(TimePrecision.DateOnly, new DateOnly(2026, 9, 21), null, monday, new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(3)))
            .Should().Be(WeekPlacement.LaterBeforeCutoff);
        ReportCalendar.Place(TimePrecision.Unknown, null, null, monday, asOf).Should().Be(WeekPlacement.UnknownDate);
    }

    [Fact]
    public void Scope_TeamGrantSeesOwnAndIncomingWork_OnlyOrganizationGrantHasOrganizationAuthority()
    {
        Guid org = Guid.NewGuid(), child = Guid.NewGuid(), teamA = Guid.NewGuid(), teamB = Guid.NewGuid(), teamUnderChild = Guid.NewGuid();
        OrganizationNode[] tree = [new(org, null), new(child, org)];
        TeamNode[] teams = [new(teamA, null), new(teamB, null), new(teamUnderChild, child)];
        var team = ServiceAccountScope.Resolve([new(ScopeKind.Team, null, teamA)], tree, teams);
        team.Covers(Anchor(null, teamA)).Should().BeTrue();
        team.Covers(Anchor(null, teamB)).Should().BeFalse();
        team.Covers(Anchor(null, teamB) with { OpenRequestTeamIds = [teamA] }).Should().BeTrue();
        team.Covers(Anchor(null, teamB) with { IncomingHandoverTeamIds = [teamA] }).Should().BeTrue();
        team.CoversAtOrganizationLevel(Anchor(org, teamA)).Should().BeFalse();

        var organization = ServiceAccountScope.Resolve([new(ScopeKind.Organization, org, null)], tree, teams);
        organization.Covers(Anchor(child, null)).Should().BeTrue("descendant organizations are included");
        organization.CoversTeam(teamUnderChild).Should().BeTrue();
        organization.CoversTeam(teamB).Should().BeFalse();
        ServiceAccountScope.Resolve([], tree, teams).IsEmpty.Should().BeTrue();
        ServiceAccountScope.None.Covers(Anchor(org, teamA)).Should().BeFalse();
    }

    private static WeekPlacement Place(DateTimeOffset at, DateOnly monday, DateTimeOffset asOf) =>
        ReportCalendar.Place(TimePrecision.Instant, ReportCalendar.LocalDate(at), at, monday, asOf);

    private static AccountScopeAnchor Anchor(Guid? org, Guid? owner) => new(org, owner, [], []);

    private static ActionFacts Facts(ServiceAccountActionResult result, ServiceAccountRecordKind kind, DateOnly? actualOn) =>
        new(ServiceAccountActionType.PasswordChange, result, kind, actualOn, null, false, false, false, false);
}
