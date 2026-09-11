using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient
{
    private async Task<object> ReferencedEvidenceAsync(JsonElement related, InUseReferencedRequestContract contract,
        Func<JsonElement, object> shape, Func<string, string?> alias, CancellationToken token)
    {
        const string prefix = "(LCSIMS_ServiceInstance)m_rid.";
        var linkKeys = new HashSet<string>(StringComparer.Ordinal)
        { "SET." + prefix + "id", contract.ReferenceCellKind + "." + prefix + contract.RfcProperty };
        var cache = new Dictionary<string, object>(StringComparer.Ordinal);
        var links = new List<object>();
        JsonElement[] rows = TuruncuHatQueryParser.GetItems(related).EnumerateArray().ToArray();
        var identities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonElement row in rows)
        {
            try
            {
                if (EvidenceValues(row, linkKeys).GetValueOrDefault("SET." + prefix + "id") is { } identity)
                { identities[identity] = identities.GetValueOrDefault(identity) + 1; }
            }
            catch (InvalidDataException) { /* Per-row ambiguity is reported below without traversal. */ }
        }
        foreach (JsonElement row in rows)
        {
            Dictionary<string, string?> cells;
            try
            { cells = EvidenceValues(row, linkKeys); }
            catch (InvalidDataException) { links.Add(new { State = "AmbiguousCells" }); continue; }
            string? server = cells.GetValueOrDefault("SET." + prefix + "id");
            string? reference = cells.GetValueOrDefault(contract.ReferenceCellKind + "." + prefix + contract.RfcProperty);
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(reference))
            { links.Add(new { ServiceItem = server is null ? null : alias(server), State = "MissingIdentityOrReference" }); continue; }
            if (!long.TryParse(server, NumberStyles.None, CultureInfo.InvariantCulture, out long serverId) || serverId <= 0
                || serverId.ToString(CultureInfo.InvariantCulture) != server || identities.GetValueOrDefault(server) != 1)
            { links.Add(new { ServiceItem = alias(server), State = "AmbiguousServiceItemIdentity" }); continue; }
            bool valid = contract.ReferenceKind == "SourceId"
                ? long.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0 && id.ToString(CultureInfo.InvariantCulture) == reference
                : Regex.IsMatch(reference, @"\AOR-[0-9]{1,20}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!valid)
            { links.Add(new { ServiceItem = alias(server), State = "UnsupportedReferenceValue" }); continue; }
            if (!cache.TryGetValue(reference, out object? evidence))
            {
                evidence = await ReadReferencedEvidenceAsync(reference, contract, shape, token);
                cache.Add(reference, evidence);
            }
            links.Add(new { ServiceItem = alias(server), Reference = alias(reference), Evidence = evidence });
        }
        return new { Links = links, DistinctLookups = cache.Count, Traversal = "One hop only; no assignment or persistence" };
    }

    private async Task<object> ReadReferencedEvidenceAsync(string reference, InUseReferencedRequestContract contract,
        Func<JsonElement, object> shape, CancellationToken token)
    {
        try
        {
            string filter = contract.ReferenceKind == "SourceId" ? $"#%id%#={reference}" : $"#%p_code%#='{reference}'";
            // No active/catalogue/group filter: an exact related request can be closed or outside In Use.
            string[] selects = ["id", "p_code", "p_rel_requester"];
            using JsonDocument response = await QueryAsync("SMSS_oRFF", [filter], selects, "in-use-evidence-rfc", token, 65536);
            InUseRelatedRequestReporter parsed = InUseRelatedRequestParser.Parse("", "", reference, contract,
                response.RootElement, DateTimeOffset.UtcNow);
            if (parsed.State != "ExactMatch")
            { return new { parsed.State }; }
            return new
            {
                State = "ExactMatchNotBusinessOwnership",
                Cells = shape(response.RootElement),
                RequesterKey = "KEY.p_rel_requester",
                ReporterSelector = "p_rel_requester",
                ReporterLabel = "Bildiren",
                ReporterDisplayKey = "KEY.p_rel_requester",
                ReporterReferenceKey = "SET.p_rel_requester",
                ReporterState = parsed.DisplayState,
                ReporterReferenceState = parsed.ReferenceState,
                RequesterState = parsed.DisplayState // Compatibility key; observed UI meaning is Bildiren.
            };
        }
        catch (ExternalIntegrationException ex)
        { return new { State = ex.ErrorCode == SecureOps.Shared.Contracts.Api.OperationalErrorCodes.OperationalSourceAuthenticationFailed ? "Forbidden" : "Failed" }; }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or TuruncuHatQueryResultException)
        { return new { State = "MalformedOrAmbiguous" }; }
    }

    internal static Dictionary<string, string?> EvidenceValues(JsonElement row, HashSet<string>? selectedKeys = null)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        int count = 0;
        void Read(JsonElement cell, int depth)
        {
            if (depth > 3 || result.Count >= 64)
            { throw new InvalidDataException("Evidence bound exceeded."); }
            if (cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() <= 64)
            { foreach (JsonElement child in cell.EnumerateArray()) { Read(child, depth + 1); } return; }
            if (++count > 64 || cell.ValueKind != JsonValueKind.Object || !cell.TryGetProperty("Key", out JsonElement key)
                || key.ValueKind != JsonValueKind.String || !cell.TryGetProperty("Value", out JsonElement value))
            { throw new InvalidDataException("Semantic evidence cells required."); }
            if (selectedKeys is not null && !selectedKeys.Contains(key.GetString()!))
            { return; }
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null or JsonValueKind.Number)
                || !result.TryAdd(key.GetString()!, value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString()))
            { throw new InvalidDataException("Semantic evidence cells required; duplicates are ambiguous."); }
        }
        Read(row, 0);
        return result;
    }
}
