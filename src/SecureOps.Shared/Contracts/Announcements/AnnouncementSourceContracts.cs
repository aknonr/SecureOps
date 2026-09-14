using System.Text.Json.Serialization;

namespace SecureOps.Shared.Contracts.Announcements;

/// <summary>Allowlisted profile choice. State is Configured, Unconfigured or Invalid.</summary>
public sealed record MaintenanceProfileChoice(string Name, string Label, string State, string[] Missing);

/// <summary>Explicit owner-authorized submission. Profile must be allowlisted and configured.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementSourceSubmission(string Profile, string OcoReference, string SubmissionKey);

/// <summary>
/// Durable job status. Poll only while <c>terminal</c> is false; once true the result never changes.
/// <c>duplicateOf</c> is set when an identical submission key returned the existing job.
/// </summary>
public sealed record AnnouncementSourceJobStatus(Guid JobId, Guid DraftId, string Profile,
    string OcoReference, string State, bool Terminal, DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt, string? ErrorCode, Guid? DuplicateOf);

/// <summary>
/// One reviewable field difference. <c>state</c> is Unchanged, Changed, SourceUnavailable or
/// RequiresOperatorOffset. <c>sourceText</c> is the raw source value when it cannot be a draft value.
/// </summary>
public sealed record ProposedField(string Field, string? Current, string? Proposed,
    string? SourceText, string Origin, string State);

/// <summary>Precise recipient difference. Nothing is silently replaced; removals are preserved.</summary>
public sealed record RecipientDifference(string[] Current, string[] Proposed, string[] Added,
    string[] Removed, string[] PreservedManual, string[] PreservedRemoval);

/// <summary>Retrieval completeness surfaced to the reviewer verbatim.</summary>
public sealed record SourceCompletenessView(bool DevicesComplete, int DeviceCount, int DevicePagesRead,
    int DevicesRequested, int ServicesResolved, int ServicesAmbiguous, int ServicesMissing,
    int ServicesFailed, bool Partial, string[] Warnings);

/// <summary>
/// Reviewable proposal built from one snapshot plus the current draft and overrides.
/// <c>audience</c> is always DistributionRequest: these recipients are not a final-announcement audience.
/// </summary>
public sealed record AnnouncementSourceProposal(Guid JobId, Guid DraftId, string Profile,
    string State, DateTimeOffset CapturedAt, long DraftVersion, ProposedField[] Fields,
    string[] ProposedAffectedServices, string[] AmbiguousServices, string[] UnresolvedDevices,
    RecipientDifference To, RecipientDifference Cc, bool HighPriority, string Audience,
    SourceCompletenessView Completeness, bool Stale, long OverrideVersion = 0);

/// <summary>
/// Reviewed application request. Only listed fields are written; <c>expectedVersion</c> must equal
/// the current draft version. Manual recipient edits are preserved, never overwritten.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementSourceApply(Guid JobId, long ExpectedVersion, string[] Fields,
    bool ApplyRecipients, bool ApplyAffectedServices, long ExpectedOverrideVersion = 0);

/// <summary>Result of a reviewed application: the new draft version and what was actually written.</summary>
public sealed record AnnouncementSourceApplyResult(Guid DraftId, long Version, string[] AppliedFields,
    bool RecipientsApplied, bool AffectedServicesApplied, string[] SkippedFields);
