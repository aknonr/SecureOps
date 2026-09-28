using System.Globalization;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>
/// Value conversions for staged source cells. Excel 1900-system serials and ISO strings are read
/// without assigning a time zone; date-only values keep date-only precision and nothing is invented.
/// </summary>
public static class ImportValues
{
    private static readonly string[] _dateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss",
        "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "d.M.yyyy HH:mm", "d.M.yyyy HH:mm:ss"
    ];

    /// <summary>Earliest serial accepted as a real date (1900-03-01); earlier values are rejected, never shown as 1900.</summary>
    private const double _minimumSerial = 61;
    private const double _maximumSerial = 2958465;

    /// <summary>Serializes a naive timestamp without zone information.</summary>
    public static string Naive(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Serializes a business date.</summary>
    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Converts a cell (serial number or text) to a naive timestamp. Null when blank; false when invalid.</summary>
    public static bool TryTimestamp(SheetCell? cell, out DateTime? value) =>
        cell?.Number is { } serial ? TrySerial(serial, out value) : TryTimestamp(cell?.Text, out value);

    /// <summary>Parses a text timestamp (ISO or Turkish day-first); blank is a valid "unknown".</summary>
    public static bool TryTimestamp(string? text, out DateTime? value)
    {
        value = null;
        string? clean = ServiceAccountText.Clean(text);
        if (clean is null)
        {
            return true;
        }

        if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial))
        {
            return TrySerial(serial, out value);
        }

        if (DateTime.TryParseExact(clean, _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            && parsed.Year is >= 1901 and <= 9999)
        {
            value = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            return true;
        }

        return false;
    }

    /// <summary>Converts to a business date; any time of day is dropped because the source zone is unknown.</summary>
    public static bool TryDate(string? text, out DateOnly? value)
    {
        value = null;
        if (!TryTimestamp(text, out DateTime? timestamp))
        {
            return false;
        }

        value = timestamp is { } t ? DateOnly.FromDateTime(t) : null;
        return true;
    }

    /// <summary>True when the timestamp carries a time of day (not midnight).</summary>
    public static bool HasTime(DateTime value) => value.TimeOfDay != TimeSpan.Zero;

    private static bool TrySerial(double serial, out DateTime? value)
    {
        value = null;
        if (double.IsNaN(serial) || serial < _minimumSerial || serial > _maximumSerial)
        {
            return false;
        }

        // Excel 1900 system: FromOADate matches Excel for serials after the fictitious 1900-02-29.
        var parsed = DateTime.FromOADate(serial);
        value = DateTime.SpecifyKind(new DateTime(parsed.Ticks - parsed.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Unspecified);
        return true;
    }
}
