namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Read-only proposed Jira mapping for explicit operator review.</summary>
public sealed record JiraPreviewResponse(
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
    string? AssigneeUsername = null,
    bool SimulationMode = false,
    string? SimulationNotice = null,
    bool ReadOnlyIntegrationMode = false,
    string? ReadOnlyNotice = null,
    string? ReporterUsername = null,
    string? IssueTypeId = null,
    string? TeamCustomField = null,
    string? TeamValue = null,
    string? RequesterWatcherCustomField = null,
    IReadOnlyList<string>? Labels = null);
