namespace SecureOps.Shared.Configuration;

/// <summary>Configuration for safe Jira draft generation and integration policy.</summary>
public sealed class JiraIntegrationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jira";

    /// <summary>Jira provider: Disabled, or Fake only in an explicitly synthetic environment.</summary>
    public string Provider { get; set; } = "Disabled";

    /// <summary>HTTPS provider base URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Complete runtime Authorization header value; scheme remains deployment-owned.</summary>
    public string Authorization { get; set; } = string.Empty;

    /// <summary>Configured Jira project key used in previews.</summary>
    public string ProjectKey { get; set; } = "TEST";

    /// <summary>Configured Jira issue type used in previews.</summary>
    public string IssueType { get; set; } = "Task";

    /// <summary>Corporate Jira issue-type identifier.</summary>
    public string IssueTypeId { get; set; } = string.Empty;

    /// <summary>Version of the reviewed Jira field mapping.</summary>
    public string MappingVersion { get; set; } = "v1";

    /// <summary>Policy when exact requester resolution is unavailable: Block or ProceedUnassigned.</summary>
    public string UnresolvedRequesterPolicy { get; set; } = "Block";

    /// <summary>Maximum Jira summary length.</summary>
    public int SummaryMaxLength { get; set; } = 255;

    /// <summary>Configured separator between OR code and short description.</summary>
    public string SummarySeparator { get; set; } = ": ";

    /// <summary>Configured team custom-field key.</summary>
    public string TeamCustomField { get; set; } = string.Empty;

    /// <summary>Configured team custom-field value.</summary>
    public string TeamValue { get; set; } = string.Empty;

    /// <summary>Configured requester/watcher custom-field key.</summary>
    public string RequesterWatcherCustomField { get; set; } = string.Empty;

    /// <summary>Configured issue labels.</summary>
    public string[] Labels { get; set; } = [];

    /// <summary>TCP connection timeout.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    /// <summary>Per-request timeout.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum accepted response bytes.</summary>
    public int MaxResponseBytes { get; set; } = 1_048_576;

    /// <summary>Maximum attempts for safe Jira user-search reads.</summary>
    public int UserSearchMaxAttempts { get; set; } = 3;

    /// <summary>Delay between retryable Jira user-search attempts.</summary>
    public int UserSearchRetryDelayMilliseconds { get; set; } = 500;
}
