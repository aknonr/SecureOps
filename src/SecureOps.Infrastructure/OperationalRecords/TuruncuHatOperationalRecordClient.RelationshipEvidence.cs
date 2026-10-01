using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient
{
    /// <summary>Legacy request evidence only; never interprets unknown keys as approved relationships.</summary>
    public Task<JsonElement> DiagnoseAsync(string sourceId, CancellationToken token) => DiagnoseAsync(sourceId, new Dictionary<string, string>(), token);

    /// <summary>Inspects direct field candidates; an independently verified RFC contract permits one bounded hop.</summary>
    public async Task<JsonElement> DiagnoseAsync(string sourceId, IReadOnlyDictionary<string, string> dictionary, CancellationToken token,
        InUseReferencedRequestContract? referencedRequest = null, Action<JsonElement>? privateComparison = null)
    {
        referencedRequest?.Validate(dictionary);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        token = deadline.Token;
        if (dictionary.Count > 2 || dictionary.Any(p => p.Key is not ("Virtual PC User" or "RFC Kaydı")
            || !Regex.IsMatch(p.Value, @"\A(?:p_|c_)[A-Za-z0-9_]{1,100}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            || new[] { "password", "token", "session", "secret", "authorization" }.Any(s => p.Value.Contains(s, StringComparison.OrdinalIgnoreCase))))
        { throw new InvalidDataException("Only direct Virtual PC User/RFC field candidates are accepted."); }
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
        object Shape(JsonElement document, int maximumRows, string? stage = null, HashSet<string>? privateKeys = null)
        {
            JsonElement rows = TuruncuHatQueryParser.GetItems(document);
            if (rows.GetArrayLength() > maximumRows)
            { throw new InvalidDataException("Diagnostic result count exceeded."); }
            var privateRows = new List<object>();
            object[] shaped = rows.EnumerateArray().Select(row =>
            {
                var values = new List<object>();
                object[] cells = Cells(row, aliases, 0, privateKeys: privateKeys,
                    privateValues: values).Take(65).ToArray();
                if (cells.Length > 64)
                { throw new InvalidDataException("Diagnostic cell bound exceeded."); }
                privateRows.Add(values);
                return (object)cells;
            }).ToArray();
            if (stage is not null)
            { privateComparison?.Invoke(JsonSerializer.SerializeToElement(new { Stage = stage, Rows = privateRows })); }
            return shaped;
        }
        object rootShape = Shape(root.RootElement, 1);
        var comparisonKeys = new HashSet<string>(dictionary.Values.SelectMany(field => new[]
        { "SET.(LCSIMS_ServiceInstance)m_rid." + field, "KEY.(LCSIMS_ServiceInstance)m_rid." + field }), StringComparer.Ordinal)
        { "SET.(LCSIMS_ServiceInstance)m_rid.id" };
        object serviceShape = Shape(related.RootElement, 10, "ServiceItems", comparisonKeys);
        object? referenced = referencedRequest is null ? null : await ReferencedEvidenceAsync(related.RootElement, referencedRequest,
            document => Shape(document, 2, "ReferencedRequest", ["SET.id", "SET.p_code", "KEY.p_rel_requester", "SET.p_rel_requester"]),
            value => aliases.GetValueOrDefault(value), token);
        return JsonSerializer.SerializeToElement(new
        {
            Root = rootShape,
            ServiceItems = serviceShape,
            CandidateFields = dictionary.Select(field => new
            {
                Label = field.Key,
                Property = field.Value,
                Contract = "CandidateNotApproved",
                Rows = JsonSerializer.SerializeToElement(serviceShape).EnumerateArray().Select(row => new
                {
                    State = row.EnumerateArray().Any(cell => cell.GetProperty("Key").GetString() is { } key
                        && (key == "SET.(LCSIMS_ServiceInstance)m_rid." + field.Value
                            || key == "KEY.(LCSIMS_ServiceInstance)m_rid." + field.Value)) ? "Returned" : "Omitted",
                    Cells = row.EnumerateArray().Where(cell =>
                        cell.GetProperty("Key").GetString() is { } key
                        && (key == "SET.(LCSIMS_ServiceInstance)m_rid." + field.Value
                            || key == "KEY.(LCSIMS_ServiceInstance)m_rid." + field.Value)).ToArray()
                }).ToArray()
            }).ToArray(),
            ReferencedRequests = referenced,
            Completeness = "Unverified",
            AffectedAssets = "NotQueried",
            Mapping = "Unverified; no persisted enrichment"
        });
    }

    private static IEnumerable<object> Cells(JsonElement cell, Dictionary<string, string> aliases, int depth, string path = "$",
        HashSet<string>? privateKeys = null, List<object>? privateValues = null)
    {
        if (depth > 3)
        { throw new InvalidDataException("Diagnostic nesting exceeded."); }
        if (cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() <= 64)
        {
            int index = 0;
            foreach (JsonElement child in cell.EnumerateArray())
            { foreach (object result in Cells(child, aliases, depth + 1, path + "[" + index++ + "]", privateKeys, privateValues)) { yield return result; } }
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
        int? cardinality = value.ValueKind == JsonValueKind.Array ? value.GetArrayLength()
            : value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().Count() : null;
        if (cardinality is not null && (cardinality > 64 || privateKeys?.Contains(name) != true
            || name == "SET.(LCSIMS_ServiceInstance)m_rid.id"))
        { throw new InvalidDataException("Expanded non-candidate Value contract is unsupported; no flattening permitted."); }
        string? scalar = value.ValueKind == JsonValueKind.String ? value.GetString()
            : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        string valueState = value.ValueKind == JsonValueKind.Null ? "Null"
            : scalar is not null && string.IsNullOrWhiteSpace(scalar) ? "Empty" : "Returned";
        string shape = scalar is null ? value.ValueKind.ToString() : valueState == "Empty" ? "Blank"
            : Regex.IsMatch(scalar, @"\AOR-[0-9]{1,20}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ? "OrCode"
            : long.TryParse(scalar, NumberStyles.None, CultureInfo.InvariantCulture, out long number)
                && number > 0 && number.ToString(CultureInfo.InvariantCulture) == scalar ? "PositiveInteger" : "Text";
        if (privateKeys?.Contains(name) == true)
        {
            privateValues?.Add(new
            {
                Path = path,
                Key = name,
                Alias = alias,
                Type = value.ValueKind.ToString(),
                Cardinality = cardinality,
                Value = cardinality is null ? value.Clone() : (JsonElement?)null
            });
        }
        yield return new { Path = path, Key = name, Type = value.ValueKind.ToString(), Alias = alias, ValueState = valueState, Shape = shape, Cardinality = cardinality };
    }
}
