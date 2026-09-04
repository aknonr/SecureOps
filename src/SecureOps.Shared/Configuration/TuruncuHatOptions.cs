namespace SecureOps.Shared.Configuration;

/// <summary>Server-owned Turuncu Hat wire and business configuration.</summary>
public sealed class TuruncuHatOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "TuruncuHat";

    /// <summary>HTTPS provider base URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Complete runtime Authorization header value.</summary>
    public string Authorization { get; set; } = string.Empty;
    /// <summary>Runtime integration username.</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>Runtime integration password.</summary>
    public string Password { get; set; } = string.Empty;
    /// <summary>Tenant identifier.</summary>
    public int TenantId { get; set; }
    /// <summary>Operational Record source base object.</summary>
    public string SourceBaseObject { get; set; } = string.Empty;
    /// <summary>Required related-group identifier.</summary>
    public int RelatedGroupId { get; set; }
    /// <summary>Excluded DCC identifiers.</summary>
    public int[] ExcludedDccIds { get; set; } = [];
    /// <summary>BPM activity base object.</summary>
    public string ActivityBaseObject { get; set; } = string.Empty;
    /// <summary>BPM activity task-model identifier.</summary>
    public int ActivityTaskModelId { get; set; }
    /// <summary>BPM activity group identifier.</summary>
    public int ActivityGroupId { get; set; }
    /// <summary>BPM process main-object type identifier.</summary>
    public int ActivityMainObjectTypeId { get; set; }
    /// <summary>BPM completed status identifier.</summary>
    public int CompletedStatusId { get; set; }
    /// <summary>Configured completion comment containing a {JiraKey} placeholder.</summary>
    public string CompletionCommentTemplate { get; set; } = string.Empty;
    /// <summary>Zero-based LoginResult segment containing the session identifier.</summary>
    public int SessionIdSegmentIndex { get; set; } = 1;
    /// <summary>Explicit bounded session lifetime pending approved expiry semantics.</summary>
    public int SessionLifetimeSeconds { get; set; }
    /// <summary>TCP connection timeout.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;
    /// <summary>Per-request timeout.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
    /// <summary>Maximum accepted response bytes.</summary>
    public int MaxResponseBytes { get; set; } = 1_048_576;
    /// <summary>Maximum accepted decoded description length.</summary>
    public int MaxDescriptionLength { get; set; } = 8000;
    /// <summary>Enables bounded, secret-free wire-contract metadata logging in TEST only.</summary>
    public bool DiagnosticContractLogging { get; set; }
}
