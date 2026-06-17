using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Resolves a PAM account or AD username to approved read-only identity fields.
/// </summary>
public interface IIdentityLookupService
{
    /// <summary>
    /// Looks up one exact account.
    /// </summary>
    /// <param name="request">Lookup request.</param>
    /// <param name="context">Caller context for auditing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lookup result.</returns>
    public Task<IdentityLookupResult> LookupAsync(
        IdentityLookupRequest request,
        IdentityLookupExecutionContext context,
        CancellationToken cancellationToken);
}
