using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Service-level identity lookup result.
/// </summary>
/// <param name="Status">Lookup status.</param>
/// <param name="Response">API response when a trusted response can be returned.</param>
/// <param name="ErrorCode">Machine-readable error code.</param>
/// <param name="ErrorMessage">Human-readable error message.</param>
public sealed record IdentityLookupResult(
    IdentityLookupResultStatus Status,
    IdentityLookupResponse? Response,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Internal identity lookup result statuses.
/// </summary>
public enum IdentityLookupResultStatus
{
    /// <summary>
    /// A matching directory user was found.
    /// </summary>
    Found,

    /// <summary>
    /// The input was rejected before directory lookup.
    /// </summary>
    Invalid,

    /// <summary>
    /// No matching directory user was found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The lookup failed because the provider was unavailable or errored.
    /// </summary>
    Failed
}
