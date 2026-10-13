namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>
/// The pasted reply text (EVENT-GUIDANCE-DESIGN §7). At most 20 000 characters. Never stored, logged or echoed: the answer is built
/// from parsed values only.
/// </summary>
/// <param name="Text">Pasted text.</param>
public sealed record MailHintsRequest(string? Text)
{
    // The framework prints action arguments at Trace level (ToString); the pasted text must never reach a log that way.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Text = [redacted]");
        return true;
    }
}

/// <summary>
/// Field suggestions found by fixed rules (no language model, nothing written). Nothing is pre-selected: the user confirms or corrects
/// every field. An empty list means the product found nothing automatically, not that the mail lacks it.
/// </summary>
/// <param name="RuleSet">Rule table version, e.g. <c>olay-1</c>.</param>
/// <param name="EventSuggestions">Event kinds in rule order.</param>
/// <param name="References">OR/OCO candidates in order of appearance.</param>
/// <param name="Dates">Date candidates in order of appearance; none is selected.</param>
/// <param name="Warnings"><c>PasswordLikeContent</c> (the text may hold a password; it was not kept) or <c>NothingFound</c>.</param>
public sealed record MailHints(string RuleSet, IReadOnlyList<MailHintEvent> EventSuggestions, IReadOnlyList<MailHintReference> References,
    IReadOnlyList<MailHintDate> Dates, IReadOnlyList<string> Warnings);

/// <summary>One suggested event kind.</summary>
/// <param name="EventKind">PasswordChanged, AccountDeleted, GmsaConverted, OwnerChanged or ServiceMoved.</param>
/// <param name="Reason"><c>Matched:&lt;ruleId&gt;</c>.</param>
public sealed record MailHintEvent(string EventKind, string Reason);

/// <summary>One record number candidate; the type comes from the prefix written in the text.</summary>
/// <param name="Type">OR or OCO.</param>
/// <param name="Number">Canonical number with prefix.</param>
/// <param name="Rule">Rule id.</param>
public sealed record MailHintReference(string Type, string Number, string Rule);

/// <summary>One date candidate (never in the future).</summary>
/// <param name="Value">The date.</param>
/// <param name="Kind">Explicit, or AssumedYear when the text had no year (the user must confirm).</param>
/// <param name="Rule">Rule id.</param>
public sealed record MailHintDate(DateOnly Value, string Kind, string Rule);
