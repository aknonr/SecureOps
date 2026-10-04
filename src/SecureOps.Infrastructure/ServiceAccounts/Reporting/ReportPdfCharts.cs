using System.Globalization;
using System.Text;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// The PDF chart page: up to six charts in a 2 × 3 grid, drawn with plain PDF path operators (rectangles, lines, Bézier
/// markers) and the writer's standard fonts. Every value is printed as text beside its mark, so the page reads without
/// colour and reconciles with the XLSX "Grafik verisi" sheet. Deterministic; no images, fonts or compression.
/// </summary>
internal static class ReportPdfCharts
{
    /// <summary>Charts that fit the page.</summary>
    internal const int Slots = 6;

    private const double _gap = 12;
    private const double _heading = 16;
    private const double _label = 6.2;
    private const double _small = 5.6;
    private const string _ink = "0.10 0.12 0.16";
    private const string _muted = "0.40 0.43 0.48";
    private const string _rule = "0.780 0.800 0.835";
    private const string _grid = "0.898 0.906 0.922";
    private const string _headingFill = "0.902 0.918 0.941";
    private static readonly string[] _markers = ["circle", "square", "triangle", "diamond"];

    /// <summary>Content stream of the chart page (without the footer).</summary>
    internal static string Page(IReadOnlyList<ReportChart> charts, double pageWidth, double pageHeight, double margin, double footerSpace)
    {
        Canvas canvas = new();
        double width = pageWidth - 2 * margin, top = pageHeight - margin;
        canvas.Rect(_headingFill, margin, top - _heading, width, _heading);
        canvas.Text(margin + 4, top - 11.5, 8, true, _ink, "GRAFİKLER · DEĞERLER DETAY TABLOLARIYLA VE XLSX \"GRAFİK VERİSİ\" SAYFASIYLA AYNIDIR");
        double gridTop = top - _heading - 8, bottom = margin + footerSpace;
        double boxWidth = (width - _gap) / 2, boxHeight = (gridTop - bottom - 2 * _gap) / 3;
        for (int i = 0; i < charts.Count && i < Slots; i++)
        {
            double x = margin + i % 2 * (boxWidth + _gap), y = gridTop - i / 2 * (boxHeight + _gap) - boxHeight;
            Chart(canvas, charts[i], x, y, boxWidth, boxHeight);
        }

        return canvas.ToString();
    }

    private static void Chart(Canvas canvas, ReportChart chart, double x, double y, double w, double h)
    {
        canvas.Stroke(_rule, 0.6, $"{N(x)} {N(y)} {N(w)} {N(h)} re");
        canvas.Text(x + 6, y + h - 12, 7.5, true, _ink, Fit(chart.Title, (int)((w - 12) / (7.5 * 0.6))));
        double legend = chart.Series.Count > 1 ? 14 : 0;
        if (legend > 0)
        {
            double lx = x + 6;
            for (int s = 0; s < chart.Series.Count; s++)
            {
                Marker(canvas, s, lx + 3, y + 8, 2.6);
                canvas.Text(lx + 8, y + 6, _small, false, _muted, chart.Series[s].Name);
                lx += 16 + chart.Series[s].Name.Length * _small * 0.6;
            }
        }

        double px = x + 6, py = y + 6 + legend, pw = w - 12, ph = h - 22 - legend;
        if (chart.Note is { Length: > 0 } note)
        {
            canvas.Text(x + w - 6 - Math.Min(note.Length, 60) * _small * 0.6, y + h - 12, _small, false, _muted, Fit(note, 60));
        }

        switch (chart.Kind)
        {
            case ReportChartKind.Bar:
                Bars(canvas, chart, px, py, pw, ph);
                break;
            case ReportChartKind.Column:
                Columns(canvas, chart, px, py, pw, ph);
                break;
            default:
                Lines(canvas, chart, px, py, pw, ph);
                break;
        }
    }

    /// <summary>Horizontal bars, one series: label | bar | value, categories top-down.</summary>
    private static void Bars(Canvas canvas, ReportChart chart, double px, double py, double pw, double ph)
    {
        int n = chart.Categories.Count;
        int labelChars = Math.Min(chart.Categories.Max(c => c.Length), (int)(pw * 0.4 / (_label * 0.6)));
        double labelWidth = labelChars * _label * 0.6 + 6, valueWidth = 26, maxBar = pw - labelWidth - valueWidth;
        double row = ph / n, bar = Math.Min(10, row * 0.62);
        IReadOnlyList<long> values = chart.Series[0].Values;
        for (int i = 0; i < n; i++)
        {
            double cy = py + ph - (i + 0.5) * row;
            canvas.Text(px, cy - 2.2, _label, false, _muted, Fit(chart.Categories[i], labelChars));
            double length = Math.Max(1, values[i] * maxBar / chart.Peak);
            canvas.Rect(Color(0), px + labelWidth, cy - bar / 2, length, bar);
            canvas.Text(px + labelWidth + length + 3, cy - 2.2, _label, false, _ink, Value(values[i]));
        }
    }

    /// <summary>Grouped vertical columns on one axis, values printed above each column.</summary>
    private static void Columns(Canvas canvas, ReportChart chart, double px, double py, double pw, double ph)
    {
        long top = ReportCharts.AxisTop(chart.Peak);
        double axis = 20, baseline = py + 10, plotHeight = ph - 18, plotLeft = px + axis, plotWidth = pw - axis;
        Grid(canvas, top, px, baseline, plotLeft, plotWidth, plotHeight);
        int n = chart.Categories.Count, s = chart.Series.Count;
        double group = plotWidth / n, bar = Math.Min(12, group * 0.78 / s);
        int labelChars = Math.Max(3, (int)(group / (_small * 0.6)) - 1);
        for (int i = 0; i < n; i++)
        {
            double start = plotLeft + i * group + (group - bar * s) / 2;
            for (int k = 0; k < s; k++)
            {
                long value = chart.Series[k].Values[i];
                double height = Math.Max(0.8, value * plotHeight / top), bx = start + k * bar;
                canvas.Rect(Color(k), bx + 0.4, baseline, bar - 0.8, height);
                canvas.Text(bx + bar / 2 - Value(value).Length * _small * 0.3, baseline + height + 2, _small, false, _ink, Value(value));
            }

            string label = Fit(chart.Categories[i], labelChars);
            canvas.Text(plotLeft + i * group + group / 2 - label.Length * _small * 0.3, py + 2, _small, false, _muted, label);
        }
    }

    /// <summary>Lines with distinct markers; the last value of each series is printed at its end.</summary>
    private static void Lines(Canvas canvas, ReportChart chart, double px, double py, double pw, double ph)
    {
        long top = ReportCharts.AxisTop(chart.Peak);
        double axis = 20, baseline = py + 10, plotHeight = ph - 16, plotLeft = px + axis + 4, plotWidth = pw - axis - 22;
        Grid(canvas, top, px, baseline, plotLeft - 4, plotWidth + 8, plotHeight);
        int n = chart.Categories.Count;
        double X(int i) => n == 1 ? plotLeft + plotWidth / 2 : plotLeft + i * plotWidth / (n - 1);
        double Y(long v) => baseline + v * plotHeight / top;
        int every = n > 8 ? 2 : 1;
        for (int i = 0; i < n; i += every)
        {
            canvas.Text(X(i) - chart.Categories[i].Length * _small * 0.3, py + 2, _small, false, _muted, chart.Categories[i]);
        }

        for (int s = chart.Series.Count - 1; s >= 0; s--)
        {
            IReadOnlyList<long> values = chart.Series[s].Values;
            StringBuilder path = new();
            for (int i = 0; i < n; i++)
            {
                path.Append(CultureInfo.InvariantCulture, $"{N(X(i))} {N(Y(values[i]))} {(i == 0 ? "m" : "l")} ");
            }

            canvas.Stroke(Color(s), 1.2, path.ToString().TrimEnd());
            for (int i = 0; i < n; i++)
            {
                Marker(canvas, s, X(i), Y(values[i]), 2.2);
            }

            canvas.Text(X(n - 1) + 5, Y(values[n - 1]) - 2, _small, true, _ink, Value(values[n - 1]));
        }
    }

    /// <summary>Recessive gridlines at 0, half and top with their values on the left.</summary>
    private static void Grid(Canvas canvas, long top, double px, double baseline, double left, double width, double height)
    {
        foreach ((long value, double level) in new[] { (0L, 0.0), (top / 2, 0.5), (top, 1.0) })
        {
            double gy = baseline + level * height;
            canvas.Stroke(level == 0 ? _rule : _grid, level == 0 ? 0.6 : 0.4, $"{N(left)} {N(gy)} m {N(left + width)} {N(gy)} l");
            canvas.Text(px, gy - 2, _small, false, _muted, Value(value));
        }
    }

    /// <summary>Series identity by shape as well as colour: circle, square, triangle, diamond.</summary>
    private static void Marker(Canvas canvas, int series, double cx, double cy, double r)
    {
        string color = Color(series);
        switch (_markers[series % _markers.Length])
        {
            case "circle":
                double k = r * 0.5523;
                canvas.Fill(color, $"{N(cx + r)} {N(cy)} m {N(cx + r)} {N(cy + k)} {N(cx + k)} {N(cy + r)} {N(cx)} {N(cy + r)} c "
                    + $"{N(cx - k)} {N(cy + r)} {N(cx - r)} {N(cy + k)} {N(cx - r)} {N(cy)} c {N(cx - r)} {N(cy - k)} {N(cx - k)} {N(cy - r)} {N(cx)} {N(cy - r)} c "
                    + $"{N(cx + k)} {N(cy - r)} {N(cx + r)} {N(cy - k)} {N(cx + r)} {N(cy)} c h");
                break;
            case "square":
                canvas.Rect(color, cx - r, cy - r, 2 * r, 2 * r);
                break;
            case "triangle":
                canvas.Fill(color, $"{N(cx)} {N(cy + r * 1.2)} m {N(cx + r * 1.15)} {N(cy - r)} l {N(cx - r * 1.15)} {N(cy - r)} l h");
                break;
            default:
                canvas.Fill(color, $"{N(cx)} {N(cy + r * 1.3)} m {N(cx + r * 1.3)} {N(cy)} l {N(cx)} {N(cy - r * 1.3)} l {N(cx - r * 1.3)} {N(cy)} l h");
                break;
        }
    }

    private static string Color(int series)
    {
        string hex = ReportChartXml.Colors[series % ReportChartXml.Colors.Length];
        return string.Join(' ', Enumerable.Range(0, 3).Select(i =>
            (int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0).ToString("0.###", CultureInfo.InvariantCulture)));
    }

    private static string Value(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Fit(string value, int max) => max <= 1 || value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string N(double value) => ReportPdfWriter.N(value);

    /// <summary>Collects shapes and text separately; text goes into one BT … ET object after the shapes.</summary>
    private sealed class Canvas
    {
        private readonly StringBuilder _shapes = new();
        private readonly StringBuilder _text = new();

        public void Rect(string color, double x, double y, double w, double h) =>
            _shapes.Append(CultureInfo.InvariantCulture, $"{color} rg {N(x)} {N(y)} {N(w)} {N(h)} re f\n");

        public void Fill(string color, string path) => _shapes.Append(CultureInfo.InvariantCulture, $"{color} rg {path} f\n");

        public void Stroke(string color, double width, string path) =>
            _shapes.Append(CultureInfo.InvariantCulture, $"{color} RG {N(width)} w {path} S\n");

        public void Text(double x, double y, double size, bool bold, string color, string value) =>
            _text.Append(CultureInfo.InvariantCulture, $"{color} rg /{(bold ? "F2" : "F1")} {N(size)} Tf\n1 0 0 1 {N(x)} {N(y)} Tm ({ReportPdfWriter.Encode(value)}) Tj\n");

        public override string ToString() => _shapes + "BT\n" + _text + "ET";
    }
}
