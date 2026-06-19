namespace SecureOps.Shared.Configuration;

/// <summary>
/// Configuration for SecureOps audit persistence.
/// </summary>
public sealed class AuditOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Audit";

    /// <summary>
    /// Audit provider. Supported values are InMemory, File, and SqlServer.
    /// </summary>
    public string Provider { get; set; } = "File";

    /// <summary>
    /// Whether privileged operations must fail when audit cannot accept a record.
    /// </summary>
    public bool FailClosed { get; set; } = true;

    /// <summary>
    /// Whether production requires a persistent audit store.
    /// </summary>
    public bool RequirePersistentStoreInProduction { get; set; } = true;

    /// <summary>
    /// File audit settings.
    /// </summary>
    public AuditFileOptions File { get; set; } = new();

    /// <summary>
    /// Audit queue settings.
    /// </summary>
    public AuditQueueOptions Queue { get; set; } = new();

    /// <summary>
    /// Background audit flush interval in seconds.
    /// </summary>
    public int FlushIntervalSeconds { get; set; } = 1;
}

/// <summary>
/// File audit persistence settings.
/// </summary>
public sealed class AuditFileOptions
{
    /// <summary>
    /// Directory where audit JSONL files are written.
    /// </summary>
    public string Directory { get; set; } = @"D:\SecureOps\Audit";

    /// <summary>
    /// Audit file prefix.
    /// </summary>
    public string FilePrefix { get; set; } = "secureops-audit";

    /// <summary>
    /// Rolling interval. Day is the only supported value in Phase 1A.
    /// </summary>
    public string RollingInterval { get; set; } = "Day";

    /// <summary>
    /// Maximum audit file size before sequence rollover.
    /// </summary>
    public int MaxFileSizeMB { get; set; } = 50;

    /// <summary>
    /// Number of audit files retained in the file directory.
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 31;
}

/// <summary>
/// Bounded audit queue settings.
/// </summary>
public sealed class AuditQueueOptions
{
    /// <summary>
    /// Whether persistent audit writes are queued.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of audit events allowed in memory.
    /// </summary>
    public int Capacity { get; set; } = 1000;

    /// <summary>
    /// Queue behavior when full. Supported values are FailClosed and DropAndCriticalLog.
    /// </summary>
    public string FullBehavior { get; set; } = "FailClosed";
}
