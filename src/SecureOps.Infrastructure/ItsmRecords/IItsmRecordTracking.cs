namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Module-independent, read-only tracking summary of one OR by its source record id (ADR-0032). The id comes from
/// a prior <see cref="IItsmRecordLookup"/> Found result (<see cref="ItsmRecord.SourceId"/>). Implementations never
/// write, never return a person field or comment text, and answer Unknown for any part the source could not prove:
/// an absent or partial answer is never presented as "no activity" or "no comment".
/// </summary>
public interface IItsmRecordTracking
{
    /// <summary>
    /// Reads the tracking summary. An invalid id returns Unknown/InvalidIdentifier without a remote call.
    /// Caller cancellation propagates as <see cref="OperationCanceledException"/>; a dependency timeout is Unknown.
    /// </summary>
    public Task<ItsmRecordTrackingResult> GetOrTrackingAsync(string? sourceId, CancellationToken cancellationToken);
}

/// <summary>Whether one part of a tracking summary was proven.</summary>
public enum ItsmTrackingPartState
{
    /// <summary>The source answered completely; <see cref="ItsmTrackingPart{T}.Value"/> is set.</summary>
    Known,
    /// <summary>The source could not prove this part; see the reason. Never means "empty".</summary>
    Unknown
}

/// <summary>One independently proven (or unproven) part of a tracking summary.</summary>
/// <typeparam name="T">Value type of the part.</typeparam>
/// <param name="State">Whether the part was proven.</param>
/// <param name="Reason">Why the part is Unknown; None when Known.</param>
/// <param name="Value">The value when Known.</param>
public sealed record ItsmTrackingPart<T>(ItsmTrackingPartState State, ItsmRecordLookupReason Reason, T? Value)
{
    /// <summary>A proven part.</summary>
    public static ItsmTrackingPart<T> Known(T value) => new(ItsmTrackingPartState.Known, ItsmRecordLookupReason.None, value);

    /// <summary>An unproven part.</summary>
    public static ItsmTrackingPart<T> Unknown(ItsmRecordLookupReason reason) => new(ItsmTrackingPartState.Unknown, reason, default);
}

/// <summary>
/// OR header fields. Values are raw source text: the update time's time-zone semantics are not proven, and status
/// and stage are source codes, not meanings, and are null unless their field names are configured (ADR-0032).
/// The labels are the source's own display text for those two codes (the <c>KEY.</c> cell next to the
/// <c>SET.</c> value, ADR-0032 amendment 2026-10-14), shown beside the code as the source wrote it; they are not
/// a product interpretation and are null when the source sent none. No other display cell is read.
/// </summary>
/// <param name="LastModifiedText">Raw last-modified text (<c>m_last_mod_dt</c>); null when the source left it empty.</param>
/// <param name="RawStatusCode">Raw status code when a status field is configured and filled.</param>
/// <param name="RawStage">Raw stage code when a stage field is configured and filled.</param>
/// <param name="StatusLabel">Source display text of the status code; null when absent, empty or no status field is configured.</param>
/// <param name="StageLabel">Source display text of the stage code; null when absent, empty or no stage field is configured.</param>
/// <param name="RawAssignedGroup">Raw assigned-group code when an assigned-group field is configured and filled (TH-11); the group is a code, never a person.</param>
public sealed record ItsmOrState(
    string? LastModifiedText,
    string? RawStatusCode,
    string? RawStage,
    string? StatusLabel = null,
    string? StageLabel = null,
    string? RawAssignedGroup = null);

/// <summary>
/// One workflow activity of the OR. The person who completed it is never selected. Group and status are source
/// codes; times are raw text.
/// </summary>
/// <param name="ActivityId">Source activity id; orders the list ascending.</param>
/// <param name="RawGroup">Raw group code.</param>
/// <param name="RawStatus">Raw status code.</param>
/// <param name="CreatedText">Raw creation time text.</param>
/// <param name="CompletedText">Raw completion time text; null while the activity is open or the source left it empty.</param>
/// <param name="TaskModelLabel">Source display text of the task model, read only when an activity allow-list is configured (TH-11); a task name, never a person.</param>
public sealed record ItsmActivity(
    string ActivityId,
    string? RawGroup,
    string? RawStatus,
    string? CreatedText,
    string? CompletedText,
    string? TaskModelLabel = null);

/// <summary>Number of comments on the OR and the raw creation time of the newest one. No text, no author.</summary>
/// <param name="Count">Comments returned by a complete, single-page response.</param>
/// <param name="LastCommentText">Raw creation time text of the comment with the highest id; null when there is none or it is empty.</param>
public sealed record ItsmCommentSummary(int Count, string? LastCommentText);

/// <summary>
/// Outcome of one tracking read. <see cref="Status"/> describes the OR header row; <see cref="State"/>,
/// <see cref="Activities"/> and <see cref="Comments"/> are set only when it is Found, and each of the last two
/// carries its own Known/Unknown so a partial answer stays visible.
/// </summary>
/// <param name="SourceId">Normalized source record id; null when the input was invalid.</param>
/// <param name="Status">What the source proved about the OR row.</param>
/// <param name="Reason">Why the OR row result is Unknown; None otherwise.</param>
/// <param name="State">Header fields when Found.</param>
/// <param name="Activities">Workflow activities when Found.</param>
/// <param name="Comments">Comment summary when Found.</param>
/// <param name="Provider">Answering provider (Disabled, Fake or TuruncuHat); Fake results are synthetic evidence.</param>
public sealed record ItsmRecordTrackingResult(
    string? SourceId,
    ItsmRecordLookupStatus Status,
    ItsmRecordLookupReason Reason,
    ItsmOrState? State,
    ItsmTrackingPart<IReadOnlyList<ItsmActivity>>? Activities,
    ItsmTrackingPart<ItsmCommentSummary>? Comments,
    string Provider)
{
    /// <summary>Exactly one OR row matched.</summary>
    public static ItsmRecordTrackingResult Found(
        string sourceId,
        ItsmOrState state,
        ItsmTrackingPart<IReadOnlyList<ItsmActivity>> activities,
        ItsmTrackingPart<ItsmCommentSummary> comments,
        string provider) =>
        new(sourceId, ItsmRecordLookupStatus.Found, ItsmRecordLookupReason.None, state, activities, comments, provider);

    /// <summary>A complete response held no active OR with this id.</summary>
    public static ItsmRecordTrackingResult NotFound(string sourceId, string provider) =>
        new(sourceId, ItsmRecordLookupStatus.NotFound, ItsmRecordLookupReason.None, null, null, null, provider);

    /// <summary>More than one OR row matched.</summary>
    public static ItsmRecordTrackingResult Ambiguous(string sourceId, string provider) =>
        new(sourceId, ItsmRecordLookupStatus.Ambiguous, ItsmRecordLookupReason.None, null, null, null, provider);

    /// <summary>The source could not prove an answer for the OR row.</summary>
    public static ItsmRecordTrackingResult Unknown(string? sourceId, ItsmRecordLookupReason reason, string provider) =>
        new(sourceId, ItsmRecordLookupStatus.Unknown, reason, null, null, null, provider);
}

/// <summary>
/// Strict allow-list for a source record id: 1-20 ASCII digits that are not all zeros, after trimming outer
/// whitespace. Source ids are numeric, so the value is placed in the filter verbatim and cannot widen the match.
/// </summary>
public static class ItsmSourceId
{
    /// <summary>Returns the trimmed id, or null when it is not an acceptable numeric source id.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null || value.Length > ItsmRecordNumber.MaxDigits + 16)
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length is > 0 and <= ItsmRecordNumber.MaxDigits
            && trimmed.All(char.IsAsciiDigit)
            && trimmed.Any(c => c != '0')
                ? trimmed
                : null;
    }
}
