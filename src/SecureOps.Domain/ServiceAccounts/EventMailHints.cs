using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>An event suggestion with the rule that produced it.</summary>
/// <param name="EventKind">Suggested event kind name.</param>
/// <param name="Reason"><c>Matched:&lt;ruleId&gt;</c>.</param>
public sealed record HintedEvent(string EventKind, string Reason);

/// <summary>A record number candidate.</summary>
/// <param name="Type"><c>OR</c> or <c>OCO</c>, taken from the prefix written in the text.</param>
/// <param name="Number">Canonical upper-case number including its prefix, in the module's record number format.</param>
/// <param name="Rule">Rule id.</param>
public sealed record HintedReference(string Type, string Number, string Rule);

/// <summary>A date candidate.</summary>
/// <param name="Value">The date.</param>
/// <param name="Kind"><c>Explicit</c> (the text names the year) or <c>AssumedYear</c> (current year assumed; the user must confirm).</param>
/// <param name="Rule">Rule id.</param>
public sealed record HintedDate(DateOnly Value, string Kind, string Rule);

/// <summary>The whole answer: suggestions only, nothing selected, nothing quoted from the text.</summary>
/// <param name="RuleSet">Rule table version.</param>
/// <param name="Events">Event suggestions in rule order.</param>
/// <param name="References">Record number candidates in order of appearance.</param>
/// <param name="Dates">Date candidates in order of appearance.</param>
/// <param name="Warnings"><c>PasswordLikeContent</c> or <c>NothingFound</c>.</param>
public sealed record MailHintResult(string RuleSet, IReadOnlyList<HintedEvent> Events, IReadOnlyList<HintedReference> References,
    IReadOnlyList<HintedDate> Dates, IReadOnlyList<string> Warnings);

/// <summary>
/// Deterministic field suggestions from a pasted reply (EVENT-GUIDANCE-DESIGN §7): record numbers, dates and event phrases found by the
/// rule table in <see cref="EventMailHintRules"/>. Pure function: no I/O, no clock besides the supplied date, no language model. It never
/// searches for or returns person names, e-mail addresses, phone numbers or signatures, and never copies any part of the text into the
/// answer: a number or date is re-built from parsed values, and a password-like fragment only raises a warning.
/// </summary>
public static class EventMailHints
{
    /// <summary>Warning: the text holds something that may be a password; it is neither returned nor stored.</summary>
    public const string PasswordLikeContent = "PasswordLikeContent";

    /// <summary>Warning: no rule matched (the product could not find anything; this says nothing about the mail).</summary>
    public const string NothingFound = "NothingFound";

    /// <summary>Suggests fields for <paramref name="text"/>. Text longer than <see cref="EventMailHintRules.MaxTextLength"/> yields null (refuse before matching).</summary>
    /// <param name="text">The pasted text; never stored or logged by this method.</param>
    /// <param name="today">The current business date: later dates are never suggested and year-less dates assume this year.</param>
    public static MailHintResult? Analyze(string? text, DateOnly today)
    {
        if (text is null || text.Length > EventMailHintRules.MaxTextLength)
        {
            return null;
        }

        string folded = Fold(text);
        List<HintedEvent> events = [];
        foreach (EventMailHintRules.EventRule rule in EventMailHintRules.EventRules)
        {
            if (HappenedPhrase(rule, folded))
            {
                events.Add(new HintedEvent(rule.Kind, "Matched:" + rule.Id));
            }
        }

        IReadOnlyList<HintedReference> references = References(folded);
        IReadOnlyList<HintedDate> dates = Dates(folded, today);
        List<string> warnings = [];
        if (LooksLikePassword(text, folded))
        {
            warnings.Add(PasswordLikeContent);
        }

        if (events.Count == 0 && references.Count == 0 && dates.Count == 0)
        {
            warnings.Add(NothingFound);
        }

        return new MailHintResult(EventMailHintRules.RuleSet, events, references, dates, warnings);
    }

    /// <summary>
    /// Lower case with accents and Turkish letters removed (ı→i, İ→i, ş→s, ğ→g, ç→c, ö→o, ü→u); digits and ASCII are unchanged.
    /// Used for matching only, so character positions are irrelevant.
    /// </summary>
    public static string Fold(string text)
    {
        // Normalize throws on an unpaired surrogate; such characters are dropped first (hostile input must not become a server error).
        StringBuilder clean = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                clean.Append(text[i]).Append(text[++i]);
            }
            else if (!char.IsSurrogate(text[i]))
            {
                clean.Append(text[i]);
            }
        }

        StringBuilder builder = new(text.Length);
        foreach (char c in clean.ToString().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c switch { 'ı' or 'İ' => 'i', 'ß' => 's', _ => char.ToLowerInvariant(c) });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the rule's subject and action words occur close together (at most <see cref="EventMailHintRules.MaxGap"/> characters apart,
    /// either order, one sentence) in a phrase that does not say it did not happen or has not happened yet. A rule without an action
    /// matches on its single word. Proximity is checked here, not in the pattern, to keep every pattern small and linear.
    /// </summary>
    private static bool HappenedPhrase(EventMailHintRules.EventRule rule, string folded)
    {
        if (rule.Action is null)
        {
            return rule.Subject.Matches(folded).Any(m => !EventMailHintRules.NotHappened.IsMatch(m.ValueSpan));
        }

        Match[] subjects = [.. rule.Subject.Matches(folded).Take(200)];
        Match[] actions = [.. rule.Action.Matches(folded).Take(200)];
        foreach (Match subject in subjects)
        {
            foreach (Match action in actions)
            {
                Match first = subject.Index <= action.Index ? subject : action;
                Match second = ReferenceEquals(first, subject) ? action : subject;
                int gapStart = first.Index + first.Length;
                if (second.Index < gapStart || second.Index - gapStart > EventMailHintRules.MaxGap
                    || !OneSentence(folded.AsSpan(gapStart, second.Index - gapStart)))
                {
                    continue;
                }

                // The negation / future check covers the whole phrase, both words and what lies between them.
                if (!EventMailHintRules.NotHappened.IsMatch(folded.AsSpan(first.Index, second.Index + second.Length - first.Index)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>No sentence end in the text; a dot between two digits (a date such as 01.10.2026) is not a sentence end.</summary>
    private static bool OneSentence(ReadOnlySpan<char> gap)
    {
        for (int i = 0; i < gap.Length; i++)
        {
            if (gap[i] is '!' or '?' or '\n' || gap[i] == '.' && !(i > 0 && i + 1 < gap.Length && char.IsAsciiDigit(gap[i - 1]) && char.IsAsciiDigit(gap[i + 1])))
            {
                return false;
            }
        }

        return true;
    }

    private static List<HintedReference> References(string folded)
    {
        List<HintedReference> found = [];
        foreach (Match match in EventMailHintRules.RecordNumber.Matches(folded))
        {
            string type = match.Groups["type"].Value.ToUpperInvariant();
            string digits = match.Groups["n"].Value.TrimEnd('-', '_', '.', '/');

            // The module's own record number format decides; this method only finds candidates.
            if (digits.Length == 0 || ChangePlanRules.OcoNumber(type + "-" + digits) is not { } number
                || found.Any(r => r.Number == number))
            {
                continue;
            }

            found.Add(new HintedReference(type, number, "record-" + type.ToLowerInvariant()));
            if (found.Count == EventMailHintRules.MaxResults)
            {
                break;
            }
        }

        return found;
    }

    private static List<HintedDate> Dates(string folded, DateOnly today)
    {
        List<(int Index, HintedDate Date)> found = [];
        void Add(int index, DateOnly? value, string kind, string rule)
        {
            if (value is { } date && date <= today && found.All(f => f.Date.Value != date))
            {
                found.Add((index, new HintedDate(date, kind, rule)));
            }
        }

        foreach (Match m in EventMailHintRules.DayMonthYear.Matches(folded))
        {
            Add(m.Index, Build(folded, m, Int(m, "y"), Int(m, "m"), Int(m, "d")), "Explicit", "date-numeric");
        }

        foreach (Match m in EventMailHintRules.IsoDate.Matches(folded))
        {
            Add(m.Index, Build(folded, m, Int(m, "y"), Int(m, "m"), Int(m, "d")), "Explicit", "date-iso");
        }

        foreach (Match m in EventMailHintRules.DayMonthName.Matches(folded))
        {
            if (!EventMailHintRules.Months.TryGetValue(m.Groups["m"].Value, out int month))
            {
                continue;
            }

            bool hasYear = m.Groups["y"].Success;
            DateOnly? value = Build(folded, m, hasYear ? Int(m, "y") : today.Year, month, Int(m, "d"));
            Add(m.Index, value, hasYear ? "Explicit" : "AssumedYear", hasYear ? "date-named" : "date-named-no-year");
        }

        foreach (Match m in EventMailHintRules.MonthNameDay.Matches(folded))
        {
            if (EventMailHintRules.Months.TryGetValue(m.Groups["m"].Value, out int month))
            {
                Add(m.Index, Build(folded, m, Int(m, "y"), month, Int(m, "d")), "Explicit", "date-named");
            }
        }

        return [.. found.OrderBy(f => f.Index).Select(f => f.Date).Take(EventMailHintRules.MaxResults)];
    }

    private static int Int(Match match, string group) => int.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture);

    /// <summary>A real calendar date, or null (31.02, month 13, a number glued to more digits).</summary>
    private static DateOnly? Build(string text, Match match, int year, int month, int day)
    {
        bool glued = match.Index > 0 && char.IsAsciiDigit(text[match.Index - 1])
            || match.Index + match.Length < text.Length && char.IsAsciiDigit(text[match.Index + match.Length]);
        return glued || year is < 2000 or > 2100 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            ? null : new DateOnly(year, month, day);
    }

    /// <summary>A password keyword with a value that looks like a secret, or a long mixed-class token. Only a yes/no leaves this method.</summary>
    private static bool LooksLikePassword(string original, string folded)
    {
        foreach (Match match in EventMailHintRules.PasswordAssignment.Matches(folded))
        {
            // Plain words ("parola: degisti,") are not secrets; a value with a digit or a symbol inside is treated as one.
            ReadOnlySpan<char> value = match.Groups["v"].ValueSpan.TrimEnd(".,;:!?)\"'");
            if (value.IndexOfAny("0123456789!@#$%^&*+=~|<>[]{}") >= 0 && !value.StartsWith("http", StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Long random-looking tokens, judged on the original text (case matters): three of lower, upper, digit, symbol; no URL or path.
        int start = -1;
        for (int i = 0; i <= original.Length; i++)
        {
            bool inToken = i < original.Length && !char.IsWhiteSpace(original[i]);
            if (inToken && start < 0)
            {
                start = i;
            }
            else if (!inToken && start >= 0)
            {
                if (RandomLooking(original.AsSpan(start, i - start)))
                {
                    return true;
                }

                start = -1;
            }
        }

        return false;
    }

    private static bool RandomLooking(ReadOnlySpan<char> token)
    {
        if (token.Length is < 16 or > 128 || token.Contains('/') || token.Contains('@'))
        {
            return false;
        }

        bool lower = false, upper = false, digit = false, symbol = false;
        foreach (char c in token)
        {
            lower |= char.IsAsciiLetterLower(c);
            upper |= char.IsAsciiLetterUpper(c);
            digit |= char.IsAsciiDigit(c);
            symbol |= !char.IsAsciiLetterOrDigit(c);
        }

        return (lower ? 1 : 0) + (upper ? 1 : 0) + (digit ? 1 : 0) + (symbol ? 1 : 0) >= 3;
    }
}
