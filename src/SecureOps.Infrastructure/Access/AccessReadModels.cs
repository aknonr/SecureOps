using SecureOps.Domain.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>An access request paired with optional safe profile enrichment.</summary>
public sealed record AccessRequestReadModel(ApplicationAccessRequest Request, AccessIdentityProfile? Profile);

/// <summary>Authoritative administrative access-user state.</summary>
public sealed record AccessUserReadModel(
    ApplicationUser User,
    AccessIdentityProfile? Profile,
    IReadOnlyList<ApplicationAccessRequest> RequestHistory)
{
    /// <summary>The newest request, including a terminal rejection when present.</summary>
    public ApplicationAccessRequest? LatestRequest => RequestHistory
        .OrderByDescending(request => request.RequestedAt)
        .FirstOrDefault();
}
