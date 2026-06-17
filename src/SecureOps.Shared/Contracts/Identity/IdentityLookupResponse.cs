namespace SecureOps.Shared.Contracts.Identity;

/// <summary>
/// Response returned by identity lookup.
/// </summary>
/// <param name="Status">Found or NotFound.</param>
/// <param name="NormalizedAccount">Account after configured normalization.</param>
/// <param name="Source">Lookup source used for the result.</param>
/// <param name="User">Approved user fields when found.</param>
public sealed record IdentityLookupResponse(
    string Status,
    string NormalizedAccount,
    string Source,
    IdentityLookupUserDto? User);
