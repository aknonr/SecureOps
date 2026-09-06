using FluentAssertions;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class SdmPublicationProblemTests
{
    [Theory]
    [InlineData(OperationalErrorCodes.JiraUnavailable)]
    [InlineData(OperationalErrorCodes.JiraValidationFailed)]
    [InlineData(OperationalErrorCodes.JiraCreateFailed)]
    [InlineData(OperationalErrorCodes.WorkflowAlreadyInProgress)]
    [InlineData(OperationalErrorCodes.WorkflowConflict)]
    public void ReconciliationStage_OverridesErrorCodeAndRetryFlag(string code)
    {
        UiProblem problem = UiProblemFactory.FromResponse(503, new ProblemDetailsPayload
        {
            Code = code, Stage = "jira-reconciliation", Retryable = true, CorrelationId = "synthetic-support"
        });
        problem.Title.Should().Be("Jira sonucu doğrulanmalı");
        problem.Retryable.Should().BeFalse();
        problem.CorrelationId.Should().Be("synthetic-support");
        problem.NextSteps.Should().NotContain(step => step.Contains("yeniden başlatılabilir", StringComparison.Ordinal));
    }

    [Fact]
    public void UnacknowledgedPublication_PreservesSupportReferenceAndBlocksRetry()
    {
        UiProblem original = UiProblemFactory.TimedOut() with { CorrelationId = "synthetic-support" };
        UiProblem result = UiProblemFactory.UncertainPublication(original);
        result.Retryable.Should().BeFalse();
        result.CorrelationId.Should().Be(original.CorrelationId);
        result.Stage.Should().Be("jira-reconciliation");
    }
}
