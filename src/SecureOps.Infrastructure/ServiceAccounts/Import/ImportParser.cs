using System.Globalization;
using System.Text;
using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>
/// Turns uploaded bytes into staged rows for one profile. It never evaluates formulas, never invents
/// dates, never infers a parser for unseen headers and never regenerates identity from row order.
/// </summary>
public static class ImportParser
{
    /// <summary>The only accepted migration package schema.</summary>
    public const string PackageSchema = "wasas.service-account-migration.v1";

    private const int _headerSearchRows = 20;
    private const int _maxGenericMappings = 20;
    private const int _archiveBlockGap = 3;

    /// <summary>Parses the file for the profile. File-level problems throw <see cref="ImportFileException"/>.</summary>
    public static StagedFile Parse(string profile, byte[] bytes, StageImportRequest request, string sha256, SpreadsheetLimits limits) => profile switch
    {
        ServiceAccountImportProfiles.CoordinationList => Table(bytes, request, limits, ImportHeaders.CoordinationList, ServiceAccountImportProfiles.CoordinationList),
        ServiceAccountImportProfiles.DbaHandover => Table(bytes, request, limits, ImportHeaders.DbaHandover, ServiceAccountImportProfiles.DbaHandover),
        ServiceAccountImportProfiles.Generic => Generic(bytes, request, limits),
        ServiceAccountImportProfiles.LegacyWorkbook => LegacyWorkbook(bytes, sha256, limits),
        ServiceAccountImportProfiles.LegacyPackage => LegacyPackage(bytes, limits),
        _ => throw new ImportFileException("UnknownProfile")
    };

    /// <summary>Stable legacy key shared by the workbook and the JSON package built from the same workbook bytes.</summary>
    public static string LegacyReference(string workbookSha256, string sheet, int row) =>
        $"legacy:{workbookSha256.ToLowerInvariant()}:{sheet}:{row.ToString(CultureInfo.InvariantCulture)}";

    private static StagedFile Table(byte[] bytes, StageImportRequest request, SpreadsheetLimits limits,
        IReadOnlyDictionary<string, string> headers, string observationProfile)
    {
        (SheetData sheet, List<string> warnings) = OneSheet(bytes, request.Sheet, limits);
        (int headerIndex, Dictionary<string, string> columns) = FindHeader(sheet, h => ImportHeaders.Field(headers, h), StagedFields.Account)
            ?? throw new ImportFileException("RequiredHeaderMissing");
        List<ImportColumnMapping> mapping = Mapping(sheet.Rows[headerIndex], columns, "Profilde tanımlı değil; içe alınmadı.");
        int formulas = 0;
        List<StagedRow> rows = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (SheetRow row in sheet.Rows.Skip(headerIndex + 1))
        {
            StagedRow staged = Stage(sheet.Name, row, sheet.Rows[headerIndex], columns, StagedKinds.Observation, null, null, ref formulas,
                new() { [StagedFields.ObservationProfile] = observationProfile });
            if (staged.Fields.Values.All(v => v is null))
            {
                continue;
            }

            rows.Add(Duplicate(staged, seen));
        }

        return new StagedFile(rows, mapping, warnings, formulas, 0, []);
    }

    private static StagedFile Generic(byte[] bytes, StageImportRequest request, SpreadsheetLimits limits)
    {
        (SheetData sheet, List<string> warnings) = OneSheet(bytes, request.Sheet, limits);
        if (sheet.Rows.Count == 0)
        {
            throw new ImportFileException("EmptyFile");
        }

        SheetRow header = sheet.Rows[0];
        IReadOnlyList<ImportColumnMapping> requested = request.Mapping ?? [];
        Dictionary<string, string> targets = new(StringComparer.Ordinal);
        foreach (ImportColumnMapping line in requested.Where(m => m.TargetField is not null && !m.Ignored))
        {
            if (!ServiceAccountImportFields.All.Contains(line.TargetField!, StringComparer.Ordinal) || targets.ContainsValue(Canonical(line.TargetField!))
                || ServiceAccountText.LabelKey(line.SourceHeader) is not { } key || targets.Count >= _maxGenericMappings)
            {
                throw new ImportFileException("MappingInvalid");
            }

            targets[key] = Canonical(line.TargetField!);
        }

        var columns = header.Cells
            .Where(c => ServiceAccountText.LabelKey(c.Value.Display) is { } k && targets.ContainsKey(k))
            .ToDictionary(c => c.Key, c => targets[ServiceAccountText.LabelKey(c.Value.Display)!]);
        List<ImportColumnMapping> mapping = Mapping(header, columns, "Eşlenmedi; içe alınmadı.");
        if (!columns.ContainsValue(StagedFields.Account))
        {
            // Mapping is required before rows can be staged; headers are returned for the operator to map.
            return new StagedFile([], mapping, [.. warnings, "MappingRequired"], 0, 0, []);
        }

        int formulas = 0;
        List<StagedRow> rows = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (SheetRow row in sheet.Rows.Skip(1))
        {
            StagedRow staged = Stage(sheet.Name, row, header, columns, StagedKinds.Observation, null, null, ref formulas,
                new() { [StagedFields.ObservationProfile] = ServiceAccountImportProfiles.Generic });
            if (staged.Fields.Values.All(v => v is null))
            {
                continue;
            }

            staged = Duplicate(staged, seen);
            rows.Add(staged);
            if (staged["OwnerTeam"] is not null)
            {
                rows.Add(staged with { Kind = StagedKinds.Ownership });
            }
        }

        return new StagedFile(rows, mapping, warnings, formulas, 0, []);
    }

    private static StagedFile LegacyWorkbook(byte[] bytes, string sha256, SpreadsheetLimits limits)
    {
        // Archive sheets (original source copies) carry no report date in the workbook; they are not re-dated
        // here. Observations come from a declared-date source (coordination-list profile or migration package).
        string[] wanted = [.. ImportHeaders.Legacy.Keys];
        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(bytes, limits, wanted, out IReadOnlyList<string> readerWarnings);
        List<string> warnings = [.. readerWarnings, .. ImportHeaders.LegacyArchives.Keys.Select(k => "ArchiveSheetNotImported:" + k)];
        List<StagedRow> rows = [];
        List<ImportColumnMapping> mapping = [];
        int formulas = 0, ignored = 0;
        foreach (SheetData sheet in sheets)
        {
            bool archive = ImportHeaders.LegacyArchives.TryGetValue(sheet.Name, out IReadOnlyDictionary<string, string>? archiveHeaders);
            (string kind, IReadOnlyDictionary<string, string> headers) = archive
                ? (StagedKinds.Observation, archiveHeaders!)
                : ImportHeaders.Legacy[sheet.Name];
            string anchor = kind == StagedKinds.Finding ? StagedFields.LegacyId : StagedFields.Account;
            if (FindHeader(sheet, h => ImportHeaders.Field(headers, h), anchor) is not { } found)
            {
                warnings.Add("LegacySheetHeaderMissing:" + sheet.Name);
                continue;
            }

            (int headerIndex, Dictionary<string, string> columns) = found;
            SheetRow header = sheet.Rows[headerIndex];
            int helper = header.Cells.Count(c => !columns.ContainsKey(c.Key) && c.Value.Display is not null);
            ignored += helper;
            mapping.AddRange(Mapping(header, columns, "Yardımcı/özet sütun; iş verisi olarak alınmadı.").Select(m => m with { SourceHeader = sheet.Name + " / " + m.SourceHeader }));
            string profile = sheet.Name == "Kaynak_DBA" ? ServiceAccountImportProfiles.DbaHandover : ServiceAccountImportProfiles.CoordinationList;
            HashSet<string> seen = new(StringComparer.Ordinal);
            int previous = header.RowNumber;
            foreach (SheetRow row in sheet.Rows.Skip(headerIndex + 1))
            {
                if (archive && (row.RowNumber - previous > _archiveBlockGap || IsTitleRow(row)))
                {
                    // Later blocks are older source periods; they are never re-dated to this batch.
                    warnings.Add("ArchiveBlockIgnored:" + sheet.Name + ":" + row.RowNumber.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                previous = row.RowNumber;
                if (!row.Cells.Any(c => columns.TryGetValue(c.Key, out string? f) && f != StagedFields.Status && !c.Value.Formula
                    && c.Value.Display is { Length: > 0 }))
                {
                    continue;
                }

                Dictionary<string, string?> extra = archive ? new() { [StagedFields.ObservationProfile] = profile } : [];
                StagedRow staged = Stage(sheet.Name, row, header, columns, kind, archive ? null : LegacyReference(sha256, sheet.Name, row.RowNumber),
                    null, ref formulas, extra);
                if (staged.Fields.Where(f => f.Key != StagedFields.ObservationProfile).All(f => f.Value is null))
                {
                    continue;
                }

                AddLegacy(rows, archive ? Duplicate(staged, seen) : staged);
            }
        }

        if (!sheets.Any(s => s.Name == "Hesap_Bilgileri"))
        {
            throw new ImportFileException("LegacyWorkbookSheetsMissing");
        }

        return new StagedFile(rows, mapping, warnings, formulas, ignored, []);
    }

    private static StagedFile LegacyPackage(byte[] bytes, SpreadsheetLimits limits)
    {
        if (bytes.Length > limits.MaxUploadBytes)
        {
            throw new ImportFileException("FileTooLarge");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException)
        {
            throw new ImportFileException("NotAMigrationPackage");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "schemaVersion") != PackageSchema)
            {
                throw new ImportFileException("UnsupportedPackageSchema");
            }

            string workbookSha = Text(root, "reportWorkbookSha256") is { Length: 64 } sha && sha.All(Uri.IsHexDigit)
                ? sha : throw new ImportFileException("PackageWorkbookHashMissing");
            Dictionary<string, string[]> links = Links(root);
            List<StagedRow> rows = [];
            List<string> warnings = [];
            int formulas = 0;
            if (!root.TryGetProperty("records", out JsonElement records) || records.ValueKind != JsonValueKind.Object)
            {
                throw new ImportFileException("PackageRecordsMissing");
            }

            foreach ((string sheet, (string kind, IReadOnlyDictionary<string, string> headers)) in ImportHeaders.Legacy)
            {
                if (!records.TryGetProperty(sheet, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
                {
                    warnings.Add("PackageSectionMissing:" + sheet);
                    continue;
                }

                foreach (JsonElement item in items.EnumerateArray())
                {
                    int rowNumber = item.TryGetProperty("legacySourceRow", out JsonElement r) && r.TryGetInt32(out int n) ? n
                        : throw new ImportFileException("PackageRowInvalid");
                    Guid? key = Guid.TryParse(Text(item, "migrationKey"), out Guid g) ? g : null;
                    Dictionary<string, SheetCell?> cells = [];
                    if (item.TryGetProperty("fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty field in fields.EnumerateObject())
                        {
                            cells[field.Name] = Cell(field.Value);
                        }
                    }

                    Dictionary<string, string?> extra = [];
                    if (kind == StagedKinds.Communication && key is { } comm && links.TryGetValue(comm.ToString("D"), out string[]? linked))
                    {
                        extra[StagedFields.LinkedAccounts] = JsonSerializer.Serialize(linked);
                    }

                    AddLegacy(rows, StageNamed(sheet, rowNumber, cells, headers, kind, LegacyReference(workbookSha, sheet, rowNumber), key, ref formulas, extra));
                }
            }

            rows.AddRange(SourceRows(root, "Book1", ImportHeaders.CoordinationList, ServiceAccountImportProfiles.CoordinationList, ref formulas));
            rows.AddRange(SourceRows(root, "DBA", ImportHeaders.DbaHandover, ServiceAccountImportProfiles.DbaHandover, ref formulas));
            List<string[]> aliases = [];
            if (root.TryGetProperty("personAliases", out JsonElement pairs) && pairs.ValueKind == JsonValueKind.Array)
            {
                aliases.AddRange(pairs.EnumerateArray()
                    .Where(p => p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 2)
                    .Select(p => new[] { p[0].GetString() ?? string.Empty, p[1].GetString() ?? string.Empty })
                    .Where(p => p[0].Length is > 0 and <= 200 && p[1].Length is > 0 and <= 200));
            }

            DateOnly? suggested = ImportValues.TryDate(Text(root, "sourceReportDate"), out DateOnly? declared) ? declared : null;
            return new StagedFile(rows, [], warnings, formulas, 0, aliases, suggested);
        }
    }

    private static IEnumerable<StagedRow> SourceRows(JsonElement root, string section, IReadOnlyDictionary<string, string> headers, string profile, ref int formulas)
    {
        if (!root.TryGetProperty("sourceRows", out JsonElement sources) || !sources.TryGetProperty(section, out JsonElement table)
            || table.ValueKind != JsonValueKind.Array || table.GetArrayLength() == 0)
        {
            return [];
        }

        JsonElement[] all = [.. table.EnumerateArray()];
        string?[] header = [.. all[0].EnumerateArray().Select(h => h.ValueKind == JsonValueKind.String ? h.GetString() : null)];
        if (header.Select(h => ImportHeaders.Field(headers, h)).Count(f => f is not null) != headers.Count)
        {
            throw new ImportFileException("PackageSourceHeaderMismatch");
        }

        List<StagedRow> rows = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        // Entry 0 is the header: data rows are 2..N in sheet numbering and the header is never an account.
        for (int i = 1; i < all.Length; i++)
        {
            JsonElement[] values = [.. all[i].EnumerateArray()];
            Dictionary<string, SheetCell?> cells = [];
            for (int c = 0; c < header.Length && c < values.Length; c++)
            {
                if (header[c] is { } name)
                {
                    cells[name] = Cell(values[c]);
                }
            }

            StagedRow staged = StageNamed("sourceRows." + section, i + 1, cells, headers, StagedKinds.Observation, null, null, ref formulas,
                new() { [StagedFields.ObservationProfile] = profile });
            rows.Add(Duplicate(staged, seen));
        }

        return rows;
    }

    private static bool IsTitleRow(SheetRow row) =>
        row.Cells.Count >= 3 && row.Cells.Values.Select(c => c.Display).Distinct(StringComparer.Ordinal).Count() == 1;

    private static void AddLegacy(List<StagedRow> rows, StagedRow staged)
    {
        rows.Add(staged);
        if (staged.Kind == StagedKinds.Account && (!ServiceAccountText.IsPlaceholder(staged[StagedFields.OwnerTeam]) || !ServiceAccountText.IsPlaceholder(staged[StagedFields.OwnerPerson])))
        {
            rows.Add(staged with { Kind = StagedKinds.Ownership });
        }
    }

    private static Dictionary<string, string[]> Links(JsonElement root)
    {
        Dictionary<string, string[]> links = new(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("communicationLinks", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (Text(item, "communicationMigrationKey") is { } key && item.TryGetProperty("accounts", out JsonElement accounts) && accounts.ValueKind == JsonValueKind.Array)
                {
                    links[key] = [.. accounts.EnumerateArray().Select(a => a.GetString()).OfType<string>()];
                }
            }
        }

        return links;
    }

    private static SheetCell? Cell(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new SheetCell(value.GetString(), null, false),
        JsonValueKind.Number => new SheetCell(null, value.GetDouble(), false),
        JsonValueKind.True => new SheetCell("TRUE", null, false),
        JsonValueKind.False => new SheetCell("FALSE", null, false),
        _ => null
    };

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static StagedRow StageNamed(string sheet, int rowNumber, Dictionary<string, SheetCell?> cells, IReadOnlyDictionary<string, string> headers,
        string kind, string? legacy, Guid? key, ref int formulas, Dictionary<string, string?>? extra = null)
    {
        Dictionary<string, SheetCell?> byField = [];
        Dictionary<string, string?> original = new(StringComparer.Ordinal);
        foreach ((string name, SheetCell? cell) in cells)
        {
            original[name] = cell?.Display;
            if (ImportHeaders.Field(headers, name) is { } field)
            {
                byField[field] = cell;
            }
        }

        return Build(sheet, rowNumber, kind, original, byField, legacy, key, ref formulas, extra);
    }

    private static StagedRow Stage(string sheet, SheetRow row, SheetRow header, Dictionary<string, string> columns, string kind,
        string? legacy, Guid? key, ref int formulas, Dictionary<string, string?>? extra = null)
    {
        Dictionary<string, SheetCell?> byField = [];
        Dictionary<string, string?> original = new(StringComparer.Ordinal);
        foreach ((string column, SheetCell headerCell) in header.Cells.OrderBy(c => SpreadsheetReader.ColumnIndex(c.Key)))
        {
            row.Cells.TryGetValue(column, out SheetCell? cell);
            if (headerCell.Display is { } label)
            {
                original[label] = cell?.Display;
            }

            if (columns.TryGetValue(column, out string? field))
            {
                byField[field] = cell;
            }
        }

        return Build(sheet, row.RowNumber, kind, original, byField, legacy, key, ref formulas, extra);
    }

    private static StagedRow Build(string sheet, int rowNumber, string kind, Dictionary<string, string?> original, Dictionary<string, SheetCell?> byField,
        string? legacy, Guid? key, ref int formulas, Dictionary<string, string?>? extra)
    {
        Dictionary<string, string?> fields = new(StringComparer.Ordinal);
        List<string> errors = [], warnings = [];
        foreach ((string field, SheetCell? cell) in byField)
        {
            if (cell?.Formula == true && field != StagedFields.LegacyId)
            {
                formulas++;
                warnings.Add("FormulaValue:" + field);
            }

            switch (ImportHeaders.KindOf(field))
            {
                case ImportHeaders.ValueKind.Date:
                    if (ImportValues.TryTimestamp(cell, out DateTime? date))
                    {
                        fields[field] = date is { } d ? ImportValues.Date(DateOnly.FromDateTime(d)) : null;
                        if (date is { } withTime && ImportValues.HasTime(withTime))
                        {
                            warnings.Add("TimeWithoutZoneKeptAsDate:" + field);
                        }
                    }
                    else
                    {
                        errors.Add("InvalidDate:" + field);
                    }

                    break;
                case ImportHeaders.ValueKind.Timestamp:
                    if (ImportValues.TryTimestamp(cell, out DateTime? stamp))
                    {
                        fields[field] = stamp is { } s ? ImportValues.Naive(s) : null;
                    }
                    else
                    {
                        // Source observations may state "no information" in a date column: keep the text, invent no date.
                        fields[field] = null;
                        fields[field + "Text"] = ServiceAccountText.Clean(cell?.Display);
                        warnings.Add("ObservationTextInDateColumn:" + field);
                    }

                    break;
                default:
                    fields[field] = ServiceAccountText.Clean(cell?.Display);
                    break;
            }
        }

        foreach ((string name, string? value) in extra ?? [])
        {
            fields[name] = value;
        }

        bool accountOptional = kind is StagedKinds.Communication;
        if (!accountOptional && ServiceAccountText.AccountKey(fields.GetValueOrDefault(StagedFields.Account)) is null)
        {
            errors.Add("AccountNameMissing");
        }

        return new StagedRow(sheet, rowNumber, kind, original, fields, errors, warnings, legacy, key);
    }

    private static StagedRow Duplicate(StagedRow row, HashSet<string> seen) =>
        ServiceAccountText.AccountKey(row[StagedFields.Account]) is { } account && !seen.Add(account)
            ? row with { Errors = [.. row.Errors, "DuplicateInFile"] }
            : row;

    private static (int HeaderIndex, Dictionary<string, string> Columns)? FindHeader(SheetData sheet, Func<string?, string?> field, string anchor)
    {
        for (int i = 0; i < Math.Min(_headerSearchRows, sheet.Rows.Count); i++)
        {
            var columns = sheet.Rows[i].Cells
                .Select(c => (c.Key, Field: field(c.Value.Display)))
                .Where(c => c.Field is not null)
                .GroupBy(c => c.Field!)
                .ToDictionary(g => g.First().Key, g => g.Key);
            if (columns.ContainsValue(anchor))
            {
                return (i, columns);
            }
        }

        return null;
    }

    private static List<ImportColumnMapping> Mapping(SheetRow header, Dictionary<string, string> columns, string ignoredNote) =>
        [.. header.Cells
            .Where(c => c.Value.Display is not null)
            .OrderBy(c => SpreadsheetReader.ColumnIndex(c.Key))
            .Select(c => columns.TryGetValue(c.Key, out string? field)
                ? new ImportColumnMapping(c.Value.Display!, field)
                : new ImportColumnMapping(c.Value.Display!, null, Ignored: true, Note: ignoredNote))];

    private static (SheetData Sheet, List<string> Warnings) OneSheet(byte[] bytes, string? sheetName, SpreadsheetLimits limits)
    {
        if (!SpreadsheetReader.IsZip(bytes))
        {
            return (Csv(bytes, limits), ["CsvUtf8Assumed"]);
        }

        IReadOnlyList<string> names = SpreadsheetReader.SheetNames(bytes, limits);
        string name = sheetName ?? names.FirstOrDefault() ?? throw new ImportFileException("EmptyFile");
        if (!names.Contains(name, StringComparer.Ordinal))
        {
            throw new ImportFileException("SheetNotFound");
        }

        IReadOnlyList<SheetData> sheets = SpreadsheetReader.Read(bytes, limits, [name], out IReadOnlyList<string> warnings);
        return (sheets.Single(), [.. warnings]);
    }

    /// <summary>RFC 4180 CSV (UTF-8, delimiter detected from the header); values are text only.</summary>
    private static SheetData Csv(byte[] bytes, SpreadsheetLimits limits)
    {
        if (bytes.Length > limits.MaxUploadBytes)
        {
            throw new ImportFileException("FileTooLarge");
        }

        if (bytes.Take(512).Any(b => b == 0))
        {
            throw new ImportFileException("UnsupportedFileType");
        }

        string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        string firstLine = text.Split('\n', 2)[0];
        char delimiter = new[] { ';', ',', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();
        List<SheetRow> rows = [];
        List<string> current = [];
        StringBuilder cell = new();
        bool quoted = false;
        int rowNumber = 1;
        for (int i = 0; i <= text.Length; i++)
        {
            char c = i < text.Length ? text[i] : '\n';
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                current.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\n')
            {
                current.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                if (current.Count > limits.MaxColumns)
                {
                    throw new ImportFileException("TooManyColumns");
                }

                if (current.Any(v => v.Length > 0))
                {
                    rows.Add(new SheetRow(rowNumber, current.Select((v, index) => (Column(index), v))
                        .Where(p => p.v.Length > 0).ToDictionary(p => p.Item1, p => new SheetCell(p.v, null, false))));
                    if (rows.Count > limits.MaxRowsPerSheet + 1)
                    {
                        throw new ImportFileException("TooManyRows");
                    }
                }

                current = [];
                rowNumber++;
            }
            else
            {
                cell.Append(c);
            }

            if (cell.Length > limits.MaxCellChars)
            {
                throw new ImportFileException("CellTooLong");
            }
        }

        return new SheetData("csv", rows);
    }

    private static string Column(int index)
    {
        string name = string.Empty;
        for (int i = index + 1; i > 0; i = (i - 1) / 26)
        {
            name = (char)('A' + (i - 1) % 26) + name;
        }

        return name;
    }

    private static string Canonical(string target) => target switch
    {
        ServiceAccountImportFields.AccountName => StagedFields.Account,
        ServiceAccountImportFields.Domain => StagedFields.Domain,
        ServiceAccountImportFields.Organization => StagedFields.Organization,
        ServiceAccountImportFields.OwnerTeam => StagedFields.OwnerTeam,
        ServiceAccountImportFields.ConsumerTeam => StagedFields.ConsumerTeam,
        ServiceAccountImportFields.PasswordLastSet => StagedFields.PasswordLastSet,
        ServiceAccountImportFields.LastLogon => StagedFields.LastLogonAdOrLdap,
        _ => StagedFields.Comment
    };
}
