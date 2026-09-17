namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Exact-attempt post-state evidence. Acknowledged activity updates are not VerifiedClosed.</summary>
public sealed record SourceClosureObservation(string State, string SourceRecordId, string OrCode, string JiraKey,
    string AttemptReference, DateTimeOffset ObservedAt, string? SourceVersion, string? AuthoritativeCloser,
    string EvidenceContract, string? ErrorCode = null)
{
    /// <summary>Identity and attempt must all match before a verified completion can be persisted.</summary>
    public bool Matches(string sourceId, string code, string jiraKey, string attempt) => State == "VerifiedClosed"
        && SourceRecordId == sourceId && OrCode == code && JiraKey == jiraKey && AttemptReference == attempt
        && ObservedAt <= DateTimeOffset.UtcNow.AddSeconds(5) && ObservedAt >= DateTimeOffset.UtcNow.AddMinutes(-5)
        && !string.IsNullOrWhiteSpace(SourceVersion) && !string.IsNullOrWhiteSpace(EvidenceContract);
}
