using System.Globalization;
using System.Text.Json;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>Keyed rows from one query plus whether the response was complete.</summary>
internal sealed record TuruncuHatItsmRecordRows(IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows, int Malformed, bool Complete);

/// <summary>
/// Strict keyed reader for ITSM lookup rows. Every cell must carry a <c>Key</c>; a cell is matched by its exact
/// <c>SET.{select}</c> key, never by position. A row missing a selected key, with a conflicting duplicate key,
/// with more than 32 cells or with an oversized key or value is counted as malformed, not skipped silently.
/// The envelope and application-error rules are the shared <see cref="TuruncuHatQueryParser.GetItems"/>.
/// </summary>
internal static class TuruncuHatItsmRecordRowReader
{
    private const int _maxCellCount = 32;
    private const int _maxKeyLength = 128;
    private const int _maxValueLength = 4000;
    private const string _rowNumberKey = "num";

    public static TuruncuHatItsmRecordRows Read(
        JsonElement root, IReadOnlyList<string> selects, IReadOnlyCollection<string>? optionalSelects = null)
    {
        JsonElement items = TuruncuHatQueryParser.GetItems(root);
        HashSet<string> optional = [.. (optionalSelects ?? []).Select(select => "SET." + select)];
        string[] expected = [.. selects.Select(select => "SET." + select).Where(key => !optional.Contains(key))];
        List<IReadOnlyDictionary<string, string?>> rows = [];
        int malformed = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (TryRow(item, expected, out Dictionary<string, string?> row))
            {
                // An optional select the source left out (an empty value) reads as null, never as a shifted cell.
                foreach (string key in optional)
                {
                    row.TryAdd(key, null);
                }

                rows.Add(row);
            }
            else
            {
                malformed++;
            }
        }

        return new(rows, malformed, IsSinglePage(root));
    }

    private static bool TryRow(JsonElement item, string[] expected, out Dictionary<string, string?> row)
    {
        row = new(StringComparer.Ordinal);
        if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() is 0 or > _maxCellCount)
        {
            return false;
        }

        foreach (JsonElement cell in item.EnumerateArray())
        {
            JsonElement holder = cell;
            if (cell.ValueKind == JsonValueKind.Array)
            {
                if (cell.GetArrayLength() != 1)
                {
                    return false;
                }

                holder = cell[0];
            }

            if (holder.ValueKind != JsonValueKind.Object
                || !holder.TryGetProperty("Key", out JsonElement keyElement) || keyElement.ValueKind != JsonValueKind.String
                || !holder.TryGetProperty("Value", out JsonElement valueElement) || !TryText(valueElement, out string? value))
            {
                return false;
            }

            string? key = keyElement.GetString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > _maxKeyLength || (value?.Length ?? 0) > _maxValueLength)
            {
                return false;
            }

            if (string.Equals(key, _rowNumberKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!row.TryAdd(key, value) && !string.Equals(row[key], value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return expected.All(row.ContainsKey);
    }

    // The reviewed request has no page parameter: more than one page means rows this read cannot reach.
    private static bool IsSinglePage(JsonElement root)
    {
        JsonElement result = root.GetProperty("QueryResult");
        int? maxPages = ReadInt(result, "MaxPages");
        int? pageNo = ReadInt(result, "PageNO") ?? ReadInt(result, "PageNo");
        int? recordCount = ReadInt(result, "RecordCount");
        int itemCount = result.GetProperty("Items").GetArrayLength();
        return maxPages is null or <= 1 && pageNo is null or <= 1 && (recordCount is null || recordCount <= itemCount);
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
