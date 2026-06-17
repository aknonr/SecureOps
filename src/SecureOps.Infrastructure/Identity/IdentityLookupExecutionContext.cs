namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Caller context used for authorization-aware audit entries.
/// </summary>
/// <param name="Actor">Actor identity.</param>
/// <param name="SourceIp">Source IP when available.</param>
/// <param name="CorrelationId">Trace correlation ID when available.</param>
public sealed record IdentityLookupExecutionContext(
    string Actor,
    string? SourceIp,
    string? CorrelationId);
