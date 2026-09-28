using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.Import;

/// <summary>
/// Evidenced source headers mapped to canonical fields. Matching uses the exact label key, so column
/// order never changes meaning. Legacy helper/formula columns are listed separately and never imported.
/// </summary>
public static class ImportHeaders
{
    /// <summary>Field value types.</summary>
    public enum ValueKind
    {
        /// <summary>Free text.</summary>
        Text,
        /// <summary>Business date (date-only).</summary>
        Date,
        /// <summary>Naive source timestamp (no zone).</summary>
        Timestamp
    }

    /// <summary>Coordination list headers (evidenced, seven columns).</summary>
    public static IReadOnlyDictionary<string, string> CoordinationList { get; } = Map(new()
    {
        ["Kullanıcı Adı"] = StagedFields.Account,
        ["Son Parola Değişiklik Zamanı"] = StagedFields.PasswordLastSet,
        ["AD veya LDAP Son Oturum Açma Zamanı"] = StagedFields.LastLogonAdOrLdap,
        ["AD Son Oturum Açma Zamanı"] = StagedFields.LastLogonAd,
        ["Organizasyon"] = StagedFields.Organization,
        ["Grup Direktorlugu"] = StagedFields.GroupDirectorate,
        ["Yorum"] = StagedFields.Comment
    });

    /// <summary>DBA handover headers (evidenced, four columns).</summary>
    public static IReadOnlyDictionary<string, string> DbaHandover { get; } = Map(new()
    {
        ["Kullanıcı Adı"] = StagedFields.Account,
        ["Ekip"] = StagedFields.SourceTeam,
        ["Kullanan_Ekip"] = StagedFields.ConsumerTeam,
        ["WASAS_Devir"] = StagedFields.HandoverFlag
    });

    /// <summary>Legacy workbook/package input sheets → (entity kind, header map).</summary>
    public static IReadOnlyDictionary<string, (string Kind, IReadOnlyDictionary<string, string> Headers)> Legacy { get; } =
        new Dictionary<string, (string, IReadOnlyDictionary<string, string>)>(StringComparer.Ordinal)
        {
            ["Hesap_Bilgileri"] = (StagedKinds.Account, Map(new()
            {
                ["Servis Hesabı"] = StagedFields.Account,
                ["Sorumlu Ekip"] = StagedFields.OwnerTeam,
                ["Sorumlu Kişi"] = StagedFields.OwnerPerson,
                ["Rapor Organizasyonu"] = StagedFields.Organization,
                ["Turuncu Hat OR No"] = StagedFields.Or,
                ["OCO No"] = StagedFields.Oco,
                ["Açıklama"] = StagedFields.Notes,
                ["Domain / SID"] = StagedFields.Domain,
                ["Kullanan Ekip"] = StagedFields.ConsumerTeam,
                ["Hesap ID"] = StagedFields.LegacyId
            })),
            ["Talep_Takibi"] = (StagedKinds.Request, Map(new()
            {
                ["Servis Hesabı"] = StagedFields.Account,
                ["Muhatap Ekip"] = StagedFields.TargetTeam,
                ["Takip Sorumlusu"] = StagedFields.FollowupPerson,
                ["Beklenen Aksiyon"] = StagedFields.ActionType,
                ["Durum"] = StagedFields.Status,
                ["Sonraki Takip"] = StagedFields.NextFollowup,
                ["Turuncu Hat OR No"] = StagedFields.Or,
                ["Konu / Not"] = StagedFields.Notes,
                ["İlk Gönderim Tarihi"] = StagedFields.FirstSent,
                ["Son Yanıt Tarihi"] = StagedFields.LastReply,
                ["Plan Başlangıcı"] = StagedFields.PlanStart,
                ["Plan Bitişi"] = StagedFields.PlanEnd,
                ["Plan Bildirim Tarihi"] = StagedFields.PlanAnnounced,
                ["Bildiren / İrtibat Kişisi"] = StagedFields.ContactPerson,
                ["Jira / Diğer Kayıt"] = StagedFields.OtherRecord,
                ["Kayıt No"] = StagedFields.LegacyId
            })),
            ["Islem_Gecmisi"] = (StagedKinds.Action, Map(new()
            {
                ["Servis Hesabı"] = StagedFields.Account,
                ["İşlem Tarihi"] = StagedFields.ActualDate,
                ["İşlem Türü"] = StagedFields.ActionType,
                ["Sonuç"] = StagedFields.Result,
                ["İşlemi Yapan Ekip"] = StagedFields.PerformerTeam,
                ["İşlemi Yapan Kişi"] = StagedFields.PerformerPerson,
                ["Turuncu Hat OR No"] = StagedFields.Or,
                ["OCO No"] = StagedFields.Oco,
                ["Kayıt Türü"] = StagedFields.RecordKind,
                ["Kanıt / Açıklama"] = StagedFields.Evidence,
                ["Doğrulama Tarihi"] = StagedFields.VerifiedDate,
                ["Doğrulayan"] = StagedFields.Verifier,
                ["Kaynak Notu"] = StagedFields.SourceNote,
                ["Kayıt No"] = StagedFields.LegacyId
            })),
            ["Mail_Gunlugu"] = (StagedKinds.Communication, Map(new()
            {
                ["Servis Hesabı"] = StagedFields.Account,
                ["Mail Tarihi"] = StagedFields.OccurredOn,
                ["Yön"] = StagedFields.Direction,
                ["Mail Türü"] = StagedFields.CommunicationKind,
                ["Muhatap Ekip"] = StagedFields.ContactTeam,
                ["Mail Konusu"] = StagedFields.Subject,
                ["Kısa Açıklama"] = StagedFields.Summary,
                ["Turuncu Hat OR No"] = StagedFields.Or,
                ["Kaydı Giren"] = StagedFields.EnteredBy,
                ["Anlamlı Yanıt"] = StagedFields.MeaningfulReply,
                ["Mail Bağlantısı"] = StagedFields.Link,
                ["Kayıt Kapsamı"] = StagedFields.RecordScope,
                ["Kayıt No"] = StagedFields.LegacyId
            })),
            ["Teknik_Bulgular"] = (StagedKinds.Finding, Map(new()
            {
                ["Servis Hesabı (Giriş)"] = StagedFields.Account,
                ["Sunucu"] = StagedFields.Server,
                ["Bileşen Türü"] = StagedFields.ComponentType,
                ["Bileşen Adı"] = StagedFields.ComponentName,
                ["Ortam"] = StagedFields.Environment,
                ["Tarama Zamanı"] = StagedFields.ScanAt,
                ["Tarama Durumu"] = StagedFields.ScanResult,
                ["Eşleşme"] = StagedFields.MatchResult,
                ["Kanıt Bağlantısı"] = StagedFields.Evidence,
                ["Kapsam / Log Penceresi"] = StagedFields.Coverage,
                ["Sorumlu Ekip"] = StagedFields.OwningTeam,
                ["Job ID"] = StagedFields.JobReference,
                ["Açıklama"] = StagedFields.Notes,
                ["Bulgu Durumu"] = StagedFields.FindingStatus,
                ["Bulgu ID"] = StagedFields.LegacyId
            })),
            ["DBA_Devir"] = (StagedKinds.Handover, Map(new()
            {
                ["Servis Hesabı"] = StagedFields.Account,
                ["Kaynak Ekip"] = StagedFields.SourceTeam,
                ["Kullanan Ekip"] = StagedFields.ConsumerTeam,
                ["Hedef Ekip"] = StagedFields.TargetTeam,
                ["Devir Durumu"] = StagedFields.HandoverStatus,
                ["Bildirim Tarihi"] = StagedFields.ProposedOn,
                ["Kabul Tarihi (Giriş)"] = StagedFields.AcceptedOn,
                ["Hedef Aksiyon"] = StagedFields.TargetAction,
                ["Plan / Uygunluk Notu"] = StagedFields.SuitabilityNote,
                ["Kaynak"] = StagedFields.SourceNote
            }))
        };

    /// <summary>Legacy archive sheets holding the original source rows (observations).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LegacyArchives { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Kaynak_Orijinal"] = CoordinationList,
            ["Kaynak_DBA"] = DbaHandover
        };

    /// <summary>Canonical field value kinds.</summary>
    public static ValueKind KindOf(string field) => field switch
    {
        StagedFields.NextFollowup or StagedFields.FirstSent or StagedFields.LastReply or StagedFields.PlanStart or StagedFields.PlanEnd
            or StagedFields.PlanAnnounced or StagedFields.ActualDate or StagedFields.VerifiedDate or StagedFields.OccurredOn
            or StagedFields.ProposedOn or StagedFields.AcceptedOn => ValueKind.Date,
        StagedFields.PasswordLastSet or StagedFields.LastLogonAdOrLdap or StagedFields.LastLogonAd or StagedFields.ScanAt => ValueKind.Timestamp,
        _ => ValueKind.Text
    };

    /// <summary>Label key → field lookup for a header map.</summary>
    public static string? Field(IReadOnlyDictionary<string, string> map, string? header) =>
        ServiceAccountText.LabelKey(header) is { } key && map.TryGetValue(key, out string? field) ? field : null;

    private static Dictionary<string, string> Map(Dictionary<string, string> labels) =>
        labels.ToDictionary(pair => ServiceAccountText.LabelKey(pair.Key)!, pair => pair.Value, StringComparer.Ordinal);
}
