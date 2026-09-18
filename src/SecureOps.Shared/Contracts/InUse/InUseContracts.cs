using System.Text.Json.Serialization;

namespace SecureOps.Shared.Contracts.InUse;

/// <summary>A value and its exact source; null is unresolved, never inferred.</summary>
public sealed record InUseEvidence(string? Value, string Source);

/// <summary>One proposed field; null means classification or approved configuration is still missing.</summary>
public sealed record InUsePolicyField(string ServerId, string Field, string? Value, string Origin);

/// <summary>Version-bound proposals reviewed explicitly; not successful checks or source ownership.</summary>
public sealed record InUsePolicyProposal(string Revision, string Fingerprint, IReadOnlyList<InUsePolicyField> Fields);

/// <summary>One related server, retaining independent service/environment evidence.</summary>
public sealed record InUseServer(string Id, IReadOnlyDictionary<string, InUseEvidence> Fields)
{
    /// <summary>Per-service-item RFC reporter observation from explicit refresh, separate from ownership and assignment.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InUseRelatedRequestReporter? RelatedRequestReporter { get; init; }
}

/// <summary>Read-only source snapshot; relationships are distinct from local assignment.</summary>
public sealed record InUseSource(string Id, string Code, string Title, InUseEvidence Requester,
    InUseEvidence ServiceOwner, InUseEvidence ProvisioningTeam, IReadOnlyList<InUseServer> Servers,
    string RelationshipEvidence, bool Synthetic)
{
    /// <summary>Trusted provider/tenant namespace; legacy unknown namespaces are never inferred from hostnames.</summary>
    public string? IdentityScope { get; init; }
    /// <summary>Complete, Observed (completeness unverified), Partial, NotQueried, Forbidden, Failed or Ambiguous.</summary>
    public string ServiceItemsState { get; init; } = "NotQueried";
    /// <summary>A separate relationship, never populated from service items.</summary>
    public string AffectedAssetsState { get; init; } = "NotQueried";
    /// <summary>Null until the affected-assets relationship is independently verified.</summary>
    public int? AffectedAssetCount { get; init; }
    /// <summary>Technical source creator, not requester, Virtual PC User or reviewer.</summary>
    public InUseEvidence? Creator { get; init; }
    /// <summary>Verified parent OR creation as ISO-8601 plus provenance; never a referenced request date.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InUseEvidence? Creation { get; init; }
    /// <summary>Verified parent lifecycle Open/Closed plus provenance; absent is unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InUseEvidence? Lifecycle { get; init; }
}

/// <summary>One explicitly reviewed technical answer. Unknown is a first-class value.</summary>
public sealed record InUseAnswer(string ServerId, string Check, string Value, string Evidence)
{
    /// <summary>Individual, Bulk or PreviousReview, with trusted acceptance provenance after save.</summary>
    public InUseAnswerOrigin? Origin { get; init; }
}

/// <summary>Saved review bound to the source version, with server-authenticated provenance.</summary>
public sealed record InUseDraft(long SourceVersion, IReadOnlyList<InUseAnswer> Answers, string Notes,
    Guid ReviewedBy, DateTimeOffset ReviewedAt)
{
    /// <summary>Trusted profile-at-save display. Historical missing labels are not invented.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewedByLabel { get; init; }
    /// <summary>Server-generated proposal snapshot accepted by the saving actor.</summary>
    public InUsePolicyProposal? Policy { get; init; }
}

/// <summary>Persisted independent In Use aggregate.</summary>
public sealed record InUseRecord(Guid Id, InUseSource Source, string SourceHash, long SourceVersion,
    long Version, Guid? AssigneeId, string? AssigneeLabel, InUseDraft? Draft, DateTimeOffset LastSeenAt)
{
    /// <summary>Authenticated actor who last changed the optional assignment, not the reviewer or source closer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AssignedBy { get; init; }
    /// <summary>Trusted profile-at-assignment display.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssignedByLabel { get; init; }
    /// <summary>Server UTC assignment/unassignment time, absent for historical unknown actions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? AssignedAt { get; init; }
    /// <summary>Local review state, not authoritative source or BPM state.</summary>
    public string Status => Draft is null ? "Unreviewed" : Draft.SourceVersion == SourceVersion ? "Draft" : "Stale";
    /// <summary>Available historical archive versions; populated only by the detail service.</summary>
    public IReadOnlyList<long> ArchivedVersions { get; init; } = [];
    /// <summary>Local first observation only; old aggregates without this evidence remain null.</summary>
    public DateTimeOffset? FirstSeenAt { get; init; }
    /// <summary>Last locally confirmed completion intent, never proof of an external operation.</summary>
    public InUseCompletion? Completion { get; init; }
    /// <summary>Current proposal for explicit review; supplied on detail reads, not persisted as source evidence.</summary>
    public InUsePolicyProposal? PolicyProposal { get; init; }
}

/// <summary>Bounded persisted query; mine is resolved from the authenticated user.</summary>
public sealed record InUseQuery(string? Search = null, string View = "all", string? Status = null, int Page = 1, int PageSize = 25);

/// <summary>Refresh health independent of retained records.</summary>
public sealed record InUseRefreshState(long Version, DateTimeOffset? LastAttemptAt, DateTimeOffset? LastSuccessfulAt,
    bool Complete, string? Issue)
{
    /// <summary>Initial state before any operator-triggered refresh.</summary>
    public static InUseRefreshState Empty => new(0, null, null, false, "NotRefreshed");
    /// <summary>A local freshness indication, never proof that source state remains unchanged.</summary>
    public bool Stale => LastSuccessfulAt is null || LastSuccessfulAt < DateTimeOffset.UtcNow.AddHours(-24) || Issue is not null;
}

/// <summary>Consistent persisted page and refresh metadata.</summary>
public sealed record InUsePage(IReadOnlyList<InUseRecord> Items, int Total, int Page, int PageSize, InUseRefreshState Refresh);

/// <summary>Explicit operator refresh command identifier.</summary>
public sealed record RefreshInUseRequest(Guid CommandId);

/// <summary>Manual assignment to an approved application identity, or explicit unassignment.</summary>
public sealed record AssignInUseRequest(long ExpectedVersion, Guid? AssigneeId, string Reason);

/// <summary>Replace a review draft without altering source data.</summary>
public sealed record SaveInUseDraftRequest(long ExpectedVersion, long SourceVersion, IReadOnlyList<InUseAnswer> Answers, string Notes)
{
    /// <summary>Only the exact displayed proposal may be accepted. Null retains the prior source-bound snapshot.</summary>
    public string? ReviewedPolicyFingerprint { get; init; }
}

/// <summary>Export only the exact reviewed aggregate shown to the operator.</summary>
public sealed record ExportInUseRequest(long ExpectedVersion, bool Archive = false, long? ArchivedVersion = null);

/// <summary>One explicitly authorized source identity, cross-checked against the stored record.</summary>
public sealed record InUseDiagnosticRequest(long ExpectedVersion, string SourceId);

/// <summary>Minimal authorized assignment-picker projection, not directory search.</summary>
public sealed record InUseAssignee(Guid Id, string Label);

/// <summary>Exact worksheet preview, shared with the generated workbook.</summary>
public sealed record InUseSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Version-bound local preparation, never an uploaded or approved source attachment.</summary>
public sealed record InUseReport(Guid RecordId, long Version, long SourceVersion, string Sha256,
    string FileName, byte[] Content, IReadOnlyList<InUseSheet> Sheets)
{
    /// <summary>Verified application actor, never supplied by the caller.</summary>
    public Guid PreparedBy { get; init; }
    /// <summary>Trusted profile snapshot; absent on historical archives.</summary>
    public string? PreparedByLabel { get; init; }
    /// <summary>Preparation timestamp retained on repeated archive requests.</summary>
    public DateTimeOffset PreparedAt { get; init; }
    /// <summary>Exact source identity bound to this artifact.</summary>
    public string SourceId { get; init; } = "";
    /// <summary>True only after durable archive commit or verified archive read.</summary>
    public bool Archived { get; init; }
    /// <summary>Workbook byte count, independently checked when reading the archive.</summary>
    public long Size { get; init; }
}

/// <summary>Stable check codes shared by validation, workbook and UI.</summary>
public static class InUseChecks
{
    /// <summary>Local report readiness never asserts global source completeness or authorizes source closure.</summary>
    public static bool RelationshipReady(InUseSource source) => source.ServiceItemsState is "Complete" or "Observed" && source.Servers.Count > 0;
    /// <summary>The three legacy operator questions; other checks are historical evidence only.</summary>
    public static IReadOnlyList<string> OperatorCodes { get; } = ["InternetOut", "InternetIn", "Microsegmented"];
    /// <summary>First missing required answer, shared by UI and server readiness validation.</summary>
    public static InUseAnswer? Missing(InUseSource source, IReadOnlyList<InUseAnswer> answers) =>
        source.Servers.SelectMany(s => OperatorCodes.Select(c => new InUseAnswer(s.Id, c, "Unknown", "")))
            .FirstOrDefault(required => !answers.Any(a => a.ServerId == required.ServerId && a.Check == required.Check && a.Value is "Yes" or "No"));
    /// <summary>Allowed independently reviewed values.</summary>
    public static IReadOnlyList<string> Values { get; } = ["Unknown", "Yes", "No", "NotApplicable"];
    /// <summary>No check defaults to a successful result.</summary>
    public static IReadOnlyList<string> Codes { get; } = ["InternetOut", "InternetIn", "Microsegmented", "NmsRequested", "MemoryAlarm", "CpuAlarm", "UpDownAlarm", "DiskAlarm", "Verified"];
}
