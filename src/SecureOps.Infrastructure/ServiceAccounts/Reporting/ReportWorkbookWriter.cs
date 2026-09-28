using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// Deterministic, text-and-number XLSX writer: no formulas, shared formulas, macros, external links or
/// calculation chain. Text that a spreadsheet could interpret as a formula is neutralized; dates are real
/// date serials with a date number format. Same document → same bytes.
/// </summary>
public static class ReportWorkbookWriter
{
    private const string _main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const int _dateStyle = 1;
    private const int _headerStyle = 2;
    private static readonly char[] _formulaStarts = ['=', '+', '-', '@', '\t', '\r', '\n'];

    /// <summary>Renders the document; entry timestamps are fixed to the snapshot time.</summary>
    public static byte[] Write(ReportDocument document)
    {
        using MemoryStream stream = new();
        DateTimeOffset stamp = document.CreatedAt.Year < 1980 ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) : document.CreatedAt;
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            List<(string Name, List<IReadOnlyList<ReportCell>> Rows, bool HasHeaderBlock)> sheets = [("Rapor", [.. document.Header.Select(h => (IReadOnlyList<ReportCell>)[h.Label, h.Value])], false)];
            foreach (ReportSection section in document.Sections)
            {
                sheets.Add((SheetName(section.Title, sheets.Select(s => s.Name)), [section.Headers.Select(h => (ReportCell)h).ToArray(), .. section.Rows], true));
            }

            Add(zip, "[Content_Types].xml", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>{string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))}</Types>
                """);
            Add(zip, "_rels/.rels", stamp, """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Add(zip, "xl/workbook.xml", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="{_main}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>{string.Concat(sheets.Select((s, i) => $"<sheet name=\"{SecurityElement.Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))}</sheets></workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))}<Relationship Id="rId{sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """);
            Add(zip, "xl/styles.xml", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="{_main}"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                """);
            for (int i = 0; i < sheets.Count; i++)
            {
                Add(zip, $"xl/worksheets/sheet{i + 1}.xml", stamp, Sheet(sheets[i].Rows, sheets[i].HasHeaderBlock));
            }
        }

        return stream.ToArray();
    }

    /// <summary>Prefixes text that could be evaluated as a formula when copied or re-saved.</summary>
    public static string Neutralize(string text) => text.Length > 0 && _formulaStarts.Contains(text[0]) ? "'" + text : text;

    private static string Sheet(List<IReadOnlyList<ReportCell>> rows, bool headerRow)
    {
        StringBuilder xml = new($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{_main}\">");
        int columns = rows.Count == 0 ? 1 : rows.Max(r => r.Count);
        xml.Append("<cols>");
        for (int c = 1; c <= columns; c++)
        {
            int width = Math.Clamp(rows.Select(r => r.Count >= c ? r[c - 1].Display.Length : 0).DefaultIfEmpty(10).Max() + 2, 10, 70);
            xml.Append(CultureInfo.InvariantCulture, $"<col min=\"{c}\" max=\"{c}\" width=\"{width}\" customWidth=\"1\"/>");
        }

        xml.Append("</cols><sheetData>");
        for (int r = 0; r < rows.Count; r++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Count; c++)
            {
                ReportCell cell = rows[r][c];
                string reference = Column(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                string style = headerRow && r == 0 ? $" s=\"{_headerStyle}\"" : string.Empty;
                if (cell.Number is { } number)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"{style}><v>{number}</v></c>");
                }
                else if (cell.Date is { } date)
                {
                    int serial = date.DayNumber - new DateOnly(1899, 12, 30).DayNumber;
                    xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{_dateStyle}\"><v>{serial}</v></c>");
                }
                else
                {
                    xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Escape(Neutralize(cell.Text ?? string.Empty))}</t></is></c>");
                }
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    private static string Escape(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            // XML 1.0 forbids most control characters; drop them rather than produce a file Excel must repair.
            if (c < 0x20 && c is not ('\t' or '\n' or '\r'))
            {
                continue;
            }

            builder.Append(c switch { '<' => "&lt;", '>' => "&gt;", '&' => "&amp;", '"' => "&quot;", _ => c.ToString() });
        }

        return builder.ToString();
    }

    private static string SheetName(string title, IEnumerable<string> existing)
    {
        string name = new([.. title.Where(c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']' or '\''))]);
        name = name.Length > 31 ? name[..31] : name;
        string candidate = name;
        for (int i = 2; existing.Contains(candidate, StringComparer.OrdinalIgnoreCase); i++)
        {
            candidate = name[..Math.Min(name.Length, 28)] + " " + i.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
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

    private static void Add(ZipArchive zip, string name, DateTimeOffset stamp, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = stamp;
        using Stream output = entry.Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(content.Trim());
        output.Write(bytes);
    }
}
