using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

public sealed class ServiceAccountUsageRuleTests
{
    private static readonly Guid _account = Guid.NewGuid();

    private static UsageFact Usage(UsageKind kind, DatabaseEngine? engine = null, bool? verified = null, bool excepted = false) =>
        new(Guid.NewGuid(), _account, kind, engine, verified, excepted);

    private static AccountWorkState Work(params ServiceAccountActionType[] open) =>
        AccountWorkState.None with { OpenRequestTypes = open.ToHashSet() };

    [Theory]
    [InlineData(UsageKind.FileShare, null, RecommendedPath.SolutionTeamHandover, "KB-DOSYA")]
    [InlineData(UsageKind.ScheduledTask, null, RecommendedPath.SeekAlternative, "KB-GOREV")]
    [InlineData(UsageKind.UncApplication, null, RecommendedPath.MoveToLocalDisk, "KB-UNC")]
    [InlineData(UsageKind.IisVirtualDirectory, null, RecommendedPath.RemoveVirtualDirectoryDependency, "KB-IIS-SANAL")]
    [InlineData(UsageKind.IisAppPool, null, RecommendedPath.ManualDecision, "KB-YOK")]
    [InlineData(UsageKind.Database, DatabaseEngine.SqlServer, RecommendedPath.GmsaEvaluation, "KB-VT-SQL")]
    [InlineData(UsageKind.Database, DatabaseEngine.Oracle, RecommendedPath.ManualDecision, "KB-VT-ORACLE")]
    [InlineData(UsageKind.Database, DatabaseEngine.Unknown, RecommendedPath.NeedsInformation, "KB-VT-MOTOR")]
    [InlineData(UsageKind.WindowsService, null, RecommendedPath.VerifyNeed, "KB-SERVIS")]
    public void EachKnowledgeBaseRule_ExplainsItsPath(UsageKind kind, DatabaseEngine? engine, RecommendedPath path, string code)
    {
        AccountRuleEvaluation result = ServiceAccountUsageRules.Evaluate(_account, [Usage(kind, engine)], false, "SYN EXEC", AccountWorkState.None);

        result.Path.Should().Be(path);
        result.Items.Should().ContainSingle().Which.RuleCode.Should().Be(code);
        result.Items[0].Reason.Should().NotBeNullOrWhiteSpace();
        result.Conformance.Should().Be(path switch
        {
            RecommendedPath.NeedsInformation => RuleConformance.IncompleteInformation,
            RecommendedPath.ManualDecision => RuleConformance.ManualReviewPending,
            _ => RuleConformance.Unplanned
        });
    }

    [Fact]
    public void SqlServerUsage_NamesTheConfiguredExecutorTeam_AndIsNeverSuitability()
    {
        AccountRuleEvaluation result = ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.Database, DatabaseEngine.SqlServer)], false, "SYN EXEC",
            AccountWorkState.None);

        result.Items[0].Reason.Should().Contain("SYN EXEC").And.Contain("öneri uygunluk değildir");
        ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.Database, DatabaseEngine.SqlServer)], false, null, AccountWorkState.None)
            .Items[0].Reason.Should().Contain("ayar bekleniyor");
    }

    [Fact]
    public void Oracle_IsAnUnverifiedManualReview_NeverAnEstablishedRemoval()
    {
        AccountRuleEvaluation result = ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.Database, DatabaseEngine.Oracle)], false, "SYN EXEC",
            AccountWorkState.None);

        result.Path.Should().Be(RecommendedPath.ManualDecision);
        result.Conformance.Should().Be(RuleConformance.ManualReviewPending);
        result.Items[0].Reason.Should().Contain("onaylı bir iş kuralı yok").And.Contain("Kaldırma veya silme önerilmez");
        result.Items.Should().NotContain(i => i.Path == RecommendedPath.NoServiceAccountNeeded);
        ServiceAccountUsageRules.Conformance(RecommendedPath.ManualDecision, Work(ServiceAccountActionType.Review)).Should().Be(RuleConformance.Planned);
        ServiceAccountUsageRules.Conformance(RecommendedPath.ManualDecision, Work(ServiceAccountActionType.Deletion))
            .Should().Be(RuleConformance.ManualReviewPending, "a deletion request does not answer the review");
        ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.Database, DatabaseEngine.Oracle), Usage(UsageKind.FileShare)], false, null,
            AccountWorkState.None).Path.Should().Be(RecommendedPath.SolutionTeamHandover, "Oracle adds no handover target, so no split");
    }

    [Fact]
    public void SqlTeamAccount_IsGmsaEvaluation_EvenWithoutUsage()
    {
        AccountRuleEvaluation result = ServiceAccountUsageRules.Evaluate(_account, [], true, "SYN EXEC", AccountWorkState.None);

        result.Path.Should().Be(RecommendedPath.GmsaEvaluation);
        result.Items.Should().ContainSingle().Which.RuleCode.Should().Be("SQL-EKIP");
        result.Conformance.Should().Be(RuleConformance.Unplanned);
    }

    [Fact]
    public void NoUsage_IsNotAssessed_AndVerifiedServiceAloneNeedsItsResource()
    {
        ServiceAccountUsageRules.Evaluate(_account, [], false, null, AccountWorkState.None).Conformance.Should().Be(RuleConformance.NotAssessed);

        AccountRuleEvaluation verified = ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.WindowsService, verified: true)], false, null,
            AccountWorkState.None);
        verified.Path.Should().Be(RecommendedPath.NeedsInformation);
        verified.Items.Should().ContainSingle().Which.RuleCode.Should().Be("KB-SERVIS-KAYNAK");

        ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.WindowsService, verified: true), Usage(UsageKind.FileShare)], false, null,
            AccountWorkState.None).Path.Should().Be(RecommendedPath.SolutionTeamHandover);
    }

    [Fact]
    public void DifferentTargetTeams_RecommendSplitting_ButAnExceptionRemovesAUsageFromTheDecision()
    {
        AccountRuleEvaluation split = ServiceAccountUsageRules.Evaluate(_account,
            [Usage(UsageKind.Database, DatabaseEngine.SqlServer), Usage(UsageKind.FileShare)], false, "SYN EXEC", AccountWorkState.None);
        split.Path.Should().Be(RecommendedPath.SplitAccount);
        split.Items.Should().Contain(i => i.RuleCode == "KB-BOLUNME");

        AccountRuleEvaluation excepted = ServiceAccountUsageRules.Evaluate(_account,
            [Usage(UsageKind.Database, DatabaseEngine.SqlServer), Usage(UsageKind.FileShare, excepted: true)], false, "SYN EXEC", AccountWorkState.None);
        excepted.Path.Should().Be(RecommendedPath.GmsaEvaluation);
        excepted.Items.Should().Contain(i => i.RuleCode == "KB-DOSYA" && i.Excepted);
    }

    [Fact]
    public void AllItemsExcepted_IsAReasonedException_NotAViolation()
    {
        ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.ScheduledTask, excepted: true)], false, null, AccountWorkState.None)
            .Conformance.Should().Be(RuleConformance.Exception);
    }

    [Fact]
    public void Preconditions_TakePrecedence_OverHandoverTargets()
    {
        ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.WindowsService), Usage(UsageKind.Database, DatabaseEngine.SqlServer)], false, null,
            AccountWorkState.None).Path.Should().Be(RecommendedPath.VerifyNeed);
        ServiceAccountUsageRules.Evaluate(_account, [Usage(UsageKind.UncApplication), Usage(UsageKind.Database, DatabaseEngine.SqlServer)], false, null,
            AccountWorkState.None).Path.Should().Be(RecommendedPath.GmsaEvaluation);
    }

    [Fact]
    public void Conformance_FollowsExistingWork_WithoutInventingCompletion()
    {
        ServiceAccountUsageRules.Conformance(RecommendedPath.GmsaEvaluation, Work(ServiceAccountActionType.GmsaHandover)).Should().Be(RuleConformance.Planned);
        ServiceAccountUsageRules.Conformance(RecommendedPath.GmsaEvaluation, AccountWorkState.None with { HasGmsaTransition = true })
            .Should().Be(RuleConformance.Planned);
        ServiceAccountUsageRules.Conformance(RecommendedPath.GmsaEvaluation, AccountWorkState.None with { GmsaConverted = true })
            .Should().Be(RuleConformance.Planned, "a reported conversion is not verified");
        ServiceAccountUsageRules.Conformance(RecommendedPath.GmsaEvaluation, AccountWorkState.None with { GmsaVerifiedClosure = true })
            .Should().Be(RuleConformance.Completed);
        ServiceAccountUsageRules.Conformance(RecommendedPath.NoServiceAccountNeeded, Work(ServiceAccountActionType.Deletion)).Should().Be(RuleConformance.Planned);
        ServiceAccountUsageRules.Conformance(RecommendedPath.NoServiceAccountNeeded, Work(ServiceAccountActionType.PasswordChange))
            .Should().Be(RuleConformance.Unplanned, "a password change is not the expected work");
        ServiceAccountUsageRules.Conformance(RecommendedPath.SolutionTeamHandover, AccountWorkState.None with { HandoverProposed = true })
            .Should().Be(RuleConformance.Planned);
        ServiceAccountUsageRules.Conformance(RecommendedPath.SolutionTeamHandover, AccountWorkState.None with { HandoverAccepted = true })
            .Should().Be(RuleConformance.Completed);
        ServiceAccountUsageRules.Conformance(RecommendedPath.SeekAlternative, Work(ServiceAccountActionType.Review)).Should().Be(RuleConformance.Planned);
        ServiceAccountUsageRules.Conformance(RecommendedPath.SeekAlternative, AccountWorkState.None).Should().Be(RuleConformance.Unplanned);
    }
}
