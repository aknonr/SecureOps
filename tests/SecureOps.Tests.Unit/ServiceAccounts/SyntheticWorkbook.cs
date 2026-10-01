using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>Cell for synthetic workbooks: text, number or formula with cached value.</summary>
internal sealed record SynCell(string? Text = null, double? Number = null, string? Formula = null)
{
    public static implicit operator SynCell(string text) => new(text);
    public static implicit operator SynCell(double number) => new(Number: number);
}

/// <summary>Minimal synthetic OpenXML writer for tests (no real data).</summary>
internal static class SyntheticWorkbook
{
    public static byte[] Create(IReadOnlyList<(string Name, IReadOnlyList<(int Row, SynCell?[] Cells)> Rows)> sheets, bool macro = false, string? padEntry = null, int padBytes = 0)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))
                + "</Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + string.Concat(sheets.Select((s, i) => $"<sheet name=\"{SecurityElement.Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))
                + "</sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
                + "</Relationships>");
            for (int i = 0; i < sheets.Count; i++)
            {
                StringBuilder xml = new("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                foreach ((int row, SynCell?[] cells) in sheets[i].Rows)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{row}\">");
                    for (int c = 0; c < cells.Length; c++)
                    {
                        if (cells[c] is not { } cell)
                        {
                            continue;
                        }

                        string reference = Column(c) + row.ToString(CultureInfo.InvariantCulture);
                        if (cell.Formula is { } formula)
                        {
                            xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"str\"><f>{SecurityElement.Escape(formula)}</f><v>{SecurityElement.Escape(cell.Text ?? string.Empty)}</v></c>");
                        }
                        else if (cell.Number is { } number)
                        {
                            xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{number.ToString(CultureInfo.InvariantCulture)}</v></c>");
                        }
                        else
                        {
                            xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(cell.Text ?? string.Empty)}</t></is></c>");
                        }
                    }

                    xml.Append("</row>");
                }

                xml.Append("</sheetData></worksheet>");
                Add(zip, $"xl/worksheets/sheet{i + 1}.xml", xml.ToString());
            }

            if (macro)
            {
                Add(zip, "xl/vbaProject.bin", "synthetic");
            }

            if (padEntry is not null)
            {
                ZipArchiveEntry entry = zip.CreateEntry(padEntry, CompressionLevel.SmallestSize);
                using Stream output = entry.Open();
                output.Write(new byte[padBytes]);
            }
        }

        return stream.ToArray();
    }

    public static string Column(int index)
    {
        string name = string.Empty;
        for (int i = index + 1; i > 0; i = (i - 1) / 26)
        {
            name = (char)('A' + (i - 1) % 26) + name;
        }

        return name;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
