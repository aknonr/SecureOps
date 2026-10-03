using System.Globalization;
using System.Text;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// Deterministic PDF (1.4) renderer for report snapshots, A4 landscape. Uses the standard Courier and Courier-Bold fonts
/// with a WinAnsi base encoding whose unused slots are remapped to the Turkish glyphs (Ğ ğ İ ı Ş ş). Layout: a title band,
/// the snapshot metadata, then each section with a heading bar and a table whose header row is shaded and whose rows
/// alternate. The text of every line stays a plain fixed-width line ("label | value"), so the PDF reconciles with the XLSX.
/// No scripts, forms, links, compression or embedded files. Same document → same bytes.
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

    private static readonly Dictionary<char, byte> _turkish = new()
    {
        ['Ğ'] = 0x80,
        ['ğ'] = 0x81,
        ['İ'] = 0x82,
        ['ı'] = 0x83,
        ['Ş'] = 0x84,
        ['ş'] = 0x85
    };

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
        foreach (ReportSection section in document.Sections)
        {
            lines.Add(new Line(LineKind.Blank, string.Empty));
            lines.Add(new Line(LineKind.Section, section.Title.ToUpper(CultureInfo.GetCultureInfo("tr-TR"))));
            lines.AddRange(Table(section));
        }

        List<Line> wrapped = [.. lines.SelectMany(Wrap)];
        int firstPage = (int)((_pageHeight - 2 * _margin - _titleBand - _footerSpace) / _lineHeight);
        int otherPages = (int)((_pageHeight - 2 * _margin - _footerSpace) / _lineHeight);
        List<List<Line>> pages = [[.. wrapped.Take(firstPage)]];
        pages.AddRange(wrapped.Skip(firstPage).Chunk(otherPages).Select(c => c.ToList()));
        return Build(document.Title, pages);
    }

    /// <summary>Extracts the rendered text lines of a PDF produced by this writer (tests and reconciliation).</summary>
    public static IReadOnlyList<string> ExtractLines(byte[] pdf)
    {
        List<string> lines = [];
        string text = Encoding.Latin1.GetString(pdf);
        foreach (string part in text.Split('\n'))
        {
            int start = part.IndexOf('(', StringComparison.Ordinal);
            if (start < 0 || !part.EndsWith(") Tj", StringComparison.Ordinal))
            {
                continue;
            }

            StringBuilder builder = new();
            string body = part[(start + 1)..^4];
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '\\' && i + 1 < body.Length)
                {
                    c = body[++i];
                }

                builder.Append(_turkish.FirstOrDefault(p => p.Value == c).Key is var tr && tr != default ? tr : c switch { '\u0096' => '–', '\u0097' => '—', _ => c });
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

    private static byte[] Build(string title, List<List<Line>> pages)
    {
        List<byte[]> objects = [];
        const int pagesId = 2, fontId = 3, boldId = 4, firstPage = 5;
        const string encoding = "/Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [128 /Gbreve /gbreve /Idotaccent /dotlessi /Scedilla /scedilla] >>";
        string kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + i * 2} 0 R"));
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"));
        objects.Add(Ascii($"<< /Type /Font /Subtype /Type1 /BaseFont /Courier {encoding} >>"));
        objects.Add(Ascii($"<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold {encoding} >>"));
        for (int i = 0; i < pages.Count; i++)
        {
            byte[] stream = Encoding.Latin1.GetBytes(Page(title, pages[i], i, pages.Count));
            objects.Add(Ascii($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {N(_pageWidth)} {N(_pageHeight)}] "
                + $"/Resources << /Font << /F1 {fontId} 0 R /F2 {boldId} 0 R >> >> /Contents {firstPage + i * 2 + 1} 0 R >>"));
            objects.Add([.. Ascii($"<< /Length {stream.Length} >>\nstream\n"), .. stream, .. Ascii("\nendstream")]);
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
    private static string Page(string title, List<Line> lines, int index, int count)
    {
        StringBuilder shapes = new();
        StringBuilder text = new();
        double width = _pageWidth - 2 * _margin;
        double top = _pageHeight - _margin;
        if (index == 0)
        {
            shapes.Append(CultureInfo.InvariantCulture, $"{_band} rg {N(_margin)} {N(top - _titleBand + 6)} {N(width)} {N(_titleBand - 6)} re f\n");
            text.Append(CultureInfo.InvariantCulture, $"1 1 1 rg /F2 {N(_titleSize)} Tf\n1 0 0 1 {N(_margin + 10)} {N(top - _titleBand + 16)} Tm ({Encode(title)}) Tj\n");
            top -= _titleBand;
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
                text.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {N(_margin + 4)} {N(baseline)} Tm ({Encode(line.Text)}) Tj\n");
            }

            y -= _lineHeight;
        }

        shapes.Append(CultureInfo.InvariantCulture, $"{_ruleStroke} RG 0.6 w {N(_margin)} {N(_margin + _footerSpace - 8)} m {N(_margin + width)} {N(_margin + _footerSpace - 8)} l S\n");
        text.Append(CultureInfo.InvariantCulture, $"0.40 0.43 0.48 rg /F1 {N(_fontSize)} Tf\n1 0 0 1 {N(_margin)} {N(_margin + 2)} Tm ({Encode("Kurum içi · salt okunur rapor nüshası")}) Tj\n");
        text.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {N(_pageWidth - _margin - 15 * _charWidth)} {N(_margin + 2)} Tm ({Encode($"Sayfa {index + 1}/{count}")}) Tj\n");
        return shapes + "BT\n" + text + "ET";
    }

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Encode(string line)
    {
        StringBuilder builder = new(line.Length);
        foreach (char c in line)
        {
            char mapped = _turkish.TryGetValue(c, out byte code) ? (char)code
                : c == '…' ? '.'
                : c == '–' ? '\u0096'
                : c == '—' ? '\u0097'
                : c < 0x80 || c is >= '\u00A0' and <= '\u00FF' ? c
                : '?';
            if (mapped is '(' or ')' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(mapped);
        }

        return builder.ToString();
    }

    private static byte[] Ascii(string value) => Encoding.Latin1.GetBytes(value);

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes);
}
