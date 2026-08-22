using System.Net;
using System.Text.Json;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Strict parser for the evidenced positional Turuncu Hat query projection.</summary>
internal static class TuruncuHatQueryParser
{
    public static ParsedSourceRecords ParseSource(JsonElement root, int maximumDescriptionLength)
    {
        JsonElement items = GetItems(root);
        List<OperationalRecordSourceItem> parsed = [];
        int malformed = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!TryValue(item, 0, out string? sourceId)
                || !TryValue(item, 1, out string? orCode)
                || !TryValue(item, 2, out string? encodedTitle)
                || !TryValue(item, 3, out string? encodedDescription)
                || !TryOptionalValue(item, 4, out string? requester))
            {
                malformed++;
                continue;
            }

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

    public static IReadOnlyList<string> ParseActivityIds(JsonElement root)
    {
        JsonElement items = GetItems(root);
        List<string> ids = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (TryValue(item, 0, out string? id) && !string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id.Trim());
            }
        }

        return ids;
    }

    private static JsonElement GetItems(JsonElement root)
    {
        if (!root.TryGetProperty("QueryResult", out JsonElement queryResult)
            || queryResult.ValueKind != JsonValueKind.Object
            || !queryResult.TryGetProperty("Items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Turuncu Hat query response did not match the reviewed contract.");
        }

        return items;
    }

    private static bool TryOptionalValue(JsonElement item, int index, out string? value)
    {
        if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() <= index)
        {
            value = null;
            return false;
        }

        JsonElement cell = item[index];
        if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() == 0)
        {
            value = null;
            return true;
        }

        JsonElement first = cell[0];
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("Value", out JsonElement element))
        {
            value = null;
            return false;
        }

        value = AsString(element);
        return true;
    }

    private static bool TryValue(JsonElement item, int index, out string? value) =>
        TryOptionalValue(item, index, out value) && !string.IsNullOrWhiteSpace(value);

    private static string? AsString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        JsonValueKind.Null => null,
        _ => null
    };

    private static HashSet<string> Duplicates(IEnumerable<string> values) => values
        .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ParsedSourceRecords(IReadOnlyList<OperationalRecordSourceItem> Items, int MalformedCount);
