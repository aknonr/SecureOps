namespace SecureOps.Shared.Configuration;

/// <summary>Bounded read-only Directory Explorer configuration.</summary>
public sealed class DirectoryExplorerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DirectoryExplorer";

    /// <summary>Default response page size.</summary>
    public int DefaultPageSize { get; set; } = 50;
    /// <summary>Maximum response page size.</summary>
    public int MaxPageSize { get; set; } = 100;
    /// <summary>Maximum records one provider query may enumerate.</summary>
    public int ProviderResultLimit { get; set; } = 10_000;
    /// <summary>Maximum provider execution time.</summary>
    public int ProviderTimeoutSeconds { get; set; } = 5;
    /// <summary>Opaque continuation token lifetime.</summary>
    public int ContinuationTokenLifetimeSeconds { get; set; } = 300;
    /// <summary>Maximum exact group input length.</summary>
    public int MaxGroupInputLength { get; set; } = 256;
    /// <summary>Maximum required operational-purpose length.</summary>
    public int MaxPurposeLength { get; set; } = 256;
    /// <summary>Conservative query-cache settings.</summary>
    public DirectoryExplorerCacheOptions Cache { get; set; } = new();
}

/// <summary>Bounded in-process Directory Explorer cache settings.</summary>
public sealed class DirectoryExplorerCacheOptions
{
    /// <summary>Whether provider results may be cached.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Cache lifetime in seconds.</summary>
    public int TtlSeconds { get; set; } = 15;
    /// <summary>Maximum cached operation/page entries.</summary>
    public int MaxEntries { get; set; } = 250;
}
