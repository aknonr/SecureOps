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
    string? AssigneeUsername = null);

/// <summary>Confirmed Jira issue creation result.</summary>
public sealed record JiraIssueCreationResult(string IssueKey);
