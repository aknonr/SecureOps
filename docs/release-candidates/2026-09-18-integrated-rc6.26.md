# rc6.26 - entegre TEST teslimat ve aktivasyon raporu

## Karar

Bu bir **kurulmamış adaydır**, tüm iş akışlarının kurumsal aktivasyonu değildir.
Yerel ürün/SQL/paket kontrolleri ile gerçek hedef kabulü ayrı tutulur. TEST
dizinlerine bu çalışma ortamından erişilemedi; kurulu bileşen sürümü, etkili
arşiv yolu, 022 durumu, relay veya kaynak erişimi doğrulanmadı. Kurumsal gönderim,
upload, kapanış, grant, migration, IIS değişimi ve dağıtım yapılmadı.

## Teslimat kimliği

- Ürün değişiklikleri: `d6b285c5bd35be37f7be1ab7897c201d84c3cf42`.
- Nihai build kaynağı: `028cbd2e4ec7068d71a33088d7c651e4df21644a`.
- API/UI/Worker ürün sürümü: `0.1.0+028cbd2e4ec7068d71a33088d7c651e4df21644a`.
- Aday: `C:\SecureOpsBuild\release\2026-09-18-pilot-rc6.26`.
- Bileşen/delta/Branding/araç SHA-256: adayın `release-artifacts.sha256` dosyası.
- Dosya bazlı manifest, runtime ve kaynak bağı: `release-metadata.json`, `manifests/`.
- Bağımsız araç: `diagnostics/inuse-evidence-win-x64-028cbd2.zip`;
  SHA-256 `C1B7C1536276C180D090C25320F24358ACD68F6D2455D76518189F29A1EC16BF`.
  Araç EXE SHA-256 `0272E844155253BABDB29A14F147E79470DCB841B972DD5FCADCDD8BC64C6E94`.
  Tam bağımlılık ağacı tarandı; yalnız argümansız, ağsız kullanım yolu çalıştırıldı.
- Şema 001-023. DBA paketi 022-023 içerir; hedefte 022 doğrulanmışsa yalnız 023.
  001-021/Hangfire 9 tekrar uygulanmaz; runtime schema preparation kapalı kalır.
- rc6.25 collector PDB kapısında başarısız oldu, son metadata yoktur; kurulmaz.
  rc6.26 yalnız bu somut paket hatasının düzeltilmesi nedeniyle üretildi.
  rc6.23/24 ve başarısız rc6.25 korundu.

Bu raporu içeren kapanış commit'i build commit'inden ayrıdır. Farkı teslimat
kanıtı ve mevcut görünür kaynak/kaydet-sorgula yoluna uyarlanmış tarayıcı testidir;
paketlenmiş ürün/konfigürasyon değişmez. Yeniden ZIP üretimi gerekmez.

## Gereksinim ve kanıt

| Sonuç | Uygulama | rc6.26 paket kanıtı | Hedef ayarı / kurumsal kabul / aktivasyon |
|---|---|---|---|
| In Use üç soru, seçili bulk, istisna, tarihli tekrar kullanım | Var; rc6.24 davranışı korunuyor | İki OR, kaydet/yükle, bir istisna, önceki üç cevabın açık kabulü | Hedefte doğrulanmadı |
| Excel ve arşiv | Dört sayfa, 29/22 düzen, metin kimlikler, değişmez JSON | Rapor/hash tekrar indirme ve gerçek API/Worker restart geçti | Gerçek arşiv yolu/ACL/taşıma bekliyor |
| In Use upload/BPM/OR readback | Kalıcı executor ve fixture var; gerçek adapter eksik | Fixture tamamlama geçti; kurumsal sözleşme kanıtı değil | Aktive edilmez; aşağıdaki kaynak sözleşmeleri ve adapter gerekli |
| OR → SDM | Pozitif politika ServerRequest; Jira-only niyeti kalıcı | Önceki SQL/restart regresyonu korundu; gerçek Jira testi yapılmadı | Kesin OR/hedef/aktör ve tipe özel eşleme bekliyor |
| OCO kaynak | Gerçek adapter ve Worker yolu var | Paket API/Worker: NoWorker/WrongQueue, seçici uygulama, süreç kesintisi kurtarma | SCCM/Turuncu Hat hedef işi bekliyor |
| OCO hazırlık/indir | v3 ve tarihsel v2; altı orijinal CID korunuyor | 155 servis, altı görüntü, aynı MIME indirme, sonraki düzenlemeden etkilenmeyen hazırlık | Outlook/kurumsal kaynak kabulü bekliyor |
| OCO deneme/dağıtım | Ayrı yetki/flag, gerçek SMTP, kalıcı intent/Unknown koruması | 7 yerel SQL/SMTP testi yeniden geçti; bu ZIP için yeni SMTP tarayıcı tekrarı tamamlanmadı | Relay/From/TLS, kesin deneme ve ayrı dağıtım onayı bekliyor |
| Yönetim raporu | Mevcut dashboard, SQL kesit/filtre/sayfa/Excel, yetki ve OCO sahipliği | Aynı kesit detay/Excel, iki tema/mobil/native %200, In Use sonuç mutabakatı | 023 ve hedef kabulü sonrası kullanılabilir |
| Kolektör | Completion-evidence modu, JSON-only, salt okunur | Aynı build, manifest/dependency/usage kapısı geçti | Sayısal onaylı OR ve özel JSON ile operatör çalıştırır |

Normal regresyon: 1.349 unit, 278 integration geçti; normal modda 49 opt-in
atlandı. Ayrı çalıştırmalar: 35 resource SQL (001-023), 4 rapor SQL, 6 kaynak SQL,
7 SMTP SQL ve 1 paket kaynak-process testi. Bunlar örtüşebilir; tek bir yeni
toplam olarak toplanmaz. Build sıfır uyarı/hata; format ve OpenAPI eşitlik geçti.

## Kanıt dizini

`C:\SecureOpsBuild\validation\integrated-activation-20260918`:

| Dosya / dizin | Gözlem |
|---|---|
| `final-accepted_net8.0_*.trx`, `format-build-source.json` | Son kod regresyonu, format |
| `workflow-final.trx`, `source-sql.trx`, `mail-sql-accepted.trx` | Yetki/SQL/SMTP ayrık sonuçlar |
| `package-source-host.trx`, `package-source/source-475a97471de8417fbe71f5c3786183a5/acceptance.json` | Aynı ZIP API/Worker, kesinti sonrası Attempts=2, 112,54 saniye yerel gözlem |
| `package-dashboard/result.json`, `synthetic-management.xlsx` (aynı alt dizin) | 7 katkı satırı; sentetik varsayılan dışlama, yetkisiz doğrudan erişim reddi, kesit/Excel |
| `package-dashboard/dashboard-light-1366.png`, `dashboard-dark-390.png` | VDI/mobil gerçek ekran |
| `package-dashboard-zoom/result.json` | Gerçek Chrome %200; 1366 dış/674 iç genişlik, taşma yok |
| `package-inuse/result.json` | İki OR, bulk kökeni/override/reuse, fixture executor, aynı rapor hash'i |
| `package-inuse-restart.json` | Yeniden başlatma sonrası aynı operasyon, üç önceki cevap, aynı hash |
| `package-inuse-management.json`, `package-inuse-management.xlsx` | Bir arşiv, bir doğrulanmış ek, bir BPM yanıtı, bir kapanış ayrı ölçüler; tek tamamlanma toplamı değil |
| `package-inuse-native-zoom/presentation.json` | Gerçek %100/%200, iki tema; örneklenen minimum kontrast 5,2967/4,8912; tam WCAG iddiası değil |
| `package-preparations/results.json`, `prepared.eml`, `review-1440.png` | Gönderimsiz değişmez hazırlık; 155 servis, altı orijinal görüntü, mobil/erişim reddi |
| `package-source-browser-keyboard/results.json` | Görünür kaynak bölümü, kaydet-sorgula, seçici uygulama, alıcı çıkarmalarını koruma, kısmi/stale/duplicate/reload, değişmez MIME, mobil klavye dönüşü, yetki kaybı |

Eski tarayıcı betiği kaldırılmış açma/sorgulama düğmelerini arıyordu; mevcut
akışa uyarlandı. Ayrı fareyle mobil Düzenle dönüşü denemesinde form görünürlük
beklemesi doldu; klavye Enter ve aria-pressed doğrulamasıyla tam akış geçti.
Başarısız ekranlar korundu. Mobil fare/dokunma dönüşü hedef tarayıcı kabulünde
tekrar kontrol edilmelidir; bu gözlem sessizce başarılı sayılmadı.

Yeni SMTP sink/fixture/tarayıcı tekrarının birleşik komutu yürütme ilkesi
tarafından çalıştırılmadan reddedildi. Engeli aşmak için başka yürütme yolu
kullanılmadı; bu ZIP için SMTP/restart tarayıcı kapısı açık kalır. Önceki yerel
SMTP kanıtı bunun yerine exact-package kanıtı diye etiketlenmez. `readyForInstallation`
bu nedenle ve hedef önkoşulları doğrulanmadığından false kalır.

## Dosyalar nerede?

| Belge | Konum / anlam |
|---|---|
| Eski script Excel'i | Script'in çalıştığı makinede `C:\InUse\InUse_<OR>_<yyyyMMdd_HHmm>.xlsx` |
| Önerilen TEST arşivi | `D:\SecureOpsData\InUseReports`; etkili `InUseReports:Directory` hedefte doğrulanmadı |
| Mevcut arşiv biçimi | `<root>\<record-GUID>\<version>.json`; XLSX bayt/hash/aktör/sürüm zarfı. Gevşek XLSX beklenmez |
| Yerelde doğrulanan etkili kök | Kanıt dizininde `package-inuse-hosts\private-reports`; `98106e51-48b6-4566-8037-5342e9104e6e\2.json` |
| Tarayıcı indirmesi | Kullanıcının seçtiği tarayıcı indirme konumu; API klasörü değildir |
| Kaynak eki | Kesin kaynak OR'sine `<OR>_InUse.xlsx`; yerel arşiv ve yükleme aynı olay değildir |

Yerel rapor SHA-256:
`1CBB0DE13EB4E89B9C5CD6E2A055AF1C6A7A2761E90366B7944E3D55BC769ABE`.
Worker bu klasöre erişerek değil, SQL'deki dondurulmuş baytlarla çalışır.
Hedef taşıma: yazımları koordine durdur, yedek/manifest al, yapıyı/baytları koru,
tek mevcut IIS anahtarını güncelle, gerçek API kimliğiyle eski/yeni indirmeyi
doğrula. Eski kopyayı doğrulamadan kaldırma; Full Control verme. Ayrıntılı adımlar
`docs/integrated-activation-tr.md` ve paket `operator-runbook-tr.md` içindedir.

## Kısa kullanıcı yolu

1. In Use: OR → İncele → eksik cevaplar/önceki inceleme → seçili değişiklik önizlemesi
   ve açık kabul → Taslağı kaydet → Excel/arşiv. Sahip/aspect bilinmiyorsa kaynak
   bilgisini doğrulat; Bildiren'i sahip olarak doldurma.
2. Ortam/NMS önerisini incele: PROD/DEV/TEST kaynak sınıflamasıdır; bilinmeyen
   NonProd yapılmaz. Öneri kabulü alarmın kurulduğunu kanıtlamaz.
3. Arşiv başarılı, indirme başarısızsa Arşivden indir; yeni rapor/işlem yaratma.
   Upload doğrulanmış, BPM başarısızsa eki tekrar yükleme; adım kanıtını koru.
   Son OR readback bilinmiyorsa "kapandı" kabul etme.
4. OCO: profil/kayıt → Kaydet ve kaynağı sorgula → seçili önerileri uygula →
   hazırlık/indir. Gönderim isteğe bağlıdır; kendime deneme ve dağıtım ayrı onaydır.
5. Yönetim Panosu → İş akışı sonuçları → dönem/saat dilimi → raporu yenile →
   Kayıtlar veya Excel. Hata sıfır değildir; eski kartlar ayrı kesittir.

İlk sentetik OR: 4 bireysel cevap düzenlemesi, 3 kopyalama, 1 istisna, 1 taslak
kaydı; ikinci OR: önceki 3 cevabı seçme/tek açık kabul/tek kayıt, yeniden cevap
yazma 0. Zaman veya yüzde tasarruf iddiası yoktur.

## Aktivasyonu engelleyen somut işler

Kaynak sahibi: tek onaylı sayısal OR için dinamik vaka KEY/SET ve koşullu sürüm
semantiği; ek kimlik/içerik okuması ve kayıp-yanıt mutabakatı; tek aktivitenin
önkoşulu; yetkili OR son-durum selector/yanıt/anlamı. Kolektör son iki sözleşmeyi
kendiliğinden keşfetmez. Bu kanıtlardan sonra gerçek adapter uygulanıp test edilir.
Jira sahibi: kesin ServerRequest hedef/alan/aktör ve diğer türlerin ayrı politikası.
Mesajlaşma sahibi: relay/TLS/From; operatör: kesin OCO/profil/Mail ve ayrı kitle.
TEST/DBA: sürüm/022/env/ACL/arşiv yedeği; operasyon sahibi: foreground Worker
oturumu, saat aralığı, izleme ve restart sorumlusu. Kesintisiz hizmet kanıtlanmadı.

**Doğrudan cevap:** Bu aday beklenen Excel düzenini üretip değişmez arşivleyebilir
(yerel paket kanıtı var; gerçek örnek XLSX karşılaştırması hâlâ yok). Doğru kurumsal
OR'ye yükleyip eki ve son OR kapanışını doğruladığı henüz söylenemez. Bu aşama
sadece ayar açılarak tamamlanmaz. OCO gönderim kodu vardır, ancak bu hedefte
gönderim/Outlook/gelen kutusu kabulü yoktur. Tüm iş akışları aktive edilmemiştir.

Gönderilmeyecek, sınırlamaları açık ekip duyurusu taslağı paket runbook'unun
son bölümündedir. Bu çalışma sırasında ekip adına duyuru gönderilmedi.

## Sahip istisnası ve muhasebe

Sahibin entegre kapsam için verdiği satır sınırı istisnası uygulandı; AGENTS.md
değiştirilmedi. Devralınan değişiklikler ve yeni dosyalar hesaba katıldı; commit
sayacı sıfırlamadı. Kümülatif sayım aşağıda kapanış öncesi diff ile doğrulanır.
8c1b58d başlangıcına göre: 72 dosya, +3390/-64. Tam post-rc6.22 aa9e4d2 başlangıcına göre: 118 dosya, +7533/-1056.
