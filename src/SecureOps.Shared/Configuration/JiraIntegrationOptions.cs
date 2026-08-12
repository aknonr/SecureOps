namespace SecureOps.Shared.Configuration;

/// <summary>Configuration for safe Jira draft generation and integration policy.</summary>
public sealed class JiraIntegrationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jira";

    /// <summary>Jira provider. Only Fake is implemented until the enterprise contract is approved.</summary>
    public string Provider { get; set; } = "Fake";

    /// <summary>Configured Jira project key used in previews.</summary>
    public string ProjectKey { get; set; } = "TEST";

    /// <summary>Configured Jira issue type used in previews.</summary>
    public string IssueType { get; set; } = "Task";

    /// <summary>Version of the reviewed Jira field mapping.</summary>
    public string MappingVersion { get; set; } = "v1";

    /// <summary>Policy when exact requester resolution is unavailable: Block or ProceedUnassigned.</summary>
    public string UnresolvedRequesterPolicy { get; set; } = "Block";

    /// <summary>Maximum Jira summary length.</summary>
    public int SummaryMaxLength { get; set; } = 255;
}
