using System.Globalization;
using System.Net;
using System.Text.Json;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Strict parser for direct keyed and legacy nested Turuncu Hat query projections.</summary>
internal static class TuruncuHatQueryParser
{
    private const int MaxSourceProjectionCellCount = 32;
    private const int MaxProjectionKeyLength = 128;
    private const string SourceIdKey = "SET.id";
    private const string OrCodeKey = "SET.p_code";
    private const string TitleKey = "SET.p_name";
    private const string DescriptionKey = "SET.p_description";
    private const string RequesterDisplayKey = "KEY.p_rel_requester";
    private const string RequesterInternalKey = "SET.p_rel_requester";
    private const string RowNumberKey = "num";

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
            if (!TrySourceProjection(item, expectedKeys, out IReadOnlyList<string?> values)
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
            if (!TryProjection(item, expectedKeys, out IReadOnlyList<string?> values)
                || string.IsNullOrWhiteSpace(values[0]))
            {
                throw new InvalidDataException("Turuncu Hat activity projection was invalid.");
            }

            ids.Add(values[0]!.Trim());
        }

        return ids;
    }

    private static JsonElement GetItems(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("QueryResult", out JsonElement queryResult)
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

    internal static bool IsSuccessfulUpdate(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("UpdateResult", out JsonElement result)
        && result.ValueKind == JsonValueKind.Object
        && result.TryGetProperty("Success", out JsonElement success)
        && success.ValueKind == JsonValueKind.True
        && !HasTextError(result, "ErrorDescription")
        && !HasTextError(result, "ErrorDetails")
        && ReadOptionalInt(result, "ErrorNo").GetValueOrDefault() == 0;

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

        if (!TryReadProjectionCells(item, expectedKeys.Count, out IReadOnlyList<ProjectionCell> cells))
        {
            return false;
        }

        if (cells.Any(cell => cell.Key is not null) && cells.Any(cell => cell.Key is null))
        {
            return false;
        }

        List<string?> projected = new(expectedKeys.Count);
        HashSet<string> suppliedKeys = new(StringComparer.Ordinal);
        int position = 0;
        foreach (ProjectionCell cell in cells)
        {
            if (cell.Key is not null
                && (!suppliedKeys.Add(cell.Key)
                    || !string.Equals(cell.Key, expectedKeys[position], StringComparison.Ordinal)))
            {
                return false;
            }

            projected.Add(cell.Value);
            position++;
        }

        values = projected;
        return true;
    }

    private static bool TrySourceProjection(
        JsonElement item,
        IReadOnlyList<string> expectedKeys,
        out IReadOnlyList<string?> values)
    {
        values = [];
        if (!TryReadProjectionCells(item, MaxSourceProjectionCellCount, out IReadOnlyList<ProjectionCell> cells))
        {
            return false;
        }

        bool hasKeyedCells = cells.Any(cell => cell.Key is not null);
        if (!hasKeyedCells)
        {
            if (cells.Count != expectedKeys.Count)
            {
                return false;
            }

            values = cells.Select(cell => cell.Value).ToArray();
            return true;
        }

        if (cells.Any(cell => cell.Key is null))
        {
            return false;
        }

        Dictionary<string, string?> keyedValues = new(StringComparer.Ordinal);
        foreach (ProjectionCell cell in cells)
        {
            string key = cell.Key!;
            if (key.Length > MaxProjectionKeyLength)
            {
                return false;
            }

            if (!keyedValues.TryAdd(key, cell.Value))
            {
                if (IsRecognizedSourceKey(key)
                    || !string.Equals(keyedValues[key], cell.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        if (!keyedValues.ContainsKey(SourceIdKey)
            || !keyedValues.ContainsKey(OrCodeKey)
            || !keyedValues.ContainsKey(TitleKey)
            || !keyedValues.ContainsKey(DescriptionKey))
        {
            return false;
        }

        keyedValues.TryGetValue(RequesterDisplayKey, out string? requester);
        values =
        [
            keyedValues[SourceIdKey],
            keyedValues[OrCodeKey],
            keyedValues[TitleKey],
            keyedValues[DescriptionKey],
            requester
        ];
        return true;
    }

    private static bool TryReadProjectionCells(
        JsonElement item,
        int maximumCellCount,
        out IReadOnlyList<ProjectionCell> cells)
    {
        cells = [];
        if (item.ValueKind != JsonValueKind.Array
            || item.GetArrayLength() == 0
            || item.GetArrayLength() > maximumCellCount)
        {
            return false;
        }

        List<ProjectionCell> parsed = new(item.GetArrayLength());
        JsonValueKind? cellShape = null;
        foreach (JsonElement cell in item.EnumerateArray())
        {
            if (cell.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)
                || (cellShape is not null && cellShape != cell.ValueKind))
            {
                return false;
            }

            cellShape ??= cell.ValueKind;
            JsonElement valueObject;
            if (cell.ValueKind == JsonValueKind.Array)
            {
                if (cell.GetArrayLength() > 1)
                {
                    return false;
                }

                if (cell.GetArrayLength() == 0)
                {
                    parsed.Add(new ProjectionCell(null, null));
                    continue;
                }

                valueObject = cell[0];
            }
            else
            {
                valueObject = cell;
            }

            if (valueObject.ValueKind != JsonValueKind.Object
                || !valueObject.TryGetProperty("Value", out JsonElement valueElement)
                || !TryAsString(valueElement, out string? value)
                || !TryReadKey(valueObject, out string? key))
            {
                return false;
            }

            parsed.Add(new ProjectionCell(key, value));
        }

        cells = parsed;
        return true;
    }

    private static bool TryReadKey(JsonElement valueObject, out string? key)
    {
        key = null;
        if (!valueObject.TryGetProperty("Key", out JsonElement keyElement)
            || keyElement.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (keyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        key = keyElement.GetString();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = null;
        }

        return true;
    }

    private static bool IsRecognizedSourceKey(string key) => key is
        SourceIdKey or OrCodeKey or TitleKey or DescriptionKey or RequesterDisplayKey
        or RequesterInternalKey or RowNumberKey;

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

    private sealed record ProjectionCell(string? Key, string? Value);
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
