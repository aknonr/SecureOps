using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// Deterministic, text-and-number XLSX writer: no formulas, shared formulas, macros, external links or
/// calculation chain. Text that a spreadsheet could interpret as a formula is neutralized; dates are real
/// date serials with a date number format. Table sheets get a shaded, bordered header row that stays frozen while
/// scrolling and an AutoFilter; the cover sheet starts with the report title. The executive summary carries native Excel
/// charts whose series reference the last sheet ("Grafik verisi") and cache the same values. Same document → same bytes.
/// </summary>
public static class ReportWorkbookWriter
{
    private const string _main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const int _dateStyle = 1;
    private const int _headerStyle = 2;
    private const int _titleStyle = 3;
    private const int _tileLabelStyle = 4;
    private const int _tileValueStyle = 5;
    private const int _subtitleStyle = 6;
    private const int _tileNoteStyle = 7;
    private const int _blockTitleStyle = 8;
    private const int _dashboardColumns = 12;
    private const int _tilesPerRow = 4;
    private const int _rowsPerChart = 16;
    private const string _chartDataSheet = "Grafik verisi";
    private static readonly char[] _formulaStarts = ['=', '+', '-', '@', '\t', '\r', '\n'];

    /// <summary>Renders the document; entry timestamps are fixed to the snapshot time.</summary>
    public static byte[] Write(ReportDocument document)
    {
        using MemoryStream stream = new();
        DateTimeOffset stamp = document.CreatedAt.Year < 1980 ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) : document.CreatedAt;
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            List<(string Name, List<IReadOnlyList<ReportCell>> Rows, bool HasHeaderBlock)> tables =
                [("Rapor", [[document.Title], [], .. document.Header.Select(h => (IReadOnlyList<ReportCell>)[h.Label, h.Value])], false)];
            foreach (ReportSection section in document.Sections)
            {
                tables.Add((SheetName(section.Title, tables.Select(s => s.Name).Append("Yönetici özeti").Append(_chartDataSheet)), [section.Headers.Select(h => (ReportCell)h).ToArray(), .. section.Rows], true));
            }

            List<ReportChartXml.DataBlock> blocks = ChartData(document, tables);
            List<(string Name, string Xml, string? Filter)> sheets = [.. tables.Select(t => (t.Name, Sheet(t.Rows, t.HasHeaderBlock), FilterRange(t.Rows, t.HasHeaderBlock)))];
            int chartRow = 0;
            if (document.Dashboard is { } dashboard)
            {
                sheets.Insert(0, ("Yönetici özeti", DashboardSheet(document.Title, dashboard, blocks.Count, out chartRow), null));
            }

            Add(zip, "[Content_Types].xml", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>{string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))}{ChartContentTypes(blocks.Count)}</Types>
                """);
            Add(zip, "_rels/.rels", stamp, """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Add(zip, "xl/workbook.xml", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="{_main}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>{string.Concat(sheets.Select((s, i) => $"<sheet name=\"{SecurityElement.Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))}</sheets>{FilterNames(sheets)}</workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))}<Relationship Id="rId{sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """);
            Add(zip, "xl/styles.xml", stamp, $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="{_main}"><fonts count="7"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="14"/><color rgb="FF1B2A45"/><name val="Calibri"/></font><font><sz val="10"/><color rgb="FF4A5568"/><name val="Calibri"/></font><font><b/><sz val="20"/><color rgb="FF1B2A45"/><name val="Calibri"/></font><font><i/><sz val="10"/><color rgb="FF4A5568"/><name val="Calibri"/></font><font><sz val="9"/><color rgb="FF6B7280"/><name val="Calibri"/></font></fonts><fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9E0EB"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF2F5FA"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="3"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style="thin"><color rgb="FF8D99AE"/></bottom><diagonal/></border><border><left style="thin"><color rgb="FFC7CCD5"/></left><right style="thin"><color rgb="FFC7CCD5"/></right><top style="thin"><color rgb="FFC7CCD5"/></top><bottom style="thin"><color rgb="FFC7CCD5"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="9"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/><xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="0" fontId="3" fillId="3" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="4" fillId="3" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="0" fontId="5" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="0" fontId="6" fillId="3" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                """);
            for (int i = 0; i < sheets.Count; i++)
            {
                Add(zip, $"xl/worksheets/sheet{i + 1}.xml", stamp, sheets[i].Xml);
            }

            if (blocks.Count > 0)
            {
                Add(zip, "xl/worksheets/_rels/sheet1.xml.rels", stamp, "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/></Relationships>");
                Add(zip, "xl/drawings/drawing1.xml", stamp, ReportChartXml.Drawing(blocks.Count, chartRow, _rowsPerChart));
                Add(zip, "xl/drawings/_rels/drawing1.xml.rels", stamp, ReportChartXml.DrawingRelationships(blocks.Count));
                for (int i = 0; i < blocks.Count; i++)
                {
                    Add(zip, $"xl/charts/chart{i + 1}.xml", stamp, ReportChartXml.Chart(blocks[i], _chartDataSheet));
                }
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Adds the chart-data sheet (last) for the executive-summary charts and returns where each chart's cells are. Only a
    /// document with an executive summary has charts; all-zero charts are left out (their values are in the detail sections).
    /// </summary>
    private static List<ReportChartXml.DataBlock> ChartData(ReportDocument document, List<(string Name, List<IReadOnlyList<ReportCell>> Rows, bool HasHeaderBlock)> tables)
    {
        ReportChart[] charts = [.. (document.Dashboard?.Charts ?? []).Where(c => !c.IsEmpty)];
        List<ReportChartXml.DataBlock> blocks = [];
        if (charts.Length == 0)
        {
            return blocks;
        }

        List<IReadOnlyList<ReportCell>> data = [[_chartDataSheet + " (yönetici özetindeki grafiklerin değerleri)"]];
        foreach (ReportChart chart in charts)
        {
            data.Add([]);
            data.Add([chart.Title]);
            data.Add([chart.Kind == ReportChartKind.Line ? "Hafta başı" : "Kalem", .. chart.Series.Select(s => (ReportCell)s.Name)]);
            int header = data.Count;
            for (int i = 0; i < chart.Categories.Count; i++)
            {
                int index = i;
                data.Add([chart.Categories[i], .. chart.Series.Select(s => new ReportCell(Number: s.Values[index]))]);
            }

            blocks.Add(new(chart, header, header + 1, data.Count));
        }

        tables.Add((_chartDataSheet, data, false));
        return blocks;
    }

    private static string ChartContentTypes(int count) => count == 0
        ? string.Empty
        : "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>"
          + string.Concat(Enumerable.Range(1, count).Select(i => $"<Override PartName=\"/xl/charts/chart{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>"));

    /// <summary>Prefixes text that could be evaluated as a formula when copied or re-saved.</summary>
    public static string Neutralize(string text) => text.Length > 0 && _formulaStarts.Contains(text[0]) ? "'" + text : text;

    private static string Sheet(List<IReadOnlyList<ReportCell>> rows, bool headerRow)
    {
        StringBuilder xml = new($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{_main}\">");
        int columns = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.Count));
        if (headerRow && rows.Count > 0)
        {
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>"
                + "<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/></sheetView></sheetViews>");
        }

        xml.Append("<cols>");
        for (int c = 1; c <= columns; c++)
        {
            int width = Math.Clamp(rows.Skip(headerRow ? 0 : 1).Select(r => r.Count >= c ? r[c - 1].Display.Length : 0).DefaultIfEmpty(10).Max() + 2, 10, 70);
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
                string style = r == 0 ? $" s=\"{(headerRow ? _headerStyle : _titleStyle)}\"" : string.Empty;
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

        xml.Append("</sheetData>");
        if (FilterRange(rows, headerRow) is { } range)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<autoFilter ref=\"{range}\"/>");
        }

        xml.Append("</worksheet>");
        return xml.ToString();
    }

    /// <summary>Header-and-data range of a table sheet with at least one data row, e.g. A1:E31; null otherwise.</summary>
    private static string? FilterRange(List<IReadOnlyList<ReportCell>> rows, bool headerRow) =>
        headerRow && rows.Count > 1 && rows[0].Count > 0
            ? $"A1:{Column(rows[0].Count - 1)}{rows.Count.ToString(CultureInfo.InvariantCulture)}"
            : null;

    /// <summary>The hidden filter-database names Excel itself writes for each AutoFilter (avoids a repair prompt).</summary>
    private static string FilterNames(List<(string Name, string Xml, string? Filter)> sheets)
    {
        StringBuilder names = new();
        for (int i = 0; i < sheets.Count; i++)
        {
            if (sheets[i].Filter is { } range)
            {
                string[] corners = range.Split(':');
                string absolute = string.Join(':', corners.Select(c => "$" + new string([.. c.TakeWhile(char.IsLetter)]) + "$" + new string([.. c.SkipWhile(char.IsLetter)])));
                string sheet = sheets[i].Name.Replace("'", "''", StringComparison.Ordinal);
                names.Append(CultureInfo.InvariantCulture, $"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">{Escape($"'{sheet}'!{absolute}")}</definedName>");
            }
        }

        return names.Length == 0 ? string.Empty : "<definedNames>" + names + "</definedNames>";
    }

    /// <summary>
    /// Executive summary sheet: title, scope/period line, headline tiles (four per row, each a merged 3-column box with label,
    /// value and note) and short tables whose columns are spread over twelve grid columns. Values only, no formulas.
    /// </summary>
    private static string DashboardSheet(string title, ReportDashboard dashboard, int charts, out int chartRow)
    {
        SortedDictionary<int, SortedDictionary<int, string>> cells = [];
        List<string> merges = [];
        Dictionary<int, double> heights = [];
        void Put(int row, int column, ReportCell? cell, int style)
        {
            string reference = Column(column) + row.ToString(CultureInfo.InvariantCulture);
            string xml = cell switch
            {
                { Number: { } n } => $"<c r=\"{reference}\" s=\"{style}\"><v>{n.ToString(CultureInfo.InvariantCulture)}</v></c>",
                { Date: { } d } => $"<c r=\"{reference}\" s=\"{(style == 0 ? _dateStyle : style)}\"><v>{(d.DayNumber - new DateOnly(1899, 12, 30).DayNumber).ToString(CultureInfo.InvariantCulture)}</v></c>",
                { } text => $"<c r=\"{reference}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Escape(Neutralize(text.Text ?? string.Empty))}</t></is></c>",
                null => $"<c r=\"{reference}\" s=\"{style}\"/>"
            };
            if (!cells.TryGetValue(row, out SortedDictionary<int, string>? line))
            {
                cells[row] = line = [];
            }

            line[column] = xml;
        }

        void Span(int row, int first, int count, ReportCell? cell, int style)
        {
            Put(row, first, cell, style);
            for (int c = first + 1; c < first + count; c++)
            {
                Put(row, c, null, style);
            }

            if (count > 1)
            {
                merges.Add($"{Column(first)}{row}:{Column(first + count - 1)}{row}");
            }
        }

        Span(1, 0, _dashboardColumns, title, _titleStyle);
        heights[1] = 24;
        Span(2, 0, _dashboardColumns, dashboard.Subtitle, _subtitleStyle);
        int r = 4;
        const int tileWidth = _dashboardColumns / _tilesPerRow;
        foreach (ReportTile[] group in dashboard.Tiles.Chunk(_tilesPerRow))
        {
            for (int i = 0; i < group.Length; i++)
            {
                Span(r, i * tileWidth, tileWidth, group[i].Label, _tileLabelStyle);
                Span(r + 1, i * tileWidth, tileWidth, new ReportCell(Number: group[i].Value), _tileValueStyle);
                Span(r + 2, i * tileWidth, tileWidth, group[i].Note ?? string.Empty, _tileNoteStyle);
            }

            heights[r + 1] = 32;
            r += 4;
        }

        // Charts follow the tiles, before the tables; each pair of charts takes a fixed band of rows (no cell sits under a chart).
        chartRow = 0;
        if (charts > 0)
        {
            r++;
            Span(r++, 0, _dashboardColumns, "Grafikler (değerler \"" + _chartDataSheet + "\" sayfasında)", _blockTitleStyle);
            chartRow = r;
            r += (charts + 1) / 2 * (_rowsPerChart + 1);
        }

        foreach (ReportSection block in dashboard.Blocks)
        {
            r++;
            Span(r++, 0, _dashboardColumns, block.Title, _blockTitleStyle);
            int[] spans = [.. Enumerable.Range(0, block.Headers.Count).Select(i => _dashboardColumns / block.Headers.Count + (i < _dashboardColumns % block.Headers.Count ? 1 : 0))];
            int[] starts = [.. spans.Select((_, i) => spans.Take(i).Sum())];
            for (int c = 0; c < block.Headers.Count; c++)
            {
                Span(r, starts[c], spans[c], block.Headers[c], _headerStyle);
            }

            r++;
            if (block.Rows.Count == 0)
            {
                Span(r++, 0, _dashboardColumns, "(kayıt yok)", _subtitleStyle);
            }

            foreach (IReadOnlyList<ReportCell> row in block.Rows)
            {
                for (int c = 0; c < block.Headers.Count; c++)
                {
                    Span(r, starts[c], spans[c], c < row.Count ? row[c] : string.Empty, 0);
                }

                r++;
            }
        }

        StringBuilder xml = new($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{_main}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        xml.Append("<sheetViews><sheetView workbookViewId=\"0\" showGridLines=\"0\" tabSelected=\"1\"/></sheetViews>");
        xml.Append(CultureInfo.InvariantCulture, $"<cols><col min=\"1\" max=\"{_dashboardColumns}\" width=\"13\" customWidth=\"1\"/></cols><sheetData>");
        foreach ((int row, SortedDictionary<int, string> line) in cells)
        {
            string height = heights.TryGetValue(row, out double h) ? $" ht=\"{h.ToString(CultureInfo.InvariantCulture)}\" customHeight=\"1\"" : string.Empty;
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{row}\"{height}>").Append(string.Concat(line.Values)).Append("</row>");
        }

        xml.Append("</sheetData>");
        if (merges.Count > 0)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<mergeCells count=\"{merges.Count}\">").Append(string.Concat(merges.Select(m => $"<mergeCell ref=\"{m}\"/>"))).Append("</mergeCells>");
        }

        if (charts > 0)
        {
            xml.Append("<drawing r:id=\"rId1\"/>");
        }

        xml.Append("</worksheet>");
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
