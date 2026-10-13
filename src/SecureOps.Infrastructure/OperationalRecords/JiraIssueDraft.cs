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

/// <summary>
/// Read-only view of what a Jira-create retry would send, compared with the reviewed draft's durable
/// fingerprint. Retry is accepted only when <paramref name="MatchesPersistedDraft"/> is true.
/// </summary>
/// <param name="Draft">Draft rebuilt exactly as the retry would rebuild it for this actor.</param>
/// <param name="PersistedMappingVersion">Mapping version fixed on the transfer.</param>
/// <param name="PersistedIdempotencyKey">Reviewed-draft fingerprint fixed on the transfer.</param>
/// <param name="MatchesPersistedDraft">Whether the rebuilt draft has the persisted fingerprint.</param>
public sealed record JiraRetryDraft(
    JiraIssueDraft Draft,
    string PersistedMappingVersion,
    string PersistedIdempotencyKey,
    bool MatchesPersistedDraft);

/// <summary>Validated create fields shared by the preview and Jira adapter.</summary>
public sealed record JiraIssueFieldMapping(
    string IssueTypeId,
    string TeamCustomField,
    string TeamValue,
    string RequesterWatcherCustomField,
    IReadOnlyList<string> Labels);

/// <summary>Confirmed Jira issue creation result.</summary>
public sealed record JiraIssueCreationResult(string IssueKey);
