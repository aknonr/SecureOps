using System.Globalization;
using System.Net;
using System.Text.Json;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Strict parser for evidenced keyed and legacy positional Turuncu Hat query projections.</summary>
internal static class TuruncuHatQueryParser
{
    public static ParsedSourceRecords ParseSource(
        JsonElement root,
        int maximumDescriptionLength,
        IReadOnlyList<string> expectedKeys)
    {
        JsonElement items = GetItems(root);
        List<OperationalRecordSourceItem> parsed = [];
        int malformed = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!TryProjection(item, expectedKeys, out IReadOnlyList<string?> values)
                || string.IsNullOrWhiteSpace(values[0])
                || string.IsNullOrWhiteSpace(values[1])
                || string.IsNullOrWhiteSpace(values[2])
                || string.IsNullOrWhiteSpace(values[3]))
            {
                malformed++;
                continue;
            }

            string sourceId = values[0]!;
            string orCode = values[1]!;
            string encodedTitle = values[2]!;
            string encodedDescription = values[3]!;
            string? requester = values[4];
            string title = WebUtility.HtmlDecode(encodedTitle!).Trim();
            string description = WebUtility.HtmlDecode(encodedDescription!).Trim();
            requester = string.IsNullOrWhiteSpace(requester) ? null : requester.Trim();
            if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 128
                || string.IsNullOrWhiteSpace(orCode) || orCode.Length > 64
                || string.IsNullOrWhiteSpace(title) || title.Length > 500
                || description.Length > maximumDescriptionLength
                || (requester?.Length ?? 0) > 256)
            {
                malformed++;
                continue;
            }

            parsed.Add(new OperationalRecordSourceItem(
                sourceId.Trim(),
                orCode.Trim(),
                title,
                description,
                requester,
                CreatedAt: null,
                Environment: null,
                ServerReference: null,
                ApplicationReference: null,
                IsOpen: true,
                VersionToken: null,
                LastModifiedAt: null));
        }

        HashSet<string> duplicateIds = Duplicates(parsed.Select(item => item.SourceRecordId));
        HashSet<string> duplicateCodes = Duplicates(parsed.Select(item => item.OrCode));
        OperationalRecordSourceItem[] unique = parsed
            .Where(item => !duplicateIds.Contains(item.SourceRecordId) && !duplicateCodes.Contains(item.OrCode))
            .ToArray();
        malformed += parsed.Count - unique.Length;
        return new ParsedSourceRecords(unique, malformed);
    }

    public static IReadOnlyList<string> ParseActivityIds(JsonElement root, IReadOnlyList<string> expectedKeys)
    {
        JsonElement items = GetItems(root);
        List<string> ids = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (TryProjection(item, expectedKeys, out IReadOnlyList<string?> values)
                && !string.IsNullOrWhiteSpace(values[0]))
            {
                ids.Add(values[0]!.Trim());
            }
        }

        return ids;
    }

    private static JsonElement GetItems(JsonElement root)
    {
        if (!root.TryGetProperty("QueryResult", out JsonElement queryResult)
            || queryResult.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Turuncu Hat query response did not match the reviewed contract.");
        }

        bool hasErrorDescription = HasTextError(queryResult, "ErrorDescription");
        bool hasErrorDetails = HasTextError(queryResult, "ErrorDetails");
        int? errorNo = ReadOptionalInt(queryResult, "ErrorNo");
        bool hasItems = queryResult.TryGetProperty("Items", out JsonElement items)
            && items.ValueKind == JsonValueKind.Array;
        int? recordCount = ReadOptionalInt(queryResult, "RecordCount");
        int? tenantMetadata = ReadOptionalInt(queryResult, "TenantId");
        int? pageNo = ReadOptionalInt(queryResult, "PageNO", "PageNo");
        int? maxPages = ReadOptionalInt(queryResult, "MaxPages");

        if (hasErrorDescription || hasErrorDetails || errorNo.GetValueOrDefault() != 0)
        {
            throw new TuruncuHatQueryResultException(
                errorNo,
                hasErrorDescription,
                hasErrorDetails,
                hasItems,
                recordCount,
                tenantMetadata,
                pageNo,
                maxPages);
        }

        if (!hasItems)
        {
            throw new InvalidDataException("Turuncu Hat query response did not match the reviewed contract.");
        }

        return items;
    }

    private static bool HasTextError(JsonElement queryResult, string propertyName)
    {
        if (!queryResult.TryGetProperty(propertyName, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            _ => throw new InvalidDataException("Turuncu Hat query error metadata had an invalid type.")
        };
    }

    private static int? ReadOptionalInt(JsonElement queryResult, params string[] propertyNames)
    {
        JsonElement value = default;
        bool found = false;
        foreach (string propertyName in propertyNames)
        {
            if (queryResult.TryGetProperty(propertyName, out value))
            {
                found = true;
                break;
            }
        }

        if (!found || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int numeric))
        {
            return numeric;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
        {
            return numeric;
        }

        throw new InvalidDataException("Turuncu Hat query error metadata had an invalid type.");
    }

    private static bool TryProjection(
        JsonElement item,
        IReadOnlyList<string> expectedKeys,
        out IReadOnlyList<string?> values)
    {
        values = [];
        if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != expectedKeys.Count)
        {
            return false;
        }

        List<(string? Key, string? Value)> cells = [];
        foreach (JsonElement cell in item.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() > 1)
            {
                return false;
            }

            if (cell.GetArrayLength() == 0)
            {
                cells.Add((null, null));
                continue;
            }

            JsonElement first = cell[0];
            if (first.ValueKind != JsonValueKind.Object
                || !first.TryGetProperty("Value", out JsonElement valueElement)
                || !TryAsString(valueElement, out string? value))
            {
                return false;
            }

            string? key = first.TryGetProperty("Key", out JsonElement keyElement)
                && keyElement.ValueKind == JsonValueKind.String
                ? keyElement.GetString()
                : null;
            cells.Add((string.IsNullOrWhiteSpace(key) ? null : key, value));
        }

        int keyedCount = cells.Count(cell => cell.Key is not null);
        if (keyedCount == 0)
        {
            values = cells.Select(cell => cell.Value).ToArray();
            return true;
        }

        if (keyedCount != expectedKeys.Count)
        {
            return false;
        }

        Dictionary<string, string?> keyed = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string? key, string? value) in cells)
        {
            if (!keyed.TryAdd(key!, value))
            {
                return false;
            }
        }

        if (keyed.Count != expectedKeys.Count || expectedKeys.Any(key => !keyed.ContainsKey(key)))
        {
            return false;
        }

        values = expectedKeys.Select(key => keyed[key]).ToArray();
        return true;
    }

    private static bool TryAsString(JsonElement element, out string? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => null,
            _ => null
        };
        return element.ValueKind is JsonValueKind.String
            or JsonValueKind.Number
            or JsonValueKind.True
            or JsonValueKind.False
            or JsonValueKind.Null;
    }

    private static HashSet<string> Duplicates(IEnumerable<string> values) => values
        .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ParsedSourceRecords(IReadOnlyList<OperationalRecordSourceItem> Items, int MalformedCount);

internal sealed class TuruncuHatQueryResultException : Exception
{
    public TuruncuHatQueryResultException(
        int? errorNo,
        bool hasErrorDescription,
        bool hasErrorDetails,
        bool hasItems,
        int? recordCount,
        int? tenantMetadata,
        int? pageNo,
        int? maxPages)
        : base("Turuncu Hat reported a query application error.")
    {
        ErrorNo = errorNo;
        HasErrorDescription = hasErrorDescription;
        HasErrorDetails = hasErrorDetails;
        HasItems = hasItems;
        RecordCount = recordCount;
        TenantMetadata = tenantMetadata;
        PageNo = pageNo;
        MaxPages = maxPages;
    }

    public int? ErrorNo { get; }
    public bool HasErrorDescription { get; }
    public bool HasErrorDetails { get; }
    public bool HasItems { get; }
    public int? RecordCount { get; }
    public int? TenantMetadata { get; }
    public int? PageNo { get; }
    public int? MaxPages { get; }
}
