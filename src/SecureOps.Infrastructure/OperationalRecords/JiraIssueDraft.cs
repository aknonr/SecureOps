namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Validated proposed Jira fields before remote creation.</summary>
public sealed record JiraIssueDraft(
    Guid OperationalRecordId,
    string OrCode,
    string ProjectKey,
    string IssueType,
    string Summary,
    string Description,
    string? RequesterAccountId,
    string MappingVersion,
    string IdempotencyKey,
    IReadOnlyList<string> Warnings,
    JiraIssueFieldMapping FieldMapping,
    string? AssigneeUsername = null,
    string? ReporterUsername = null,
    bool SourceCloseRequested = false)
{
    /// <summary>Existing source classification or explicitly declared review type.</summary>
    public SecureOps.Domain.OperationalRecords.OperationalRecordClassification? RequestType { get; init; }
    /// <summary>True only for an operator-declared, non-publishable review draft.</summary>
    public bool ReviewOnly { get; init; }
    /// <summary>Safe existing evaluation and mapping blockers.</summary>
    public IReadOnlyList<string> BlockingConditions { get; init; } = [];
    /// <summary>Loaded source/workflow version used for review.</summary>
    public long RecordVersion { get; init; }
}

/// <summary>Validated create fields shared by the preview and Jira adapter.</summary>
public sealed record JiraIssueFieldMapping(
    string IssueTypeId,
    string TeamCustomField,
    string TeamValue,
    string RequesterWatcherCustomField,
    IReadOnlyList<string> Labels);

/// <summary>Confirmed Jira issue creation result.</summary>
public sealed record JiraIssueCreationResult(string IssueKey);
