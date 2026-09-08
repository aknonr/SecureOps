using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient
{
    /// <summary>Legacy request evidence only; never interprets unknown keys as approved relationships.</summary>
    public async Task<JsonElement> DiagnoseAsync(string sourceId, CancellationToken token)
    {
        if (!_operationalOptions.ReadOnlyIntegrationMode || _operationalOptions.ControlledTestWritesEnabled
            || _operationalOptions.SourceCloseEnabled || !long.TryParse(sourceId, NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0)
        { throw new InvalidDataException("Diagnostic requires an exact identity and all write fences."); }
        using JsonDocument root = await QueryAsync("SMSS_oRFF",
            [$"#%id%#={id} AND #%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)"],
            _sourceSelects, "in-use-evidence-root", token, 65536);
        ParsedSourceRecords parsed = TuruncuHatQueryParser.ParseSource(root.RootElement, _options.MaxDescriptionLength, _sourceSelects, true);
        if (parsed.MalformedCount != 0 || parsed.Items.Count != 1 || parsed.Items[0].SourceRecordId != sourceId)
        { throw new InvalidDataException("Exact authorized root was not uniquely returned."); }
        string[] fields = ["id", "p_name", "p_SI_def_server_type", "p_SI_def_environment", "p_rel_company_owner",
            "c_new_SI_major_project", "p_SI_def_network_segment", "p_SI_ip_SI_address_1", "p_SI_def_os_name",
            "p_def_os_version", "c_new_SI_major_project.p_rel_obs", "p_rel_asset_item.p_rel_lbs",
            "p_rel_asset_item.p_rel_lbs.m_parent", "p_def_category", "c_new_SI_major_project.id"];
        using JsonDocument related = await QueryAsync("rel", [$"#%m_tid%#=100049 and #%m_lid%#={id}"],
            fields.Select(f => "(LCSIMS_ServiceInstance)m_rid." + f).ToArray(), "in-use-evidence-rel", token, 65536);
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
        return JsonSerializer.SerializeToElement(new
        {
            Root = Shape(root.RootElement, 1),
            ServiceItems = Shape(related.RootElement, 10),
            Completeness = "Unverified",
            AffectedAssets = "NotQueried",
            Mapping = "Unverified; no persisted enrichment"
        });
    }

    private static IEnumerable<object> Cells(JsonElement cell, Dictionary<string, string> aliases, int depth)
    {
        if (depth > 3)
        { throw new InvalidDataException("Diagnostic nesting exceeded."); }
        if (cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() <= 64)
        {
            foreach (JsonElement child in cell.EnumerateArray())
            { foreach (object result in Cells(child, aliases, depth + 1)) { yield return result; } }
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
        yield return new { Key = name, Type = value.ValueKind.ToString(), Alias = alias };
    }
}
