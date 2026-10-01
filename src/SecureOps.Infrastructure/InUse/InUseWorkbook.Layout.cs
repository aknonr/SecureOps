using System.Xml;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public static partial class InUseWorkbook
{
    private static int ColumnWidth(string sheet, int column) => sheet == "Sunucular" ? (column == 0 ? 52 : 48)
        : column is 4 or 6 ? 48 : column is 15 or 16 or 17 ? 36 : 24;

    private static int RowHeight(string sheet, IReadOnlyList<string> row)
    {
        int lines = row.Select((value, column) => value.Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (ColumnWidth(sheet, column) * 0.8)))))
            .DefaultIfEmpty(1).Max();
        return Math.Min(409, Math.Max(30, lines * 16 + 8));
    }

    private static void WriteLayout(XmlWriter writer, InUseSheet sheet)
    {
        bool servers = sheet.Name == "Sunucular";
        writer.WriteStartElement("sheetViews", _spreadsheet);
        writer.WriteStartElement("sheetView", _spreadsheet);
        writer.WriteAttributeString("workbookViewId", "0");
        Element(writer, "pane", _spreadsheet, ("xSplit", servers ? "1" : "0"), ("ySplit", "1"),
            ("topLeftCell", servers ? "B2" : "A2"), ("activePane", servers ? "bottomRight" : "bottomLeft"), ("state", "frozen"));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("cols", _spreadsheet);
        for (int column = 0; column < sheet.Rows.Max(r => r.Count); column++)
        {
            string index = (column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Element(writer, "col", _spreadsheet, ("min", index), ("max", index),
                ("width", ColumnWidth(sheet.Name, column).ToString(System.Globalization.CultureInfo.InvariantCulture)), ("customWidth", "1"));
        }
        writer.WriteEndElement();
    }

    private static void WriteStyles(XmlWriter writer)
    {
        writer.WriteStartElement("styleSheet", _spreadsheet);
        writer.WriteStartElement("fonts", _spreadsheet);
        writer.WriteAttributeString("count", "2");
        for (int font = 0; font < 2; font++)
        {
            writer.WriteStartElement("font", _spreadsheet);
            if (font == 1)
            { Element(writer, "b", _spreadsheet); }
            Element(writer, "sz", _spreadsheet, ("val", "11"));
            Element(writer, "name", _spreadsheet, ("val", "Calibri"));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("fills", _spreadsheet);
        writer.WriteAttributeString("count", "2");
        foreach (string pattern in new[] { "none", "gray125" })
        {
            writer.WriteStartElement("fill", _spreadsheet);
            Element(writer, "patternFill", _spreadsheet, ("patternType", pattern));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("borders", _spreadsheet);
        writer.WriteAttributeString("count", "1");
        Element(writer, "border", _spreadsheet);
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyleXfs", _spreadsheet);
        writer.WriteAttributeString("count", "1");
        Element(writer, "xf", _spreadsheet, ("numFmtId", "0"), ("fontId", "0"), ("fillId", "0"), ("borderId", "0"));
        writer.WriteEndElement();
        writer.WriteStartElement("cellXfs", _spreadsheet);
        writer.WriteAttributeString("count", "2");
        foreach (string font in new[] { "0", "1" })
        {
            writer.WriteStartElement("xf", _spreadsheet);
            foreach ((string key, string value) in new[] { ("numFmtId", "49"), ("fontId", font), ("fillId", "0"),
                ("borderId", "0"), ("xfId", "0"), ("applyFont", "1"), ("applyAlignment", "1"), ("applyNumberFormat", "1") })
            { writer.WriteAttributeString(key, value); }
            Element(writer, "alignment", _spreadsheet, ("vertical", "top"), ("wrapText", "1"));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyles", _spreadsheet);
        writer.WriteAttributeString("count", "1");
        Element(writer, "cellStyle", _spreadsheet, ("name", "Normal"), ("xfId", "0"), ("builtinId", "0"));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
