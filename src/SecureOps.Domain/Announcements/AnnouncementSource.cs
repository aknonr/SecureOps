namespace SecureOps.Domain.Announcements;

/// <summary>Protected maintenance-profile allowlist. Configuration can never add a profile name.</summary>
public static class MaintenanceProfiles
{
    /// <summary>The only accepted profile names, in operator display order.</summary>
    public static readonly string[] Allowed = ["NonProd", "Prod01", "Prod02", "ProdSingle", "ProdRPA"];

    /// <summary>Exact, case-sensitive allowlist membership.</summary>
    public static bool IsAllowed(string? name) => name is not null && Array.IndexOf(Allowed, name) >= 0;
}

/// <summary>One allowlisted profile and whether complete configuration makes it usable.</summary>
public sealed record MaintenanceProfileState(string Name, string Label, string State, string[] Missing);

/// <summary>A device observed in a collection. Membership is not OCO scope or approval.</summary>
public sealed record SourceDevice(string Name, string CollectionId, DateTimeOffset RetrievedAt);

/// <summary>
/// A service derived from device relationships. Resolution is Resolved, Ambiguous, Missing or Failed;
/// ambiguous relationships keep every candidate instead of selecting a position.
/// </summary>
public sealed record SourceService(string Name, string[] Devices, string Resolution, DateTimeOffset RetrievedAt);

/// <summary>
/// Raw proposed OCO work values exactly as the source returned them. TimeZoneSemantics stays
/// Unresolved: no instant, offset or restart time is derived from these strings.
/// </summary>
public sealed record SourceWorkWindow(string? ProposedStartText, string? ProposedFinishText,
    string? ProposedStartDate, string TimeZoneSemantics, string Resolution, DateTimeOffset RetrievedAt);

/// <summary>Explicit retrieval completeness; Partial is never presented as a whole result.</summary>
public sealed record SourceCompleteness(bool DevicesComplete, int DeviceCount, int DevicePagesRead,
    int DevicesRequested, int ServicesResolved, int ServicesAmbiguous, int ServicesMissing,
    int ServicesFailed, bool Partial, string[] Warnings);

/// <summary>Immutable captured source evidence, stored separately from operator overrides.</summary>
public sealed record AnnouncementSourceSnapshot(Guid JobId, Guid DraftId, Guid OwnerId, string Profile,
    string OcoReference, DateTimeOffset CapturedAt, string CollectionId,
    IReadOnlyList<SourceDevice> Devices, IReadOnlyList<SourceService> Services,
    SourceWorkWindow? Work, SourceCompleteness Completeness)
{
    /// <summary>Worker profile identity; absent only on historical snapshots.</summary>
    public string? ProfileFingerprint { get; init; }
}

/// <summary>Durable source job. State survives process restart because it lives in SQL, not memory.</summary>
public sealed record AnnouncementSourceJob(Guid JobId, Guid OwnerId, Guid DraftId, string Profile,
    string OcoReference, string State, DateTimeOffset SubmittedAt, DateTimeOffset UpdatedAt,
    string? ErrorCode, AnnouncementSourceSnapshot? Snapshot);

/// <summary>Terminal and non-terminal source job states.</summary>
public static class AnnouncementSourceJobStates
{
    /// <summary>Accepted and durably recorded; not yet picked up.</summary>
    public const string Queued = "Queued";
    /// <summary>Claimed by a Worker.</summary>
    public const string Running = "Running";
    /// <summary>Complete evidence for every requested part.</summary>
    public const string Succeeded = "Succeeded";
    /// <summary>Usable but explicitly incomplete evidence.</summary>
    public const string Partial = "Partial";
    /// <summary>No usable evidence; ErrorCode carries the reason.</summary>
    public const string Failed = "Failed";

    /// <summary>States that can no longer change.</summary>
    public static bool IsTerminal(string state) => state is Succeeded or Partial or Failed;
}

/// <summary>
/// Operator decisions kept apart from source snapshots: manual recipient additions,
/// explicit removals and which snapshot was last applied. Version guards stale writes.
/// </summary>
public sealed record AnnouncementSourceOverrides(Guid DraftId, Guid OwnerId, long Version,
    string? Profile, string[] ManualTo, string[] ManualCc, string[] RemovedTo, string[] RemovedCc,
    Guid? AppliedJobId, DateTimeOffset? AppliedCapturedAt, DateTimeOffset UpdatedAt)
{
    /// <summary>Empty overrides for a draft that has never used source integration.</summary>
    public static AnnouncementSourceOverrides Empty(Guid draftId, Guid owner) =>
        new(draftId, owner, 0, null, [], [], [], [], null, null, DateTimeOffset.UnixEpoch);
}
