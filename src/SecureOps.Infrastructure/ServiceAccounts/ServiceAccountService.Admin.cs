using System.Security.Claims;
using SecureOps.Domain.Access;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    private static readonly string[] _orgKinds = ["Directorate", "Department", "Group", "Other"];

    /// <summary>Organizations (picker data; names only).</summary>
    public Task<SaResult<IReadOnlyList<OrganizationView>>> OrganizationsAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async _ => new SaResult<IReadOnlyList<OrganizationView>>(
            await repository!.OrganizationsAsync(cancellationToken)), cancellationToken);

    /// <summary>Teams (picker data; names only).</summary>
    public Task<SaResult<IReadOnlyList<TeamView>>> TeamsAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async _ => new SaResult<IReadOnlyList<TeamView>>(
            await repository!.TeamsAsync(cancellationToken)), cancellationToken);

    /// <summary>Module person references by label prefix; this is not a directory or personnel search.</summary>
    public Task<SaResult<IReadOnlyList<PersonView>>> PeopleAsync(ClaimsPrincipal principal, AccessOperationContext context, string? search,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async _ => ValidText(search, 100)
            ? new SaResult<IReadOnlyList<PersonView>>(await repository!.PeopleAsync(search, 50, cancellationToken))
            : SaResult<IReadOnlyList<PersonView>>.Fail(SaErrors.Invalid, "search"), cancellationToken);

    /// <summary>Creates or renames an organization (administration).</summary>
    public Task<SaResult<Guid>> SaveOrganizationAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid? id, SaveOrganizationRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, caller =>
            !ValidText(request.Name, 200, true) || !_orgKinds.Contains(request.Kind, StringComparer.Ordinal) || request.ParentId == id && id is not null
                || id is not null && string.IsNullOrEmpty(request.ExpectedVersion)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "name"))
                : repository!.SaveOrganizationAsync(id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Creates, moves or renames a team (administration).</summary>
    public Task<SaResult<Guid>> SaveTeamAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid? id, SaveTeamRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, caller =>
            !ValidText(request.Name, 200, true) || id is not null && string.IsNullOrEmpty(request.ExpectedVersion)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "name"))
                : repository!.SaveTeamAsync(id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Creates a provisional person reference.</summary>
    public Task<SaResult<Guid>> CreatePersonAsync(ClaimsPrincipal principal, AccessOperationContext context, CreatePersonRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async caller => ValidText(request.DisplayName, 200, true) && !ServiceAccountText.IsPlaceholder(request.DisplayName)
            ? new SaResult<Guid>(await repository!.CreatePersonAsync(request.DisplayName, caller.Actor, cancellationToken))
            : SaResult<Guid>.Fail(SaErrors.Invalid, "displayName"), cancellationToken);

    /// <summary>Records a verified directory identity for a person (administration, evidence required).</summary>
    public Task<SaResult<Guid>> VerifyPersonAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, VerifyPersonRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, caller =>
            !ValidText(request.Evidence, 400, true) || request.Upn is null && request.DirectoryObjectId is null || !ValidText(request.Upn, 256) || !ValidText(request.DirectoryObjectId, 128)
                || request.Upn is { } upn && !upn.Contains('@', StringComparison.Ordinal)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "upn"))
                : repository!.VerifyPersonAsync(id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Adds an evidenced alias to a person.</summary>
    public Task<SaResult<Guid>> AddPersonAliasAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, AddPersonAliasRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, caller =>
            !ValidText(request.Alias, 200, true) || !ValidText(request.Evidence, 400, true)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "alias"))
                : repository!.AddPersonAliasAsync(id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Active scope grants.</summary>
    public Task<SaResult<IReadOnlyList<ScopeGrantView>>> GrantsAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, async _ => new SaResult<IReadOnlyList<ScopeGrantView>>(
            await repository!.GrantsAsync(cancellationToken)), cancellationToken);

    /// <summary>
    /// Grants data scope to an existing approved application user identified exactly. Imported people, names or
    /// directory claims never grant scope; the administrator cannot grant to themself.
    /// </summary>
    public Task<SaResult<Guid>> CreateGrantAsync(ClaimsPrincipal principal, AccessOperationContext context, CreateScopeGrantRequest request,
        IAccessRepository users, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, async caller =>
        {
            if (!Enum.TryParse(request.ScopeKind, false, out ScopeKind kind) || !ValidText(request.Reason, 400, true) || !ValidText(request.CorporateIdentity, 256, true)
                || kind == ScopeKind.Organization != (request.OrganizationId is not null) || kind == ScopeKind.Team != (request.TeamId is not null))
            {
                return SaResult<Guid>.Fail(SaErrors.Invalid, "scopeKind");
            }

            if (await users.GetUserAsync(request.CorporateIdentity.Trim(), cancellationToken) is not { Status: AccessStatus.Approved } target)
            {
                return SaResult<Guid>.Fail(SaErrors.Invalid, "corporateIdentity");
            }

            return target.Id == caller.User.Id
                ? SaResult<Guid>.Fail(SaErrors.Forbidden, "selfGrant")
                : await repository!.CreateGrantAsync(target.Id, kind, request.OrganizationId, request.TeamId, request.Reason.Trim(), caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Revokes a grant.</summary>
    public Task<SaResult<Guid>> RevokeGrantAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, RevokeScopeGrantRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, caller => !ValidText(request.Reason, 400, true)
            ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "reason"))
            : repository!.RevokeGrantAsync(id, request, caller.Actor, cancellationToken), cancellationToken);
}
