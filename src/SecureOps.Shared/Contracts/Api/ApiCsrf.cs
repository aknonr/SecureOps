namespace SecureOps.Shared.Contracts.Api;

/// <summary>Explicit request-intent contract for unsafe SecureOps API calls; not authentication.</summary>
public static class ApiCsrf
{
    /// <summary>Non-secret header that browser forms cannot supply.</summary>
    public const string HeaderName = "X-SecureOps-Csrf";

    /// <summary>Required header value.</summary>
    public const string HeaderValue = "1";
}
