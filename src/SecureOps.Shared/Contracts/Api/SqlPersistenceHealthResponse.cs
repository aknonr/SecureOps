namespace SecureOps.Shared.Contracts.Api;

/// <summary>Sanitized readiness state for configured SQL persistence.</summary>
public sealed record SqlPersistenceHealthResponse(
    string Status,
    bool SqlServerConfigured,
    string? ErrorCode);
