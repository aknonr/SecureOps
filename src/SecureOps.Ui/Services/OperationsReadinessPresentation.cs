using System.Globalization;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Ui.Services;

internal sealed record OperationStatusRow(string Label, string Status, string Reason, string Next);

internal static class OperationsReadinessPresentation
{
    internal static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("dd.MM.yyyy HH:mm:ss 'UTC (+00:00)'", CultureInfo.InvariantCulture);

    internal static IReadOnlyList<OperationStatusRow> Rows(OperationsReadiness report)
    {
        string? Setting(string key) => report.Settings.FirstOrDefault(s => s.Key == key)?.Value;
        bool? Flag(string key) => bool.TryParse(Setting(key), out bool value) ? value : null;
        OperationStatusRow source = report.Source.State switch
        {
            "Disabled" => new OperationStatusRow("OCO kaynakları", "Kapalı", "Kaynak toplama bu API yapılandırmasında etkin değil.", "Onaylı kaynak ve profil ayarlarını API/Worker raporlarıyla karşılaştırın."),
            "ConfigurationMissing" => new("OCO kaynakları", "Yapılandırma eksik", "Kaynak toplama için gerekli ayarlar tamamlanmamış.", "Eksik anahtarları teknik ayrıntılarda inceleyin; kaynak sahibiyle doğrulayın."),
            "Ready" => new("OCO kaynakları", "Kısmen doğrulandı", "Kuyruk hazır; kaynak bağlantısı sınanmadı.", "Onaylı OCO/profil işinin cihaz, servis ve tarih sonucunu doğrulayın."),
            "NoWorker" or "WrongQueue" => new("OCO kaynakları", "Kısmen doğrulandı", "Kaynak ayarları kontrol edildi; iş kuyruğu hazır değil.", "Worker sonucunu inceleyin ve normal başlangıç raporuyla karşılaştırın."),
            "NotChecked" => new("OCO kaynakları", "Kontrol edilmedi", "Kaynak kontrol sonucu bulunmuyor.", "Durumu yeniden kontrol edin."),
            _ => new("OCO kaynakları", "Kontrol başarısız", "Kaynak hazırlığı doğrulanamadı.", "Yeniden kontrol edin; sorun sürerse tanılama raporunu destekle paylaşın.")
        };
        if (Flag("Hangfire:Enabled") == false)
        { source = source with { Reason = source.Reason + " İş kuyruğu API tarafında etkin değil." }; }
        OperationStatusRow worker = report.Source.WorkerState switch
        {
            "NotChecked" => new OperationStatusRow("Worker", "Kontrol edilmedi", "Bu kontrolde Worker heartbeat sorgusu yapılmadı.", "Normal Worker oturumunun tanılama raporunu alın ve API ile karşılaştırın."),
            "Ready" => new("Worker", "Doğrulandı", $"{report.Source.MatchingWorkers} eşleşen Worker heartbeat kaydı bulundu; iş sonucu değildir.", "Seçilen işin kayıtlı terminal sonucunu ayrıca doğrulayın."),
            "NoWorker" => new("Worker", "Kontrol başarısız", "Sorguda güncel Worker heartbeat kaydı bulunamadı.", "Worker oturumunu, veritabanını ve kuyruk eşleşmesini kontrol edin."),
            "WrongQueue" => new("Worker", "Kontrol başarısız", "Güncel Worker kayıtları beklenen kuyrukla eşleşmiyor.", "Onaylı API/Worker kuyruk ayarlarını karşılaştırın."),
            _ => new("Worker", "Kontrol başarısız", "Worker durumu okunamadı; çalışmadığı sonucu çıkarılamaz.", "Tanılama raporunu destekle paylaşın.")
        };
        bool? mail = Flag("AnnouncementMail:Enabled"), self = Flag("AnnouncementMail:SelfTestEnabled"), send = Flag("AnnouncementMail:SendEnabled");
        bool mailOff = mail == false || self == false && send == false;
        var mailRow = new OperationStatusRow("Mail gönderimi", mailOff ? "Kapalı" : "Kontrol edilmedi",
            mailOff ? "Kendime deneme ve dağıtım etkin değil." : "Gönderim kontrolleri yapılandırılmış olabilir; SMTP bağlantısı sınanmadı.",
            mailOff ? "Onaylı relay politikası ve ayrı gönderim kontrollerini operatörle doğrulayın." : "API/Worker politikasını karşılaştırın; yalnız seçili kendime deneme ile kabul yapın.");
        int validated = report.Assets.Count(a => a.State == "Validated");
        bool allAssets = report.Assets.Count > 0 && validated == report.Assets.Count;
        bool allBundles = report.Bundles.Count > 0 && report.Bundles.All(b => b.State == "Validated");
        string bundleReason = report.Bundles.Any(b => b.State == "PresentNotValidated")
            ? "Şablon paketi mevcut; doğrulaması tamamlanmadı."
            : allBundles ? "Şablon paketi doğrulandı." : "Şablon paketi doğrulanmadı.";
        var assets = new OperationStatusRow("Duyuru görselleri",
            allAssets && allBundles ? "Doğrulandı" : validated > 0 ? "Kısmen doğrulandı" : report.Assets.Count == 0 ? "Kontrol edilmedi" : "Kontrol başarısız",
            $"{validated} dosya doğrulandı. {bundleReason}", "Paket sonucunu ve hazırlanan maili inceleyin; Outlook/gelen kutusu kabulü ayrıdır.");
        OperationStatusRow archive = report.ReportState switch
        {
            "ReadableWriteNotTested" => new OperationStatusRow("Rapor arşivi", "Kısmen doğrulandı", "Okuma doğrulandı. Yazma kontrolü yapılmadı.", "Yetkili rapor oluşturma kabulünde yazmayı ve aynı baytlarla yeniden indirmeyi doğrulayın."),
            "NotConfigured" or "DirectoryMissing" => new("Rapor arşivi", "Yapılandırma eksik", "Arşiv yolu tanımlı değil veya dizin bulunamadı.", "Etkin arşiv yolunu operatörle doğrulayın; otomatik dizin taşıma yapmayın."),
            "NotChecked" => new("Rapor arşivi", "Kontrol edilmedi", "Arşiv kontrol sonucu bulunmuyor.", "Durumu yeniden kontrol edin."),
            _ => new("Rapor arşivi", "Kontrol başarısız", "Arşiv okuma erişimi doğrulanamadı.", "Etkin API kimliği ve dizin erişimini operatörle inceleyin.")
        };
        List<OperationStatusRow> rows = [source, mailRow, worker, assets, archive];
        if (Setting("InUseCompletion:Enabled") is not null)
        {
            bool disabled = Flag("InUseCompletion:Enabled") == false || Setting("InUseCompletion:Provider") == "Disabled";
            rows.Add(new("In Use kaynak tamamlama", disabled ? "Kapalı" : "Kontrol edilmedi",
                disabled ? "Kaynak tamamlama bu API yapılandırmasında etkin değil." : "Etkin ayar, gerçek ek yükleme veya OR kapanış kanıtı değildir.",
                "Gerçek adapter ve kaynak sözleşmesi kabulünü tamamlayın; yalnız bayrak açmak yeterli değildir."));
        }
        return rows;
    }
}
