namespace SecureOps.Shared.Configuration;

/// <summary>Configuration for operational-record source and persistence providers.</summary>
public sealed class OperationalRecordsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "OperationalRecords";

    /// <summary>Source provider: Disabled, or Fake in an explicitly allowed non-production environment.</summary>
    public string SourceProvider { get; set; } = "Disabled";

    /// <summary>Repository provider: InMemory for local tests or SqlServer for durable runtime state.</summary>
    public string RepositoryProvider { get; set; } = "InMemory";

    /// <summary>Maximum active records accepted from one bounded source query.</summary>
    public int MaxImportCount { get; set; } = 100;

    /// <summary>Bounded actor lease duration for Jira create/retry commands.</summary>
    public int ClaimLeaseSeconds { get; set; } = 120;
}
