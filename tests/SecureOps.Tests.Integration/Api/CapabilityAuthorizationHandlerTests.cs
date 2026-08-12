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
using SecureOps.Shared.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Api;

public sealed class CapabilityAuthorizationHandlerTests
{
    [Fact]
    public async Task FirstUnknownUser_BecomesPendingWithOneAccessRequest()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\new.user");

        AccessServiceResult<EnsureAccessUserResult> first = await fixture.Service.GetCurrentAsync(principal, Fixture.UserContext, CancellationToken.None);
        AccessServiceResult<EnsureAccessUserResult> second = await fixture.Service.GetCurrentAsync(principal, Fixture.UserContext, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value!.User.Status.Should().Be(AccessStatus.Pending);
        first.Value.UserCreated.Should().BeTrue();
        first.Value.RequestCreated.Should().BeTrue();
        second.Value!.UserCreated.Should().BeFalse();
        second.Value.RequestCreated.Should().BeFalse();
        second.Value.PendingRequest!.Id.Should().Be(first.Value.PendingRequest!.Id);
        (await fixture.Repository.ListRequestsAsync(AccessRequestStatus.Pending, CancellationToken.None)).Should().ContainSingle();
    }

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
    public async Task PendingUser_CannotUseProtectedOperationalCapability()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\pending.operator");

        AuthorizationHandlerContext authorization = await fixture.AuthorizeAsync(principal, Capabilities.OperationalRecordsView);

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
        result.Value.User.Capabilities.Should().Contain(Capabilities.OperationalRecordsView);
    }

    [Fact]
    public async Task ConfiguredBootstrapAdministrator_CanAccessAdministrationAndIsFullyAudited()
    {
        const string bootstrapIdentity = "CONTOSO\\bootstrap.admin";
        Fixture fixture = new([bootstrapIdentity]);
        ClaimsPrincipal principal = Principal(bootstrapIdentity);

        AuthorizationHandlerContext authorization = await fixture.AuthorizeAsync(principal, Capabilities.AccessManageUsers);
        ApplicationUser user = (await fixture.Repository.GetUserAsync(bootstrapIdentity, CancellationToken.None))!;

        authorization.HasSucceeded.Should().BeTrue();
        user.Status.Should().Be(AccessStatus.Approved);
        user.Roles.Should().ContainSingle("Admin");
        fixture.Audit.Events.Should().Contain(audit => audit.Action == AuditActions.AccessApproved && audit.Actor == "system:configured-bootstrap");
        fixture.Audit.Events.Should().Contain(audit => audit.Action == AuditActions.RoleAssigned && audit.Actor == "system:configured-bootstrap");
    }

    [Fact]
    public async Task ReplacingRolesWithTheSameSet_IsIdempotent()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\role.user");
        ApplicationUser user = await fixture.ApproveAsync(principal, "Operator");

        AccessServiceResult<AccessMutationResult> first = await fixture.Service.ReplaceRolesAsync(
            user.Id,
            ["Lead", "Operator"],
            "Updated for deterministic role testing.",
            Fixture.AdminContext,
            CancellationToken.None);
        AccessServiceResult<AccessMutationResult> second = await fixture.Service.ReplaceRolesAsync(
            user.Id,
            ["operator", "lead", "Lead"],
            "Repeated deterministic role update.",
            Fixture.AdminContext,
            CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value!.AddedRoles.Should().BeEmpty();
        second.Value.RemovedRoles.Should().BeEmpty();
        second.Value.User!.Roles.Should().BeEquivalentTo("Lead", "Operator");
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

        public Fixture(string[]? bootstrapAdministrators = null)
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-access-test" };
            _accessor = new HttpContextAccessor { HttpContext = HttpContext };
            IOptions<AccessOptions> options = Options.Create(new AccessOptions
            {
                DemoCompatibilityEnabled = false,
                BootstrapAdministrators = bootstrapAdministrators ?? []
            });
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
        public InMemoryAccessRepository Repository => _repository;
        public InMemoryAuditWriter Audit => _audit;

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
