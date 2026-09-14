using System.Globalization;

namespace SecureOps.Ui.Services;

/// <summary>Explains server-returned field keys without relaxing or replacing API validation.</summary>
public static class AnnouncementFieldFeedback
{
    /// <summary>Values are read only; dates, seconds and offsets are never repaired here.</summary>
    public static string Message(string key, IReadOnlyDictionary<string, string> values)
    {
        bool Time(string field, out DateTimeOffset time) => DateTimeOffset.TryParse(values.GetValueOrDefault(field),
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        if (key is "WorkStart" or "WorkEnd" or "RestartStart" or "RestartEnd")
        {
            if (!Time(key, out DateTimeOffset end))
            { return "Tarih, saat ve dakikayı tamamlayın; saniye ve saat dilimini kontrol edin."; }
            string startKey = key == "WorkEnd" ? "WorkStart" : "RestartStart";
            if (key.EndsWith("End", StringComparison.Ordinal) && Time(startKey, out DateTimeOffset start) && end <= start)
            { return "Bitiş, başlangıçtan sonra olmalı; tarih ve saat dilimini kontrol edin."; }
            return "Başlangıç ve bitiş tarihlerini birlikte tamamlayın.";
        }
        return key switch
        {
            "AnnouncementDate" => "Geçerli bir duyuru tarihi girin.",
            "To" or "Cc" => "Alıcı adreslerini kontrol edin; en az bir Alıcı gerekli.",
            "BannerRevision" => "Kullanılabilir bir görsel paketi seçin.",
            "AffectedServices" => "Etkilenen servisleri kontrol edin.",
            _ => "Bu alanı tamamlayın ve izin verilen uzunluğu kontrol edin."
        };
    }
}
