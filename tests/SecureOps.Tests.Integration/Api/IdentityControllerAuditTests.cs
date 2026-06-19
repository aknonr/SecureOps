using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Api.Controllers;
using SecureOps.Api.Validation;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityControllerAuditTests
{
    [Fact]
    public async Task LookupAsync_WhenValidationFails_WritesRejectedAuditAndDoesNotCallService()
    {
        InMemoryAuditWriter audit = new();
        CountingIdentityLookupService service = new();
        IdentityController controller = CreateController(service, audit);

        ActionResult<IdentityLookupResponse> result = await controller.LookupAsync(
            new IdentityLookupRequest("", ""),
            CancellationToken.None);

        ObjectResult objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        objectResult.Value.Should().BeOfType<ApiErrorResponse>()
            .Which.ErrorCode.Should().Be("PurposeRequired");
        service.Calls.Should().Be(0);
        audit.Events.Should().ContainSingle(x => x.Action == AuditActions.IdentityLookupRejected);
    }

    [Fact]
    public async Task LookupAsync_WhenValidationAuditUnavailable_ReturnsServiceUnavailableAndDoesNotCallService()
    {
        CountingIdentityLookupService service = new();
        IdentityController controller = CreateController(service, new ThrowingAuditWriter());

        ActionResult<IdentityLookupResponse> result = await controller.LookupAsync(
            new IdentityLookupRequest("", ""),
            CancellationToken.None);

        ObjectResult objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        service.Calls.Should().Be(0);
    }

    private static IdentityController CreateController(
        IIdentityLookupService service,
        IAuditWriter auditWriter)
    {
        DefaultHttpContext httpContext = new();
        httpContext.TraceIdentifier = "trace-controller-test";
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.4.22.18");

        return new IdentityController(
            service,
            new IdentityLookupRequestValidator(),
            auditWriter,
            Options.Create(new IdentityLookupOptions()),
            NullLogger<IdentityController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private sealed class CountingIdentityLookupService : IIdentityLookupService
    {
        public int Calls { get; private set; }

        public Task<IdentityLookupResult> LookupAsync(
            IdentityLookupRequest request,
            IdentityLookupExecutionContext context,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new IdentityLookupResult(IdentityLookupResultStatus.NotFound, null, null, null));
        }
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Audit unavailable.");
        }
    }
}
