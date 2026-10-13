# Servis Hesapları — Ekip rolleri ve kapsam kurulumu (ürün içinden)

Tarih: 2026-10-04. Kime: Servis Hesapları modülünü ekiplere açacak yönetici.
Her şey **uygulamanın kendi ekranlarından** yapılır. SQL ile rol, rol ataması veya kapsam yazılmaz
(AGENTS.md, ADR-0010/0022, ADR-0026). Aşağıdaki kişi adları örnektir (`syn.*`), gerçek ad yazmayın.

## İki ayrı katman

| Katman | Ne belirler | Nereden verilir | Kim verir |
|---|---|---|---|
| **Yetki** (rol paketi) | Hangi tür işi yapabilir (gör, iş gir, doğrula, aktar, rapor, yönet) | Yönetim → Rol tanımları (`/access/roles`), Kullanıcılar (`/access/users`) | Kullanıcı yönetimi + rol atama yetkisi olan yönetici |
| **Kapsam** (`svcacct.ScopeGrants`) | Hangi ekibin/kurumun hesaplarını görür | Servis Hesapları → Modül yönetimi → Kapsam yetkileri | `ServiceAccounts.Administer` sahibi, **kendisi dışındaki** kişiye |

Yetki tek başına hesap göstermez; kapsam tek başına işlem açmaz. İkisi birlikte gerekir.

## 1. Rol paketlerini oluşturun (bir kez)

Yönetim → **Rol tanımları** → **Yeni rol**. Her rol için kod, ad, amaç yazılır; "Servis Hesapları" grubundaki
işlemler işaretlenir; **Etkiyi incele** ile sunucunun hesapladığı etki görülür; ardından **Değişikliği uygula**.

| Kod | Ad (öneri) | İşlemler | Amaç (öneri) |
|---|---|---|---|
| `sa-ekip-uyesi` | Servis hesabı — ekip üyesi | View, Work | Kendi ekibinin hesaplarında iş kaydı girer (talep/plan, işlem bildirimi, yazışma, bulgu, kanıt). |
| `sa-koordinator` | Servis hesabı — koordinatör | View, Work, Assign, Verify, Import, Report | Listeleri içe aktarır, sahiplik/devir kararı verir, işlemleri doğrular, rapor alır. |
| `sa-yonetici` | Servis hesabı — modül yöneticisi | View, Administer | Ekip/kurum sözlüğünü ve kapsam yetkilerini yönetir; iş sonucunu değiştirmez. |

Notlar:
- Kod yalnız harf, rakam, `-` ve `_` içerebilir (en çok 64 karakter). Sürüm sıfırsa yeni rol oluşur.
- Korumalı çekirdek **Admin** rolü burada değiştirilemez; 028 ile yedi modül işleminin hepsine zaten sahiptir.
- Kendi sahip olduğunuz bir role, sizde olmayan bir işlem ekleyemezsiniz (`selfEscalation`). Bu bir koruma.

## 2. Kişilere rol atayın

1. Kişi uygulamaya bir kez giriş yapar. Erişimi henüz onaylı değilse talebi Yönetim → **Erişim Talepleri** ekranında
   onaylanır (kendi talebinizi siz onaylayamazsınız). Kapsam listesinde yalnız onaylı kullanıcılar görünür.
2. Yönetim → **Kullanıcılar** → kişi → **Rolleri değiştir** → ilgili `sa-*` rolü seçilir → etki incelenir → uygulanır.
3. Kişi **Erişimim** (`/access/me`) sayfasında yeni işlemleri görür.

## 3. Kapsam verin

Servis Hesapları → **Modül yönetimi** → **Kapsam yetkileri**. Kişi, onaylı kullanıcı listesinden seçilir; elle yazılmaz.

| Rol | Önerilen kapsam | Neden |
|---|---|---|
| `sa-ekip-uyesi` | **Ekip** (kendi ekibi) | Yalnız kendi ekibinin hesaplarını ve ona yönelen talepleri görür. |
| `sa-koordinator` | **Kurum** veya **Tüm kurum** | İçe aktarım kurum düzeyinde kapsam ister; ekip kapsamı yetmez. |
| `sa-yonetici` | Gerekirse **Tüm kurum** | Kapsam vermek için kendi kapsamı gerekmez (yalnız Administer); hesapları görmek için gerekir. |

- İlk kurulum: modülde hiç kapsam yokken bir modül yöneticisi kendine **bir kez** "Tüm kurum" alır (ADR-0026,
  migration 029). Sonraki her kapsamı **başka** bir yönetici verir.
- Ekip ve kurumlar ilk içe aktarımla oluşur; gerekirse Modül yönetimi sözlüğünden elle de eklenir. Ekip kapsamı ancak ekip kaydı varken verilebilir.
- Verilen ve geri alınan her kapsam denetim kaydına ve modül geçmişine yazılır; satırlar silinmez.

## 4. Kontrol

| Kişi | Beklenen |
|---|---|
| `syn.ekip-uyesi` | Liste yalnız kendi ekibinin hesapları; iş kaydı girebilir; içe aktarım ve rapor sekmeleri görünmez. |
| `syn.koordinator` | İçe aktarım açılır (kurum kapsamı); rapor alabilir; doğrulama yapabilir. |
| `syn.yonetici` | Modül yönetimi açılır; kendine kapsam veremez (`selfGrant`). |
| Kapsamı olmayan kişi | Boş liste yerine "Önce veri kapsamınız tanımlanmalı" açıklaması. |

Bu sayfa SQL komutu, rol ataması veya gerçek kişi adı içermez. Windows/TEST'te yürütülme sonucu
`WINDOWS-ACCEPTANCE.md` içinde kaydedilir.
