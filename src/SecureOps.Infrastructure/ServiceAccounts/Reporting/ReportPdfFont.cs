using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// One embedded TrueType font (Liberation Mono, SIL OFL 1.1, shipped unmodified as an assembly resource). Parses only what
/// a PDF needs, in managed code: the Windows Unicode BMP <c>cmap</c> (format 4), advance widths (<c>hhea</c>/<c>hmtx</c>),
/// and the descriptor metrics (<c>head</c>, <c>OS/2</c>, <c>post</c>, <c>name</c>). The parsed font is immutable and
/// shared; per-document glyph usage lives in <see cref="ReportPdfText"/>.
/// </summary>
public sealed class ReportPdfFont
{
    private readonly Dictionary<char, ushort> _glyphs;

    private ReportPdfFont(string resource, byte[] data)
    {
        Resource = resource;
        Data = data;
        Dictionary<string, (int Offset, int Length)> tables = Tables(data);
        ReadOnlySpan<byte> head = Table(data, tables, "head");
        UnitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head[18..]);
        BoundingBox = [Short(head, 36), Short(head, 38), Short(head, 40), Short(head, 42)];
        ReadOnlySpan<byte> hhea = Table(data, tables, "hhea");
        Ascent = Short(hhea, 4);
        Descent = Short(hhea, 6);
        int metrics = BinaryPrimitives.ReadUInt16BigEndian(hhea[34..]);
        int glyphCount = BinaryPrimitives.ReadUInt16BigEndian(Table(data, tables, "maxp")[4..]);
        ReadOnlySpan<byte> hmtx = Table(data, tables, "hmtx");
        Advances = new ushort[glyphCount];
        for (int g = 0; g < glyphCount; g++)
        {
            Advances[g] = BinaryPrimitives.ReadUInt16BigEndian(hmtx[(Math.Min(g, metrics - 1) * 4)..]);
        }

        ReadOnlySpan<byte> os2 = Table(data, tables, "OS/2");
        CapHeight = BinaryPrimitives.ReadUInt16BigEndian(os2) >= 2 && os2.Length >= 90 ? Short(os2, 88) : Ascent;
        Bold = BinaryPrimitives.ReadUInt16BigEndian(os2[4..]) >= 600;
        ReadOnlySpan<byte> post = Table(data, tables, "post");
        FixedPitch = BinaryPrimitives.ReadUInt32BigEndian(post[12..]) != 0;
        PostScriptName = PostScript(Table(data, tables, "name"));
        _glyphs = Cmap(Table(data, tables, "cmap"));
        Unicode = _glyphs.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Min(p => p.Key));
        Fallback = _glyphs.TryGetValue('?', out ushort question) ? question : (ushort)0;
    }

    /// <summary>Regular weight.</summary>
    public static ReportPdfFont Regular { get; } = Load("SecureOps.Reporting.LiberationMono-Regular.ttf");

    /// <summary>Bold weight.</summary>
    public static ReportPdfFont BoldFace { get; } = Load("SecureOps.Reporting.LiberationMono-Bold.ttf");

    /// <summary>Assembly resource name.</summary>
    public string Resource { get; }

    /// <summary>Unmodified font file.</summary>
    public byte[] Data { get; }

    /// <summary>Font design units per em.</summary>
    public int UnitsPerEm { get; }

    /// <summary>xMin, yMin, xMax, yMax in design units.</summary>
    public int[] BoundingBox { get; }

    /// <summary>Typographic ascent (design units).</summary>
    public int Ascent { get; }

    /// <summary>Typographic descent (design units, negative).</summary>
    public int Descent { get; }

    /// <summary>Capital height (design units).</summary>
    public int CapHeight { get; }

    /// <summary>Weight class 600 or above.</summary>
    public bool Bold { get; }

    /// <summary>Monospaced, as the PDF layout assumes.</summary>
    public bool FixedPitch { get; }

    /// <summary>PostScript name (name ID 6).</summary>
    public string PostScriptName { get; }

    /// <summary>Advance width per glyph (design units).</summary>
    public ushort[] Advances { get; }

    /// <summary>Glyph → lowest mapped code point, for text extraction.</summary>
    public IReadOnlyDictionary<ushort, char> Unicode { get; }

    /// <summary>Glyph used for characters the font does not cover ("?").</summary>
    public ushort Fallback { get; }

    /// <summary>Glyph for a character, or the fallback glyph.</summary>
    public ushort Glyph(char c) => _glyphs.TryGetValue(c, out ushort glyph) ? glyph : Fallback;

    /// <summary>Whether the font maps the character.</summary>
    public bool Covers(char c) => _glyphs.ContainsKey(c);

    /// <summary>Design units scaled to PDF glyph space (1/1000 em).</summary>
    public int Scale(int units) => (int)Math.Round(units * 1000.0 / UnitsPerEm, MidpointRounding.AwayFromZero);

    /// <summary>The font file compressed for a <c>FontFile2</c> stream (zlib, deterministic for the same runtime).</summary>
    public byte[] Compressed()
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Data);
        }

        return output.ToArray();
    }

    private static ReportPdfFont Load(string resource)
    {
        using Stream stream = typeof(ReportPdfFont).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded font resource {resource} is missing.");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return new ReportPdfFont(resource, buffer.ToArray());
    }

    private static int Short(ReadOnlySpan<byte> table, int offset) => BinaryPrimitives.ReadInt16BigEndian(table[offset..]);

    private static Dictionary<string, (int Offset, int Length)> Tables(byte[] data)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        Dictionary<string, (int, int)> tables = new(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int record = 12 + i * 16;
            tables[Encoding.ASCII.GetString(data, record, 4)] =
                ((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12)));
        }

        return tables;
    }

    private static ReadOnlySpan<byte> Table(byte[] data, Dictionary<string, (int Offset, int Length)> tables, string tag) =>
        tables.TryGetValue(tag, out (int Offset, int Length) t) ? data.AsSpan(t.Offset, t.Length)
            : throw new InvalidOperationException($"Font table {tag} is missing.");

    /// <summary>Windows Unicode BMP subtable (platform 3, encoding 1), format 4.</summary>
    private static Dictionary<char, ushort> Cmap(ReadOnlySpan<byte> cmap)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);
        for (int i = 0; i < count; i++)
        {
            int record = 4 + i * 8;
            if (BinaryPrimitives.ReadUInt16BigEndian(cmap[record..]) != 3 || BinaryPrimitives.ReadUInt16BigEndian(cmap[(record + 2)..]) != 1)
            {
                continue;
            }

            ReadOnlySpan<byte> sub = cmap[(int)BinaryPrimitives.ReadUInt32BigEndian(cmap[(record + 4)..])..];
            if (BinaryPrimitives.ReadUInt16BigEndian(sub) != 4)
            {
                continue;
            }

            int segments = BinaryPrimitives.ReadUInt16BigEndian(sub[6..]) / 2;
            int ends = 14, starts = ends + segments * 2 + 2, deltas = starts + segments * 2, ranges = deltas + segments * 2;
            Dictionary<char, ushort> map = [];
            for (int s = 0; s < segments; s++)
            {
                int end = BinaryPrimitives.ReadUInt16BigEndian(sub[(ends + s * 2)..]);
                int start = BinaryPrimitives.ReadUInt16BigEndian(sub[(starts + s * 2)..]);
                int delta = BinaryPrimitives.ReadInt16BigEndian(sub[(deltas + s * 2)..]);
                int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(sub[(ranges + s * 2)..]);
                for (int c = start; c <= end && c != 0xFFFF; c++)
                {
                    int glyph;
                    if (rangeOffset == 0)
                    {
                        glyph = (c + delta) & 0xFFFF;
                    }
                    else
                    {
                        int at = ranges + s * 2 + rangeOffset + (c - start) * 2;
                        glyph = BinaryPrimitives.ReadUInt16BigEndian(sub[at..]);
                        glyph = glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
                    }

                    if (glyph != 0)
                    {
                        map[(char)c] = (ushort)glyph;
                    }
                }
            }

            return map;
        }

        throw new InvalidOperationException("Font has no Windows Unicode BMP (3,1) format 4 cmap.");
    }

    private static string PostScript(ReadOnlySpan<byte> name)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
        int strings = BinaryPrimitives.ReadUInt16BigEndian(name[4..]);
        for (int i = 0; i < count; i++)
        {
            int record = 6 + i * 12;
            int platform = BinaryPrimitives.ReadUInt16BigEndian(name[record..]);
            if (BinaryPrimitives.ReadUInt16BigEndian(name[(record + 6)..]) != 6 || platform is not (1 or 3))
            {
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 8)..]);
            ReadOnlySpan<byte> value = name.Slice(strings + BinaryPrimitives.ReadUInt16BigEndian(name[(record + 10)..]), length);
            string text = platform == 3 ? Encoding.BigEndianUnicode.GetString(value) : Encoding.ASCII.GetString(value);
            return new string([.. text.Where(c => c is > ' ' and < (char)127 and not ('[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or '/' or '%'))]);
        }

        throw new InvalidOperationException("Font has no PostScript name.");
    }
}

/// <summary>
/// Per-document text encoder for the two embedded fonts: turns strings into Identity-H hex glyph strings, records which
/// glyphs were used (for the <c>/W</c> widths and the <c>ToUnicode</c> map) and writes the font objects.
/// </summary>
internal sealed class ReportPdfText
{
    private readonly SortedDictionary<ushort, char>[] _used = [new(), new()];

    /// <summary>A text-showing operand, e.g. <c>&lt;002B0048&gt;</c>, for the regular or bold font.</summary>
    public string Show(string value, bool bold)
    {
        ReportPdfFont font = bold ? ReportPdfFont.BoldFace : ReportPdfFont.Regular;
        SortedDictionary<ushort, char> used = _used[bold ? 1 : 0];
        StringBuilder hex = new(value.Length * 4 + 2);
        hex.Append('<');
        foreach (char c in value)
        {
            ushort glyph = font.Glyph(char.IsSurrogate(c) ? '?' : c);
            used.TryAdd(glyph, font.Covers(c) ? c : '?');
            hex.Append(glyph.ToString("X4", CultureInfo.InvariantCulture));
        }

        return hex.Append('>').ToString();
    }

    /// <summary>
    /// Font objects for <c>/F1</c> (regular) and <c>/F2</c> (bold). <paramref name="type0Ids"/> are the already referenced
    /// Type0 object numbers; the descendant, descriptor, file and ToUnicode objects get numbers from <paramref name="next"/>.
    /// Returns (object number, body) pairs in object-number order.
    /// </summary>
    public IEnumerable<(int Id, byte[] Body)> Objects(int[] type0Ids, int next)
    {
        List<(int, byte[])> objects = [];
        for (int f = 0; f < 2; f++)
        {
            ReportPdfFont font = f == 1 ? ReportPdfFont.BoldFace : ReportPdfFont.Regular;
            int cid = next++, descriptor = next++, file = next++, unicode = next++;
            string name = "/" + font.PostScriptName;
            objects.Add((type0Ids[f], Latin1($"<< /Type /Font /Subtype /Type0 /BaseFont {name} /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {unicode} 0 R >>")));
            string widths = string.Join(' ', _used[f].Keys.Select(g => $"{g} [{font.Scale(font.Advances[g])}]"));
            int defaultWidth = font.Scale(font.Advances[font.Glyph(' ')]);
            objects.Add((cid, Latin1($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont {name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
                + $"/FontDescriptor {descriptor} 0 R /DW {defaultWidth} /W [{widths}] /CIDToGIDMap /Identity >>")));
            int flags = (font.FixedPitch ? 1 : 0) | 32;
            string box = string.Join(' ', font.BoundingBox.Select(font.Scale));
            objects.Add((descriptor, Latin1($"<< /Type /FontDescriptor /FontName {name} /Flags {flags} /FontBBox [{box}] /ItalicAngle 0 "
                + $"/Ascent {font.Scale(font.Ascent)} /Descent {font.Scale(font.Descent)} /CapHeight {font.Scale(font.CapHeight)} /StemV {(font.Bold ? 120 : 80)} /FontFile2 {file} 0 R >>")));
            byte[] compressed = font.Compressed();
            objects.Add((file, [.. Latin1($"<< /Length {compressed.Length} /Length1 {font.Data.Length} /Filter /FlateDecode >>\nstream\n"), .. compressed, .. Latin1("\nendstream")]));
            byte[] cmap = Latin1(ToUnicode(_used[f]));
            objects.Add((unicode, [.. Latin1($"<< /Length {cmap.Length} >>\nstream\n"), .. cmap, .. Latin1("\nendstream")]));
        }

        return objects;
    }

    private static string ToUnicode(SortedDictionary<ushort, char> used)
    {
        StringBuilder cmap = new("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n"
            + "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n"
            + "1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach ((ushort Glyph, char Char)[] chunk in used.Select(p => (p.Key, p.Value)).Chunk(100))
        {
            cmap.Append(CultureInfo.InvariantCulture, $"{chunk.Length} beginbfchar\n");
            foreach ((ushort glyph, char c) in chunk)
            {
                cmap.Append(CultureInfo.InvariantCulture, $"<{glyph:X4}> <{(int)c:X4}>\n");
            }

            cmap.Append("endbfchar\n");
        }

        return cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend").ToString();
    }

    private static byte[] Latin1(string value) => Encoding.Latin1.GetBytes(value);
}
