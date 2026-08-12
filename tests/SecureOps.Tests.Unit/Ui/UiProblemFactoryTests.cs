using FluentAssertions;
using MudBlazor;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Verifies backend failures reach the operator as actionable states rather than status codes, and
/// that no raw API text is passed through as user-facing copy.
/// </summary>
public sealed class UiProblemFactoryTests
{
    [Theory]
    [InlineData(OperationalErrorCodes.AccessPending, UiProblemKind.AccessPending)]
    [InlineData(OperationalErrorCodes.AccessDisabled, UiProblemKind.AccessDisabled)]
    [InlineData(OperationalErrorCodes.AccessDenied, UiProblemKind.Forbidden)]
    [InlineData(OperationalErrorCodes.IdentityNotFound, UiProblemKind.NotFound)]
    [InlineData(OperationalErrorCodes.InvalidIdentityInput, UiProblemKind.Validation)]
    [InlineData(OperationalErrorCodes.RateLimitExceeded, UiProblemKind.RateLimited)]
    [InlineData(OperationalErrorCodes.AuditStoreUnavailable, UiProblemKind.UpstreamUnavailable)]
    [InlineData(OperationalErrorCodes.OperationalRecordAlreadyClaimed, UiProblemKind.Conflict)]
    [InlineData(OperationalErrorCodes.OperationalRecordChanged, UiProblemKind.Conflict)]
    [InlineData(OperationalErrorCodes.JiraAlreadyCreated, UiProblemKind.Conflict)]
    [InlineData(OperationalErrorCodes.WorkflowAlreadyCompleted, UiProblemKind.Conflict)]
    public void FromResponse_ClassifiesKnownCodes(string code, UiProblemKind expected)
    {
        UiProblem problem = UiProblemFactory.FromResponse(409, new ProblemDetailsPayload { Code = code });

        problem.Kind.Should().Be(expected);
        problem.Code.Should().Be(code);
    }

    [Fact]
    public void FromResponse_ProducesTurkishGuidanceForEveryKnownCode()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            new ProblemDetailsPayload { Code = OperationalErrorCodes.OperationalRecordAlreadyClaimed });

        problem.Title.Should().NotBeNullOrWhiteSpace();
        problem.Explanation.Should().NotBeNullOrWhiteSpace();
        problem.NextSteps.Should().NotBeEmpty("an operator needs to know what to do next");
    }

    [Fact]
    public void FromResponse_CarriesTheSupportReferenceThrough()
    {
        ProblemDetailsPayload payload = new()
        {
            Code = OperationalErrorCodes.JiraCreateFailed,
            CorrelationId = "corr-123",
            Stage = "jira"
        };

        UiProblem problem = UiProblemFactory.FromResponse(502, payload);

        problem.CorrelationId.Should().Be("corr-123");
        problem.Stage.Should().Be("jira");
        problem.StatusCode.Should().Be(502);
    }

    [Fact]
    public void FromResponse_FallsBackToTraceIdWhenCorrelationIdIsAbsent()
    {
        ProblemDetailsPayload payload = new() { Code = "Whatever", TraceId = "trace-9" };

        UiProblemFactory.FromResponse(500, payload).CorrelationId.Should().Be("trace-9");
    }

    [Fact]
    public void FromResponse_LetsTheApiRetryableFlagOverrideTheDefault()
    {
        // JiraAlreadyCreated defaults to non-retryable, but the server is the authority on whether a
        // durable command may be re-issued.
        ProblemDetailsPayload payload = new()
        {
            Code = OperationalErrorCodes.JiraAlreadyCreated,
            Retryable = true
        };

        UiProblemFactory.FromResponse(409, payload).Retryable.Should().BeTrue();
    }

    [Fact]
    public void FromResponse_MarksConflictStatesAsNeedingARefresh()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            409,
            new ProblemDetailsPayload { Code = OperationalErrorCodes.OperationalRecordChanged });

        problem.RequiresRefresh.Should().BeTrue(
            "the UI must reload authoritative state before letting the operator act again");
    }

    [Fact]
    public void FromResponse_ReadsTheLegacyApiErrorResponseShape()
    {
        // Endpoints not yet migrated to ProblemDetails still classify correctly.
        ProblemDetailsPayload payload = new()
        {
            ErrorCode = OperationalErrorCodes.IdentityProviderUnavailable,
            Message = "internal detail",
            CorrelationId = "legacy-1"
        };

        UiProblem problem = UiProblemFactory.FromResponse(503, payload);

        problem.Kind.Should().Be(UiProblemKind.UpstreamUnavailable);
        problem.CorrelationId.Should().Be("legacy-1");
        problem.Explanation.Should().NotContain("internal detail", "raw API text is never shown");
    }

    [Fact]
    public void FromResponse_DegradesUnknownCodesToASafeStatusClassification()
    {
        UiProblem problem = UiProblemFactory.FromResponse(
            503,
            new ProblemDetailsPayload { Code = "SomeFutureBackendCode" });

        problem.Kind.Should().Be(UiProblemKind.UpstreamUnavailable);
        problem.Code.Should().Be("SomeFutureBackendCode", "the real code stays quotable for support");
        problem.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(401, UiProblemKind.SessionExpired)]
    [InlineData(403, UiProblemKind.Forbidden)]
    [InlineData(404, UiProblemKind.NotFound)]
    [InlineData(400, UiProblemKind.Validation)]
    [InlineData(409, UiProblemKind.Conflict)]
    [InlineData(429, UiProblemKind.RateLimited)]
    [InlineData(500, UiProblemKind.UpstreamUnavailable)]
    public void FromResponse_ClassifiesByStatusWhenThereIsNoBody(int status, UiProblemKind expected)
    {
        UiProblemFactory.FromResponse(status, payload: null).Kind.Should().Be(expected);
    }

    [Fact]
    public void SessionExpiry_IsTheOnlyKindThatSendsTheOperatorBackToSignIn()
    {
        UiProblemFactory.FromResponse(401, null).RequiresSignIn.Should().BeTrue();
        UiProblemFactory.FromResponse(403, null).RequiresSignIn.Should().BeFalse();
        UiProblemFactory.NetworkFailure().RequiresSignIn.Should().BeFalse();
    }

    [Fact]
    public void PendingAccess_IsInformational_NotAnError()
    {
        // Waiting for approval is a normal state; styling it as an error trains operators to ignore
        // real errors.
        UiProblem problem = UiProblemFactory.FromResponse(
            403,
            new ProblemDetailsPayload { Code = OperationalErrorCodes.AccessPending });

        problem.Severity.Should().Be(Severity.Info);
    }

    [Fact]
    public void TransportFailures_AreRetryableAndCarryNoReference()
    {
        UiProblem network = UiProblemFactory.NetworkFailure();
        UiProblem timeout = UiProblemFactory.TimedOut();

        network.Kind.Should().Be(UiProblemKind.Network);
        network.Retryable.Should().BeTrue();
        network.CorrelationId.Should().BeNull("no response was received to carry one");

        timeout.Kind.Should().Be(UiProblemKind.Timeout);
        timeout.Retryable.Should().BeTrue();
        timeout.RequiresRefresh.Should().BeTrue("the request may have completed server-side");
    }
}
