namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Explicit input origin. Actor/time are always assigned by the server, never trusted from requests.</summary>
public sealed record InUseAnswerOrigin(string Kind, string? SourceServerId = null, Guid? ReviewId = null,
    Guid? AcceptedBy = null, string? AcceptedByLabel = null, DateTimeOffset? AcceptedAt = null)
{
    /// <summary>Explicit copy-time value; later source-answer edits do not alter accepted copies.</summary>
    public string? CopiedValue { get; init; }
}

/// <summary>Immutable server-linked review; absent identity cannot be used for cross-record reuse.</summary>
public sealed record InUseServerReview(Guid Id, Guid RecordId, string OrCode, long RecordVersion, long SourceVersion,
    string IdentityKey, string ContextHash, InUseServer Server, IReadOnlyList<InUseAnswer> Answers,
    Guid ReviewerId, string? ReviewerLabel, DateTimeOffset ReviewedAt, DateTimeOffset ObservedAt,
    InUsePolicyProposal? Policy)
{
    /// <summary>Current local invalidation; immutable answers remain available as historical evidence.</summary>
    public bool Invalidated { get; init; }
}

/// <summary>Current observation and a permission-filtered, bounded history page.</summary>
public sealed record InUseServerHistory(InUseServer Current, DateTimeOffset ObservedAt, string? IdentityKey,
    IReadOnlyList<InUseServerReview> Items, int Total, int Page, int PageSize,
    IReadOnlyList<InUseReuseProposal> Proposals);

/// <summary>Prior answers are proposals only; changed/missing context is explicitly blocked.</summary>
public sealed record InUseReuseProposal(Guid ReviewId, bool CanReuse, string Reason, IReadOnlyList<string> ChangedFields);

/// <summary>Per-server source comparison; null means a legacy draft lacks comparison evidence.</summary>
public static class InUseSourceChanges
{
    /// <summary>Inventory lifecycle fences execution, but is not one of the reviewed server-answer inputs.</summary>
    public static bool AffectsReview(string field) => field != "STATUS";
    /// <summary>Compare exact source values/provenance, not record-wide version increments or answer edits.</summary>
    public static IReadOnlyList<string>? Fields(InUseServer current, InUseDraft? draft)
    {
        if (draft?.ReviewedServers is null)
        { return null; }
        InUseServer? previous = draft.ReviewedServers.FirstOrDefault(s => s.Id == current.Id);
        if (previous is null)
        { return ["ServerAdded"]; }
        return current.Fields.Keys.Union(previous.Fields.Keys, StringComparer.Ordinal)
            .Where(key => current.Fields.GetValueOrDefault(key) != previous.Fields.GetValueOrDefault(key))
            .Order(StringComparer.Ordinal).ToArray();
    }
}
