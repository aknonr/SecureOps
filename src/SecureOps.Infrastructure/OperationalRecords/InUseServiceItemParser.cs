using System.Globalization;
using System.Text.Json;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Exact semantic service-item projection evidenced by the bounded operator collection.</summary>
public static class InUseServiceItemParser
{
    private const string _prefix = "(LCSIMS_ServiceInstance)m_rid.";
    private static readonly (string Field, string Property, bool Reference)[] _fields =
    [
        ("ENVANTER_ID", "id", false), ("HOSTNAME", "p_name", false),
        ("SERVER TYPE", "p_SI_def_server_type", true), ("SI_ENVIRONMENT", "p_SI_def_environment", true),
        ("CONSUMER_COMPANY", "p_rel_company_owner", true), ("SERVICE NAME (ÜRÜN/UYGULAMA)", "c_new_SI_major_project", true),
        ("NETWORK SEGMENT", "p_SI_def_network_segment", true), ("IP ADDRESS", "p_SI_ip_SI_address_1", false),
        ("OS NAME", "p_SI_def_os_name", true), ("OS_VERSION", "p_def_os_version", true),
        ("SERVICE OWNER DIRECTORATE", "c_new_SI_major_project.p_rel_obs", true),
        ("BUILDING", "p_rel_asset_item.p_rel_lbs", true), ("CITY", "p_rel_asset_item.p_rel_lbs.m_parent", true),
        ("Device_Type", "p_def_category", true), ("ITMC_Service_ID", "c_new_SI_major_project.id", false)
    ];
    internal static string[] Selects => _fields.Select(f => _prefix + f.Property).ToArray();
    internal static string[] ReporterSelects => [.. Selects, _prefix + "c_rfc_record"];

    /// <summary>Parses observed rows only; rejects ambiguous identities and never proves global completeness.</summary>
    public static IReadOnlyList<InUseServer> Parse(JsonElement response, bool includeRfc = false)
    {
        JsonElement rows = TuruncuHatQueryParser.GetItems(response);
        if (rows.GetArrayLength() > 10)
        { throw new InvalidDataException("Service-item row limit exceeded."); }
        var allowed = _fields.SelectMany(f => f.Reference ? new[] { "KEY." + _prefix + f.Property, "SET." + _prefix + f.Property }
            : new[] { "SET." + _prefix + f.Property }).Append("num").ToHashSet(StringComparer.Ordinal);
        List<InUseServer> servers = [];
        if (includeRfc)
        { allowed.Add("SET." + _prefix + "c_rfc_record"); }
        foreach (JsonElement row in rows.EnumerateArray())
        {
            var cells = new Dictionary<string, string?>(StringComparer.Ordinal);
            Read(row, cells, allowed, 0);
            if (!cells.TryGetValue("SET." + _prefix + "id", out string? id)
                || !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long numericId) || numericId <= 0
                || id != numericId.ToString(CultureInfo.InvariantCulture) || servers.Any(s => s.Id == id))
            { throw new InvalidDataException("Service-item identity is missing or ambiguous."); }
            var fields = new Dictionary<string, InUseEvidence>(StringComparer.Ordinal);
            foreach ((string Field, string Property, bool Reference) field in _fields)
            {
                string key = (field.Reference ? "KEY." : "SET.") + _prefix + field.Property;
                fields[field.Field] = Evidence(cells, key);
                if (field.Reference)
                { fields["Reference: " + field.Field] = Evidence(cells, "SET." + _prefix + field.Property); }
            }
            string? serviceId = fields["ITMC_Service_ID"].Value;
            if (includeRfc)
            { fields["RFC Kaydı"] = Evidence(cells, "SET." + _prefix + "c_rfc_record"); }
            string? reference = fields["Reference: SERVICE NAME (ÜRÜN/UYGULAMA)"].Value;
            if (serviceId is not null && reference is not null && serviceId != reference)
            { throw new InvalidDataException("Service identity projections disagree."); }
            servers.Add(new(id!, fields));
        }
        return servers.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
    }

    private static InUseEvidence Evidence(Dictionary<string, string?> cells, string key) =>
        new(cells.GetValueOrDefault(key), (cells.ContainsKey(key) ? "TuruncuHat: " : "Missing response cell: ") + key);

    private static void Read(JsonElement node, Dictionary<string, string?> cells, HashSet<string> allowed, int depth)
    {
        if (depth > 3 || cells.Count >= 64)
        { throw new InvalidDataException("Service-item nesting/cell limit exceeded."); }
        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() <= 64)
        {
            foreach (JsonElement child in node.EnumerateArray())
            { Read(child, cells, allowed, depth + 1); }
            return;
        }
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("Key", out JsonElement key)
            || key.ValueKind != JsonValueKind.String || !node.TryGetProperty("Value", out JsonElement value)
            || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) || !allowed.Contains(key.GetString()!))
        { throw new InvalidDataException("Unsupported semantic service-item cell."); }
        string? text = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        if (text?.Length > 1000 || !cells.TryAdd(key.GetString()!, string.IsNullOrWhiteSpace(text) ? null : text))
        { throw new InvalidDataException("Service-item cell is oversized or duplicated."); }
    }
}
