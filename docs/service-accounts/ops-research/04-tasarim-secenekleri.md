# 4. Tasarım seçenekleri ve öneri

Güncelleme 2026-10-06: sahip parola değişimini PAM'ın yükümlülüğü olarak belirledi (M3 kapandı, M4 geçerli). Okuma ve
gMSA geçişi onaya hazır taslak olarak yazıldı: ADR-0024 revizyon 2 ve ADR-0028; Bilgi Güvenliği'ne paket:
[06-onay-paketi.md](06-onay-paketi.md).

Durum: **öneri**. Hiçbiri kabul edilmiş karar değildir; yazma içeren her seçenek yeni ADR, kurum onayı ve Faz 8 için
proje sahibi + yönetim kararı ister. Tahminler geliştirici iş günüdür (inceleme, Windows doğrulaması ve onay bekleme
süresi hariç) ve belirsizdir.

## Önerinin özü

1. **Önce bulmayı hızlandır ve ürüne al** (JEA ile salt okunur, Worker üzerinden). Aracın kullanıcıya verdiği değerin
   çoğu "hesap nerede çalışıyor"u hızlı ve eksiksiz görmek; bu, yazma olmadan yapılabilir.
2. **Parolayı ortadan kaldırarak değiştir:** ürün bir gün değişiklik yapacaksa ilk ve tek katalog **gMSA'ya geçiş**
   olsun. gMSA'da parola yok; hiçbir yere taşınmaz, kimse bilmez.
3. **Klasik parola döndürmeyi ürüne alma;** PAM ekibinin sürecinde kalsın, modül planı, referansı ve kanıtı tutsun.
4. Arada geçen sürede **manuel değişikliği rehberli yap:** modül bileşen bazında kontrol listesi üretir, kişi kendi
   yetkisiyle uygular, ardından gMSA kontrol taraması kanıt olur.

Neden bu sıra: risk en çok yazmada, değer en çok bulmada. Bulma onayı (ADR-0024) yazma onayından çok daha kolay ve ayrı
alınabilir; yazma gelmezse bile kazanç kalıcıdır.

## Karar 1 — Sunuculara nasıl ulaşılır

| Seçenek | Nasıl | Güvenlik | Onay | Tahmin | Öneri |
|---|---|---|---|---|---|
| **A. Kişi çalıştırır, yükler** (bugün, ADR-0027) | Toplayıcı her sunucuda, birleştir, yükle | Ürün bağlanmaz; kalite kişiye bağlı | Yok (mevcut) | — | **Yedek yol olarak kalsın** |
| **B. Worker → WinRM (Kerberos, HTTPS 5986) → JEA okuma uç noktası** (ADR-0024) | Hangfire işi, sınırlı paralellik, sunucu başına tek çağrı, sonuçlar tarama kaydı olarak | Tek görünür fonksiyon, `NoLanguage`, sanal hesapla çalışır, parola okumaz | Bilgi Güvenliği + Siber Güvenlik (JEA), sunucu sahipleri (pilot), Worker erişim yolu kararı, ADR-0024 kabulü | 10–15 gün + pilot | **Önerilen** |
| C. Worker → varsayılan uç nokta, kişinin kimliğiyle | Aracın yaptığı | Kısıtsız oturum; kural 3 istisnası | Kural 3 istisnası + yeni ADR | 5–8 gün | **Reddedilsin** — aracın ana riskini ürüne taşır |
| D. BeyondTrust aracılı oturum + JEA | Worker PAM'dan oturum alır | En güçlü iz; PAM yeteneğine bağlı | PAM ekibi, Bilgi Güvenliği | Bilinmiyor | B ile birlikte değerlendirilsin (açık karar) |

B'nin ayrıntısı (ADR-0024'ten, değişiklik önerileriyle):

- **Kimlik:** Worker'ın bağlandığı hesap bir **gMSA** olsun (parolası kimsede yok). Okuma ve yazma için ayrı hesap ve ayrı
  AD grubu; okuma hesabı ele geçse bile yazamaz.
- **Uç nokta:** `SecureOpsServiceAccountUsage` — tek fonksiyon `Get-SecureOpsAccountUsage`. Eklenmesi önerilen isteğe bağlı
  kaynaklar: COM+ kimliği, hizmet/toplu iş oturumu hakları (geçici dosyasız okuma yolu doğrulanmalı), gMSA ön koşul
  kontrolü (sunucu bu gMSA'nın parolasını alabilir mi; yöntem doğrulanmalı).
- **Çağrı:** betik metni yok; `AddCommand` + parametreler. Bir sunucuya tek çağrı, partideki tüm hesaplar (≤ 20) birlikte.
- **Sunucu listesi:** kişi verir (yapıştır / CSV) ya da kayıtlı "sunucu kümesi"nden; ITSM (RFS) ve vCenter kaynakları
  sözleşme gelene kadar **sahte bağdaştırıcı** (kural 8). vCenter'daki "adında SQL olanı at" gibi filtreler yok; her
  planlanan sunucu sonuçta görünür.

## Karar 2 — Değişikliği kim yapar

| Seçenek | Güvenlik | Onay gereksinimi | Tahmin | Öneri |
|---|---|---|---|---|
| **M1. İnsan yapar, modül yönlendirir** | Değişiklik bugünkü gibi kişinin yetkisinde; modül plan, kontrol listesi, OCO bağı, kanıt | Yok | 6–10 gün | **Hemen** |
| **M2. Ürün gMSA'ya geçişi uygular** (ayrı JEA yazma uç noktası) | Parola yok; dar katalog; iki kişi onayı; bileşen başına "beklenen mevcut kimlik" kontrolü | Yeni katalog ADR'si (ADR-0006 eki), Faz 8 ön koşulu istisnası (proje sahibi + yönetim), Bilgi Güvenliği + Siber Güvenlik, sunucu sahipleri, değişiklik kurulu (OCO) | 25–35 gün + pilot | **Orta vade, onaylanırsa** |
| M3. Ürün klasik parolayı döndürür | Parola üretilir, AD'de sıfırlanır ve **her sunucuya taşınır** (kaçınılmaz); en geniş saldırı yüzeyi | M2'nin hepsi + PAM ekibi + AD yetki devri (parola sıfırlama) | 30–45 gün + pilot | **Önerilmez** |
| M4. PAM ekibi döndürür, modül izler | Parola PAM'da kalır; bağımlı servis/görev güncellemesi PAM ürününün yeteneğine bağlı (doğrulanmalı) | PAM ekibi; modül tarafında onay gerekmez | Modül tarafı 3–5 gün | **Klasik hesaplar için önerilen** |

## Toplu işlem akışı (M1'de kayıt olarak, M2'de uygulamalı)

Her adım değişmez kayıt bırakır; bir adım geçmeden sonraki açılmaz.

| Adım | Ne olur | Kim | Kural |
|---|---|---|---|
| **1. Plan** | Hesaplar seçilir (kapsam içi olanlar), hedef: gMSA adı (031 alanı) veya "manuel parola değişimi"; son tarama(lar) bileşen listesini verir; eksik/erişilemeyen sunucu varsa plan uyarır | Work + sorumlu dayanak | Tarama yoksa veya kapsam eksikse plan "eksik" kalır, onaya gidemez |
| **2. Önizleme** | Bileşen bazında **şimdi → sonra** tablosu: sunucu, tür, ad, mevcut kimlik, hedef kimlik, yeniden başlatma gerekir mi, ön koşul (gMSA alınabilir mi), risk işareti (bakılamadı, desteklenmeyen tür: IIS "connect as", COM+) | Sistem | Önizleme bir sürüm numarası taşır; plan değişirse eski onay geçersiz (içe aktarmadaki desen) |
| **3. Onay** | OCO numarası zorunlu, bakım penceresi, gerekçe; **onaylayan ≠ isteyen** | Yeni yetenek (ör. `ServiceAccounts.ChangeApprove`) | Önizleme sürümüne bağlı; süresi dolar |
| **4. Uygulama** (M2) | Dalga dalga: 1 sunucu (kanarya) → küçük grup → kalan; sunucu başına tek çağrı, bileşenler sırayla; her bileşende **önce yeniden oku, beklenen mevcut kimlik değilse dokunma** | Worker, ayrı yazma kimliği | Hata oranı eşiği aşılırsa durur; yönetici acil durdurma; `Idempotency-Key` |
| **5. Doğrulama** | Otomatik gMSA kontrol taraması (`expectedAccount`) + servis/havuz durumu; sonuç kanıt olarak eklenir; **doğrulayan kişi** mevcut eylem doğrulamasını yapar | Sistem + Verify yetkisi | "Başarılı" yalnız yeniden okunan durumla; kişi doğrulamadan eylem doğrulanmış sayılmaz |
| **6. Geri dönüş** | Önizleme anındaki eski değerler saklıdır; gMSA'dan eski hesaba dönüş **eski parolayı gerektirir**, bu yüzden geri dönüş kişi tarafından PAM'daki parolayla yapılır; modül tam listeyi verir | Kişi | Eski hesap, gözlem süresi bitene kadar etkin ve parolası değişmeden kalır (kapatma ayrı ve sonraki eylem) |

M1'de 4. adım "kişi uyguladı" kaydıdır: modül bileşen bazında kontrol listesini gösterir, kişi işaretler, 5. adım aynıdır.

## Parolanın hiç dolaşmaması

- **gMSA:** parola AD'de, sunucu kendisi alır. Ürüne, Worker'a, ekrana, günlüğe hiçbir parola gelmez. Ürün yalnız kimliği
  `CONTOSO\gmsa_x$` olarak ve **boş parola** ile yazar. Ön koşullar AD ekibinin işidir (gMSA oluşturma, parolayı alabilecek
  sunucu grubu); modül bunun için istek metnini ve listeyi üretir, AD'ye yazmaz.
- **gMSA ile yapılamayanlar (doğrulanmalı):** IIS sanal dizin "connect as" kimliği parola ister — gMSA ile desteklenmediği
  biliniyor; doğrudan kimlik doğrulamaya (uygulama havuzu kimliği) geçiş gerekir. COM+ uygulama kimliğinde gMSA desteği
  belirsiz. İkisi de ilk katalogda yok; önizlemede "manuel" olarak işaretlenir.
- **Klasik parola (M3 seçilirse, önerilmez):** Worker bellekte üretir, yalnız `SecureString`; AD'de **sıfırlama** (eski
  parola gerekmez, ayrı OU'da devredilmiş yetki); JEA fonksiyonu parametreyi `SecureString` alır (JEA transkripti parametre
  değerlerini yazar; `SecureString` yalnız tür adı olarak görünür); komut satırına asla yazılmaz (`schtasks /rp` yok);
  saklanmaz, gösterilmez. Yine de her sunucuya gider — bu yüzden önerilmez.

## Denetim

- Ekleme-yalnız tablolar (öneri): `ChangePlans`, `ChangePlanItems` (önizleme anındaki eski/yeni değer), `ChangeApprovals`,
  `ChangeExecutions`, `ChangeStepResults` (sunucu, bileşen, önce, sonra, süre, sonuç türü — hata mesajı değil, ADR-0024'teki
  gibi yalnız tür), `ChangeAborts`. Her geçiş aynı işlemde modül geçmişi + `audit.AuditLog`.
- Denetim operasyonel kanıttır; kişi karşılaştırması, sıralama, "kim kaç değişiklik yaptı" ekranı yok (kural 5).
- Parola, gizli değer, ham hata metni hiçbir kayda girmez.

## Hız

| Konu | Öneri | Not |
|---|---|---|
| Okuma paralelliği | 32 eşzamanlı sunucu (ayar), açılış 15 sn, işlem 180 sn | ADR-0024 değerleri; pilotta ölçülmeli |
| Sunucu başına | Tek çağrı, tüm hesaplar birlikte, SID çevirisi yok, IIS XML bir kez | Aracın 2–4. yavaşlık nedeni yok |
| Erişilebilirlik | Ayrı TCP ön filtresi yok; bağlantı zaman aşımı yeterli. "Son denemede erişilemedi" yalnız ipucu, **sunucuyu dışarıda bırakmaz** | Aracın R8 riskini önler |
| İlerleme | Sunucu bitince sonuç hemen yazılır; arayüz sayaç ve sunucu listesini canlı gösterir | Tüm iş bitmeden kısmi sonuç görünür |
| Yazma paralelliği | Dalgalar (1 → 5 → en çok 8–16 eşzamanlı), bileşen başına 120 sn, eşik aşılırsa dur | Hız değil güvenlik öncelikli |
| Tahmini süre | 200 sunucu okuma ≈ birkaç dakika | **Tahmin**; ağ ve DC gecikmesine bağlı, ölçülmedi |

## Arayüzde yönlendirme

- **"Sıradaki adım" kartı** (hesap sayfası): durumdan hesaplanır — "tarama yok → tarama planla", "karar bekleyen 4 eşleşme",
  "gMSA adı istenmedi", "gerçekleşti, doğrulama bekliyor". Her kartta "neden" bir cümle ve tek düğme.
- **Değişiklik sihirbazı** (toplu): 6 adımlı ilerleme çubuğu; her adımda "ne olacak / ne olmayacak" kutusu; engelleyen
  ön koşullar nasıl giderileceğiyle birlikte listelenir.
- **Önizleme tablosu:** renk tek başına anlam taşımaz; "yeniden başlatılacak", "bakılamadı", "manuel" metin rozetleri.
- **Kısa sözlük ipuçları:** gMSA, OCO, kapsam, "bulunamadı ≠ kullanılmıyor".
- **Dürüst durumlar:** "uygulandı" yalnız yeniden okumayla; erişilemeyen sunucu "bilgi yok"; reddedilen istek nedeniyle.
- Mevcut kalite çıtası: klavye, 390 px, %200 yakınlaştırma, açık/koyu.

## Özet karşılaştırma

| Paket | Kazanç | Risk | Onay | Tahmin |
|---|---|---|---|---|
| A+ / M1 (rehberli manuel + toplu plan) | Orta: düzen, izlenebilirlik | Çok düşük | Yok | 10–16 gün |
| B (JEA okuma, Worker) | Yüksek: hız, ölçek, eksiksiz kapsam | Düşük | Bilgi Güv., Siber Güv., sunucu sahipleri, erişim yolu kararı | 10–15 gün + pilot |
| M2 (gMSA geçişini ürün uygular) | Yüksek: hız, hatasız uygulama, parola yok | Orta (yazma) | + Faz 8 istisnası, yönetim, değişiklik kurulu | 25–35 gün + pilot |
| M3 (klasik parola döndürme) | Orta | Yüksek | M2 + PAM + AD yetki devri | 30–45 gün |
| M4 (PAM döndürür) | Orta | Düşük (bizim tarafta) | PAM ekibi | 3–5 gün |
