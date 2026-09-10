using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient
{
    private async Task<object> ReferencedEvidenceAsync(JsonElement related, InUseReferencedRequestContract contract,
        Func<JsonElement, object> shape, Func<string, string?> alias, CancellationToken token)
    {
        const string prefix = "(LCSIMS_ServiceInstance)m_rid.";
        var cache = new Dictionary<string, object>(StringComparer.Ordinal);
        var links = new List<object>();
        foreach (JsonElement row in TuruncuHatQueryParser.GetItems(related).EnumerateArray())
        {
            Dictionary<string, string?> cells;
            try
            { cells = EvidenceValues(row); }
            catch (InvalidDataException) { links.Add(new { State = "AmbiguousCells" }); continue; }
            string? server = cells.GetValueOrDefault("SET." + prefix + "id");
            string? reference = cells.GetValueOrDefault(contract.ReferenceCellKind + "." + prefix + contract.RfcProperty);
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(reference))
            { links.Add(new { ServiceItem = server is null ? null : alias(server), State = "MissingIdentityOrReference" }); continue; }
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
            string lookup = contract.ReferenceKind == "SourceId" ? "id" : "p_code";
            string filter = contract.ReferenceKind == "SourceId" ? $"#%id%#={reference}" : $"#%p_code%#='{reference}'";
            // No active/catalogue/group filter: an exact related request can be closed or outside In Use.
            using JsonDocument response = await QueryAsync("SMSS_oRFF", [filter],
                ["id", "p_code", "p_rel_requester", contract.ReporterProperty], "in-use-evidence-rfc", token, 65536);
            JsonElement rows = TuruncuHatQueryParser.GetItems(response.RootElement);
            if (rows.GetArrayLength() != 1)
            { return new { State = rows.GetArrayLength() == 0 ? "NotFoundOrNotVisible" : "AmbiguousMatch" }; }
            Dictionary<string, string?> cells = EvidenceValues(rows[0]);
            if (cells.GetValueOrDefault("SET." + lookup) != reference
                || !long.TryParse(cells.GetValueOrDefault("SET.id"), NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0)
            { return new { State = "IdentityMismatch" }; }
            return new
            {
                State = "ExactMatchNotBusinessOwnership",
                Cells = shape(response.RootElement),
                RequesterKey = "KEY.p_rel_requester",
                ReporterSelector = contract.ReporterProperty
            };
        }
        catch (ExternalIntegrationException ex)
        { return new { State = ex.ErrorCode == SecureOps.Shared.Contracts.Api.OperationalErrorCodes.OperationalSourceAuthenticationFailed ? "Forbidden" : "Failed" }; }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or TuruncuHatQueryResultException)
        { return new { State = "MalformedOrAmbiguous" }; }
    }

    private static Dictionary<string, string?> EvidenceValues(JsonElement row)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        void Read(JsonElement cell, int depth)
        {
            if (depth > 3 || result.Count >= 64)
            { throw new InvalidDataException("Evidence bound exceeded."); }
            if (cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() <= 64)
            { foreach (JsonElement child in cell.EnumerateArray()) { Read(child, depth + 1); } return; }
            if (cell.ValueKind != JsonValueKind.Object || !cell.TryGetProperty("Key", out JsonElement key)
                || key.ValueKind != JsonValueKind.String || !cell.TryGetProperty("Value", out JsonElement value)
                || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || !result.TryAdd(key.GetString()!, value.GetString()))
            { throw new InvalidDataException("Semantic evidence cells required; duplicates are ambiguous."); }
        }
        Read(row, 0);
        return result;
    }
}
