using System.Security.Claims;

namespace SecureOps.Infrastructure.Access;

/// <summary>Maps an authenticated identity source to a stable corporate principal.</summary>
public interface ICorporatePrincipalResolver
{
    /// <summary>Resolves the principal without granting application access.</summary>
    public CorporatePrincipal? Resolve(ClaimsPrincipal principal);
}
