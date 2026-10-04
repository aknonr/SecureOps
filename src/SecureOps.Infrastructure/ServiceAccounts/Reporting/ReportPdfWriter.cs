using System.Globalization;
using System.Text;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// Deterministic PDF (1.4) renderer for report snapshots, A4 landscape. Text uses the embedded Liberation Mono regular and
/// bold fonts (SIL OFL 1.1, unmodified) as Type0 / CIDFontType2 / Identity-H with a ToUnicode map, so every Turkish letter
/// renders the same in every viewer and stays searchable and copyable. Layout: a title band, the snapshot metadata, then
/// each section with a heading bar and a table whose header row is shaded and whose rows alternate. The text of every
/// line stays a plain fixed-width line ("label | value"), so the PDF reconciles with the XLSX. Only the two font files
/// are compressed (FlateDecode); no scripts, forms, links or embedded files. Same document → same bytes.
/// </summary>
public static class ReportPdfWriter
{
    private const double _pageWidth = 841.89;
    private const double _pageHeight = 595.28;
    private const double _margin = 32;
    private const double _fontSize = 8;
    private const double _lineHeight = 11;
    private const double _titleSize = 15;
    private const double _titleBand = 34;
    private const double _footerSpace = 22;
    private const double _charWidth = _fontSize * 0.6;
    private const int _columns = (int)((_pageWidth - 2 * _margin) / _charWidth) - 2;
    private const string _band = "0.106 0.165 0.271";
    private const string _sectionFill = "0.902 0.918 0.941";
    private const string _headerFill = "0.851 0.878 0.922";
    private const string _stripeFill = "0.965 0.969 0.976";
    private const string _ruleStroke = "0.780 0.800 0.835";
    private const string _tileFill = "0.949 0.961 0.980";
    private const int _tilesPerRow = 4;
    private const double _tileHeight = 46;
    private const double _tileGap = 8;
    private const double _tileValueSize = 18;

    private enum LineKind
    {
        Meta,
        Section,
        TableHeader,
        Row,
        StripedRow,
        Note,
        Blank
    }

    private sealed record Line(LineKind Kind, string Text);

    /// <summary>Renders the title band, header lines and sections as shaded fixed-width tables.</summary>
    public static byte[] Write(ReportDocument document)
    {
        List<Line> lines = [.. document.Header.Select(h => new Line(LineKind.Meta, $"{h.Label}: {h.Value}"))];
        IReadOnlyList<ReportTile> tiles = document.Dashboard?.Tiles ?? [];
        foreach (ReportSection block in document.Dashboard?.Blocks ?? [])
        {
            lines.Add(new Line(LineKind.Blank, string.Empty));
            lines.Add(new Line(LineKind.Section, "YÖNETİCİ ÖZETİ · " + block.Title.ToUpper(CultureInfo.GetCultureInfo("tr-TR"))));
            lines.AddRange(Table(block));
        }

        foreach (ReportSection section in document.Sections)
        {
            lines.Add(new Line(LineKind.Blank, string.Empty));
            lines.Add(new Line(LineKind.Section, section.Title.ToUpper(CultureInfo.GetCultureInfo("tr-TR"))));
            lines.AddRange(Table(section));
        }

        List<Line> wrapped = [.. lines.SelectMany(Wrap)];
        int firstPage = (int)((_pageHeight - 2 * _margin - _titleBand - TilesHeight(tiles.Count) - _footerSpace) / _lineHeight);
        int otherPages = (int)((_pageHeight - 2 * _margin - _footerSpace) / _lineHeight);
        List<List<Line>> pages = Paginate(wrapped, firstPage, otherPages);
        ReportChart[] charts = [.. (document.Dashboard?.Charts ?? []).Where(c => !c.IsEmpty).Take(ReportPdfCharts.Slots)];
        return Build(document.Title, tiles, pages, charts);
    }

    /// <summary>Fills pages in order; a section heading, its table header or a blank line is never left alone at a page bottom.</summary>
    private static List<List<Line>> Paginate(List<Line> lines, int firstPage, int otherPages)
    {
        List<List<Line>> pages = [];
        int start = 0;
        int capacity = firstPage;
        while (start < lines.Count || pages.Count == 0)
        {
            int end = Math.Min(start + capacity, lines.Count);
            if (end < lines.Count)
            {
                while (end > start + 1 && lines[end - 1].Kind is LineKind.Section or LineKind.TableHeader or LineKind.Blank)
                {
                    end--;
                }
            }

            pages.Add([.. lines.Skip(start).Take(end - start)]);
            start = end;
            while (start < lines.Count && lines[start].Kind == LineKind.Blank)
            {
                start++;
            }

            capacity = otherPages;
        }

        return pages;
    }

    private static double TilesHeight(int count) => count == 0 ? 0 : Math.Ceiling(count / (double)_tilesPerRow) * (_tileHeight + _tileGap) + _tileGap;

    /// <summary>
    /// Extracts the rendered text lines of a PDF produced by this writer (tests and reconciliation): follows the current
    /// font (<c>/F1</c> regular, <c>/F2</c> bold) and decodes each hex glyph string through that font's cmap.
    /// </summary>
    public static IReadOnlyList<string> ExtractLines(byte[] pdf)
    {
        List<string> lines = [];
        ReportPdfFont font = ReportPdfFont.Regular;
        foreach (string part in Encoding.Latin1.GetString(pdf).Split('\n'))
        {
            if (part.EndsWith(" Tf", StringComparison.Ordinal))
            {
                font = part.Contains("/F2 ", StringComparison.Ordinal) ? ReportPdfFont.BoldFace : ReportPdfFont.Regular;
            }

            int start = part.IndexOf('<', StringComparison.Ordinal);
            if (start < 0 || !part.EndsWith("> Tj", StringComparison.Ordinal) || part.StartsWith("<<", StringComparison.Ordinal))
            {
                continue;
            }

            string hex = part[(start + 1)..^4];
            StringBuilder builder = new(hex.Length / 4);
            for (int i = 0; i + 4 <= hex.Length; i += 4)
            {
                ushort glyph = ushort.Parse(hex.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                builder.Append(font.Unicode.TryGetValue(glyph, out char c) ? c : '?');
            }

            lines.Add(builder.ToString());
        }

        return lines;
    }

    private static IEnumerable<Line> Table(ReportSection section)
    {
        int count = section.Headers.Count;
        int[] widths = [.. Enumerable.Range(0, count).Select(i => Math.Min(48, Math.Max(section.Headers[i].Length,
            section.Rows.Select(r => i < r.Count ? r[i].Display.Length : 0).DefaultIfEmpty(0).Max())))];
        string Text(IEnumerable<string> cells) => string.Join(" | ", cells.Select((c, i) => Fit(c, widths[Math.Min(i, count - 1)])));
        yield return new Line(LineKind.TableHeader, Text(section.Headers));
        for (int r = 0; r < section.Rows.Count; r++)
        {
            IReadOnlyList<ReportCell> row = section.Rows[r];
            yield return new Line(r % 2 == 1 ? LineKind.StripedRow : LineKind.Row, Text(Enumerable.Range(0, count).Select(i => i < row.Count ? row[i].Display : string.Empty)));
        }

        if (section.Rows.Count == 0)
        {
            yield return new Line(LineKind.Note, "(kayıt yok)");
        }
    }

    private static string Fit(string value, int width) => value.Length > width ? value[..(width - 1)] + "…" : value.PadRight(width);

    private static IEnumerable<Line> Wrap(Line line)
    {
        for (int i = 0; i < line.Text.Length; i += _columns)
        {
            yield return line with { Text = line.Text.Substring(i, Math.Min(_columns, line.Text.Length - i)) };
        }

        if (line.Text.Length == 0)
        {
            yield return line;
        }
    }

    private static byte[] Build(string title, IReadOnlyList<ReportTile> tiles, List<List<Line>> pages, IReadOnlyList<ReportChart> charts)
    {
        List<byte[]> objects = [];
        const int pagesId = 2, fontId = 3, boldId = 4, firstPage = 5;
        ReportPdfText text = new();
        // The chart page, when there is one, follows the first page (tiles and summary tables) and precedes the detail pages.
        int total = pages.Count + (charts.Count > 0 ? 1 : 0);
        string kids = string.Join(" ", Enumerable.Range(0, total).Select(i => $"{firstPage + i * 2} 0 R"));
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {total} >>"));
        // Placeholders: the Type0 font bodies depend on the glyphs the pages use, so they are filled in after the pages.
        objects.Add([]);
        objects.Add([]);
        for (int i = 0; i < total; i++)
        {
            string content = charts.Count > 0 && i == 1
                ? ReportPdfCharts.Page(charts, _pageWidth, _pageHeight, _margin, _footerSpace, text) + Footer(i, total, text)
                : Page(title, i == 0 ? tiles : [], pages[charts.Count > 0 && i > 1 ? i - 1 : i], i, total, text);
            byte[] stream = Encoding.Latin1.GetBytes(content);
            objects.Add(Ascii($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {N(_pageWidth)} {N(_pageHeight)}] "
                + $"/Resources << /Font << /F1 {fontId} 0 R /F2 {boldId} 0 R >> >> /Contents {firstPage + i * 2 + 1} 0 R >>"));
            objects.Add([.. Ascii($"<< /Length {stream.Length} >>\nstream\n"), .. stream, .. Ascii("\nendstream")]);
        }

        foreach ((int id, byte[] body) in text.Objects([fontId, boldId], objects.Count + 1))
        {
            if (id <= objects.Count)
            {
                objects[id - 1] = body;
            }
            else
            {
                objects.Add(body);
            }
        }

        using MemoryStream output = new();
        Write(output, Ascii("%PDF-1.4\n%âãÏÓ\n"));
        List<long> offsets = [];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write(output, Ascii($"{i + 1} 0 obj\n"));
            Write(output, objects[i]);
            Write(output, Ascii("\nendobj\n"));
        }

        long xref = output.Position;
        StringBuilder table = new($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            table.Append(offset.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(output, Ascii(table.ToString()));
        return output.ToArray();
    }

    /// <summary>One page: shapes first (band, bars, shaded rows, rules), then one absolutely positioned text line per row.</summary>
    private static string Page(string title, IReadOnlyList<ReportTile> tiles, List<Line> lines, int index, int count, ReportPdfText encoder)
    {
        StringBuilder shapes = new();
        StringBuilder text = new();
        double width = _pageWidth - 2 * _margin;
        double top = _pageHeight - _margin;
        if (index == 0)
        {
            shapes.Append(CultureInfo.InvariantCulture, $"{_band} rg {N(_margin)} {N(top - _titleBand + 6)} {N(width)} {N(_titleBand - 6)} re f\n");
            text.Append(CultureInfo.InvariantCulture, $"1 1 1 rg /F2 {N(_titleSize)} Tf\n1 0 0 1 {N(_margin + 10)} {N(top - _titleBand + 16)} Tm {encoder.Show(title, true)} Tj\n");
            top -= _titleBand;
        }

        if (tiles.Count > 0)
        {
            double tileWidth = (width - (_tilesPerRow - 1) * _tileGap) / _tilesPerRow;
            for (int t = 0; t < tiles.Count; t++)
            {
                double x = _margin + t % _tilesPerRow * (tileWidth + _tileGap);
                double boxTop = top - _tileGap - t / _tilesPerRow * (_tileHeight + _tileGap);
                double bottom = boxTop - _tileHeight;
                shapes.Append(CultureInfo.InvariantCulture, $"{_tileFill} rg {_ruleStroke} RG 0.6 w {N(x)} {N(bottom)} {N(tileWidth)} {N(_tileHeight)} re B\n");
                text.Append(CultureInfo.InvariantCulture, $"0.29 0.33 0.41 rg /F1 {N(_fontSize)} Tf\n1 0 0 1 {N(x + 8)} {N(boxTop - 12)} Tm {encoder.Show(tiles[t].Label, false)} Tj\n");
                text.Append(CultureInfo.InvariantCulture, $"{_band} rg /F2 {N(_tileValueSize)} Tf\n1 0 0 1 {N(x + 8)} {N(bottom + 10)} Tm {encoder.Show(tiles[t].Value.ToString(CultureInfo.InvariantCulture), true)} Tj\n");
                if (tiles[t].Note is { Length: > 0 } note)
                {
                    text.Append(CultureInfo.InvariantCulture, $"0.42 0.45 0.50 rg /F1 {N(_fontSize - 1)} Tf\n1 0 0 1 {N(x + tileWidth - 8 - note.Length * (_fontSize - 1) * 0.6)} {N(bottom + 10)} Tm {encoder.Show(note, false)} Tj\n");
                }
            }

            top -= TilesHeight(tiles.Count);
        }

        double y = top;
        foreach (Line line in lines)
        {
            double baseline = y - _lineHeight + 3;
            string? fill = line.Kind switch
            {
                LineKind.Section => _sectionFill,
                LineKind.TableHeader => _headerFill,
                LineKind.StripedRow => _stripeFill,
                _ => null
            };
            if (fill is not null)
            {
                shapes.Append(CultureInfo.InvariantCulture, $"{fill} rg {N(_margin)} {N(y - _lineHeight)} {N(width)} {N(_lineHeight)} re f\n");
            }

            if (line.Kind is LineKind.TableHeader or LineKind.Row or LineKind.StripedRow)
            {
                shapes.Append(CultureInfo.InvariantCulture, $"{_ruleStroke} RG 0.4 w {N(_margin)} {N(y - _lineHeight)} m {N(_margin + width)} {N(y - _lineHeight)} l S\n");
            }

            if (line.Text.Length > 0)
            {
                bool bold = line.Kind is LineKind.Section or LineKind.TableHeader;
                string color = line.Kind == LineKind.Note ? "0.40 0.43 0.48" : "0.10 0.12 0.16";
                text.Append(CultureInfo.InvariantCulture, $"{color} rg /{(bold ? "F2" : "F1")} {N(_fontSize)} Tf\n");
                text.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {N(_margin + 4)} {N(baseline)} Tm {encoder.Show(line.Text, bold)} Tj\n");
            }

            y -= _lineHeight;
        }

        return shapes + "BT\n" + text + "ET" + Footer(index, count, encoder);
    }

    /// <summary>Footer rule, classification line and page number (its own text object).</summary>
    private static string Footer(int index, int count, ReportPdfText encoder)
    {
        double width = _pageWidth - 2 * _margin;
        StringBuilder footer = new();
        footer.Append(CultureInfo.InvariantCulture, $"\n{_ruleStroke} RG 0.6 w {N(_margin)} {N(_margin + _footerSpace - 8)} m {N(_margin + width)} {N(_margin + _footerSpace - 8)} l S\nBT\n");
        footer.Append(CultureInfo.InvariantCulture, $"0.40 0.43 0.48 rg /F1 {N(_fontSize)} Tf\n1 0 0 1 {N(_margin)} {N(_margin + 2)} Tm {encoder.Show("Kurum içi · salt okunur rapor nüshası", false)} Tj\n");
        footer.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {N(_pageWidth - _margin - 15 * _charWidth)} {N(_margin + 2)} Tm {encoder.Show($"Sayfa {index + 1}/{count}", false)} Tj\nET");
        return footer.ToString();
    }

    internal static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static byte[] Ascii(string value) => Encoding.Latin1.GetBytes(value);

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes);
}
