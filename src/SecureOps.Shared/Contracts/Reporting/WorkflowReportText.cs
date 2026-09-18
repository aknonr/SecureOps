namespace SecureOps.Shared.Contracts.Reporting;

/// <summary>Shared Turkish report vocabulary for screen and Excel; codes remain stable API identities.</summary>
public static class WorkflowReportText
{
    /// <summary>Operator-facing status, never promoting acknowledgement to closure or delivery.</summary>
    public static string State(string value) => value switch
    {
        "Accepted" => "SMTP kabul etti",
        "Unknown" => "Sonuç belirsiz",
        "Failed" => "Başarısız",
        "Partial" => "Kısmi sonuç",
        "VerifiedClosed" => "OR kapanışı doğrulandı",
        "Acknowledged" => "İş akışı yanıtı",
        "Verified" => "Kanıt doğrulandı",
        "Archived" => "Arşivlendi",
        "Prepared" => "Hazırlandı",
        "Draft" => "Taslak",
        "Unreviewed" => "İncelenmedi",
        "Stale" => "Kaynak değişti",
        "Reviewed" => "Cevaplandı",
        "Queued" => "Kuyrukta",
        "Running" or "Dispatching" => "İşleniyor",
        "Succeeded" or "Completed" => "Yerel işlem tamamlandı",
        "Unconfirmed" => "Son durum doğrulanmadı",
        "Blocked" => "Engelli",
        "Denied" => "Yetki reddi",
        "JiraCreateFailed" => "Jira oluşturulamadı",
        "OperationalRecordCloseFailed" => "Kaynak tamamlanamadı",
        "Imported" => "Alındı",
        "Classified" => "Sınıflandırıldı",
        "NeedsManualReview" => "İnceleme gerekli",
        "Eligible" => "Uygun",
        "Previewed" => "Önizlendi",
        "CreateRequested" => "Aktarım istendi",
        "CreatingJira" => "Jira oluşturuluyor",
        "JiraCreated" => "Jira bağlantısı kayıtlı",
        "ClosingOperationalRecord" => "Kaynak sonucu bekleniyor",
        "Observed" => "Gözlem kayıtlı",
        "Incomplete" => "Eksik kaynak kanıtı",
        "Ready" => "Worker kuyruğu hazır",
        "NoWorker" => "Güncel Worker yok",
        "WrongQueue" => "Worker kuyruğu farklı",
        "Disabled" => "Kapalı",
        "ConfigurationMissing" => "Yapılandırma eksik",
        _ => "Durum eşlemesi eksik"
    };
    /// <summary>Exact classification names are not interchangeable mappings.</summary>
    public static string Type(string value) => value switch
    {
        "InUse" => "In Use",
        "ServerRequest" => "Sunucu talebi",
        "SoftwareInstallation" => "Yazılım kurulumu",
        "ServerRetirement" => "Sunucu emeklilik talebi",
        "EnvironmentRequest" => "Ortam talebi",
        "ConfigurationRequest" => "Yapılandırma talebi",
        "OperationalSupport" => "Operasyon desteği",
        "NeedsManualReview" => "Manuel inceleme",
        "NotJiraEligible" => "Jira için uygun değil",
        "Source" => "Kaynak işi",
        "Preparation" => "Duyuru hazırlığı",
        "SelfTest" => "Kendime deneme",
        "Send" => "Dağıtım",
        _ => "Tür eşlemesi eksik"
    };
    /// <summary>Timestamp definition, with current backlog explicitly separate.</summary>
    public static string Basis(string value) => value switch
    {
        "Current" => "Kesit anındaki durum",
        "PreparedAt" => "Hazırlanma",
        "EvidenceAt" or "ObservedAt" => "Sonuç gözlemi",
        "JiraCreatedAt" => "Jira bağlantısının kaydı",
        "SubmittedAt" => "Kaynak işinin başlatılması",
        "CreatedAt" => "Gönderim isteğinin kaydı",
        _ => "Bilinmiyor"
    };
    /// <summary>Unknown historical facts remain explicit limitations, not measured zeros.</summary>
    public static string Limitation(string code) => code switch
    {
        "CurrentStateNotHistoricalBacklog" => "Güncel durum, geçmiş dönemin iş yükünü yeniden oluşturmaz.",
        "ArchiveReceiptsMayNotCoverOldEnvelopes" => "Eski arşiv zarfları henüz doğrulanmış makbuz listesinde olmayabilir.",
        "UnknownOriginExcludedUnlessDemo" => "Sentetik ve kökeni bilinmeyen kayıtlar normal rapordan çıkarılır.",
        "OcoOwnerScopeOnly" => "OCO kapsamı yalnız kendi kayıtlarınızdır; ekip toplamı değildir.",
        "NoInboxDeliveryEvidence" => "SMTP kabulü gelen kutusuna teslim edildiğini kanıtlamaz.",
        "NoHistoricalCompletenessClaim" => "Kayıtlı geçmişin eksiksizliği doğrulanmadı; eksik zamanlardan süre türetilmez.",
        _ => "Ek geçmiş sınırlaması var; yöneticinin rapor sözleşmesini incelemesi gerekir."
    };
}
