using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Narrow, read-only classification of one OR for the In Use workflow (TH-11, ADR-0032 amendment 2026-10-17).
/// It answers three independent questions from the source and never writes. Every class the source could not
/// prove is Unknown; an unreadable source is never "no activity" and never hides a record.
/// </summary>
public interface IItsmWasasTracking
{
    /// <summary>
    /// Classifies the OR by its source record id. An invalid id is Unknown/InvalidIdentifier without a remote call.
    /// Caller cancellation propagates as <see cref="OperationCanceledException"/>; a dependency timeout is Unknown.
    /// </summary>
    public Task<ItsmWasasClassification> ClassifyAsync(string? sourceId, CancellationToken cancellationToken);
}

/// <summary>State of the one WASAS review activity row.</summary>
public enum ItsmWasasActivityClass
{
    /// <summary>One WASAS-group row, completion time filled and status equal to the configured approved code.</summary>
    Completed,
    /// <summary>The same single row with no completion time yet.</summary>
    Pending,
    /// <summary>The row is completed but its status is not the approved code (rejected, sent back or an unknown code).</summary>
    NotApproved,
    /// <summary>Not proven: missing, several candidate rows, incomplete or failed read, or no activity filter configured.</summary>
    Unknown
}

/// <summary>Whether the OR is closed in the source, by the configured stage and status ids.</summary>
public enum ItsmOrClosureClass
{
    /// <summary>The stage or status equals a configured closed id.</summary>
    Closed,
    /// <summary>Every configured closed list was compared against a filled value and none matched.</summary>
    Open,
    /// <summary>Not proven (nothing configured, value empty, or the read failed).</summary>
    Unknown
}

/// <summary>Whether the OR is assigned to the WASAS group in the source. Not proof of approval: sending back also changes the group.</summary>
public enum ItsmAssignmentClass
{
    /// <summary>The assigned group equals the configured WASAS group id.</summary>
    AssignedToWasas,
    /// <summary>A filled assigned group differs from the configured WASAS group id.</summary>
    NotAssignedToWasas,
    /// <summary>Not proven (nothing configured, group empty, or the read failed).</summary>
    Unknown
}

/// <summary>
/// Server-side classification. It carries no person, comment text, record id or raw source value.
/// </summary>
/// <param name="Activity">State of the WASAS review activity.</param>
/// <param name="Closure">Whether the OR is closed in the source.</param>
/// <param name="Assignment">Whether the OR is assigned to the WASAS group.</param>
/// <param name="ActivityReason">Why <see cref="Activity"/> is Unknown; None otherwise.</param>
/// <param name="Provider">Answering provider (Disabled, Fake or TuruncuHat); Fake results are synthetic evidence.</param>
public sealed record ItsmWasasClassification(
    ItsmWasasActivityClass Activity,
    ItsmOrClosureClass Closure,
    ItsmAssignmentClass Assignment,
    ItsmRecordLookupReason ActivityReason,
    string Provider)
{
    /// <summary>Nothing proven.</summary>
    public static ItsmWasasClassification Unknown(ItsmRecordLookupReason reason, string provider) =>
        new(ItsmWasasActivityClass.Unknown, ItsmOrClosureClass.Unknown, ItsmAssignmentClass.Unknown, reason, provider);
}

/// <summary>
/// The classification rules (TH-11), pure and deterministic. Configuration supplies every code; none is built in
/// except the documented default of the approved-status code in the options.
/// </summary>
public static class ItsmWasasClassifier
{
    /// <summary>Classifies from a successfully read OR header and its activity part.</summary>
    public static ItsmWasasClassification Classify(
        ItsmRecordLookupOptions options,
        ItsmOrState state,
        ItsmTrackingPart<IReadOnlyList<ItsmActivity>> activities,
        string provider)
    {
        (ItsmWasasActivityClass activity, ItsmRecordLookupReason reason) = ClassifyActivity(options, activities);
        return new(activity, ClassifyClosure(options, state), ClassifyAssignment(options, state), reason, provider);
    }

    private static ItsmOrClosureClass ClassifyClosure(ItsmRecordLookupOptions options, ItsmOrState state)
    {
        string[] stages = Ids(options.ClosedStageIds);
        string[] statuses = Ids(options.ClosedStatusIds);
        if (stages.Length == 0 && statuses.Length == 0)
        {
            return ItsmOrClosureClass.Unknown;
        }

        if ((stages.Length > 0 && Contains(stages, state.RawStage)) || (statuses.Length > 0 && Contains(statuses, state.RawStatusCode)))
        {
            return ItsmOrClosureClass.Closed;
        }

        // Open needs a filled value for every configured list: an empty cell proves nothing.
        bool stageComparable = stages.Length == 0 || !string.IsNullOrEmpty(state.RawStage);
        bool statusComparable = statuses.Length == 0 || !string.IsNullOrEmpty(state.RawStatusCode);
        return stageComparable && statusComparable ? ItsmOrClosureClass.Open : ItsmOrClosureClass.Unknown;
    }

    private static ItsmAssignmentClass ClassifyAssignment(ItsmRecordLookupOptions options, ItsmOrState state)
    {
        string wasas = options.WasasGroupId?.Trim() ?? string.Empty;
        if (wasas.Length == 0 || string.IsNullOrWhiteSpace(options.OrAssignedGroupField) || string.IsNullOrEmpty(state.RawAssignedGroup))
        {
            return ItsmAssignmentClass.Unknown;
        }

        return string.Equals(state.RawAssignedGroup.Trim(), wasas, StringComparison.Ordinal)
            ? ItsmAssignmentClass.AssignedToWasas
            : ItsmAssignmentClass.NotAssignedToWasas;
    }

    private static (ItsmWasasActivityClass, ItsmRecordLookupReason) ClassifyActivity(
        ItsmRecordLookupOptions options, ItsmTrackingPart<IReadOnlyList<ItsmActivity>> activities)
    {
        string wasas = options.WasasGroupId?.Trim() ?? string.Empty;
        string approved = options.ActivityApprovedStatus?.Trim() ?? string.Empty;
        string[] models = [.. (options.ActivityTaskModelContains ?? []).Select(fragment => fragment?.Trim() ?? string.Empty).Where(fragment => fragment.Length > 0)];
        if (wasas.Length == 0 || approved.Length == 0 || models.Length == 0)
        {
            return (ItsmWasasActivityClass.Unknown, ItsmRecordLookupReason.NotConfigured);
        }

        if (activities.State != ItsmTrackingPartState.Known || activities.Value is null)
        {
            return (ItsmWasasActivityClass.Unknown, activities.Reason == ItsmRecordLookupReason.None ? ItsmRecordLookupReason.IncompleteResponse : activities.Reason);
        }

        ItsmActivity[] candidates =
        [
            .. activities.Value.Where(activity =>
                string.Equals(activity.RawGroup?.Trim(), wasas, StringComparison.Ordinal)
                && activity.TaskModelLabel is { Length: > 0 } label
                && models.Any(fragment => label.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        ];

        // Zero rows is not "no activity" (the source may simply not show it) and several rows are ambiguous.
        if (candidates.Length != 1)
        {
            return (ItsmWasasActivityClass.Unknown, ItsmRecordLookupReason.IncompleteResponse);
        }

        ItsmActivity row = candidates[0];
        if (string.IsNullOrEmpty(row.CompletedText))
        {
            return (ItsmWasasActivityClass.Pending, ItsmRecordLookupReason.None);
        }

        if (string.IsNullOrEmpty(row.RawStatus))
        {
            return (ItsmWasasActivityClass.Unknown, ItsmRecordLookupReason.InvalidResponse);
        }

        return string.Equals(row.RawStatus.Trim(), approved, StringComparison.Ordinal)
            ? (ItsmWasasActivityClass.Completed, ItsmRecordLookupReason.None)
            : (ItsmWasasActivityClass.NotApproved, ItsmRecordLookupReason.None);
    }

    private static string[] Ids(string[]? values) =>
        [.. (values ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0)];

    private static bool Contains(string[] ids, string? raw) =>
        !string.IsNullOrEmpty(raw) && ids.Contains(raw.Trim(), StringComparer.Ordinal);
}
