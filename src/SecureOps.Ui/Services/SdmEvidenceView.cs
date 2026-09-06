using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Ui.Services;

/// <summary>Operator guidance for stable SDM evidence codes, without deriving eligibility.</summary>
public static class SdmEvidenceView
{
    /// <summary>Explains the evidence or decision needed to resolve a condition.</summary>
    public static string Guidance(string code) => code switch
    {
        "CategoryPolicyPending" => "SDM kategori politikası onaylanmadı. Süreç sahibinin hangi kategorilerin yayımlanabileceğini belirlemesi gerekiyor.",
        "CategoryUnknown" or "CategoryUnsupported" => "Kategori doğrulanamadı veya kapsam dışında. Kaynak kategorisi ve onaylı politika birlikte incelenmeli.",
        "GroupUnproven" or "GroupOutOfScope" => "İlgili grup kapsamı doğrulanmadı. Kayıt bazında yetkili grup kanıtı gerekiyor.",
        "DccUnproven" or "ExcludedDcc" => "DCC kapsamı doğrulanmadı veya hariç tutulmuş. Süreç sahibi kayıt kapsamını doğrulamalı.",
        "RequesterMissing" or "RequesterUnresolved" or "RequesterAmbiguous" => "Talep eden kişi tek bir hesapla eşleştirilemedi. Kaynak bilgisi ve onaylı tam hesap eşlemesi doğrulanmalı.",
        "ReporterUnresolved" => "Raporlayıcı eşlemesi doğrulanmadı. Yetkili kullanıcının onaylı hesap eşlemesi gerekiyor.",
        "ApprovalRequired" => "Yayımlama onayı yok. Onay yetkisi ve süreci henüz tanımlanmadığından bu ekrandan onay verilemez.",
        "ExternalWritesDisabled" => "Bu ortamda dış sistemlere yazma kapalı. Ayrı ortam aktivasyon onayı gerekiyor.",
        "SourceChanged" or "EvaluationStale" => "Kaynak değişti veya değerlendirme güncel değil. Güncel kaydı inceleyin; yenileme yayımlama onayı vermez.",
        "ReconciliationRequired" => "Önceki yayımlamanın sonucu belirsiz. Destek referansıyla mutabakat isteyin; yeni kayıt oluşturmayın.",
        "AlreadyTransferred" => "Bu kayıt daha önce aktarıldı. Mevcut Jira kaydı incelenmeli.",
        "InvalidSourceId" or "InvalidOrCode" or "InvalidTitle" or "InvalidDescription" => "Kaynak kayıt alanları geçerli değil. Eksik veya hatalı bilgiyi kaynak sistemin yetkili ekibine bildirin.",
        "InactiveSource" => "Kaynak kayıt etkin değil. Güncel kaynak durumunu yetkili ekiple doğrulayın.",
        "SyntheticIdentifier" or "UnsupportedProvider" => "Bu kaynak gerçek SDM yayımlaması için desteklenmiyor.",
        "ContradictoryEvidence" => "Kanıtlar birbiriyle çelişiyor. Destek referansıyla kayıt incelemesi isteyin.",
        "InfrastructureReferenceMissing" => "Sunucu veya IP kanıtı yok. Kaynak kaydın yapılandırılmış altyapı bilgisi gerekiyor; metinden çıkarım yapılmaz.",
        "InfrastructureReferencePresent" => "Altyapı referansı mevcut. Bu destekleyici bilgi tek başına yayımlama izni değildir.",
        "CategorySupported" => "Kategori tanınıyor; bu bilgi onaylı SDM politikası yerine geçmez.",
        "EvaluationCurrent" => "Değerlendirme mevcut kaynakla uyumlu. Yayımlama koşulları ayrıca sağlanmalı.",
        "ApprovalGranted" => "Değerlendirmede onay kanıtı bulunuyor; yayımlama yetkisi ayrıca doğrulanır.",
        _ => "Bu koşul için ek inceleme gerekiyor. Destek referansıyla platform yöneticisine başvurun."
    };

    /// <summary>Whether a displayed source snapshot still matches a locally held preview.</summary>
    public static bool SameSource(OperationalRecordResponse before, OperationalRecordResponse after) =>
        before.Id == after.Id && before.SourceRecordId == after.SourceRecordId && before.OrCode == after.OrCode
        && before.Title == after.Title && before.Description == after.Description && before.Requester == after.Requester
        && before.CreatedAt == after.CreatedAt && before.Environment == after.Environment
        && before.ServerReference == after.ServerReference && before.ApplicationReference == after.ApplicationReference
        && before.SourceChanged == after.SourceChanged && before.EvaluationStale == after.EvaluationStale;
}
