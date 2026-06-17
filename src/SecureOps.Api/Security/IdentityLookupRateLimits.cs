namespace SecureOps.Api.Security;

/// <summary>
/// Named rate-limit policies used by identity lookup endpoints.
/// </summary>
public static class IdentityLookupRateLimits
{
    /// <summary>
    /// Rate-limit policy for the privileged identity lookup endpoint.
    /// </summary>
    public const string Lookup = "IdentityLookup";
}
