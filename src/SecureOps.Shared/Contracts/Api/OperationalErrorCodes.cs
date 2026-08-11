namespace SecureOps.Shared.Contracts.Api;

/// <summary>Stable operational error codes for RFC ProblemDetails responses.</summary>
public static class OperationalErrorCodes
{
    /// <summary>Identity input was invalid.</summary>
    public const string InvalidIdentityInput = "InvalidIdentityInput";
    /// <summary>An identity was not found.</summary>
    public const string IdentityNotFound = "IdentityNotFound";
    /// <summary>The caller lacks required access.</summary>
    public const string AccessDenied = "AccessDenied";
    /// <summary>A caller exceeded the permitted request rate.</summary>
    public const string RateLimitExceeded = "RateLimitExceeded";
    /// <summary>An identity provider is unavailable.</summary>
    public const string IdentityProviderUnavailable = "IdentityProviderUnavailable";
    /// <summary>An identity provider timed out.</summary>
    public const string IdentityProviderTimeout = "IdentityProviderTimeout";
    /// <summary>An identity provider returned an invalid response.</summary>
    public const string IdentityProviderBadResponse = "IdentityProviderBadResponse";
    /// <summary>The required audit store is unavailable.</summary>
    public const string AuditStoreUnavailable = "AuditStoreUnavailable";
    /// <summary>The authenticated identity is awaiting access approval.</summary>
    public const string AccessPending = "AccessPending";
}
