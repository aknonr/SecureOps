using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient
{
    /// <summary>Legacy request evidence only; never interprets unknown keys as approved relationships.</summary>
    public Task<JsonElement> DiagnoseAsync(string sourceId, CancellationToken token) => DiagnoseAsync(sourceId, new Dictionary<string, string>(), token);

    /// <summary>Reads approved direct fields; optional explicit RFC contract permits one bounded request hop only.</summary>
    public async Task<JsonElement> DiagnoseAsync(string sourceId, IReadOnlyDictionary<string, string> dictionary, CancellationToken token,
        InUseReferencedRequestContract? referencedRequest = null)
    {
        referencedRequest?.Validate(dictionary);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        token = deadline.Token;
        if (dictionary.Count > 2 || dictionary.Any(p => p.Key is not ("Virtual PC User" or "RFC Kaydı")
            || !Regex.IsMatch(p.Value, @"\A(?:p_|c_)[A-Za-z0-9_]{1,100}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            || new[] { "password", "token", "session", "secret", "authorization" }.Any(s => p.Value.Contains(s, StringComparison.OrdinalIgnoreCase))))
        { throw new InvalidDataException("Only approved direct Virtual PC User/RFC field selectors are accepted."); }
        if (!_operationalOptions.ReadOnlyIntegrationMode || _operationalOptions.ControlledTestWritesEnabled
            || _operationalOptions.SourceCloseEnabled || !long.TryParse(sourceId, NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0)
        { throw new InvalidDataException("Diagnostic requires an exact identity and all write fences."); }
        using JsonDocument root = await QueryAsync("SMSS_oRFF",
            [$"#%id%#={id} AND #%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)"],
            _sourceSelects, "in-use-evidence-root", token, 65536);
        ParsedSourceRecords parsed = TuruncuHatQueryParser.ParseSource(root.RootElement, _options.MaxDescriptionLength, _sourceSelects, true);
        if (parsed.MalformedCount != 0 || parsed.Items.Count != 1 || parsed.Items[0].SourceRecordId != sourceId)
        { throw new InvalidDataException("Exact authorized root was not uniquely returned."); }
        using JsonDocument related = await QueryAsync("rel", [$"#%m_tid%#=100049 and #%m_lid%#={id}"],
            InUseServiceItemParser.Selects.Concat(dictionary.Values.Select(f => "(LCSIMS_ServiceInstance)m_rid." + f)).Distinct().ToArray(), "in-use-evidence-rel", token, 65536);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        object Shape(JsonElement document, int maximumRows)
        {
            JsonElement rows = TuruncuHatQueryParser.GetItems(document);
            if (rows.GetArrayLength() > maximumRows)
            { throw new InvalidDataException("Diagnostic result count exceeded."); }
            return rows.EnumerateArray().Select(row =>
            {
                object[] cells = Cells(row, aliases, 0).Take(65).ToArray();
                return cells.Length <= 64 ? cells : throw new InvalidDataException("Diagnostic cell bound exceeded.");
            }).ToArray();
        }
        object rootShape = Shape(root.RootElement, 1);
        object serviceShape = Shape(related.RootElement, 10);
        object? referenced = referencedRequest is null ? null : await ReferencedEvidenceAsync(related.RootElement, referencedRequest,
            document => Shape(document, 2), value => aliases.GetValueOrDefault(value), token);
        return JsonSerializer.SerializeToElement(new
        {
            Root = rootShape,
            ServiceItems = serviceShape,
            ReferencedRequests = referenced,
            Completeness = "Unverified",
            AffectedAssets = "NotQueried",
            Mapping = "Unverified; no persisted enrichment"
        });
    }

    private static IEnumerable<object> Cells(JsonElement cell, Dictionary<string, string> aliases, int depth, string path = "$")
    {
        if (depth > 3)
        { throw new InvalidDataException("Diagnostic nesting exceeded."); }
        if (cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() <= 64)
        {
            int index = 0;
            foreach (JsonElement child in cell.EnumerateArray())
            { foreach (object result in Cells(child, aliases, depth + 1, path + "[" + index++ + "]")) { yield return result; } }
            yield break;
        }
        if (cell.ValueKind != JsonValueKind.Object || !cell.TryGetProperty("Key", out JsonElement key)
            || key.ValueKind != JsonValueKind.String || !cell.TryGetProperty("Value", out JsonElement value))
        { throw new InvalidDataException("Diagnostic requires semantic response keys."); }
        string name = key.GetString()!;
        if (!Regex.IsMatch(name, @"\A(?:SET\.|KEY\.|num\z)[A-Za-z0-9_.()]*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            || name.Length > 128 || new[] { "password", "session", "token", "authorization", "secret" }.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)))
        { throw new InvalidDataException("Unexpected diagnostic key; stop collection."); }
        string? alias = null;
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            string text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (text.Length > 8000)
            { throw new InvalidDataException("Diagnostic value bound exceeded."); }
            if (text.Length > 0)
            {
                if (!aliases.TryGetValue(text, out alias))
                { aliases[text] = alias = $"value-{aliases.Count + 1}"; }
            }
        }
        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        { throw new InvalidDataException("Expanded Value contract requires a bounded source-owner schema example; no flattening permitted."); }
        yield return new { Path = path, Key = name, Type = value.ValueKind.ToString(), Alias = alias };
    }
}
