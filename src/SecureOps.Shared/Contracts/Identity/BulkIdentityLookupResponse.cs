namespace SecureOps.Shared.Contracts.Identity;

/// <summary>Per-account result from a bounded bulk identity lookup.</summary>
public sealed record BulkIdentityLookupItemResponse(
    string Account,
    string Status,
    IdentityLookupResponse? Result,
    string? Code = null);

/// <summary>Response for a bounded bulk identity lookup.</summary>
public sealed record BulkIdentityLookupResponse(IReadOnlyCollection<BulkIdentityLookupItemResponse> Results);
