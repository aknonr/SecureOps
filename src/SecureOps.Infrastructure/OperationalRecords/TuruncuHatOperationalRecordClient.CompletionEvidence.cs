using System.Text.Json;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class TuruncuHatOperationalRecordClient
{
    /// <summary>Bounded operator probe of script-backed completion selectors; never writes or establishes closure.</summary>
    public async Task<JsonElement> DiagnoseCompletionAsync(string sourceId, CancellationToken token)
    {
        if (!_operationalOptions.ReadOnlyIntegrationMode || _operationalOptions.ControlledTestWritesEnabled
            || _operationalOptions.SourceCloseEnabled || !InUseAspectParser.NumericId(sourceId))
        { throw new InvalidDataException("Completion evidence requires an exact identity and all write fences."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        token = deadline.Token;
        using JsonDocument root = await QueryAsync("SMSS_oRFF",
            [$"#%id%#={sourceId} AND #%m_active%#='True' AND #%p_dcc%# IN (4241) AND #%p_rel_group%# IN (68)"],
            ["id", "p_code", "p_emb_dynamic_case_orff"], "in-use-completion-evidence-root", token, 65536);
        JsonElement rows = CompletionRows(root.RootElement, 1);
        if (rows.GetArrayLength() != 1)
        { throw new InvalidDataException("Exact In Use root was not uniquely returned."); }
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var identity = new List<object>();
        object[] rootShape = Cells(rows[0], aliases, 0, privateKeys: ["SET.id"], privateValues: identity).Take(17).ToArray();
        if (rootShape.Length > 16 || identity.Count != 1)
        { throw new InvalidDataException("Exact keyed root identity is required."); }
        JsonElement idCell = JsonSerializer.SerializeToElement(identity[0]).GetProperty("Value");
        if (idCell.ValueKind != JsonValueKind.String || idCell.GetString() != sourceId)
        { throw new InvalidDataException("Source identity mismatch; no activity query performed."); }
        using JsonDocument activities = await QueryAsync("BPM_Actvty",
            [$"(#%m_actvty_task_model%#=103626 OR #%m_actvty_task_model%#=103627) AND #%m_status%#=1 AND #%m_group%#=68 AND #%m_process.m_main_object_id%#={sourceId}"],
            ["id", "m_actvty_task_model", "m_status", "m_group", "m_process.m_main_object_id"],
            "in-use-completion-evidence-activities", token, 65536);
        JsonElement activityRows = CompletionRows(activities.RootElement, 10);
        var activityShapes = new List<object[]>();
        foreach (JsonElement row in activityRows.EnumerateArray())
        {
            object[] cells = Cells(row, aliases, 0).Take(17).ToArray();
            if (cells.Length > 16)
            { throw new InvalidDataException("Activity projection bound exceeded."); }
            activityShapes.Add(cells);
        }
        return JsonSerializer.SerializeToElement(new
        {
            Root = rootShape,
            Activities = activityShapes,
            ActivitySelection = activityRows.GetArrayLength() switch { 0 => "NoneReturned", 1 => "OneCandidateNotApproved", _ => "Ambiguous" },
            DynamicCase = "SelectorKnownRepresentationNeedsReview",
            Attachment = "NotQueriedContractMissing",
            FinalOrState = "NotQueriedContractMissing",
            ConditionalMutation = "NotEstablishedByRead",
            WritesPerformed = false
        });
    }

    private static JsonElement CompletionRows(JsonElement document, int limit)
    {
        JsonElement rows = TuruncuHatQueryParser.GetItems(document);
        JsonElement result = document.GetProperty("QueryResult");
        if (rows.GetArrayLength() > limit)
        { throw new InvalidDataException("Completion evidence row bound exceeded."); }
        foreach (string field in new[] { "MaxPages", "PageNo", "PageNO", "RecordCount" })
        {
            if (!result.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            { continue; }
            string? text = value.ValueKind == JsonValueKind.String ? value.GetString()
                : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
            if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int count)
                || field == "RecordCount" && count != rows.GetArrayLength()
                || field != "RecordCount" && count > 1)
            { throw new InvalidDataException("Partial or unsupported completion projection."); }
        }
        return rows;
    }
}
