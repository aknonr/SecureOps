namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Module-independent, read-only lookup of one ITSM record by its number (ADR-0031). Turuncu Hat is the first
/// adapter; a later ITSM gets a new adapter behind the same contract. Implementations never write to the ITSM,
/// never return person fields, and answer <see cref="ItsmRecordLookupStatus.Unknown"/> whenever the source could
/// not prove an answer: "not found" is only reported for a complete, successful response with zero rows.
/// </summary>
public interface IItsmRecordLookup
{
    /// <summary>
    /// Looks up one record. An invalid number returns Unknown/InvalidNumber without a remote call.
    /// Caller cancellation propagates as <see cref="OperationCanceledException"/>; a dependency timeout is Unknown.
    /// </summary>
    public Task<ItsmRecordLookupResult> LookupAsync(ItsmRecordKind kind, string? number, CancellationToken cancellationToken);
}

/// <summary>Record types the lookup supports.</summary>
public enum ItsmRecordKind
{
    /// <summary>Operational request (OR).</summary>
    Or,
    /// <summary>Operational change (OCO).</summary>
    Oco
}

/// <summary>What the source proved about the requested number.</summary>
public enum ItsmRecordLookupStatus
{
    /// <summary>Exactly one active record with this number exists.</summary>
    Found,
    /// <summary>A complete, successful response contained no active record with this number.</summary>
    NotFound,
    /// <summary>More than one active record matched; none is chosen.</summary>
    Ambiguous,
    /// <summary>The source could not prove an answer; see <see cref="ItsmRecordLookupReason"/>. Never means "absent".</summary>
    Unknown
}

/// <summary>Bounded, secret-free reason for an Unknown result. None for the other statuses.</summary>
public enum ItsmRecordLookupReason
{
    /// <summary>No failure.</summary>
    None,
    /// <summary>The number failed the strict format allow-list; nothing was sent.</summary>
    InvalidNumber,
    /// <summary>The lookup provider is Disabled or not configured.</summary>
    NotConfigured,
    /// <summary>Transport failure or a 5xx response.</summary>
    Unavailable,
    /// <summary>The dependency did not answer within the configured request timeout.</summary>
    Timeout,
    /// <summary>Login failed, or the query was rejected again after one controlled re-login.</summary>
    AuthenticationFailed,
    /// <summary>A 4xx response or an application error in the query envelope.</summary>
    Rejected,
    /// <summary>More pages, malformed rows or rows for another number: the answer is not complete.</summary>
    IncompleteResponse,
    /// <summary>The response was oversized, not JSON, or outside the reviewed envelope.</summary>
    InvalidResponse,
    /// <summary>A source record id or user name failed the strict allow-list; nothing was sent (ADR-0032).</summary>
    InvalidIdentifier
}

/// <summary>
/// The allow-listed fields of one record. Values are raw source values: status, stage and group are source
/// codes, not meanings, until an owner-approved code list exists (ADR-0031 decision 4). Window values are the
/// source text unchanged; their time-zone semantics are not proven. No person field is ever carried.
/// </summary>
/// <param name="SourceId">Source system identifier of the record.</param>
/// <param name="Code">Record number as the source returned it.</param>
/// <param name="Title">HTML-decoded, trimmed title; may be empty.</param>
/// <param name="RawStatusCode">Raw status code (OCO only; not selected for OR until evidenced).</param>
/// <param name="RawStage">Raw stage code (OCO only).</param>
/// <param name="RawGroup">Raw related group code.</param>
/// <param name="ProposedStartText">Raw proposed window start (OCO only).</param>
/// <param name="ProposedFinishText">Raw proposed window finish (OCO only).</param>
public sealed record ItsmRecord(
    string SourceId,
    string Code,
    string Title,
    string? RawStatusCode,
    string? RawStage,
    string? RawGroup,
    string? ProposedStartText,
    string? ProposedFinishText);

/// <summary>Outcome of one lookup. <see cref="Record"/> is set only when <see cref="Status"/> is Found.</summary>
/// <param name="Kind">Requested record type.</param>
/// <param name="Number">Normalized requested number; null when the input was invalid.</param>
/// <param name="Status">What the source proved.</param>
/// <param name="Reason">Why the result is Unknown; None otherwise.</param>
/// <param name="Record">The single matching record when Found.</param>
/// <param name="Provider">Answering provider (Disabled, Fake or TuruncuHat); Fake results are synthetic evidence.</param>
public sealed record ItsmRecordLookupResult(
    ItsmRecordKind Kind,
    string? Number,
    ItsmRecordLookupStatus Status,
    ItsmRecordLookupReason Reason,
    ItsmRecord? Record,
    string Provider)
{
    /// <summary>Exactly one record matched.</summary>
    public static ItsmRecordLookupResult Found(ItsmRecordKind kind, string number, ItsmRecord record, string provider) =>
        new(kind, number, ItsmRecordLookupStatus.Found, ItsmRecordLookupReason.None, record, provider);

    /// <summary>A complete response held no matching record.</summary>
    public static ItsmRecordLookupResult NotFound(ItsmRecordKind kind, string number, string provider) =>
        new(kind, number, ItsmRecordLookupStatus.NotFound, ItsmRecordLookupReason.None, null, provider);

    /// <summary>More than one record matched.</summary>
    public static ItsmRecordLookupResult Ambiguous(ItsmRecordKind kind, string number, string provider) =>
        new(kind, number, ItsmRecordLookupStatus.Ambiguous, ItsmRecordLookupReason.None, null, provider);

    /// <summary>The source could not prove an answer.</summary>
    public static ItsmRecordLookupResult Unknown(ItsmRecordKind kind, string? number, ItsmRecordLookupReason reason, string provider) =>
        new(kind, number, ItsmRecordLookupStatus.Unknown, reason, null, provider);
}
