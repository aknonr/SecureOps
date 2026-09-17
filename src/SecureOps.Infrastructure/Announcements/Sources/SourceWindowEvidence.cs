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
        { resolution = last > first ? "Resolved" : "Invalid"; }
        else if (!Recognized(start) || !Recognized(finish))
        { resolution = "Invalid"; }
        return new(start, finish, resolution);
    }

    /// <summary>Parses only explicitly offset-bearing source values, never machine-local time.</summary>
    public static bool TryInstant(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(text?.Trim(),
            ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mmzzz",
             "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);

    /// <summary>Interprets a recognized source wall clock only after explicit offset review.</summary>
    public static string? Resolve(string? text, string? reviewedOffset)
    {
        if (TryInstant(text, out DateTimeOffset instant))
        { return instant.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture); }
        if (reviewedOffset is null || !System.Text.RegularExpressions.Regex.IsMatch(reviewedOffset, @"\A[+-]\d{2}:\d{2}\z")
            || !TimeSpan.TryParseExact(reviewedOffset[1..], @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan offset)
            || offset > TimeSpan.FromHours(14))
        { return null; }
        if (!DateTime.TryParseExact(text?.Trim(),
            ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        { return null; }
        try
        { return new DateTimeOffset(local, reviewedOffset[0] == '-' ? -offset : offset).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture); }
        catch (ArgumentException) { return null; }
    }

    private static bool Recognized(string? text) => string.IsNullOrWhiteSpace(text)
        || TryInstant(text, out _)
        || DateTime.TryParseExact(text.Trim(),
            ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
             "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
