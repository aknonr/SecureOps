using System.Globalization;
using System.Text.RegularExpressions;
using MimeKit;
using SecureOps.Domain.Announcements;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Plain-text limits and strict bare mailbox/date validation.</summary>
public static class AnnouncementValidation
{
    /// <summary>Rejects display-name lists, controls and header injection; never queries a directory.</summary>
    public static bool Address(string? value) => value is { Length: > 3 and <= 254 }
        && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
        && MailboxAddress.TryParse(value, out MailboxAddress? address) && string.IsNullOrEmpty(address.Name)
        && address.Address == value && value.Contains('@') && !value.Contains('<');

    /// <summary>Field keys are stable editor navigation targets; incomplete drafts may be saved.</summary>
    public static string[] Errors(AnnouncementContent content, bool complete)
    {
        List<string> errors = [];
        if (content.DateTextRevision is not ("iso-v1" or "tr-v1") || content.TemplateRevision == "oco-v1" && content.DateTextRevision != "iso-v1")
        { errors.Add("DateTextRevision"); }
        if (content.TemplateRevision is not ("oco-v1" or "oco-table-v2" or "oco-table-v3"))
        { errors.Add("TemplateRevision"); }
        string[] services = content.AffectedServices ?? [];
        if (services.Length > 256 || services.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256 || s.Any(char.IsControl))
            || services.Sum(s => (long)(s?.Length ?? 0) + 1) > 16000
            || content.TemplateRevision == "oco-v1" && services.Length > 0
            || complete && content.TemplateRevision != "oco-v1" && services.Length == 0)
        { errors.Add("AffectedServices"); }
        foreach ((string key, string? value) in Fields(content))
        {
            int limit = key == "Subject" ? 200 : 4000;
            if (value is null || value.Length > limit || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
                || key == "Subject" && value.Any(char.IsControl) || complete && key != "Notes" && string.IsNullOrWhiteSpace(value))
            { errors.Add(key); }
        }
        bool Date(string? value) => value is not null && Regex.IsMatch(value, @"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})\z")
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        if (!string.IsNullOrEmpty(content.AnnouncementDate) && !DateOnly.TryParseExact(content.AnnouncementDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        { errors.Add("AnnouncementDate"); }
        foreach ((string key, string? value) in new[] { ("WorkStart", content.WorkStart), ("WorkEnd", content.WorkEnd), ("RestartStart", content.RestartStart), ("RestartEnd", content.RestartEnd) })
        { if (!string.IsNullOrEmpty(value) && (value.Length > 40 || !Date(value))) { errors.Add(key); } }
        if (Date(content.WorkStart) && Date(content.WorkEnd) && DateTimeOffset.Parse(content.WorkEnd, CultureInfo.InvariantCulture) <= DateTimeOffset.Parse(content.WorkStart, CultureInfo.InvariantCulture))
        { errors.Add("WorkEnd"); }
        if (string.IsNullOrEmpty(content.RestartStart) != string.IsNullOrEmpty(content.RestartEnd)
            || Date(content.RestartStart) && Date(content.RestartEnd) && DateTimeOffset.Parse(content.RestartEnd!, CultureInfo.InvariantCulture) <= DateTimeOffset.Parse(content.RestartStart!, CultureInfo.InvariantCulture))
        { errors.Add("RestartEnd"); }
        foreach ((string key, string[]? values) in new[] { ("To", content.To), ("Cc", content.Cc) })
        { if (values is null || values.Length > 50 || values.Any(v => !Address(v)) || complete && key == "To" && values.Length == 0) { errors.Add(key); } }
        if (content.BannerRevision is null || (complete || content.BannerRevision.Length > 0) && !Regex.IsMatch(content.BannerRevision, @"\A[a-z0-9-]{1,64}\z"))
        { errors.Add("BannerRevision"); }
        return [.. errors.Distinct()];
    }

    /// <summary>One ordered field set feeds both render alternatives.</summary>
    public static (string Key, string Value)[] Fields(AnnouncementContent c) =>
        [("OcoReference", c.OcoReference), ("Scope", c.Scope), ("Subject", c.Subject), ("AnnouncementDate", c.AnnouncementDate),
        ("WorkStart", c.WorkStart), ("WorkEnd", c.WorkEnd), ("Description", c.Description), ("Impact", c.Impact), ("Checks", c.Checks), ("Notes", c.Notes)];
}
