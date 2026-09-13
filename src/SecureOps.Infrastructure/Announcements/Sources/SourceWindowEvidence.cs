using System.Globalization;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>Preserves original text. Only explicit ISO offsets establish an instant; nothing sets restart time.</summary>
public static class SourceWindowEvidence
{
    /// <summary>Classifies a bounded pair without normalizing, trimming or replacing its stored evidence.</summary>
    public static ChangeWindowResult Read(string? start, string? finish)
    {
        if (start?.Length > 128 || finish?.Length > 128)
        { throw new AnnouncementSourceException("AnnouncementSourceInvalidResponse", false); }
        string resolution = string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(finish) ? "Missing" : "Unresolved";
        if (TryInstant(start, out DateTimeOffset first) && TryInstant(finish, out DateTimeOffset last))
        { resolution = last >= first ? "Resolved" : "Invalid"; }
        else if (!Recognized(start) || !Recognized(finish))
        { resolution = "Invalid"; }
        return new(start, finish, resolution);
    }

    private static bool TryInstant(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(text?.Trim(),
            ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mmzzz",
             "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);

    private static bool Recognized(string? text) => string.IsNullOrWhiteSpace(text)
        || TryInstant(text, out _)
        || DateTime.TryParseExact(text.Trim(),
            ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
             "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
