namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Authenticated actor and request correlation supplied to workflow services.</summary>
public sealed record OperationalRecordCommandContext(
    string Actor,
    string CorrelationId,
    string? SourceIp,
    string? IdempotencyKey = null);
