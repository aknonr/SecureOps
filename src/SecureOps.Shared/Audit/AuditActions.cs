namespace SecureOps.Shared.Audit;

/// <summary>
/// Canonical audit action codes.
/// </summary>
public static class AuditActions
{
    /// <summary>
    /// A privileged identity lookup was requested.
    /// </summary>
    public const string IdentityLookupRequested = "IdentityLookupRequested";

    /// <summary>
    /// A privileged identity lookup found a matching account.
    /// </summary>
    public const string IdentityLookupSucceeded = "IdentityLookupSucceeded";

    /// <summary>
    /// A privileged identity lookup found no matching account.
    /// </summary>
    public const string IdentityLookupNotFound = "IdentityLookupNotFound";

    /// <summary>
    /// A privileged identity lookup request was rejected before provider access.
    /// </summary>
    public const string IdentityLookupRejected = "IdentityLookupRejected";

    /// <summary>
    /// A privileged identity lookup failed before returning a trusted result.
    /// </summary>
    public const string IdentityLookupFailed = "IdentityLookupFailed";

    /// <summary>
    /// Authorization denied access to the privileged identity lookup endpoint.
    /// </summary>
    public const string IdentityLookupForbidden = "IdentityLookupForbidden";

    /// <summary>
    /// API or UI authorization denied a request.
    /// </summary>
    public const string AuthorizationDenied = "AuthorizationDenied";
}
