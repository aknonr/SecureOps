namespace SecureOps.Api.Security;

/// <summary>
/// Named rate-limit policies used by identity lookup endpoints.
/// </summary>
public static class IdentityLookupRateLimits
{
    /// <summary>
    /// Rate-limit policy for the privileged identity lookup endpoint.
    /// </summary>
    public const string Lookup = ApiRateLimits.IdentityLookup;

    /// <summary>
    /// Builds a rate-limit partition key from authenticated user and endpoint.
    /// </summary>
    /// <param name="httpContext">HTTP context.</param>
    /// <returns>Rate-limit partition key.</returns>
    public static string GetPartitionKey(HttpContext httpContext)
    {
        string actor = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity.Name ?? "authenticated-unknown"
            : "anonymous";
        return $"{actor.ToLowerInvariant()}|{ApiRateLimits.IdentityLookup}";
    }
}
