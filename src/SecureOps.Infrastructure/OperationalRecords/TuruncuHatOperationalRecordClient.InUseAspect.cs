using System.Text.Json;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient
{
    private async Task<IReadOnlyList<InUseServer>> EnrichAspectsAsync(IReadOnlyList<InUseServer> servers,
        Dictionary<string, IReadOnlyDictionary<string, InUseEvidence>> cache, CancellationToken token)
    {
        if (!_options.InUseAspectLookupEnabled)
        { return servers; }
        List<InUseServer> result = [];
        foreach (InUseServer server in servers)
        {
            string? service = server.Fields.GetValueOrDefault("ITMC_Service_ID")?.Value;
            IReadOnlyDictionary<string, InUseEvidence> aspect;
            if (!InUseAspectParser.NumericId(service))
            { aspect = InUseAspectParser.Missing("ServiceIdentityMissing"); }
            else if (cache.TryGetValue(service!, out IReadOnlyDictionary<string, InUseEvidence>? cached))
            { aspect = cached; }
            else if (cache.Count >= 10)
            { aspect = InUseAspectParser.Missing("ReadLimitReached"); }
            else
            {
                try
                {
                    using JsonDocument response = await QueryAsync("LCMCMS_Service_FunctionalAspect",
                        [$"#%p_name%#='[Genel]' and #%p_rel_service_id%#={service}"],
                        ["id", "p_name", "p_rel_service_id"], "in-use-aspect", token, 65536);
                    aspect = InUseAspectParser.Parse(response.RootElement, service!);
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException or ExternalIntegrationException or TuruncuHatQueryResultException)
                { aspect = InUseAspectParser.Missing("AspectReadFailedOrUnsupported"); }
                cache.Add(service!, aspect);
            }
            Dictionary<string, InUseEvidence> fields = new(server.Fields);
            foreach ((string key, InUseEvidence value) in aspect)
            { fields[key] = value; }
            result.Add(server with { Fields = fields });
        }
        return result;
    }
}

/// <summary>Strict semantic contract for a unique [Genel] aspect of the exact service; no positional or first-row fallback.</summary>
public static class InUseAspectParser
{
    /// <summary>Bounded digits retained as text; no precision loss or identifier normalization.</summary>
    public static bool NumericId(string? value) => value is { Length: > 0 and <= 30 } && value.All(char.IsAsciiDigit) && value.Any(c => c != '0');
    /// <summary>Explicit unresolved fields, never a made-up [Genel] relationship.</summary>
    public static IReadOnlyDictionary<string, InUseEvidence> Missing(string reason) => new Dictionary<string, InUseEvidence>
    { ["ITMC_Servis_Unsuru_ID"] = new(null, reason), ["SERVICE ASPECT (Servis Unsuru)"] = new(null, reason) };
    /// <summary>The returned SET.p_rel_service_id must match the requested service, even when a filter was sent.</summary>
    public static IReadOnlyDictionary<string, InUseEvidence> Parse(JsonElement response, string service)
    {
        JsonElement rows = TuruncuHatQueryParser.GetItems(response);
        if (rows.GetArrayLength() != 1)
        { return Missing(rows.GetArrayLength() == 0 ? "AspectMissing" : "AspectAmbiguous"); }
        Dictionary<string, string?> cells = new(StringComparer.Ordinal);
        Read(rows[0], cells, 0);
        string? id = cells.GetValueOrDefault("SET.id");
        if (!NumericId(id) || cells.GetValueOrDefault("SET.p_name") != "[Genel]" || cells.GetValueOrDefault("SET.p_rel_service_id") != service)
        { return Missing("AspectRelationshipMismatch"); }
        return new Dictionary<string, InUseEvidence>
        {
            ["ITMC_Servis_Unsuru_ID"] = new(id, "TuruncuHat: exact service " + service + " / SET.id"),
            ["SERVICE ASPECT (Servis Unsuru)"] = new("[Genel]", "TuruncuHat: SET.p_name / SET.p_rel_service_id")
        };
    }
    private static void Read(JsonElement node, Dictionary<string, string?> cells, int depth)
    {
        if (depth > 3 || cells.Count > 6)
        { throw new InvalidDataException("Aspect projection bound exceeded."); }
        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() <= 6)
        { foreach (JsonElement child in node.EnumerateArray()) { Read(child, cells, depth + 1); } return; }
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("Key", out JsonElement key)
            || key.ValueKind != JsonValueKind.String || key.GetString() is not ("SET.id" or "SET.p_name" or "SET.p_rel_service_id" or "KEY.p_rel_service_id" or "num")
            || !node.TryGetProperty("Value", out JsonElement value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 1000
            || !cells.TryAdd(key.GetString()!, value.ValueKind == JsonValueKind.Null ? null : value.GetString()))
        { throw new InvalidDataException("Unsupported or duplicate aspect cell."); }
    }
}
