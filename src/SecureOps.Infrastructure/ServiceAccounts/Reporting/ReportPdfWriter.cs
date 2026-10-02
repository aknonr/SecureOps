using System.Globalization;
using System.Text;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// Minimal deterministic text PDF (1.4) renderer for report snapshots. Uses the standard Courier font with a
/// WinAnsi base encoding whose unused slots are remapped to the Turkish glyphs (Ğ ğ İ ı Ş ş). No scripts,
/// forms, links, compression or embedded files. Same document → same bytes.
/// </summary>
public static class ReportPdfWriter
{
    private const double _pageWidth = 595.28;
    private const double _pageHeight = 841.89;
    private const double _margin = 36;
    private const double _fontSize = 8;
    private const double _lineHeight = 10;
    private const int _columns = 130;
    private const int _linesPerPage = (int)((_pageHeight - 2 * _margin) / _lineHeight);

    private static readonly Dictionary<char, byte> _turkish = new()
    {
        ['Ğ'] = 0x80,
        ['ğ'] = 0x81,
        ['İ'] = 0x82,
        ['ı'] = 0x83,
        ['Ş'] = 0x84,
        ['ş'] = 0x85
    };

    /// <summary>Renders all header lines and sections as fixed-width text tables.</summary>
    public static byte[] Write(ReportDocument document)
    {
        List<string> lines = [document.Title, new string('=', Math.Min(_columns, document.Title.Length))];
        lines.AddRange(document.Header.Select(h => $"{h.Label}: {h.Value}"));
        foreach (ReportSection section in document.Sections)
        {
            lines.Add(string.Empty);
            lines.Add(section.Title.ToUpper(CultureInfo.GetCultureInfo("tr-TR")));
            lines.AddRange(Table(section));
        }

        List<List<string>> pages = [.. lines.SelectMany(Wrap).Chunk(_linesPerPage - 2).Select(c => c.ToList())];
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].Add(string.Empty);
            pages[i].Add($"Sayfa {i + 1}/{pages.Count}");
        }

        return Build(pages);
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

                builder.Append(_turkish.FirstOrDefault(p => p.Value == c).Key is var tr && tr != default ? tr : c);
            }

            lines.Add(builder.ToString());
        }

        return lines;
    }

    private static IEnumerable<string> Table(ReportSection section)
    {
        int count = section.Headers.Count;
        int[] widths = [.. Enumerable.Range(0, count).Select(i => Math.Min(48, Math.Max(section.Headers[i].Length,
            section.Rows.Select(r => i < r.Count ? r[i].Display.Length : 0).DefaultIfEmpty(0).Max())))];
        string Line(IEnumerable<string> cells) => string.Join(" | ", cells.Select((c, i) => Fit(c, widths[Math.Min(i, count - 1)])));
        yield return Line(section.Headers);
        yield return string.Join("-+-", widths.Select(w => new string('-', w)));
        foreach (IReadOnlyList<ReportCell> row in section.Rows)
        {
            yield return Line(Enumerable.Range(0, count).Select(i => i < row.Count ? row[i].Display : string.Empty));
        }

        if (section.Rows.Count == 0)
        {
            yield return "(kayıt yok)";
        }
    }

    private static string Fit(string value, int width) => value.Length > width ? value[..(width - 1)] + "…" : value.PadRight(width);

    private static IEnumerable<string> Wrap(string line)
    {
        for (int i = 0; i < line.Length; i += _columns)
        {
            yield return line.Substring(i, Math.Min(_columns, line.Length - i));
        }

        if (line.Length == 0)
        {
            yield return string.Empty;
        }
    }

    private static byte[] Build(List<List<string>> pages)
    {
        List<byte[]> objects = [];
        int pagesId = 2, fontId = 3, firstPage = 4;
        string kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + i * 2} 0 R"));
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding "
            + "/Differences [128 /Gbreve /gbreve /Idotaccent /dotlessi /Scedilla /scedilla] >> >>"));
        for (int i = 0; i < pages.Count; i++)
        {
            StringBuilder content = new();
            content.Append(CultureInfo.InvariantCulture, $"BT\n/F1 {_fontSize.ToString(CultureInfo.InvariantCulture)} Tf\n{_lineHeight.ToString(CultureInfo.InvariantCulture)} TL\n");
            content.Append(CultureInfo.InvariantCulture, $"{_margin.ToString(CultureInfo.InvariantCulture)} {(_pageHeight - _margin).ToString("0.##", CultureInfo.InvariantCulture)} Td\n");
            foreach (string line in pages[i])
            {
                content.Append('(').Append(Encode(line)).Append(") Tj\nT*\n");
            }

            content.Append("ET");
            byte[] stream = Encoding.Latin1.GetBytes(content.ToString());
            objects.Add(Ascii($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {_pageWidth.ToString(CultureInfo.InvariantCulture)} {_pageHeight.ToString(CultureInfo.InvariantCulture)}] "
                + $"/Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {firstPage + i * 2 + 1} 0 R >>"));
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

    private static string Encode(string line)
    {
        StringBuilder builder = new(line.Length);
        foreach (char c in line)
        {
            char mapped = _turkish.TryGetValue(c, out byte code) ? (char)code
                : c == '…' ? '.'
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
