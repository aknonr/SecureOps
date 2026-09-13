using System.Globalization;
using System.Text.Json;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Parsed rows plus whether the response admitted more pages than this read retrieved.
/// Complete=false forces the caller to report a partial result rather than treat page one as all rows.
/// </summary>
internal sealed record QueryOutcome(IReadOnlyList<Dictionary<string, string?>> Rows, int Malformed, bool Complete);

/// <summary>
/// Strict keyed projection reader for announcement source selects. Cells are matched by exact
/// <c>SET.{select}</c> key. Positional projections are rejected because row order is not a business
/// invariant; a malformed row is skipped and counted instead of failing the whole read or being
/// silently ignored. The reviewed query envelope carries <c>MaxPages</c> but no evidenced request
/// page parameter, so a multi-page response is reported incomplete rather than partially consumed.
/// </summary>
internal static class AnnouncementSourceQueryParser
{
    private const int _maxCellCount = 32;
    private const int _maxKeyLength = 128;
    private const int _maxValueLength = 4000;

    public static QueryOutcome Parse(JsonElement root, IReadOnlyList<string> selects)
    {
        JsonElement items = TuruncuHatQueryParser.GetItems(root);
        string[] expected = [.. selects.Select(select => "SET." + select)];
        List<Dictionary<string, string?>> rows = [];
        int malformed = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (TryRow(item, expected, out Dictionary<string, string?> row))
            { rows.Add(row); }
            else
            { malformed++; }
        }
        return new(rows, malformed, IsSinglePage(root));
    }

    private static bool TryRow(JsonElement item, string[] expected, out Dictionary<string, string?> row)
    {
        row = new(StringComparer.Ordinal);
        if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() is 0 or > _maxCellCount)
        { return false; }
        foreach (JsonElement cell in item.EnumerateArray())
        {
            JsonElement holder = cell;
            if (cell.ValueKind == JsonValueKind.Array)
            {
                if (cell.GetArrayLength() != 1)
                { return false; }
                holder = cell[0];
            }
            if (holder.ValueKind != JsonValueKind.Object
                || !holder.TryGetProperty("Key", out JsonElement keyElement) || keyElement.ValueKind != JsonValueKind.String
                || !holder.TryGetProperty("Value", out JsonElement valueElement) || !TryText(valueElement, out string? value))
            { return false; }
            string? key = keyElement.GetString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > _maxKeyLength || (value?.Length ?? 0) > _maxValueLength)
            { return false; }
            // Row metadata is not a field; duplicates of a real key must agree or the row is malformed.
            if (string.Equals(key, "num", StringComparison.Ordinal))
            { continue; }
            if (!row.TryAdd(key, value) && !string.Equals(row[key], value, StringComparison.Ordinal))
            { return false; }
        }
        return expected.All(row.ContainsKey);
    }

    // MaxPages above one means rows exist that this evidenced request shape cannot reach.
    private static bool IsSinglePage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("QueryResult", out JsonElement result))
        { return true; }
        int? maxPages = ReadInt(result, "MaxPages");
        int? pageNo = ReadInt(result, "PageNO") ?? ReadInt(result, "PageNo");
        return maxPages is null or <= 1 && pageNo is null or <= 1;
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out int numeric) => numeric,
                JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int parsed) => parsed,
                _ => null
            }
            : null;

    private static bool TryText(JsonElement element, out string? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => null
        };
        return element.ValueKind is JsonValueKind.String or JsonValueKind.Number
            or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
    }
}
