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
    string? ReporterUsername = null);

/// <summary>Validated create fields shared by the preview and Jira adapter.</summary>
public sealed record JiraIssueFieldMapping(
    string IssueTypeId,
    string TeamCustomField,
    string TeamValue,
    string RequesterWatcherCustomField,
    IReadOnlyList<string> Labels);

/// <summary>Confirmed Jira issue creation result.</summary>
public sealed record JiraIssueCreationResult(string IssueKey);
