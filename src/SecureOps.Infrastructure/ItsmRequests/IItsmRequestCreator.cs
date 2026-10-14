using SecureOps.Infrastructure.ItsmRecords;

namespace SecureOps.Infrastructure.ItsmRequests;

/// <summary>
/// Module-independent write contract: create one ITSM operational request (OR) from a typed draft (ADR-0030 §7, PROPOSED;
/// working name <c>IItsmRequestCreator</c>). OR-2 ships only the Fake and Disabled providers; there is no Turuncu Hat
/// implementation until ADR-0030 is accepted and the Turuncu Hat team's insert contract is evidenced (AGENTS.md rule 8).
/// Implementations never retry a remote write, never assume remote idempotency and report every outcome they could not prove
/// as <see cref="ItsmRequestCreateStatus.OutcomeUnknown"/>, so the caller reconciles instead of creating a second record.
/// </summary>
public interface IItsmRequestCreator
{
    /// <summary>Provider name stored with every submission (Disabled, Fake; later the real adapter's name).</summary>
    public string Provider { get; }

    /// <summary>False for Disabled: callers refuse before writing anything.</summary>
    public bool IsEnabled { get; }

    /// <summary>True when results are synthetic (Fake) and must be labelled as such.</summary>
    public bool IsSynthetic { get; }

    /// <summary>
    /// Creates the request once. Returns NotSent only when nothing was dispatched (the caller may allow a new explicit attempt).
    /// Any doubt after dispatch is OutcomeUnknown. A dependency timeout surfaces as <see cref="TimeoutException"/> or
    /// <see cref="OperationCanceledException"/>, which the caller records as OutcomeUnknown.
    /// </summary>
    public Task<ItsmRequestCreateOutcome> CreateAsync(ItsmRequestDraft draft, CancellationToken cancellationToken);
}

/// <summary>
/// What is sent, exactly as the person previewed it. No credential, session or URL is part of a draft; server-owned catalog,
/// urgency and template property codes belong to the adapter's own configuration.
/// </summary>
/// <param name="RequesterUserId">ITSM user id of the person who pressed the button (reporter and affected end user).</param>
/// <param name="RequesterHomeLocationId">That user's home location id from the mapping; required by the OR form.</param>
/// <param name="ShortDescription">Short description, sent byte for byte.</param>
/// <param name="Description">Description including the plan reference line, sent byte for byte.</param>
/// <param name="Environment">Configured environment value the person chose.</param>
/// <param name="ClientReference">Product reference (plan and account) that also appears as the description's last line.</param>
public sealed record ItsmRequestDraft(
    string RequesterUserId,
    string? RequesterHomeLocationId,
    string ShortDescription,
    string Description,
    string Environment,
    string ClientReference);

/// <summary>Typed outcome of one create call (ADR-0030 §3 states that a provider can prove).</summary>
public enum ItsmRequestCreateStatus
{
    /// <summary>Refused before anything was dispatched; a new explicit attempt is allowed.</summary>
    NotSent,
    /// <summary>The record was inserted and its number read back.</summary>
    Created,
    /// <summary>The record was inserted (source id known) but its number or a property could not be confirmed.</summary>
    PartiallyCreated,
    /// <summary>Dispatched without a provable answer. Never "absent"; reconcile, never resend.</summary>
    OutcomeUnknown
}

/// <summary>Bounded, secret-free reason codes (stored in <c>OutcomeReason</c>, migration 035).</summary>
public enum ItsmRequestCreateReason
{
    /// <summary>No failure.</summary>
    None,
    /// <summary>The provider is Disabled.</summary>
    NotConfigured,
    /// <summary>The requester has no home location; nothing was sent.</summary>
    RequesterLocationMissing,
    /// <summary>Login failed before the insert was dispatched.</summary>
    AuthenticationFailed,
    /// <summary>The dependency did not answer in time after dispatch.</summary>
    Timeout,
    /// <summary>Transport failure or 5xx after dispatch.</summary>
    Unavailable,
    /// <summary>A 4xx or <c>Success=false</c> after dispatch (not proven to mean "nothing written").</summary>
    Rejected,
    /// <summary>The insert answer was malformed or contradictory (for example success without an inserted id).</summary>
    InvalidResponse,
    /// <summary>Inserted, but the record number could not be read back.</summary>
    NumberNotReadBack,
    /// <summary>Inserted, but a template property insert failed or is unknown.</summary>
    PropertyFailed,
    /// <summary>The call ended without an answer for another reason (crash, cancellation after dispatch).</summary>
    DispatchInterrupted
}

/// <summary>Outcome of one create call. <see cref="SourceId"/> and <see cref="RecordNumber"/> are set when proven.</summary>
/// <param name="Status">What the provider proved.</param>
/// <param name="Reason">Why the record is not Created; None when it is.</param>
/// <param name="SourceId">ITSM internal record id (vendor <c>InsertedId</c>).</param>
/// <param name="RecordNumber">OR number read back by internal id.</param>
/// <param name="Provider">Answering provider; Fake results are synthetic.</param>
public sealed record ItsmRequestCreateOutcome(
    ItsmRequestCreateStatus Status,
    ItsmRequestCreateReason Reason,
    string? SourceId,
    string? RecordNumber,
    string Provider)
{
    /// <summary>Nothing was dispatched.</summary>
    public static ItsmRequestCreateOutcome NotSent(ItsmRequestCreateReason reason, string provider) =>
        new(ItsmRequestCreateStatus.NotSent, reason, null, null, provider);

    /// <summary>Dispatched without a provable answer.</summary>
    public static ItsmRequestCreateOutcome Unknown(ItsmRequestCreateReason reason, string provider) =>
        new(ItsmRequestCreateStatus.OutcomeUnknown, reason, null, null, provider);
}

/// <summary>The insert answer as the vendor returns it, reduced to the fields that decide success.</summary>
/// <param name="Success">Vendor <c>Success</c>; null when absent.</param>
/// <param name="InsertedId">Vendor <c>InsertResult.InsertedId</c>; null when absent.</param>
/// <param name="HasErrorText">Whether <c>ErrorDescription</c>/<c>ErrorDetails</c> were filled (their text is never kept).</param>
public sealed record ItsmInsertResponse(bool? Success, string? InsertedId, bool HasErrorText);

/// <summary>
/// The insert success rule every adapter shares (ADR-0032 decision 6, ADR-0030): an insert is proven only by
/// <c>Success=true</c> together with a valid <c>InsertedId</c>. Error text filled next to them is ignored, because the vendor
/// example fills it on success and treating it as failure would invite a duplicate OR on retry. Anything else after dispatch
/// (no id, <c>Success=false</c>, absent fields) is not proven either way and is OutcomeUnknown.
/// </summary>
public static class ItsmInsertRule
{
    /// <summary>The normalized inserted id when the insert is proven; otherwise null (OutcomeUnknown for the caller).</summary>
    public static string? InsertedId(ItsmInsertResponse response) =>
        response.Success == true ? ItsmSourceId.Normalize(response.InsertedId) : null;

    /// <summary>Reason for an unproven insert: Rejected for an explicit <c>Success=false</c>, otherwise InvalidResponse.</summary>
    public static ItsmRequestCreateReason UnprovenReason(ItsmInsertResponse response) =>
        response.Success == false ? ItsmRequestCreateReason.Rejected : ItsmRequestCreateReason.InvalidResponse;
}
