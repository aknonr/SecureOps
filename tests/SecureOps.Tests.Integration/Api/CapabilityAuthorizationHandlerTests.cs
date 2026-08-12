using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Api.Security;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Api;

public sealed class CapabilityAuthorizationHandlerTests
{
    [Fact]
    public async Task PendingUser_IsDenied()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\pending.user");

        AuthorizationHandlerContext authorization = await fixture.AuthorizeAsync(principal, Capabilities.IdentityLookup);

        authorization.HasSucceeded.Should().BeFalse();
        fixture.HttpContext.Items[CapabilityAuthorizationHandler.DenialCodeItem].Should().Be(OperationalErrorCodes.AccessPending);
    }

    [Fact]
    public async Task ApprovedUser_WithCapability_IsAllowed()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\approved.user");
        await fixture.ApproveAsync(principal, "Lead");

        AuthorizationHandlerContext authorization = await fixture.AuthorizeAsync(principal, Capabilities.IdentityLookup);

        authorization.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task DisabledUser_IsDeniedImmediately()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\disabled.user");
        ApplicationUser user = await fixture.ApproveAsync(principal, "Lead");
        _ = await fixture.Service.DisableAsync(user.Id, "Approved access revocation test.", Fixture.AdminContext, CancellationToken.None);

        AuthorizationHandlerContext authorization = await fixture.AuthorizeAsync(principal, Capabilities.IdentityLookup);

        authorization.HasSucceeded.Should().BeFalse();
        fixture.HttpContext.Items[CapabilityAuthorizationHandler.DenialCodeItem].Should().Be(OperationalErrorCodes.AccessDisabled);
    }

    [Fact]
    public async Task Administrator_CanApprovePendingRequest()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\request.user");
        AccessServiceResult<EnsureAccessUserResult> current = await fixture.Service.GetCurrentAsync(principal, Fixture.UserContext, CancellationToken.None);

        AccessServiceResult<AccessMutationResult> result = await fixture.Service.ApproveAsync(
            current.Value!.PendingRequest!.Id,
            ["Operator"],
            "Approved for the bounded operations role.",
            Fixture.AdminContext,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.User!.Status.Should().Be(AccessStatus.Approved);
        result.Value.User.Roles.Should().ContainSingle("Operator");
    }

    [Fact]
    public async Task NonAdministrator_LacksApprovalCapability()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\lead.user");
        _ = await fixture.ApproveAsync(principal, "Lead");

        AuthorizationHandlerContext authorization = await fixture.AuthorizeAsync(principal, Capabilities.AccessApproveRequests);

        authorization.HasSucceeded.Should().BeFalse();
        fixture.HttpContext.Items[CapabilityAuthorizationHandler.DenialCodeItem].Should().Be(OperationalErrorCodes.AccessDenied);
    }

    private static ClaimsPrincipal Principal(string name) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, name)],
        "Negotiate",
        ClaimTypes.Name,
        ClaimTypes.Role));

    private sealed class Fixture
    {
        private readonly InMemoryAccessRepository _repository = new();
        private readonly InMemoryAuditWriter _audit = new();
        private readonly IHttpContextAccessor _accessor;

        public Fixture()
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-access-test" };
            _accessor = new HttpContextAccessor { HttpContext = HttpContext };
            IOptions<AccessOptions> options = Options.Create(new AccessOptions { DemoCompatibilityEnabled = false });
            Service = new ApplicationAccessService(
                new CorporatePrincipalResolver(options),
                _repository,
                _audit,
                options,
                NullLogger<ApplicationAccessService>.Instance);
        }

        public static AccessOperationContext AdminContext => new("CONTOSO\\approved.admin", "correlation-admin", null);
        public static AccessOperationContext UserContext => new("CONTOSO\\request.user", "correlation-user", null);
        public DefaultHttpContext HttpContext { get; }
        public ApplicationAccessService Service { get; }

        public async Task<ApplicationUser> ApproveAsync(ClaimsPrincipal principal, string role)
        {
            AccessServiceResult<EnsureAccessUserResult> current = await Service.GetCurrentAsync(principal, UserContext, CancellationToken.None);
            AccessServiceResult<AccessMutationResult> approved = await Service.ApproveAsync(
                current.Value!.PendingRequest!.Id,
                [role],
                "Approved for deterministic authorization testing.",
                AdminContext,
                CancellationToken.None);
            return approved.Value!.User!;
        }

        public async Task<AuthorizationHandlerContext> AuthorizeAsync(ClaimsPrincipal principal, string capability)
        {
            HttpContext.User = principal;
            CapabilityRequirement requirement = new(capability);
            AuthorizationHandlerContext context = new([requirement], principal, resource: null);
            CapabilityAuthorizationHandler handler = new(Service, _accessor);
            await handler.HandleAsync(context);
            return context;
        }
    }
}
