using System.Globalization;
using System.Security;
using System.Text;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Reporting;

/// <summary>
/// DrawingML chart and drawing parts for the XLSX executive summary. Each series references cells on the chart-data sheet
/// and carries a value cache, so Excel shows the figures without recalculation and the values stay editable data, not
/// pictures. Output is deterministic and contains no formulas, macros or external links.
/// </summary>
internal static class ReportChartXml
{
    /// <summary>Categorical slots in fixed order (validated palette, light surface).</summary>
    internal static readonly string[] Colors = ["2A78D6", "EB6834", "1BAF7A", "EDA100"];

    private static readonly string[] _markers = ["circle", "square", "triangle", "diamond"];
    private const string _chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string _drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string _relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string _ink = "4A5568";
    private const string _grid = "E5E7EB";

    /// <summary>Where a chart's cells sit on the data sheet (1-based rows; categories in column A, series from B).</summary>
    internal sealed record DataBlock(ReportChart Chart, int HeaderRow, int FirstRow, int LastRow);

    /// <summary>Chart part XML.</summary>
    internal static string Chart(DataBlock block, string dataSheet)
    {
        ReportChart chart = block.Chart;
        StringBuilder xml = new($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><c:chartSpace xmlns:c=\"{_chartNs}\" xmlns:a=\"{_drawingNs}\" xmlns:r=\"{_relNs}\">");
        xml.Append("<c:roundedCorners val=\"0\"/><c:chart>");
        xml.Append(CultureInfo.InvariantCulture, $"<c:title><c:tx><c:rich><a:bodyPr/><a:p><a:pPr><a:defRPr sz=\"1200\" b=\"1\"/></a:pPr><a:r><a:rPr lang=\"tr-TR\" sz=\"1200\" b=\"1\"><a:solidFill><a:srgbClr val=\"1B2A45\"/></a:solidFill></a:rPr><a:t>{Escape(chart.Title)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>");
        xml.Append("<c:autoTitleDeleted val=\"0\"/><c:plotArea><c:layout/>");
        bool line = chart.Kind == ReportChartKind.Line;
        xml.Append(line
            ? "<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>"
            : $"<c:barChart><c:barDir val=\"{(chart.Kind == ReportChartKind.Bar ? "bar" : "col")}\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>");
        for (int s = 0; s < chart.Series.Count; s++)
        {
            xml.Append(Series(block, dataSheet, s, line));
        }

        if (line)
        {
            xml.Append("<c:marker val=\"1\"/>");
        }
        else
        {
            xml.Append("<c:gapWidth val=\"60\"/>");
        }

        xml.Append("<c:axId val=\"500001\"/><c:axId val=\"500002\"/>").Append(line ? "</c:lineChart>" : "</c:barChart>");
        bool horizontal = chart.Kind == ReportChartKind.Bar;
        // Horizontal bars list categories top-down (reversed axis) and print every value, so the value axis is hidden.
        xml.Append(CultureInfo.InvariantCulture, $"<c:catAx><c:axId val=\"500001\"/><c:scaling><c:orientation val=\"{(horizontal ? "maxMin" : "minMax")}\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{(horizontal ? "l" : "b")}\"/><c:numFmt formatCode=\"General\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>{Line(_grid)}{Text(900)}<c:crossAx val=\"500002\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");
        xml.Append(CultureInfo.InvariantCulture, $"<c:valAx><c:axId val=\"500002\"/><c:scaling><c:orientation val=\"minMax\"/><c:min val=\"0\"/></c:scaling><c:delete val=\"{(horizontal ? 1 : 0)}\"/><c:axPos val=\"{(horizontal ? "b" : "l")}\"/>");
        if (!horizontal)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<c:majorGridlines>{Line(_grid)}</c:majorGridlines>");
        }

        xml.Append(CultureInfo.InvariantCulture, $"<c:numFmt formatCode=\"0\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:spPr><a:ln><a:noFill/></a:ln></c:spPr>{Text(900)}<c:crossAx val=\"500001\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/>{(chart.Peak <= 10 ? "<c:majorUnit val=\"1\"/>" : string.Empty)}</c:valAx>");
        xml.Append("</c:plotArea>");
        if (chart.Series.Count > 1)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/>{Text(900)}</c:legend>");
        }

        xml.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>");
        xml.Append("<c:spPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>");
        xml.Append("<c:printSettings><c:headerFooter/><c:pageMargins b=\"0.75\" l=\"0.7\" r=\"0.7\" t=\"0.75\" header=\"0.3\" footer=\"0.3\"/><c:pageSetup/></c:printSettings>");
        xml.Append("</c:chartSpace>");
        return xml.ToString();
    }

    /// <summary>Drawing part placing the charts two per row from <paramref name="firstRow"/> (0-based) on a 12-column sheet.</summary>
    internal static string Drawing(int count, int firstRow, int rowsPerChart)
    {
        StringBuilder xml = new($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"{_drawingNs}\">");
        for (int i = 0; i < count; i++)
        {
            int column = i % 2 * 6, row = firstRow + i / 2 * (rowsPerChart + 1);
            xml.Append(CultureInfo.InvariantCulture, $"<xdr:twoCellAnchor editAs=\"oneCell\"><xdr:from><xdr:col>{column}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>");
            xml.Append(CultureInfo.InvariantCulture, $"<xdr:to><xdr:col>{column + 6}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row + rowsPerChart}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>");
            xml.Append(CultureInfo.InvariantCulture, $"<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"{i + 2}\" name=\"Grafik {i + 1}\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>");
            xml.Append(CultureInfo.InvariantCulture, $"<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"{_chartNs}\"><c:chart xmlns:c=\"{_chartNs}\" xmlns:r=\"{_relNs}\" r:id=\"rId{i + 1}\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>");
        }

        return xml.Append("</xdr:wsDr>").ToString();
    }

    /// <summary>Drawing relationships to chart1..chartN.</summary>
    internal static string DrawingRelationships(int count) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + string.Concat(Enumerable.Range(1, count).Select(i => $"<Relationship Id=\"rId{i}\" Type=\"{_relNs}/chart\" Target=\"../charts/chart{i}.xml\"/>"))
        + "</Relationships>";

    private static string Series(DataBlock block, string dataSheet, int s, bool line)
    {
        ReportChart chart = block.Chart;
        ReportChartSeries series = chart.Series[s];
        string sheet = "'" + dataSheet.Replace("'", "''", StringComparison.Ordinal) + "'!";
        string column = ((char)('B' + s)).ToString();
        string color = Colors[s % Colors.Length];
        StringBuilder xml = new();
        xml.Append(CultureInfo.InvariantCulture, $"<c:ser><c:idx val=\"{s}\"/><c:order val=\"{s}\"/>");
        xml.Append(CultureInfo.InvariantCulture, $"<c:tx><c:strRef><c:f>{Escape($"{sheet}${column}${block.HeaderRow}")}</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>{Escape(series.Name)}</c:v></c:pt></c:strCache></c:strRef></c:tx>");
        if (line)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<c:spPr><a:ln w=\"22225\" cap=\"rnd\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:round/></a:ln></c:spPr>");
            xml.Append(CultureInfo.InvariantCulture, $"<c:marker><c:symbol val=\"{_markers[s % _markers.Length]}\"/><c:size val=\"6\"/><c:spPr><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:ln w=\"9525\"><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill></a:ln></c:spPr></c:marker>");
        }
        else
        {
            xml.Append(CultureInfo.InvariantCulture, $"<c:spPr><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill></c:spPr><c:invertIfNegative val=\"0\"/>");
            xml.Append(CultureInfo.InvariantCulture, $"<c:dLbls><c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>{Text(900)}<c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls>");
        }

        string categories = $"{sheet}$A${block.FirstRow}:$A${block.LastRow}";
        string values = $"{sheet}${column}${block.FirstRow}:${column}${block.LastRow}";
        xml.Append(CultureInfo.InvariantCulture, $"<c:cat><c:strRef><c:f>{Escape(categories)}</c:f><c:strCache><c:ptCount val=\"{chart.Categories.Count}\"/>");
        for (int i = 0; i < chart.Categories.Count; i++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<c:pt idx=\"{i}\"><c:v>{Escape(chart.Categories[i])}</c:v></c:pt>");
        }

        xml.Append(CultureInfo.InvariantCulture, $"</c:strCache></c:strRef></c:cat><c:val><c:numRef><c:f>{Escape(values)}</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"{series.Values.Count}\"/>");
        for (int i = 0; i < series.Values.Count; i++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<c:pt idx=\"{i}\"><c:v>{series.Values[i]}</c:v></c:pt>");
        }

        xml.Append("</c:numCache></c:numRef></c:val>");
        if (line)
        {
            xml.Append("<c:smooth val=\"0\"/>");
        }

        return xml.Append("</c:ser>").ToString();
    }

    private static string Line(string color) => $"<c:spPr><a:ln w=\"6350\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill></a:ln></c:spPr>";

    private static string Text(int size) =>
        $"<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"{size.ToString(CultureInfo.InvariantCulture)}\"><a:solidFill><a:srgbClr val=\"{_ink}\"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang=\"tr-TR\"/></a:p></c:txPr>";

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
