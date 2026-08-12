namespace SecureOps.Shared.Contracts.Access;

/// <summary>Administrative access-request projection.</summary>
public sealed record AccessRequestResponse(
    Guid Id,
    Guid UserId,
    string CorporateIdentity,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionReason);
