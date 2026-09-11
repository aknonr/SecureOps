using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Reusable exact-response projection; never enables candidate fields in production refresh.</summary>
public static class InUseRelatedRequestParser
{
    /// <summary>Projects only a unique identity-checked request; preserves undecoded source cells independently.</summary>
    public static InUseRelatedRequestReporter Parse(string parentId, string serviceItemId, string reference,
        InUseReferencedRequestContract contract, JsonElement response, DateTimeOffset now)
    {
        InUseRelatedRequestReporter result = new(parentId, serviceItemId, reference, contract.ReferenceKind,
            null, null, null, null, "Failed", "Omitted", "Omitted", null);
        JsonElement rows = TuruncuHatQueryParser.GetItems(response);
        if (rows.GetArrayLength() != 1)
        { return result with { State = rows.GetArrayLength() == 0 ? "NotFoundOrNotVisible" : "AmbiguousMatch" }; }
        Dictionary<string, string?> cells = TuruncuHatOperationalRecordClient.EvidenceValues(rows[0],
            ["SET.id", "SET.p_code", "KEY.p_rel_requester", "SET.p_rel_requester"]);
        string? id = cells.GetValueOrDefault("SET.id"), code = cells.GetValueOrDefault("SET.p_code");
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long number) || number <= 0
            || number.ToString(CultureInfo.InvariantCulture) != id
            || !Regex.IsMatch(code ?? "", @"\AOR-[0-9]{1,20}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            || contract.ReferenceKind is not ("SourceId" or "OrCode")
            || (contract.ReferenceKind == "SourceId" ? id : code) != reference)
        { return result with { State = "IdentityMismatch" }; }
        return result with
        {
            RequestId = id,
            RequestCode = code,
            Display = cells.GetValueOrDefault("KEY.p_rel_requester"),
            UserReference = cells.GetValueOrDefault("SET.p_rel_requester"),
            State = "ExactMatch",
            DisplayState = State(cells, "KEY.p_rel_requester"),
            ReferenceState = State(cells, "SET.p_rel_requester"),
            LastVerifiedAt = now
        };
    }

    private static string State(Dictionary<string, string?> cells, string key) => !cells.TryGetValue(key, out string? value)
        ? "Omitted" : value is null ? "Null" : string.IsNullOrWhiteSpace(value) ? "Empty" : "Returned";
}
