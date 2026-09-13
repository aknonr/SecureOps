using System.Globalization;

namespace SecureOps.Ui.Services;

/// <summary>Manual wall-time editing and explicit instant-preserving offset conversion.</summary>
public sealed class AnnouncementTime
{
    /// <summary>Incomplete components stay incomplete until explicitly entered.</summary>
    public string Day { get; set; } = "";
    /// <summary>24-hour component.</summary>
    public string Hour { get; set; } = "";
    /// <summary>Every minute is supported.</summary>
    public string Minute { get; set; } = "";
    /// <summary>Advanced seconds, never rounded.</summary>
    public string Second { get; set; } = "00";
    /// <summary>Explicit offset; not a source timezone assertion.</summary>
    public string Offset { get; set; } = "+00:00";
    /// <summary>Retains partial entry as invalid text, not an old valid instant.</summary>
    public string Value => Day.Length + Hour.Length + Minute.Length == 0 ? "" : $"{Day}T{Hour}:{Minute}:{Second}{Offset}";
    /// <summary>Validates configured offsets without silently substituting another timezone.</summary>
    public static bool ValidOffset(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[+-]\d{2}:\d{2}\z")
        && DateTimeOffset.TryParse("2000-01-01T00:00:00" + value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    /// <summary>Loads original seconds/offset without changing the persisted string.</summary>
    public void Load(string value, string defaultOffset)
    {
        Offset = value.EndsWith('Z') ? "+00:00" : value.Length >= 6 && ValidOffset(value[^6..]) ? value[^6..] : defaultOffset;
        string local = value.EndsWith('Z') ? value[..^1] : value.EndsWith(Offset, StringComparison.Ordinal) ? value[..^Offset.Length] : value;
        string[] parts = local.Split('T');
        Day = parts[0];
        string[] time = (parts.Length > 1 ? parts[1] : "").Split(':');
        Hour = time[0];
        Minute = time.ElementAtOrDefault(1) ?? "";
        Second = time.ElementAtOrDefault(2) ?? "00";
    }
    /// <summary>Changes representation only; incomplete values cannot be converted.</summary>
    public bool ConvertOffset(string offset)
    {
        if (!ValidOffset(offset) || !DateTimeOffset.TryParse(Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset current))
        { return false; }
        TimeSpan target = DateTimeOffset.Parse("2000-01-01T00:00:00" + offset, CultureInfo.InvariantCulture).Offset;
        Load(current.ToOffset(target).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture), offset);
        return true;
    }
}
