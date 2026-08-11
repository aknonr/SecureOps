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
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Integration.Api;

public sealed class BulkIdentityLookupTests
{
    [Fact]
    public async Task BulkLookupAsync_NormalizesDeduplicatesAndPreservesStatusOrder()
    {
        InMemoryAuditWriter audit = new();
        IdentityController controller = CreateController(new StubLookupService(), audit);
        IdentityAccountNormalizer normalizer = new(Options.Create(new IdentityLookupOptions()));

        ActionResult<BulkIdentityLookupResponse> action = await controller.BulkLookupAsync(
            new BulkIdentityLookupRequest(["found", "FOUND", "missing", "bad*", "failed"], "Incident response verification"),
            normalizer,
            CancellationToken.None);

        BulkIdentityLookupResponse response = action.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<BulkIdentityLookupResponse>().Subject;
        response.Results.Select(result => result.Status).Should().Equal("Found", "NotFound", "Rejected", "ProviderError");
        response.Results.Select(result => result.Account).Should().Equal("found", "missing", "bad*", "failed");
        audit.Events.Select(eventItem => eventItem.Action).Should().Contain(new[] { AuditActions.BulkIdentityLookupRequested, AuditActions.BulkIdentityLookupCompleted });
    }

    private static IdentityController CreateController(IIdentityLookupService service, IAuditWriter audit)
    {
        DefaultHttpContext context = new();
        context.TraceIdentifier = "bulk-test";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.10");
        return new IdentityController(service, new IdentityLookupRequestValidator(), audit, Options.Create(new IdentityLookupOptions()), NullLogger<IdentityController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class StubLookupService : IIdentityLookupService
    {
        public Task<IdentityLookupResult> LookupAsync(IdentityLookupRequest request, IdentityLookupExecutionContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Account switch
            {
                "found" => new IdentityLookupResult(IdentityLookupResultStatus.Found, new IdentityLookupResponse("Found", "found", "Mock", null), null, null),
                "missing" => new IdentityLookupResult(IdentityLookupResultStatus.NotFound, null, null, null),
                _ => new IdentityLookupResult(IdentityLookupResultStatus.Failed, null, "ProviderUnavailable", null)
            });
        }
    }
}
