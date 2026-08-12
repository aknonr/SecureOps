using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Controllers;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Tests.Integration.Api;

public sealed class OperationalRecordsControllerTests
{
    [Fact]
    public async Task PreviewAsync_WhenRequesterIsAmbiguous_ReturnsSafeProblemDetails()
    {
        OperationalRecordsController controller = CreateController(
            OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.RequesterResolutionAmbiguous, "requester-resolution", false));

        ActionResult<JiraPreviewResponse> result = await controller.PreviewAsync(Guid.NewGuid(), CancellationToken.None);

        ObjectResult response = result.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        ProblemDetails problem = response.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(OperationalErrorCodes.RequesterResolutionAmbiguous);
        problem.Extensions["stage"].Should().Be("requester-resolution");
        problem.Extensions["retryable"].Should().Be(false);
        problem.Extensions["correlationId"].Should().Be("trace-operational-test");
        problem.Extensions.Should().NotContainKey("exception").And.NotContainKey("response");
    }

    private static OperationalRecordsController CreateController(OperationalRecordResult<JiraIssueDraft> previewResult)
    {
        DefaultHttpContext httpContext = new() { TraceIdentifier = "trace-operational-test" };
        return new OperationalRecordsController(new EmptyRecordService(), new StubTransferService(previewResult))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class EmptyRecordService : IOperationalRecordService
    {
        public Task<OperationalRecordResult<IReadOnlyList<OperationalRecord>>> ListAsync(OperationalRecordCommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OperationalRecordResult<IReadOnlyList<OperationalRecord>>.Success(Array.Empty<OperationalRecord>()));

        public Task<OperationalRecordResult<OperationalRecord>> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(OperationalRecordResult<OperationalRecord>.Fail(OperationalErrorCodes.OperationalRecordNotFound, "repository", false));
    }

    private sealed class StubTransferService(OperationalRecordResult<JiraIssueDraft> previewResult) : IJiraTransferService
    {
        public Task<OperationalRecordResult<JiraIssueDraft>> PreviewAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) => Task.FromResult(previewResult);
        public Task<OperationalRecordResult<OperationalRecord>> CreateAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationalRecordResult<OperationalRecord>> RetryAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
