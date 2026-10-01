namespace SecureOps.Shared.Configuration;

/// <summary>Actor-and-operation rate-limit configuration.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Identity exact lookup policy.</summary>
    public OperationRateLimitOptions IdentityLookup { get; set; } = new(10, 60);
    /// <summary>Identity bulk lookup policy.</summary>
    public OperationRateLimitOptions BulkIdentityLookup { get; set; } = new(4, 60);
    /// <summary>Directory principal-groups and group-metadata policy.</summary>
    public OperationRateLimitOptions DirectoryGroupQuery { get; set; } = new(20, 60);
    /// <summary>Directory direct-member enumeration policy.</summary>
    public OperationRateLimitOptions DirectoryGroupMembers { get; set; } = new(10, 60);
    /// <summary>Directory recursive enrichment and account-evidence policy.</summary>
    public OperationRateLimitOptions DirectoryEnrichment { get; set; } = new(6, 60);
    /// <summary>Separately authorized privileged-group analysis policy.</summary>
    public OperationRateLimitOptions DirectoryPrivilegedGroups { get; set; } = new(4, 60);
    /// <summary>Bounded recursive group analysis.</summary>
    public OperationRateLimitOptions DirectoryGroupAnalysis { get; set; } = new(4, 60);
    /// <summary>Authorized bounded membership export.</summary>
    public OperationRateLimitOptions DirectoryGroupExport { get; set; } = new(2, 60);
    /// <summary>Operational-record refresh policy.</summary>
    public OperationRateLimitOptions OperationalRecordRefresh { get; set; } = new(12, 60);
    /// <summary>Jira preview policy.</summary>
    public OperationRateLimitOptions JiraPreview { get; set; } = new(20, 60);
    /// <summary>Jira create policy.</summary>
    public OperationRateLimitOptions JiraCreate { get; set; } = new(6, 60);
    /// <summary>Workflow retry policy.</summary>
    public OperationRateLimitOptions WorkflowRetry { get; set; } = new(6, 60);
}

/// <summary>One fixed-window operation limit.</summary>
public sealed class OperationRateLimitOptions
{
    /// <summary>Initializes default options.</summary>
    public OperationRateLimitOptions()
    {
    }

    /// <summary>Initializes bounded defaults.</summary>
    public OperationRateLimitOptions(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }

    /// <summary>Maximum permits in one window.</summary>
    public int PermitLimit { get; set; }
    /// <summary>Window duration in seconds.</summary>
    public int WindowSeconds { get; set; }
}
