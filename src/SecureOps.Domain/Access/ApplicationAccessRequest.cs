namespace SecureOps.Domain.Access;

/// <summary>Durable request for SecureOps application access.</summary>
public sealed record ApplicationAccessRequest(
    Guid Id,
    Guid UserId,
    string CorporateIdentity,
    AccessRequestStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    string? DecidedByCorporateIdentity,
    long Version);
