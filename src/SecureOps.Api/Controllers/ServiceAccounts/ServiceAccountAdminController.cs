using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Api.Controllers.ServiceAccounts;

/// <summary>Module context, dictionaries and scope administration. Scope grants never come from imported names.</summary>
[ApiController]
[Route("api/v1/service-accounts")]
[Authorize(Policy = ServiceAccountPolicies.View)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceAccountAdminController(ServiceAccountService service, IAccessRepository users) : ControllerBase
{
    /// <summary>Caller capabilities and resolved scope.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ServiceAccountMe), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceAccountMe>> MeAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.MeAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>Organizations.</summary>
    [HttpGet("organizations")]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrganizationView>>> OrganizationsAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.OrganizationsAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>Creates an organization.</summary>
    [HttpPost("organizations")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> CreateOrganizationAsync(SaveOrganizationRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SaveOrganizationAsync(User, ServiceAccountReplies.Context(this), null, request, cancellationToken));

    /// <summary>Renames or moves an organization at the expected version.</summary>
    [HttpPut("organizations/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> SaveOrganizationAsync(Guid id, SaveOrganizationRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SaveOrganizationAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));

    /// <summary>Teams.</summary>
    [HttpGet("teams")]
    [ProducesResponseType(typeof(IReadOnlyList<TeamView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TeamView>>> TeamsAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.TeamsAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>Creates a team.</summary>
    [HttpPost("teams")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> CreateTeamAsync(SaveTeamRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SaveTeamAsync(User, ServiceAccountReplies.Context(this), null, request, cancellationToken));

    /// <summary>Renames or moves a team at the expected version.</summary>
    [HttpPut("teams/{id:guid}")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> SaveTeamAsync(Guid id, SaveTeamRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.SaveTeamAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));

    /// <summary>Module person references by label prefix (not a directory search).</summary>
    [HttpGet("people")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(IReadOnlyList<PersonView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PersonView>>> PeopleAsync([FromQuery] string? search, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.PeopleAsync(User, ServiceAccountReplies.Context(this), search, cancellationToken));

    /// <summary>Creates a provisional person reference.</summary>
    [HttpPost("people")]
    [Authorize(Policy = ServiceAccountPolicies.Work)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> CreatePersonAsync(CreatePersonRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreatePersonAsync(User, ServiceAccountReplies.Context(this), request, cancellationToken));

    /// <summary>Records a verified directory identity for a person.</summary>
    [HttpPost("people/{id:guid}/verify")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> VerifyPersonAsync(Guid id, VerifyPersonRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.VerifyPersonAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));

    /// <summary>Adds an evidenced alias.</summary>
    [HttpPost("people/{id:guid}/aliases")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> AddAliasAsync(Guid id, AddPersonAliasRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.AddPersonAliasAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));

    /// <summary>Active scope grants.</summary>
    [HttpGet("scope-grants")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(IReadOnlyList<ScopeGrantView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ScopeGrantView>>> GrantsAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.GrantsAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>Grants scope to an existing approved user identified exactly.</summary>
    [HttpPost("scope-grants")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> GrantAsync(CreateScopeGrantRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateGrantAsync(User, ServiceAccountReplies.Context(this), request, users, cancellationToken));

    /// <summary>Approved application users who can be chosen for a scope grant (no directory search).</summary>
    [HttpGet("scope-grants/candidates")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(IReadOnlyList<ScopeGrantCandidate>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ScopeGrantCandidate>>> CandidatesAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ScopeGrantCandidatesAsync(User, ServiceAccountReplies.Context(this), users, cancellationToken));

    /// <summary>State of the one-time first scope grant (ADR-0026).</summary>
    [HttpGet("scope-grants/bootstrap")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(ScopeBootstrapState), StatusCodes.Status200OK)]
    public async Task<ActionResult<ScopeBootstrapState>> BootstrapStateAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.ScopeBootstrapStateAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>One-time first scope grant to the caller while the module has never had a scope grant (ADR-0026).</summary>
    [HttpPost("scope-grants/bootstrap")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> BootstrapAsync(ScopeBootstrapRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.BootstrapScopeAsync(User, ServiceAccountReplies.Context(this), request, cancellationToken));

    /// <summary>Revokes a grant.</summary>
    [HttpPost("scope-grants/{id:guid}/revoke")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> RevokeAsync(Guid id, RevokeScopeGrantRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RevokeGrantAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));

    /// <summary>Active team roles: SQL teams and the gMSA executing team (configuration only).</summary>
    [HttpGet("team-roles")]
    [ProducesResponseType(typeof(IReadOnlyList<TeamRoleView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TeamRoleView>>> TeamRolesAsync(CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.TeamRolesAsync(User, ServiceAccountReplies.Context(this), cancellationToken));

    /// <summary>Assigns a team role with a reason; grants no access.</summary>
    [HttpPost("team-roles")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> CreateTeamRoleAsync(CreateTeamRoleRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.CreateTeamRoleAsync(User, ServiceAccountReplies.Context(this), request, cancellationToken));

    /// <summary>Revokes a team role with a reason.</summary>
    [HttpPost("team-roles/{id:guid}/revoke")]
    [Authorize(Policy = ServiceAccountPolicies.Administer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<ActionResult<Guid>> RevokeTeamRoleAsync(Guid id, RevokeTeamRoleRequest request, CancellationToken cancellationToken) =>
        ServiceAccountReplies.Reply(this, await service.RevokeTeamRoleAsync(User, ServiceAccountReplies.Context(this), id, request, cancellationToken));
}
