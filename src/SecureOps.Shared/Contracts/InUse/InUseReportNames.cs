using System.Globalization;
using System.Text;

namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Presentation names from immutable report metadata, never archive keys or access decisions.</summary>
public static class InUseReportNames
{
    /// <summary>Creates a Windows-safe download name. Timestamp is explicitly UTC; original bytes are untouched.</summary>
    public static string Download(InUseReport report)
    {
        string? code = report.SourceCode ?? report.Sheets.Concat(report.EvidenceSheets)
            .Where(s => s.Name == "Provenance").SelectMany(s => s.Rows)
            .FirstOrDefault(r => r.Count == 2 && r[0] == "Code")?.ElementAt(1);
        string source = Component(code, 48, "record-" + report.RecordId.ToString("N"));
        string actor = Component(report.PreparedByLabel, 56, "preparer") + "-" + report.PreparedBy.ToString("N")[..8];
        string time = report.PreparedAt == default ? "time-unknown" : report.PreparedAt.UtcDateTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "Z";
        return $"InUse_{source}_{actor}_{time}_v{report.Version.ToString(CultureInfo.InvariantCulture)}.xlsx";
    }

    private static string Component(string? value, int limit, string fallback)
    {
        StringBuilder result = new();
        foreach (Rune rune in (value ?? "").EnumerateRunes())
        {
            string part = Rune.IsLetterOrDigit(rune) || rune.Value is '-' or '_' ? rune.ToString() : "_";
            if (result.Length + part.Length > limit)
            { break; }
            result.Append(part);
        }
        string text = result.ToString().Trim('_');
        if (text.Length == 0)
        { return fallback; }
        string upper = text.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL" ||
            (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) && upper[3] is >= '1' and <= '9'))
        { text = "_" + text; }
        return text;
    }
}
