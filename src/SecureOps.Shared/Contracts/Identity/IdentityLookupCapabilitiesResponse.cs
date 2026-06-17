namespace SecureOps.Shared.Contracts.Identity;

/// <summary>
/// Safe metadata describing the identity lookup endpoint's supported behavior.
/// </summary>
/// <param name="MaxAccountLength">Maximum account length accepted after normalization.</param>
/// <param name="SupportsUpnLookup">Whether exact UPN-shaped lookup is enabled.</param>
/// <param name="ReturnedFields">Approved fields that may appear in successful lookup responses.</param>
/// <param name="RejectedInputClasses">Input classes rejected by design.</param>
/// <param name="RateLimitPolicy">Named rate-limit policy applied to the lookup endpoint.</param>
public sealed record IdentityLookupCapabilitiesResponse(
    int MaxAccountLength,
    bool SupportsUpnLookup,
    IReadOnlyCollection<string> ReturnedFields,
    IReadOnlyCollection<string> RejectedInputClasses,
    string RateLimitPolicy);
