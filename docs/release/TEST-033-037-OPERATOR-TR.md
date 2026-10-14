# TEST 033–037 operatör listesi

Tarih: 2026-10-14 (D3). Kurulu TEST'i 032'den 037'ye çıkarıyoruz, sonra API ve UI'ı aynı master'dan birlikte yayınlıyoruz.
Paket **gözden geçirme adayıdır**: `readyForInstallation=false`. Kurmak benim ve DBA'nın ayrı kararı; bu liste o karardan
sonra kullanılır. Paketi hazırlayan oturum TEST'e, IIS'e veya kurumsal SQL'e bağlanmadı. Yerel LocalDB provası hedef kabulü değildir.

Taban: 2026-10-13 kurulumu. 025–032 uygulanmış, 033 ve sonrası yok. API+UI `b64068a` (Negotiate 8.0.30, CVE-2026-70179'dan
etkilenen sürüm). Yeni paket Negotiate **8.0.32** taşır (`candidate.json.negotiate`). Eski `review-033-...-SONRA-KURULACAK` ve
`review-034-...-SONRA-KURULACAK` klasörleri bayattır, kullanmıyoruz.

## 0. SSMS nasıl kullanılır

TEST'te `sqlcmd` yok ve SSMS `:r` göreli yollarını çözmedi. Bu yüzden her adım `DBA/ssms/` altında **tek dosya**:
kaynak satırların birebir kopyası, `:r` ile çağrılan dosyalar yerine gömülü. Kimlikler `DBA/ssms/ssms-scripts.json` içinde.

- Dosyayı SSMS'te aç, doğru veritabanını seç, **Sorgu → SQLCMD Modu**'nu aç, tümünü çalıştır (F5).
- İlk satırlardaki `:on error exit` ilk hatada betiği durdurur (`sqlcmd -b` karşılığı); `SET QUOTED_IDENTIFIER ON` `-I` karşılığı.
- SQLCMD Modu kapalıysa betik "SQLCMD Modu kapali: hicbir sey calistirilmadi" der ve `SET NOEXEC ON` ile hiçbir şey çalışmaz.
  Modu açıp aynı pencerede yeniden çalıştır (ilk satır `NOEXEC`'i geri kapatır).
- Mesajlar sekmesinde kırmızı satır varsa **dur**. Devam etmeden önce çıktıyı change kaydına ekle.
- Dosyayı düzenleme. Hash `ssms-scripts.json` ile tutmalı. Repo karşılıkları `DBA/sql/` (sqlcmd için) ve `DBA/checks/`.

## 1. Ön kontrol (salt okunur)

1. `00-envanter-025-032.sql`: 025–032 hepsi **uygulanmış**, "DUR" satırı yok.
2. `01-envanter-033-037.sql`: 033, 034, 035, 036, 037 hepsi **uygulanmamış** (`BulunanIsaret = 0`); `ApiRolu = var`.
   `WorkerRolu = var` bekleniyor. `EKSIK` ise 037'deki Worker izin dosyasını (`54-037-...`) çalıştırma, kaydet; açık soru.
   `KISMI` veya "zaten uygulanmış" görürsen dur, hiçbir şey çalıştırma.
3. Paket ve hash: `candidate.json.supportingFiles` ile `DBA/` dosyaları, `manifests/` ile ZIP'ler tutmalı.
   Özel `wasas.sql-upgrade-review.v1` kaydı paketin `sqlReview` kimliğinde; içindeki bayraklar hedefi görmüş sayılmaz.

## 2. Yedek

- DBA, 033'e başlamadan önce veritabanının **tam yedeğini** alır, geri yüklenebildiğini kendi sürecinde doğrular.
  Yedek adı/zamanı change kaydına yazılır. Yedek yoksa başlamıyoruz.
- API ve UI klasörlerinin (IIS fiziksel yolları) olduğu gibi kopyası alınır: `b64068a` çifti + o anki `appsettings*.json` ve
  `web.config`. Geri dönüş bu çiftle yapılır; biri olmadan diğeri dönmez.

## 3. SQL sırası (033 → 037)

Her adım: ön kontrol → uygula → izin adayı(ları) → son kontrol. Beklenen çıktı tutmazsa dur, sonraki adıma geçme.
İzin dosyaları rol üyesi atamaz; TEST'te API hesabı zaten db_owner, yine de PROD ile aynı olsun diye uyguluyoruz.

| Adım | Dosya (`DBA/ssms/`) | Beklenen |
|---|---|---|
| 033 ön | `11-033-on-kontrol.sql` | `OnKosul_025_030_031 = var`, `ApiRolu = var`, `033Tablosu = 0` |
| 033 | `12-033-uygula.sql`, `13-033-SA-006-API-permissions.sql` | Hata yok |
| 033 son | `19-033-son-kontrol.sql` | `Tablo = 7`, `Tetikleyici = 8`, `GuvenilmeyenKisit = 0`, `ApiIzni = 18`, `BeklenmeyenIzin = 0`, `PlanSatiri = 0` |
| 034 ön | `21-034-on-kontrol.sql` | `OnKosul_033 = var`, `KanitKisiti = eski`, `SahipTuru = 8` |
| 034 | `22-034-uygula.sql` (izin dosyası yok) | Hata yok |
| 034 son | `29-034-son-kontrol.sql` | `KanitKisiti = yeni`, `SahipTuru = 9`, `GuvenilmeyenKisit = 0`, `PlanKaniti = 0` |
| 035 ön | `31-035-on-kontrol.sql` | `OnKosul_033 = var`, `OnKosul_034 = var`, `OrTablolari = yok`, `ApiRolu = var` |
| 035 | `32-035-uygula.sql`, `33-035-SA-008-API-permissions.sql` | Hata yok |
| 035 son | `39-035-son-kontrol.sql` | `OrTablosu = 2`, `Tetikleyici = 2`, `FiltreliTekilIndeks = 2`, `GuvenilmeyenKisit = 0`, `ApiIzni_SelectInsert = 4`, `ApiIzni_SutunUpdate = 7`, `BaskaIzin = 0`, `GonderimSatiri = 0` |
| 036 ön | `41-036-on-kontrol.sql` | `OnKosul_025 = var`, `OlayTablolari = yok`, `ApiRolu = var` |
| 036 | `42-036-uygula.sql`, `43-036-SA-009-API-permissions.sql` | Hata yok |
| 036 son | `49-036-son-kontrol.sql` | `OlayTablosu = 2`, `EklemeYalnizTetikleyici = 2`, `GuvenilmeyenKisit = 0`, `MetinSutunu = 0`, `ApiIzni_SelectInsert = 4`, `BaskaIzin = 0`, `OlaySatiri = 0` |
| 037 ön | `51-037-on-kontrol.sql` | `OnKosul_030 = var`, `TaramaIsleri = yok` |
| 037 | `52-037-uygula.sql`, `53-037-SA-010-API-permissions.sql`, `54-037-SA-010-Worker-permissions.sql` | Hata yok |
| 037 son | `59-037-son-kontrol.sql` | `Tablo = 3`, `Tetikleyici = 3`, `TekCalisanDizin = 1`, `GuvenilmeyenKisit = 0`, `Is = 0` |

Sonunda `01-envanter-033-037.sql` yeniden: beşi de **uygulanmış**. Her migration tek işlemdir; yarıda hata olursa o adımın
hiçbir nesnesi kalmaz. Bir migration'ı ikinci kez çalıştırmak "already applied" / 51396 / 51400 / 51410 / 51430 ile reddedilir;
TEST'te bunu denemiyoruz. Ayrıntı: `operator/docs/service-accounts/DBA-033-TR.md` … `DBA-037-TR.md`.

## 4. Geri dönüş (SQL)

- **Birincil:** 2. adımdaki yedekten DBA geri yüklemesi. Yedekten sonra yazılan iş/audit kaydı kaybolur; karar change kaydında.
- **Yalnız hiç kayıt yazılmadıysa** tablo düşürme betikleri DBA notlarının "Geri dönüş" bölümünde (her biri satır varsa kendini
  durdurur). Ters sırayla: 037 → 036 → 035 → 034 → 033. Kayıt varsa düşürmüyoruz; eski API bu tabloları kullanmaz.
- Eski `b64068a` API/UI 033–037 tablolarına dokunmaz; şema 037'de kalıp yalnız uygulamayı geri almak da güvenli.

## 5. API + UI birlikte yayın

- `API/secureops-api-TEST-<sha>.zip` ve `UI/secureops-ui-TEST-<sha>.zip` **aynı master SHA'sından**. ZIP hash'i `candidate.json`
  ile tutmalı. Biri olmadan diğeri yayınlanmaz (CSRF: UI tüm unsafe çağrılarda `X-SecureOps-Csrf: 1` gönderir, ADR-0029).
- Paket `appsettings*.json` ve `web.config` taşımaz. Sunucudaki mevcut yapılandırma korunur; `ApiCsrf__AllowedOrigins__*`
  2026-10-13 kurulumundaki değerle **aynı kalır** (değişmedi).
- Sıra: app pool durdur → eski klasörü yedekle (2. adım) → ZIP içeriğini aç (yapılandırma dosyalarının üzerine yazma) →
  app pool başlat. API önce, UI hemen ardından.
- **Yeni özellikler varsayılan KAPALI**, açmak ayrı karar. Yapılandırmaya şunları **eklemiyoruz**; yoksa varsayılan geçerli:
  - `ItsmRequestCreation:Provider = Disabled` (plandan OR açma; gerçek Turuncu Hat adaptörü yok, ADR-0030).
  - `ServiceAccounts:UsageScanJobs:Collector = None` (webden kullanım taraması; Worker bu pakette yok).
  Mevcut yapılandırmada bu anahtarlar varsa değerleri bu olmalı; `Fake`/`Jea` görürsen dur.
- Geri dönüş: iki app pool'u durdur, yedeklenen `b64068a` klasörlerini geri koy, başlat. Şema 037'de kalabilir.

## 6. İlk kabul (yalnız sentetik kayıt)

- [ ] Giriş ve Erişimim; Servis Hesapları açılıyor, mevcut hesaplar okunuyor.
- [ ] **Değişiklik planları** (`/service-accounts/plans`): ekran "kurulu değil" demiyor; liste boş veya mevcut. Sentetik bir hesapla
  taslak plan, önizleme; onay **başka bir kişiyle** (planlayan onaylayamaz). Plan kanıtı yükleme 503 vermiyor (034).
- [ ] **"Bir gelişme mi oldu?"** (hesap ayrıntısı): akış açılıyor, "bu ortamda kapalı (036)" uyarısı yok. Sentetik olayda önizleme
  → kayıt; oluşan kayıtlar hesap geçmişinde görünüyor.
- [ ] **OR paneli** (onaylı plan ayrıntısı): rozet "OR açma kapalı", metin "OR açma bu ortamda kapalı (sunucu yapılandırması
  varsayılanı)". "OR aç" düğmesi yok. 503 `ServiceAccountOperationalRecordsNotInstalled` görünmüyor (035 kurulu).
- [ ] **Webden tarama paneli**: "Web taraması bu ortamda kapalı" diyor, "Tarama sonucu yükle" yolu çalışıyor.
  "tabloları (037) kurulu değil" yazıyorsa 037 eksik demektir, dur.
- [ ] Başlıksız yetkili unsafe istek 403 `ApiCsrfRejected`; normal UI işlemleri geçiyor.
- [ ] Önceki kabul adımları (kapsam, içe aktarma önizlemesi, rapor) bozulmadı.

Bulgular kabul notuna; ekran görüntüsünde yalnız ilgili pencere, gerçek ad/sunucu yok. Kurumsal OIDC/Negotiate/IIS/F5 ve gerçek
SQL harmanlaması yerel provayla kapanmaz.
