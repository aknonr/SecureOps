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
        _ = await fixture.Service.DisableAsync(user.Id, "Approved access revocation test.", user.Version, Fixture.AdminContext, CancellationToken.None);

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
            current.Value.PendingRequest.Version,
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
            user.Version,
            Fixture.AdminContext,
            CancellationToken.None);
        AccessServiceResult<AccessMutationResult> second = await fixture.Service.ReplaceRolesAsync(
            user.Id,
            ["operator", "lead", "Lead"],
            "Repeated deterministic role update.",
            first.Value!.User!.Version,
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

    [Fact]
    public async Task RejectedRequest_RemainsDurableWhenUserReturns()
    {
        Fixture fixture = new();
        ClaimsPrincipal principal = Principal("CONTOSO\\rejected.user");
        AccessServiceResult<EnsureAccessUserResult> first = await fixture.Service.GetCurrentAsync(principal, Fixture.UserContext, CancellationToken.None);

        AccessServiceResult<AccessMutationResult> rejected = await fixture.Service.RejectAsync(
            first.Value!.PendingRequest!.Id,
            "Rejected after access review.",
            first.Value.PendingRequest.Version,
            Fixture.AdminContext,
            CancellationToken.None);
        AccessServiceResult<EnsureAccessUserResult> returned = await fixture.Service.GetCurrentAsync(principal, Fixture.UserContext, CancellationToken.None);

        rejected.IsSuccess.Should().BeTrue();
        returned.Value!.User.Status.Should().Be(AccessStatus.Pending);
        returned.Value.PendingRequest.Should().BeNull();
        returned.Value.LatestRequest!.Status.Should().Be(AccessRequestStatus.Rejected);
        returned.Value.RequestCreated.Should().BeFalse();
        (await fixture.Repository.ListRequestsForUserAsync(returned.Value.User.Id, CancellationToken.None))
            .Should().ContainSingle(request => request.Status == AccessRequestStatus.Rejected);
        fixture.Audit.Events.Should().Contain(audit => audit.Action == AuditActions.AccessRejected);
    }

    [Fact]
    public async Task PendingSelfApproval_ReturnsDedicatedDenial()
    {
        const string identity = "CONTOSO\\self.admin";
        Fixture fixture = new();
        AccessServiceResult<EnsureAccessUserResult> current = await fixture.Service.GetCurrentAsync(Principal(identity), Fixture.UserContext, CancellationToken.None);

        AccessServiceResult<AccessMutationResult> result = await fixture.Service.ApproveAsync(
            current.Value!.PendingRequest!.Id,
            ["Admin"],
            "Attempted self approval.",
            current.Value.PendingRequest.Version,
            new AccessOperationContext(identity, "self-correlation", null),
            CancellationToken.None);

        result.ErrorCode.Should().Be(OperationalErrorCodes.AccessSelfApprovalDenied);
    }

    [Fact]
    public async Task Mutations_DistinguishValidationLifecycleAndConcurrency()
    {
        Fixture fixture = new();
        AccessServiceResult<EnsureAccessUserResult> current = await fixture.Service.GetCurrentAsync(Principal("CONTOSO\\conflict.user"), Fixture.UserContext, CancellationToken.None);
        ApplicationAccessRequest request = current.Value!.PendingRequest!;

        (await fixture.Service.ApproveAsync(request.Id, [], "Valid reason.", request.Version, Fixture.AdminContext, CancellationToken.None))
            .ErrorCode.Should().Be(OperationalErrorCodes.AccessValidationFailed);
        (await fixture.Service.ApproveAsync(request.Id, ["Operator"], "Valid reason.", request.Version + 1, Fixture.AdminContext, CancellationToken.None))
            .ErrorCode.Should().Be(OperationalErrorCodes.AccessConcurrencyConflict);
        _ = await fixture.Service.RejectAsync(request.Id, "Rejected once.", request.Version, Fixture.AdminContext, CancellationToken.None);
        (await fixture.Service.RejectAsync(request.Id, "Rejected twice.", request.Version, Fixture.AdminContext, CancellationToken.None))
            .ErrorCode.Should().Be(OperationalErrorCodes.AccessRequestAlreadyDecided);
    }

    [Fact]
    public async Task AdministrativeReadModel_ReturnsProviderProfileRolesCapabilitiesAndHistory()
    {
        const string identity = "CONTOSO\\profile.user";
        Fixture fixture = new(profileResolver: new FixedProfileResolver(identity));
        ApplicationUser approved = await fixture.ApproveAsync(Principal(identity), "Lead");

        AccessServiceResult<AccessUserReadModel> result = await fixture.Service.GetUserAsync(approved.Id, Fixture.AdminContext, CancellationToken.None);

        result.Value!.Profile!.DisplayName.Should().Be("Resolved User");
        result.Value.User.Roles.Should().ContainSingle("Lead");
        result.Value.User.Capabilities.Should().BeEquivalentTo(AccessRoleCatalog.GetCapabilities(["Lead"]));
        result.Value.RequestHistory.Should().ContainSingle(request => request.Status == AccessRequestStatus.Approved);
        result.Value.User.Version.Should().BeGreaterThan(1);
        fixture.Audit.Events.Should().Contain(audit => audit.Action == AuditActions.AccessUserViewed);
    }

    [Fact]
    public async Task MissingProfileEnrichment_RemainsNullAndDoesNotFabricateValues()
    {
        Fixture fixture = new();
        ApplicationUser approved = await fixture.ApproveAsync(Principal("CONTOSO\\no.profile"), "Operator");

        AccessServiceResult<AccessUserReadModel> result = await fixture.Service.GetUserAsync(approved.Id, Fixture.AdminContext, CancellationToken.None);

        result.Value!.Profile.Should().BeNull();
    }

    [Fact]
    public async Task AdministrativeList_DistinguishesApprovedDisabledAndRejectedPendingState()
    {
        Fixture fixture = new();
        ApplicationUser approved = await fixture.ApproveAsync(Principal("CONTOSO\\approved.list"), "Operator");
        ApplicationUser disabledSource = await fixture.ApproveAsync(Principal("CONTOSO\\disabled.list"), "Lead");
        _ = await fixture.Service.DisableAsync(disabledSource.Id, "Disabled for list-state test.", disabledSource.Version, Fixture.AdminContext, CancellationToken.None);
        AccessServiceResult<EnsureAccessUserResult> rejectedSource = await fixture.Service.GetCurrentAsync(Principal("CONTOSO\\rejected.list"), Fixture.UserContext, CancellationToken.None);
        _ = await fixture.Service.RejectAsync(
            rejectedSource.Value!.PendingRequest!.Id,
            "Rejected for list-state test.",
            rejectedSource.Value.PendingRequest.Version,
            Fixture.AdminContext,
            CancellationToken.None);

        AccessServiceResult<IReadOnlyList<AccessUserReadModel>> result = await fixture.Service.ListUsersAsync(Fixture.AdminContext, CancellationToken.None);

        IReadOnlyList<AccessUserReadModel> users = result.Value!;
        users.Single(user => user.User.Id == approved.Id).User.Status.Should().Be(AccessStatus.Approved);
        users.Single(user => user.User.Id == disabledSource.Id).User.Status.Should().Be(AccessStatus.Disabled);
        AccessUserReadModel rejected = users.Single(user => user.User.Id == rejectedSource.Value.User.Id);
        rejected.User.Status.Should().Be(AccessStatus.Pending);
        rejected.LatestRequest!.Status.Should().Be(AccessRequestStatus.Rejected);
        fixture.Audit.Events.Should().Contain(audit => audit.Action == AuditActions.AccessUsersViewed);
    }

    private static ClaimsPrincipal Principal(string name) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, name)],
        "Negotiate",
        ClaimTypes.Name,
        ClaimTypes.Role));

    private sealed class FixedProfileResolver(string identity) : IAccessIdentityProfileResolver
    {
        public Task<AccessIdentityProfile?> ResolveAsync(string corporateIdentity, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(identity, corporateIdentity, StringComparison.OrdinalIgnoreCase)
                ? new AccessIdentityProfile("Resolved User", "profile.user", "resolved.user@contoso.invalid", "Operations", "Engineer")
                : null);
    }

    private sealed class Fixture
    {
        private readonly InMemoryAccessRepository _repository = new();
        private readonly InMemoryAuditWriter _audit = new();
        private readonly IHttpContextAccessor _accessor;

        public Fixture(string[]? bootstrapAdministrators = null, IAccessIdentityProfileResolver? profileResolver = null)
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
                profileResolver ?? new NullProfileResolver(),
                _audit,
                options,
                Options.Create(new SessionSecurityOptions()),
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
                current.Value.PendingRequest.Version,
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

        private sealed class NullProfileResolver : IAccessIdentityProfileResolver
        {
            public Task<AccessIdentityProfile?> ResolveAsync(string corporateIdentity, CancellationToken cancellationToken) =>
                Task.FromResult<AccessIdentityProfile?>(null);
        }

    }
}
