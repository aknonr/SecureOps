namespace SecureOps.Shared.Contracts.Audit;

/// <summary>
/// Safe health metadata for the configured audit store.
/// </summary>
/// <param name="Status">Audit store status.</param>
/// <param name="Provider">Configured audit provider.</param>
/// <param name="Persistent">Whether the provider writes to a persistent store.</param>
/// <param name="FailClosed">Whether privileged operations fail when audit cannot accept a record.</param>
/// <param name="QueueEnabled">Whether audit writes are queued.</param>
/// <param name="QueueCapacity">Configured queue capacity when queueing is enabled.</param>
/// <param name="QueuedCount">Current queued event count when queueing is enabled.</param>
/// <param name="LastErrorCode">Last safe audit-store failure code.</param>
public sealed record AuditStoreHealthResponse(
    string Status,
    string Provider,
    bool Persistent,
    bool FailClosed,
    bool QueueEnabled,
    int? QueueCapacity,
    int? QueuedCount,
    string? LastErrorCode);
