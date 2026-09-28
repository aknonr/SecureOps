using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>Server-owned implemented actions and Turkish operational descriptions.</summary>
public static class AccessActionCatalog
{
    /// <summary>The catalog cannot be extended by role-edit input.</summary>
    public static IReadOnlyList<AccessActionDefinition> Actions { get; } =
    [
        new(Capabilities.AnnouncementDrafts, "OCO", "Taslak", "Kendi duyuru taslaklarını düzenler ve okur."),
        new(Capabilities.AnnouncementSource, "OCO", "Kaynak", "Kaynak toplama işi ister; öneriyi inceler ve uygular."),
        new(Capabilities.AnnouncementPrepare, "OCO", "Hazırlama", "Sürüme bağlı değişmez mail hazırlar; göndermez."),
        new(Capabilities.AnnouncementSelfTest, "OCO", "Kendime deneme", "Yalnız kendi kayıtlı Mail adresine SMTP denemesi ister."),
        new(Capabilities.AnnouncementSend, "OCO", "Duyuru gönderimi", "İncelenen alıcılara açık onayla SMTP gönderimi ister."),
        new(Capabilities.InUseView, "In Use", "Kayıtları gör", "Kayıtlı sunucu incelemelerini okur."),
        new(Capabilities.InUseReview, "In Use", "İncele ve raporla", "Atamadan bağımsız yerel cevapları kaydeder ve rapor hazırlar."),
        new(Capabilities.InUseAssign, "In Use", "İnceleyici ata", "Doğrulanmış uygulama kullanıcısını isteğe bağlı atar."),
        new(Capabilities.InUseRefresh, "In Use", "Kaynağı yenile", "Kurumsal kaynaktan açık okuma başlatır; kaynak yazmaz."),
        new(Capabilities.InUseComplete, "In Use", "Talebe ekle ve tamamla", "Ayrı kaynak yazma izni ve doğrulanmış sözleşmeyle arşivlenmiş raporu ekler, görevi tamamlar ve OR son durumunu okur. Yerel inceleme izni bunu vermez."),
        new(Capabilities.ResourcesView, "Bağlantılar", "Bağlantıları kullan", "İzinli bağlantıları ve kişisel grupları kullanır."),
        new(Capabilities.ResourcesManage, "Bağlantılar", "Katalog yönet", "Paylaşılan kategori ve bağlantıları düzenler."),
        new(Capabilities.IdentityLookup, "Kimlik", "Hesap sorgula", "Sınırlı tam hesap sorgusu yapar."),
        new(Capabilities.DirectoryGroupsView, "Kimlik", "Grup sorgula", "Tam grup ve doğrudan üyelikleri okur."),
        new(Capabilities.DirectoryGroupMembersView, "Kimlik", "Grup üyelerini gör", "Tek grubun sınırlı doğrudan üyelerini okur."),
        new(Capabilities.DirectoryPrivilegedGroupsView, "Kimlik", "Ayrıcalıklı grupları gör", "Onaylı ayrıcalıklı grup kanıtını okur."),
        new(Capabilities.DirectoryGroupExport, "Kimlik", "Grup raporu indir", "Sınırlı üyelik raporu hazırlar."),
        new(Capabilities.SystemDiagnostics, "Sistem", "Sağlık bilgisi gör", "Yetkili sağlık ve tanı bilgilerini okur."),
        new(Capabilities.OperationalRecordsView, "OR / SDM", "Kayıtları gör", "Kayıtlı operasyonel kayıtları okur."),
        new(Capabilities.OperationalRecordsCreateJiraPreview, "OR / SDM", "Jira önizleme", "Sunucu politikasına bağlı taslak oluşturur; yayınlamaz."),
        new(Capabilities.OperationalRecordsCreateJira, "OR / SDM", "Jira yayınla", "Ayrı açık onay ve etkin kurumsal politika gerektirir."),
        new(Capabilities.OperationalRecordsRetry, "OR / SDM", "Kurtarma işlemi", "Yalnız desteklenen kurtarma komutunu ister; belirsiz create tekrarlanmaz."),
        new(Capabilities.OperationalRecordsViewDiagnostics, "OR / SDM", "Kaynak kanıtı gör", "Yetkili teknik kaynak kanıtını okur."),
        new(Capabilities.AccessManageUsers, "Erişim", "Kullanıcıları yönet", "Kullanıcıları listeler ve erişimi kapatır."),
        new(Capabilities.AccessApproveRequests, "Erişim", "Talepleri kararlaştır", "Başkasının erişim talebini onaylar veya reddeder."),
        new(Capabilities.AccessAssignRoles, "Erişim", "Rol ata", "Sürüm kontrollü rol atar; rol tanımı için kullanıcı yönetimi de gerekir."),
        new(Capabilities.ManagementReportingView, "Raporlar", "Yönetim raporlarını gör", "Yetkili toplu operasyon raporlarını okur."),
        .. SecureOps.Infrastructure.ServiceAccounts.ServiceAccountAccessActions.Actions,
        new(Capabilities.TeamView, "Tarihsel", "Takım", "Tarihsel hak; bağımsız uygulanmış yeni eylem değil.", false),
        new(Capabilities.AuditView, "Tarihsel", "Denetim", "Tarihsel hak; bağımsız uygulanmış yeni eylem değil.", false),
        new(Capabilities.AccessAdministration, "Tarihsel", "Erişim yönetimi", "Tarihsel hak; dar eylemler kullanılır.", false),
        new(Capabilities.AccessViewAudit, "Tarihsel", "Erişim denetimi", "Tarihsel hak; bağımsız uygulanmış yeni eylem değil.", false)
    ];
}
