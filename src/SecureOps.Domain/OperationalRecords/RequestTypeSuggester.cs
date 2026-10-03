using System.Text.RegularExpressions;

namespace SecureOps.Domain.OperationalRecords;

/// <summary>An advisory request type read from explicit words in the record title or description.</summary>
/// <param name="Type">The one supported request type the words point to.</param>
/// <param name="Terms">The source words that matched, as written, in reading order and without duplicates.</param>
/// <param name="RuleVersion">Keyword rule set that produced the suggestion.</param>
public sealed record RequestTypeSuggestion(OperationalRecordClassification Type, IReadOnlyList<string> Terms, string RuleVersion);

/// <summary>
/// Suggests the operator's request type from explicit keywords (ADR-0018 Amendment 1). The suggestion
/// only pre-selects the operator's choice: it never enters <see cref="SdmEvaluationInput"/>, never
/// changes the classification and never grants Jira eligibility. Words pointing at more than one
/// type, or at none, produce no suggestion; the operator then chooses.
/// </summary>
public static partial class RequestTypeSuggester
{
    /// <summary>Immutable keyword rule identifier; new words or semantics require a new version.</summary>
    public const string RuleVersion = "WASAS-REQUEST-TYPE-HINT-2026.10-v1";

    /// <summary>Returns a suggestion only when the text names exactly one supported request type.</summary>
    public static RequestTypeSuggestion? Suggest(string? title, string? description)
    {
        try
        {
            return Match(title, description);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological text gets no suggestion rather than a slow page; the operator still chooses.
            return null;
        }
    }

    private static RequestTypeSuggestion? Match(string? title, string? description)
    {
        string text = string.Concat(title, "\n", description);
        if (text.Length > 8501)
        { text = text[..8501]; }
        // Folding is one char to one char, so match indexes stay valid in the original text.
        string folded = string.Create(text.Length, text, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            { span[i] = Fold(source[i]); }
        });

        List<(OperationalRecordClassification Type, int Index, int Length)> hits = [];
        // "Yeni sunucu kurulumu" is a server request: its words are consumed before the
        // installation rule runs, so "kurulum" inside it cannot also vote for installation.
        char[] remaining = folded.ToCharArray();
        foreach (Match match in ServerRequestWords().Matches(folded))
        {
            hits.Add((OperationalRecordClassification.ServerRequest, match.Index, match.Length));
            Array.Fill(remaining, ' ', match.Index, match.Length);
        }
        string rest = new(remaining);
        foreach (Match match in RetirementWords().Matches(rest))
        { hits.Add((OperationalRecordClassification.ServerRetirement, match.Index, match.Length)); }
        foreach (Match match in InstallationWords().Matches(rest))
        { hits.Add((OperationalRecordClassification.SoftwareInstallation, match.Index, match.Length)); }

        if (hits.Count == 0 || hits.Select(hit => hit.Type).Distinct().Count() != 1)
        { return null; }
        string[] terms = [.. hits.OrderBy(hit => hit.Index)
            .Select(hit => text.Substring(hit.Index, hit.Length).Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        return new(hits[0].Type, terms, RuleVersion);
    }

    private static char Fold(char c) => c switch
    {
        'ı' or 'I' or 'İ' or 'i' => 'i',
        'ş' or 'Ş' => 's',
        'ğ' or 'Ğ' => 'g',
        'ü' or 'Ü' => 'u',
        'ö' or 'Ö' => 'o',
        'ç' or 'Ç' => 'c',
        _ => char.ToLowerInvariant(c)
    };

    [GeneratedRegex(@"\b(?:(?:(?:yeni|sanal)\s+)?sunucu\w*\s+(?:talep|kurulum|kurulmas|olusturul|ihtiyac)\w*|(?:yeni|sanal)\s+sunucu\w*|vm\s+talep\w*|server\s+request\w*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ServerRequestWords();

    [GeneratedRegex(@"\b(?:iade\w*|emekli\w*|decommission\w*|retire\w*|hizmet\s+disi\w*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex RetirementWords();

    [GeneratedRegex(@"\b(?:kurulum\w*|kurulmas\w*|yuklen\w*|yukleme\w*|install\w*|setup\w*|yazilim\w*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex InstallationWords();
}
