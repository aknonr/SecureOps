using System.Text.RegularExpressions;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>
/// The one rule table behind the mail hints (EVENT-GUIDANCE-DESIGN §7): event phrases, the record-number pattern, month names and the
/// password-like guard. Rules are data; matching is done by <see cref="EventMailHints"/>. Every pattern runs on text that was folded
/// first (lower case, Turkish letters and accents removed: "Şifre" → "sifre", "gMSA'ya geçti" → "gmsa'ya gecti"), is
/// <see cref="RegexOptions.NonBacktracking"/> (linear time, no catastrophic backtracking) and has a hard timeout as a second guard.
/// NonBacktracking has no look-around, so digit boundaries are checked in code. There is no language model and no network call here.
/// </summary>
public static class EventMailHintRules
{
    /// <summary>Version of this rule table, returned with every answer so a suggestion can be traced to a rule set.</summary>
    public const string RuleSet = "olay-1";

    /// <summary>Longest accepted text in characters; longer input is refused before any matching.</summary>
    public const int MaxTextLength = 20_000;

    /// <summary>Most references and most dates returned.</summary>
    public const int MaxResults = 10;

    /// <summary>Hard time limit of every single pattern.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Event kind: the owner reported a password change (a report only; password changes belong to PAM).</summary>
    public const string PasswordChanged = "PasswordChanged";

    /// <summary>Event kind: the account was deleted (reported by a person).</summary>
    public const string AccountDeleted = "AccountDeleted";

    /// <summary>Event kind: the account was converted to a gMSA.</summary>
    public const string GmsaConverted = "GmsaConverted";

    /// <summary>Event kind: the owner changed.</summary>
    public const string OwnerChanged = "OwnerChanged";

    /// <summary>Event kind: the service was moved.</summary>
    public const string ServiceMoved = "ServiceMoved";

    // Subject and action must be at most this many characters apart and in one sentence (a dot between two digits, a date, does not end it).
    /// <summary>Most characters between the subject word and the action word.</summary>
    public const int MaxGap = 70;

    // Words are stems of folded text; the same two word lists are tried in both orders ("parola ... değişti", "değişti ... parola").
    // Turkish consonant softening is part of the stems (hesap/hesabı, sahip/sahibi).
    private const string _password = @"\b(?:parola|sifre|password|passwd|credential)\w*";
    private const string _changed = @"\b(?:degis\w*|degistir\w*|yenile\w*|guncelle\w*|sifirla\w*|reset\w*|chang\w*|updat\w*|rotat\w*|renew\w*)";
    private const string _account = @"\b(?:hesa[pb]\w*|account\w*|svc\S*)";
    private const string _removed = @"\b(?:sil\w*|kaldir\w*|kapat\w*|delet\w*|remov\w*|decommission\w*|disabl\w*)";
    private const string _gmsa = @"\bgmsa\w*";
    private const string _converted = @"\b(?:gec\w*|cevi?r\w*|donus\w*|migrat\w*|convert\w*|switch\w*|transition\w*)";
    private const string _owner = @"\b(?:sahi[pb]\w*|sorumlu\w*|owner\w*)";
    private const string _handed = @"\b(?:degis\w*|devr\w*|transfer\w*|chang\w*|reassign\w*|atan\w*)";
    private const string _moved = @"\b(?:tasin\w*|tasid\w*|tasik\w*|tasiy\w*|tasimis\w*|moved|relocat\w*)";

    /// <summary>One event phrase rule: a stable id, the kind it suggests and the patterns (any one matching is enough).</summary>
    /// <param name="Id">Stable rule id returned as <c>Matched:&lt;Id&gt;</c>.</param>
    /// <param name="Kind">Suggested event kind.</param>
    /// <param name="Subject">Subject words (folded text); also the whole phrase when <paramref name="Action"/> is null.</param>
    /// <param name="Action">Action words that must occur near the subject in the same sentence (either order), or null for a single-word rule.</param>
    public sealed record EventRule(string Id, string Kind, Regex Subject, Regex? Action);

    /// <summary>The event phrase table; the order of the rows is the order of the suggestions.</summary>
    public static readonly IReadOnlyList<EventRule> EventRules =
    [
        new("pwd-changed", PasswordChanged, Build(_password), Build(_changed)),
        new("account-deleted", AccountDeleted, Build(_account), Build(_removed)),
        new("gmsa-converted", GmsaConverted, Build(_gmsa), Build(_converted)),
        new("owner-changed", OwnerChanged, Build(_owner), Build(_handed)),
        new("service-moved", ServiceMoved, Build(_moved), null)
    ];

    /// <summary>
    /// Negation or future marker inside a matched phrase ("değişmedi", "silinmedi", "yapılacak", "will", "not"): a suggestion is only
    /// useful for something reported as done, so such a match is dropped (the user can still pick the event by hand).
    /// </summary>
    public static readonly Regex NotHappened = Build(@"\w*(?:me|ma)(?:di|dik|mis|yecek|yacak)\b|\w*(?:acak|ecek)\b|\b(?:not|never|will|yet|henuz)\b|n't\b");

    /// <summary>
    /// Record number candidates: <c>OCO</c> (with <c>-</c>, <c>_</c>, a space or nothing before the digits) or <c>OR</c> (a dash or underscore is
    /// required, so the English word "or" never matches), then digits and the separators the module allows. A candidate is accepted
    /// only by <see cref="ChangePlanRules.OcoNumber"/> (the module's existing record number format), never by this pattern alone.
    /// </summary>
    public static readonly Regex RecordNumber = Build(@"\b(?<type>oco)[-_ ]?(?<n>[0-9][0-9\-_./]*)|\b(?<type>or)[-_](?<n>[0-9][0-9\-_./]*)");

    /// <summary>Dates written day.month.year or day/month/year.</summary>
    public static readonly Regex DayMonthYear = Build(@"(?<d>\d{1,2})[./](?<m>\d{1,2})[./](?<y>\d{4})");

    /// <summary>Dates written year-month-day.</summary>
    public static readonly Regex IsoDate = Build(@"(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})");

    /// <summary>Dates with a month name, with or without a year ("1 Ekim 2026", "1 October 2026", "1 Ekim").</summary>
    public static readonly Regex DayMonthName = Build(@"(?<d>\d{1,2})(?:\s*(?:inci|nci|uncu|st|nd|rd|th))?\s+(?<m>[a-z]{3,9})\b(?:,?\s+(?<y>\d{4}))?");

    /// <summary>Dates with the month first and a year ("October 1, 2026").</summary>
    public static readonly Regex MonthNameDay = Build(@"\b(?<m>[a-z]{3,9})\s+(?<d>\d{1,2})(?:st|nd|rd|th)?,?\s+(?<y>\d{4})");

    /// <summary>Folded month names (Turkish and English) to month numbers.</summary>
    public static readonly IReadOnlyDictionary<string, int> Months = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["ocak"] = 1,
        ["subat"] = 2,
        ["mart"] = 3,
        ["nisan"] = 4,
        ["mayis"] = 5,
        ["haziran"] = 6,
        ["temmuz"] = 7,
        ["agustos"] = 8,
        ["eylul"] = 9,
        ["ekim"] = 10,
        ["kasim"] = 11,
        ["aralik"] = 12,
        ["january"] = 1,
        ["february"] = 2,
        ["march"] = 3,
        ["april"] = 4,
        ["may"] = 5,
        ["june"] = 6,
        ["july"] = 7,
        ["august"] = 8,
        ["september"] = 9,
        ["october"] = 10,
        ["november"] = 11,
        ["december"] = 12,
        ["jan"] = 1,
        ["feb"] = 2,
        ["mar"] = 3,
        ["apr"] = 4,
        ["jun"] = 6,
        ["jul"] = 7,
        ["aug"] = 8,
        ["sep"] = 9,
        ["sept"] = 9,
        ["oct"] = 10,
        ["nov"] = 11,
        ["dec"] = 12
    };

    /// <summary>A password keyword followed by <c>:</c> or <c>=</c> and a value token; the value is inspected but never returned.</summary>
    public static readonly Regex PasswordAssignment = Build(@"\b(?:parola|sifre|password|passwd|pwd|pass)\s{0,3}[:=]\s{0,3}(?<v>\S{6,128})");

    /// <summary>Compiles one pattern over folded text with the shared options and timeout.</summary>
    public static Regex Build(string pattern) =>
        new(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, MatchTimeout);
}
