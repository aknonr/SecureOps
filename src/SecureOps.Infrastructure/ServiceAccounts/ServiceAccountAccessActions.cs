using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>
/// Module actions registered (additively) in the server-owned access action catalog so administrators can
/// bundle them into versioned roles. No existing role receives them and no role is seeded.
/// </summary>
public static class ServiceAccountAccessActions
{
    private const string _group = "Servis Hesapları";

    /// <summary>Assignable module actions with Turkish descriptions.</summary>
    public static IReadOnlyList<AccessActionDefinition> Actions { get; } =
    [
        new(ServiceAccountCapabilities.View, _group, "Hesapları gör", "Kapsam yetkisi verilen ekip/organizasyonun servis hesaplarını ve işlerini okur."),
        new(ServiceAccountCapabilities.Work, _group, "İş kaydı gir", "Talep/plan, işlem bildirimi, yazışma, bulgu ve kanıt ekler; başka ekibin sahipliğini değiştiremez."),
        new(ServiceAccountCapabilities.Assign, _group, "Sahiplik ve devir kararı", "Kapsamı içinde sahiplik atar/onaylar ve devir kabul/ret kararı verir."),
        new(ServiceAccountCapabilities.Verify, _group, "İşlemi doğrula", "Bildirilen işlemi tarih, doğrulayan ve kanıtla doğrular; ayrı işlem oluşturmaz."),
        new(ServiceAccountCapabilities.Import, _group, "Liste içe aktar", "Kaynak listeyi önizler, karar verir ve onaylı aktarır; organizasyon kapsamı gerekir."),
        new(ServiceAccountCapabilities.Report, _group, "Rapor ve dışa aktarım", "Kapsamındaki haftalık/yönetici raporunu, değişmez nüshaları ve Excel/PDF çıktısını alır."),
        new(ServiceAccountCapabilities.Administer, _group, "Modül yönetimi", "Ekip/organizasyon sözlüğü ve kapsam yetkilerini yönetir; iş sonucunu değiştiremez.")
    ];
}
