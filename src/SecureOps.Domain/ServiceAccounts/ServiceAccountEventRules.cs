namespace SecureOps.Domain.ServiceAccounts;

/// <summary>What happened on an account, as the person reports it (EVENT-GUIDANCE-DESIGN.md §3).</summary>
public enum AccountEventKind
{
    /// <summary>The owner replied.</summary>
    OwnerReplied,
    /// <summary>A person reported that the password was changed (PAM's work; never verified here).</summary>
    PasswordChanged,
    /// <summary>A person reported that the account was deleted.</summary>
    AccountDeleted,
    /// <summary>A person reported that the account was converted to a gMSA.</summary>
    GmsaConverted,
    /// <summary>The owner changed (only a proposal is recorded).</summary>
    OwnerChanged,
    /// <summary>A service using the account moved.</summary>
    ServiceMoved,
    /// <summary>Anything else.</summary>
    Other
}

/// <summary>How the person learned about the event.</summary>
public enum AccountEventChannel
{
    /// <summary>Mail.</summary>
    Mail,
    /// <summary>Phone.</summary>
    Phone,
    /// <summary>Meeting.</summary>
    Meeting,
    /// <summary>Anything else.</summary>
    Other
}

/// <summary>Whether a draft block may or must be sent for an event kind.</summary>
public enum EventBlockRule
{
    /// <summary>The block is refused.</summary>
    NotAllowed,
    /// <summary>The person may send it.</summary>
    Optional,
    /// <summary>The person must send it.</summary>
    Required
}

/// <summary>
/// The records one event kind may produce. Everything that decides what kind of record is written (action type, result, record
/// kind, a fixed mail direction/kind, a new request's type) comes from here, never from the client.
/// </summary>
/// <param name="Kind">Event kind.</param>
/// <param name="Communication">Whether a communication record may or must be written.</param>
/// <param name="Direction">Fixed communication direction; null when the person chooses.</param>
/// <param name="CommunicationKind">Fixed communication kind; null when the person chooses.</param>
/// <param name="ActionType">The action report the event always writes; null when it writes none.</param>
/// <param name="RecordKind">The action's record kind (closure candidate or intermediate step).</param>
/// <param name="References">Whether OR/OCO/JIRA references may be linked.</param>
/// <param name="LinkRequest">Whether an existing open request may be linked (and then optionally closed).</param>
/// <param name="CreateRequestType">The type of the request the person may ask to open; null when none.</param>
/// <param name="Ownership">Whether the event carries (and requires) an ownership proposal.</param>
/// <param name="MovesLastReply">Whether a linked request's last reply date moves forward to the event date.</param>
public sealed record AccountEventShape(
    AccountEventKind Kind,
    EventBlockRule Communication,
    CommunicationDirection? Direction,
    CommunicationKind? CommunicationKind,
    ServiceAccountActionType? ActionType,
    ServiceAccountRecordKind? RecordKind,
    bool References,
    bool LinkRequest,
    ServiceAccountActionType? CreateRequestType,
    bool Ownership,
    bool MovesLastReply);

/// <summary>
/// Event → record table (EVENT-GUIDANCE-DESIGN.md §3, owner defaults S1–S8 of 2026-10-13). Pure and deterministic. An event never
/// verifies, never closes an account and never decides ownership; an action it writes is always Performed (reported, not verified).
/// </summary>
public static class ServiceAccountEventRules
{
    /// <summary>Version of this table; part of every preview digest, so a changed table makes older previews stale.</summary>
    public const string RuleSet = "olay-2c-1";

    /// <summary>The only result an event writes for its action: reported by a person, never verified.</summary>
    public const ServiceAccountActionResult ActionResult = ServiceAccountActionResult.Performed;

    /// <summary>The records <paramref name="kind"/> may produce.</summary>
    public static AccountEventShape Shape(AccountEventKind kind) => kind switch
    {
        AccountEventKind.OwnerReplied => new(kind, EventBlockRule.Required, CommunicationDirection.Incoming, ServiceAccounts.CommunicationKind.Reply,
            null, null, References: true, LinkRequest: true, null, Ownership: false, MovesLastReply: true),
        // S1: a reported password change is an intermediate action report (PAM changes passwords; the product never verifies it here).
        AccountEventKind.PasswordChanged => new(kind, EventBlockRule.Optional, CommunicationDirection.Incoming, ServiceAccounts.CommunicationKind.Information,
            ServiceAccountActionType.PasswordChange, ServiceAccountRecordKind.Intermediate, References: true, LinkRequest: true, null, Ownership: false, MovesLastReply: false),
        // S3: deletion and gMSA conversion are closure candidates; only a later verification (Verify capability, evidence) closes the account.
        AccountEventKind.AccountDeleted => new(kind, EventBlockRule.Optional, null, null,
            ServiceAccountActionType.Deletion, ServiceAccountRecordKind.Closure, References: true, LinkRequest: true, null, Ownership: false, MovesLastReply: false),
        AccountEventKind.GmsaConverted => new(kind, EventBlockRule.Optional, null, null,
            ServiceAccountActionType.GmsaConversion, ServiceAccountRecordKind.Closure, References: true, LinkRequest: true, null, Ownership: false, MovesLastReply: false),
        AccountEventKind.OwnerChanged => new(kind, EventBlockRule.Optional, null, null,
            null, null, References: false, LinkRequest: false, null, Ownership: true, MovesLastReply: false),
        // S7: a moved service is recorded as correspondence; a review request is only opened when the person asks for it.
        AccountEventKind.ServiceMoved => new(kind, EventBlockRule.Required, null, null,
            null, null, References: true, LinkRequest: false, ServiceAccountActionType.Review, Ownership: false, MovesLastReply: false),
        AccountEventKind.Other => new(kind, EventBlockRule.Required, null, null,
            null, null, References: true, LinkRequest: false, ServiceAccountActionType.Evaluate, Ownership: false, MovesLastReply: false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
