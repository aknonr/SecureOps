namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Bounded source-system projection accepted by SecureOps.</summary>
public sealed record OperationalRecordSourceItem(
    string SourceRecordId,
    string OrCode,
    string Title,
    string Description,
    string? Requester,
    DateTimeOffset CreatedAt,
    string? Environment,
    string? ServerReference,
    string? ApplicationReference,
    bool IsOpen = true,
    string? VersionToken = null,
    DateTimeOffset? LastModifiedAt = null);
