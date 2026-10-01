using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>One cell as stored in the file. Formulas are never evaluated; only the cached value is read.</summary>
/// <param name="Text">Cell text (shared/inline/cached string), if any.</param>
/// <param name="Number">Numeric value, if the cell is numeric.</param>
/// <param name="Formula">True when the cell holds a formula; its value is a cached, unverified display value.</param>
public sealed record SheetCell(string? Text, double? Number, bool Formula)
{
    /// <summary>Display text of the cached value.</summary>
    public string? Display => Text ?? Number?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One row with its 1-based sheet row number and column-letter keyed cells.</summary>
public sealed record SheetRow(int RowNumber, IReadOnlyDictionary<string, SheetCell> Cells);

/// <summary>Sheet rows in file order.</summary>
public sealed record SheetData(string Name, IReadOnlyList<SheetRow> Rows);

/// <summary>Reader limits; exceeding any fails closed with a safe code.</summary>
public sealed record SpreadsheetLimits(int MaxUploadBytes = 20 * 1024 * 1024, long MaxTotalUncompressedBytes = 160L * 1024 * 1024,
    long MaxEntryBytes = 80L * 1024 * 1024, int MaxEntries = 400, int MaxCompressionRatio = 250, int MaxRowsPerSheet = 20000,
    int MaxColumns = 120, int MaxCellChars = 32767);

/// <summary>Safe import failure; the code is actionable and contains no file content.</summary>
public sealed class ImportFileException(string code) : Exception(code)
{
    /// <summary>Stable error code.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// Managed, read-only OpenXML reader. No macros, formulas, external links, DTDs or network access.
/// The package is validated (signature, entry count/size/ratio, macro parts) before any XML is read.
/// </summary>
public static class SpreadsheetReader
{
    private const string _main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string _rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>True when the bytes start with the ZIP local header signature.</summary>
    public static bool IsZip(ReadOnlySpan<byte> bytes) => bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;

    /// <summary>Reads the requested sheets (all when null). Unknown sheet names are ignored by the caller.</summary>
    public static IReadOnlyList<SheetData> Read(byte[] bytes, SpreadsheetLimits limits, IReadOnlyCollection<string>? sheets, out IReadOnlyList<string> warnings)
    {
        if (bytes.Length > limits.MaxUploadBytes)
        {
            throw new ImportFileException("FileTooLarge");
        }

        if (!IsZip(bytes))
        {
            throw new ImportFileException("NotAnXlsxFile");
        }

        List<string> notes = [];
        using MemoryStream stream = new(bytes, writable: false);
        using ZipArchive zip = Open(stream);
        Validate(zip, limits, notes);
        string[] shared = SharedStrings(zip, limits);
        Dictionary<string, string> targets = SheetTargets(zip);
        List<SheetData> result = [];
        foreach ((string name, string target) in targets)
        {
            if (sheets is not null && !sheets.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            ZipArchiveEntry entry = zip.GetEntry(target) ?? throw new ImportFileException("WorkbookStructureInvalid");
            result.Add(new SheetData(name, Rows(entry, shared, limits)));
        }

        warnings = notes;
        return result;
    }

    /// <summary>Returns worksheet names in workbook order.</summary>
    public static IReadOnlyList<string> SheetNames(byte[] bytes, SpreadsheetLimits limits)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using ZipArchive zip = Open(stream);
        Validate(zip, limits, []);
        return [.. SheetTargets(zip).Keys];
    }

    /// <summary>Converts a column letter reference to a zero-based index.</summary>
    public static int ColumnIndex(string column)
    {
        int index = 0;
        foreach (char c in column)
        {
            index = index * 26 + (c - 'A' + 1);
        }

        return index - 1;
    }

    private static ZipArchive Open(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException)
        {
            throw new ImportFileException("NotAnXlsxFile");
        }
    }

    private static void Validate(ZipArchive zip, SpreadsheetLimits limits, List<string> notes)
    {
        if (zip.Entries.Count > limits.MaxEntries)
        {
            throw new ImportFileException("TooManyPackageParts");
        }

        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            total += entry.Length;
            if (entry.Length > limits.MaxEntryBytes || total > limits.MaxTotalUncompressedBytes)
            {
                throw new ImportFileException("DecompressedSizeLimit");
            }

            if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > limits.MaxCompressionRatio)
            {
                throw new ImportFileException("CompressionRatioLimit");
            }

            string name = entry.FullName.ToLowerInvariant();
            if (name.EndsWith("vbaproject.bin", StringComparison.Ordinal) || name.Contains("activex", StringComparison.Ordinal))
            {
                throw new ImportFileException("MacroContentRejected");
            }

            if (name.StartsWith("xl/externallinks/", StringComparison.Ordinal) && !notes.Contains("ExternalLinksIgnored"))
            {
                notes.Add("ExternalLinksIgnored");
            }
        }

        ZipArchiveEntry types = zip.GetEntry("[Content_Types].xml") ?? throw new ImportFileException("NotAnXlsxFile");
        string content = ReadText(types, limits);
        if (content.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImportFileException("MacroContentRejected");
        }

        if (!content.Contains("spreadsheetml", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImportFileException("NotAnXlsxFile");
        }
    }

    private static Dictionary<string, string> SheetTargets(ZipArchive zip)
    {
        Dictionary<string, string> rels = [];
        using (XmlReader reader = Xml(zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new ImportFileException("WorkbookStructureInvalid")))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                    && reader.GetAttribute("Id") is { } id && reader.GetAttribute("Target") is { } target)
                {
                    rels[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
            }
        }

        Dictionary<string, string> sheets = [];
        using XmlReader workbook = Xml(zip.GetEntry("xl/workbook.xml") ?? throw new ImportFileException("WorkbookStructureInvalid"));
        while (workbook.Read())
        {
            if (workbook.NodeType == XmlNodeType.Element && workbook.LocalName == "sheet" && workbook.NamespaceURI == _main
                && workbook.GetAttribute("name") is { } name && workbook.GetAttribute("id", _rel) is { } rid && rels.TryGetValue(rid, out string? path))
            {
                sheets[name] = path;
            }
        }

        return sheets;
    }

    private static string[] SharedStrings(ZipArchive zip, SpreadsheetLimits limits)
    {
        ZipArchiveEntry? entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        List<string> values = [];
        using XmlReader reader = Xml(entry);
        while (reader.ReadToFollowing("si", _main))
        {
            StringBuilder text = new();
            using (XmlReader item = reader.ReadSubtree())
            {
                item.Read();
                item.Read();
                while (!item.EOF)
                {
                    if (item.NodeType == XmlNodeType.Element && item.LocalName == "rPh")
                    {
                        item.Skip();
                    }
                    else if (item.NodeType == XmlNodeType.Element && item.LocalName == "t")
                    {
                        text.Append(item.ReadElementContentAsString());
                        if (text.Length > limits.MaxCellChars)
                        {
                            throw new ImportFileException("CellTooLong");
                        }
                    }
                    else
                    {
                        item.Read();
                    }
                }
            }

            values.Add(text.ToString());
        }

        return [.. values];
    }

    private static List<SheetRow> Rows(ZipArchiveEntry entry, string[] shared, SpreadsheetLimits limits)
    {
        List<SheetRow> rows = [];
        using XmlReader reader = Xml(entry);
        int rowNumber = 0;
        while (reader.ReadToFollowing("row", _main))
        {
            rowNumber = int.TryParse(reader.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out int r) ? r : rowNumber + 1;
            Dictionary<string, SheetCell> cells = [];
            using (XmlReader row = reader.ReadSubtree())
            {
                row.Read();
                row.Read();
                while (!row.EOF)
                {
                    if (row.NodeType == XmlNodeType.Element && row.LocalName == "c")
                    {
                        string reference = row.GetAttribute("r") ?? string.Empty;
                        string column = new([.. reference.TakeWhile(char.IsAsciiLetterUpper)]);
                        string? type = row.GetAttribute("t");
                        using (XmlReader cell = row.ReadSubtree())
                        {
                            if (column.Length > 0 && ColumnIndex(column) < limits.MaxColumns && Cell(cell, type, shared, limits) is { } value)
                            {
                                cells[column] = value;
                            }
                        }

                        row.Skip();
                    }
                    else
                    {
                        row.Read();
                    }
                }
            }

            if (cells.Count > 0)
            {
                rows.Add(new SheetRow(rowNumber, cells));
                if (rows.Count > limits.MaxRowsPerSheet)
                {
                    throw new ImportFileException("TooManyRows");
                }
            }
        }

        return rows;
    }

    private static SheetCell? Cell(XmlReader cell, string? type, string[] shared, SpreadsheetLimits limits)
    {
        bool formula = false;
        string? value = null;
        StringBuilder? inline = null;
        cell.Read();
        cell.Read();
        while (!cell.EOF)
        {
            if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "f")
            {
                formula = true;
                cell.Skip();
            }
            else if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "v")
            {
                value = cell.ReadElementContentAsString();
            }
            else if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "t" && type == "inlineStr")
            {
                (inline ??= new StringBuilder()).Append(cell.ReadElementContentAsString());
            }
            else
            {
                cell.Read();
            }
        }

        string? text = type switch
        {
            "s" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < shared.Length => shared[i],
            "inlineStr" => inline?.ToString(),
            "str" or "e" => value,
            "b" => value == "1" ? "TRUE" : value is null ? null : "FALSE",
            _ => null
        };
        double? number = type is null or "n" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
        if (text?.Length > limits.MaxCellChars)
        {
            throw new ImportFileException("CellTooLong");
        }

        return text is null && number is null && !formula ? null : new SheetCell(text, number, formula);
    }

    private static XmlReader Xml(ZipArchiveEntry entry) => XmlReader.Create(entry.Open(), new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = false,
        CloseInput = true
    });

    private static string ReadText(ZipArchiveEntry entry, SpreadsheetLimits limits)
    {
        if (entry.Length > limits.MaxEntryBytes)
        {
            throw new ImportFileException("DecompressedSizeLimit");
        }

        using StreamReader reader = new(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
