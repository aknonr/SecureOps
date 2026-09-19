# rc6.26 sonrasi kalan isler ve TEST kabul adimlari

## Durum ve kapsam

Baslangic kaynak: `03b0c048d50cf926b5640116533df39c8685a37d` (temiz olarak
dogrulandi). Korunan rc6.26 kaynagi:
`028cbd2e4ec7068d71a33088d7c651e4df21644a`. Alti paket hash'i yeniden eslesti.
Operator API/UI/Worker ve SQL 022/023 kurulumunu bildirdi; bu makinede hedef D:
yollari erisilemiyor. Kurulu surum/kimlik/etkin ayar/kaynak sonucu dogrulanmis
sayilmadi. Hesap kilidi incelemesi ve migration tekrari yapilmadi.

Tek gereksinim/kalan-is kaydi `integrated-test-activation.md` tablosudur.
Bu kaynak degisiklikleri rc6.26 icinde yoktur. Yeni numarali ZIP yoktur; final
tarayici/paket kapisi kapanmadan aday hazir denmez. Ekip duyurusu gonderilmedi.

## Kullanici akisindaki devam

- In Use -> Rapor gecmisi: OR, sunucu, asil hazirlayan etiketi/hesabi, UTC hazirlama
  gunleri, surum ve durumla arama. Baslangic dahil, bitis gununun ertesi 00:00 UTC
  haric. Sayfa basina 25, API en fazla 100; sayim ve sayfa ayni SQL islemi.
- Her satirda asil hazirlayan, tam indirme adi, SHA ve yerel durum korunur.
  Kaynak eki dogrulamasi ayri alandir; kaldirilmis taslak eki geri almaz.
- Eski rapor gorunmuyorsa OR -> Onceki WASAS raporlari -> ilgili surumde
  Rapor kataloguna indeksle. Normal eski indirme de indeksi onarir. API ile
  `POST /api/v1/in-use/{record-GUID}/reports/index`, govde
  `{"expectedVersion": CURRENT_RECORD_VERSION,"versions":[SELECTED_VERSION]}`.
  En fazla 25 acikca secilen surum. Sembolik degerler gercek kayit surumu ile
  degistirilmeden calistirilmaz. Durdurma sonrasi ayni secim tekrar edilebilir;
  onceden bitenler korunur, metadata/hash celiskisi durdurur. Kaynak degismisse
  guncel kaydi yeniden okuyup secimi inceleyin. Tam tarihsel kapsam iddiasi yoktur.
- Kisi etiketi/hesabi/OR eski zarfta yoksa bugunku kayittan doldurulmaz.
  Arama her istekte disk/XLSX taramasi yapmaz. Yalniz indekslenmis guvenilir
  zarf metadatasini okur; katalog ayri bir XLSX deposu degildir.
- Inceleyici dialogu: dogrulanmis RFC referansi icin onayli sabit hesap eslesmesi
  varsa Onerilen kisiyi sec; alternatif ara, reddet veya Atanmamis sec. Acmak,
  secmek veya Vazgec atama yapmaz. Acik kaydetme ve gerekce gerekir. Cevaplar
  korunur. Yenileme onceki kabul/ret/alternatif kararini ezmez.

### RFC hesabini onaylama

Yeni API secenegi `InUseReporterMapping` bos gelir. Kaynak sahibi ve WASAS
erisim yoneticisi, tek bir `IdentityScope` + RFC `SET.p_rel_requester` referansinin
hangi **mevcut** uygulama `UserId`'sine ait oldugunu resmi kimlik kaniti ile
dogrular. Ad benzerligi, teknik olusturan veya entegrasyon hesabi yeterli degildir.
Kayitli `IdentityScope` ve kaynak referansi yetkili onerinin cevabinda bulunur;
uygun uygulama kullanicisi mevcut yetkili kullanici secicisinden okunur.

Yalniz API'nin mevcut ozel sunucu ayarina, onaylanan degerlerle:
`InUseReporterMapping:Revision`; `Links:0:IdentityScope`, `UserReference`,
`ApplicationUserId`, `ReviewReference`, `ValidUntil` (acik UTC ISO zamani).
IIS ortam anahtarinda `:` yerine `__` kullanilir. Gercek degerler bu belgeye
veya ortak fixture'a yazilmaz. Bu bir rol/grant veya hesap olusturma islemi degil.
Suresi dolmus, coklu, bulunamayan, onaysiz veya inceleme yetkisiz eslesme secilemez.
Secim tekrar API'de cozulur, eski fingerprint/kaynak surumu reddedilir. Atamasiz
inceleme ve tamamlama yetkili operator icin halen mumkundur.

## Uc farkli dosya adi ve konum

Legacy script: `C:\InUse\InUse_<OR>_<yyyyMMdd_HHmm>.xlsx`, script makinesinde.
API arsivi: operatorun ayarladigi `D:\SecureOpsData\InUseReports`,
`<record-GUID>\<version>.json`. JSON, Base64 XLSX + hash/surum/asil hazirlayan
metadatasidir. Uzantisi XLSX yapilmaz. `.json.lock`, FileShare.None kilit
tutamacinin tekrar kullanilan dosyasidir; sifir bayt/persist etmesi takilma kaniti
degildir. Taslak sifirlama icin GUID klasoru/JSON/kilit silinmez.

Tarayici: `InUse_<OR>_<asil-hazirlayan>-<actor8>_<yyyyMMdd_HHmmss>Z_v<surum>.xlsx`;
Z UTC'dir, indirme konumunu tarayici belirler. Kaynak attachment adi degismez:
`<OR>_InUse.xlsx`. Sonraki indiren kisi hazirlayan yerine yazilmaz. UI arsive
dogrudan erismez; Worker frozen SQL Artifact baytlarini tuketir.

Orijinal guncel ek bu oturumun dosya kaynaklarinda bulunamadi. Owner'in bildirdigi
27,044 bayt/SHA `2FDB1ABB5837BF292F8912D8ED707AAF9342A96A4804EF8A05D88BB0E5CD828A`
ile sohbetin bozuk sonlu kopyasi ayni dosya kabul edilmedi; tam dosya bir kez
istendi. Gecerli orijinal hakkindaki owner bildirimi korunur. Legacy OR ve guncel
OR farklidir; onceki gercek legacy ve ayni-sentetik-kaynak testleri korunur.

## Kaynak sahibinden tek sinirli sozlesme istegi

Bilinen upload istegini yeniden istemiyoruz. Mutation istemcisi gercek kaynak
provider'inda DI ile cozulur, ancak completion transport olarak baglanmaz:
asagidaki readback/atomik kosul kanitlari olmadan dispatcher acilmaz.

| Eksik olgu / sorumlu | Bilinen / en kucuk salt okunur istek | Acilan kod ve kabul |
|---|---|---|
| Ek kimligi ve icerik / Turuncu Hat API sahibi | Secilen tek OR'deki **mevcut** bir ek icin belgelenmis liste/lookup ve download/icerik kaniti islem yolu, kesin parent ve attachment ID alanlari, tipler, tek maskeli yanit; kayip upload cevabinin ayni baytlarla nasil aranacagi. Filename tek basina yetmez. Bilinen upload req.fBase/fId/fName/datastring/SessionID/TenantId yeniden kesfedilmez. | Ek readback adapteri ve kayip-cevap mutabakati; yeni upload yapmadan tekrar dogrulama |
| Dinamik vaka + kosullu yazma / workflow API sahibi | Mevcut completion collector ile tek secili OR'nin p_emb_dynamic_case_orff SET/KEY bicimi; ayrica 4463/4464 icin mevcut anahtar/deger/surum, sunucu destekli atomik kosul parametresi ve konflikt yanitinin belge/maskeli ornegi. GET+kosulsuzPOST veya yerel kilit CAS degildir. | Gercek property hedef/cozumleyici ve kaynak-eszamanlilik testleri |
| Nihai OR durumu / workflow sahibi | Tek OR icin otoritatif salt okunur nesne/select, durum degeri/tipi ve kapanis/sonraki-asama anlam tablosu; uygun BPM aktivitesiyle iliski. m_status=4 yalniz BPM kabulunden OR kapandi sonucu uretmez. | Closure verifier; ek basarili/BPM basarisiz ve sonraki asama ayrimi |
| RFC hesap / kaynak kimlik sahibi + WASAS erisim yoneticisi | Mevcut RFC iliskisi ve kaynak user referansi biliniyor. Bir referans icin uygulama UserId baginin onayli kaniti ve gecerlilik tarihi; isim benzerligi yok. | Yukaridaki reviewed crosswalk'un tek-kayit hedef kabul testi |
| Servis unsuru / CMDB sahibi | Her secili sunucunun kendi servis kimligine bagli 0/1/cok unsur sonucunun tam alan adlari, ID/deger tipleri ve onayli secim anlami. Diger sunucunun unsuru veya [Genel] varsayimi kullanilmaz. | Mevcut aspect adapterinin gercek wire kabul/mapping testi |

Collector komutu ve maskelenen alanlar:
`scripts/diagnostics/InUseEvidence/operator-completion-tr.md`.
rc6.26 icindeki `diagnostics/inuse-evidence-win-x64-028cbd2.zip` SHA:
`C1B7C1536276C180D090C25320F24358ACD68F6D2455D76518189F29A1EC16BF`.
Tam `tool/` agaci, .NET 8 NETCore.App ve AspNetCore.App korunur. JSON-only ozel
yapilandirma IIS'ten miras alinmaz. Yalniz bounded masked Evidence paylasilir;
CollectedNotMapped runtime esleme degildir. Bilinmeyen attachment endpoint'i
veya terminal durum anlamini bu arac kesfedemez. Kaynak sahibi yukaridaki belgeyi
verir; eski yazma scripti probe olarak calistirilmaz. ID'ler tutarli takma
degerlerle maskelenir; alan adi, tur, null/empty ve iliski esitligi korunur.

## Etkin degerden gereken degere

Once `rc626-mail-source-activation-tr.md` adimlariyla API raporu ve normal Worker
oturumunda ayni ortam/override ile `--diagnostics` JSON'u alin. Teklif edilen
ozel hedef kanit klasoru `D:\SecureOpsPrivate\Acceptance`; otomatik olusturulmaz.
Mevcut karsilastirici ilgili provider/profil/mail/DB/queue alanlarini kapsiyor:

```powershell
& 'D:\SecureOpsDelivery\configuration\Compare-OperationsReadiness.ps1' `
  -ApiReport 'D:\SecureOpsPrivate\Acceptance\api-operations.json' `
  -WorkerReport 'D:\SecureOpsPrivate\Acceptance\worker-operations.json'
```

Teslimat dizini ornektir; dogrulanmis paketin gercek konumuyla degistirin.
Karsilastirici sadece JSON okur, ayar degistirmez. Farkli/eksik alan exit 2;
esit rapor baglanti veya gonderim kabul kaniti degildir. API arsiv yolu ve
Worker kimligi gibi rol-farkli alanlar genel fingerprint esitligi gerektirmez.

| Alan | Su anki hedef kaniti | Gereken deger / tam degisiklik | Sorumlu / dogrulama |
|---|---|---|---|
| API/UI/Worker ProductVersion | rc6.26 owner bildirimi | Once DLL ProductVersion ve paket hash'i; yeni onarimlar yalniz successor'da | TEST operatoru; salt okunur envanter |
| SQL 022/023 | Kuruldu bildirimi | Yeniden calistirma yok. ops.InUseExecutions, ops.InUseServerReviews, reporting.WorkflowSnapshots ve receipt tanimlari/index/trigger durumu | DBA; mevcut kurulum kaydi + sys.objects/columns/indexes/triggers |
| InUseReports:Directory | D: ekran goruntusu/owner bildirimi | D:\SecureOpsData\InUseReports korunur; ayni anahtar tekrar eklenmez | API operatoru; etkin tanilama + eski/yeni yetkili hash-esit indirme |
| DB target, Hangfire schema/queue/PrepareSchema | Tanilama alinmadi | Iki surecin hedef/queue'su eslesir; PrepareSchema=false, schema9 kurulu | API/Worker operatoru; karsilastirici + heartbeat |
| Announcements:Enabled, AnnouncementSource:Enabled | UI kapali/eksik profil bildirimi | Yalniz onayli kaynak icin true; koleksiyon/service provider ve onayli profil revision/collection/template degerleri iki surecte eslesir | SCCM/Turuncu Hat sahibi; tek OCO/profil terminal isi |
| AnnouncementMail Enabled/SelfTestEnabled/SendEnabled | Onceki CLI false override riski; etkin cikti yok | Ilk kabulde true/true/false. Normal Worker CLI/environment false override varsa incelenerek ayni anahtar duzeltilir | Mesajlasma + Worker operatoru; mail fingerprint + tek self-test |
| AnnouncementMail: Host/Port/Security/EnvelopeMode/EnvelopeSender/AllowedRecipientDomains/PolicyRevision | Onayli deger alinmadi | Onceki kilavuzdaki alan adlari gecerli; host/port/TLS/envelope/domain degerleri **mesajlasma sahibinden**. Secret sunucuda kalir | Mesajlasma sahibi; TLS + SMTP kayitli sonuc + ayri inbox/Outlook gozlemi |
| InUseReporterMapping | Yeni kaynak, bos varsayilan | Tek onayli scope/userref -> mevcut UserId; revision/reviewReference/ValidUntil | Kaynak kimlik sahibi + WASAS admin; matched ve yetkisiz hesap testi |
| InUseCompletion | Gercek readback contract yok | Bayrak acilmaz; yukaridaki belgelerden sonra adapter/test tamamlanir | API/workflow sahibi + gelistirici |

SMTP ile kaynak toplama farkli kapilardir. Kaynak profili SCCM sunucu koleksiyonu
ve taslak metnini secer; Turuncu Hat servisleri bu sunuculardan, calisma tarihleri
OCO'dan gelir. Dogrudan OCO-servis baglantisi oldugu varsayilmaz.

## Kontrollu kabul sirasi

1. Operator secilen OCO/profili, Jira icin OR/destination/type/actor/niyeti,
   In Use icin OR/sayisal kaynak kimligini ve self-test hazirligini kaydeder.
   Dagitim hazirligi ve To/Cc ayrica acikca incelenir. Arbitrary pending kayit yok.
2. OCO: Kaynaktan getir -> jobId -> terminal durum -> cihaz/servis sayisi, kismi
   hata, saniye/offset'li tarihler ve kaynak iliskisi. Mevcut manuel/alici
   duzenlemeler degisiklik onerisi olarak korunur. Heartbeat tek basina kabul degil.
3. Kendime deneme: oturumdaki kayitli Mail, From/envelope, tek To, bos Cc,
   hazirlik/hash/OCO/tarihler incelenir. Bir onay, kayitli commandId/SMTP sonucu.
   Accepted inbox degildir; Outlook dikey duzen ve alti CID gorseli ayri gozlenir.
4. Dagitim: sadece ayri onayli hazirlik ve kitleyle, SelfTest kabulunden sonra.
   Unknown/Partial ise Message-ID ile relay mutabakati; otomatik/yeni-key retry yok.
5. OR->SDM: ServerRequest icin exact policy/destination onizleme. Jira-only
   kaynak acik kalir; transfer-and-close ayri niyettir ve kaynagin kapanis
   sozlesmesini ister. SoftwareInstallation/ServerRetirement otomatik ServerRequest
   yapilmaz. Olusan Jira kimligi once kalici yazilir; kaynak hatasinda create tekrarlanmaz.
6. In Use: gercek adapter kabulunden sonra immutable rapor -> ek ID/parent/bayt
   dogrulamasi -> tek uygun BPM -> otoritatif OR durumu. Tamamlamayi baslatan insan,
   opsiyonel inceleyici ve Worker hesabini ayri kaydedin. Acknowledged kapandi degil.
7. Ayni kayitlari /dashboard detay ve ayni veri kesiti Excel'iyle eslestirin.
   Tekrar denemeleri yeni tamamlanmis OR saymayin. Unknown veya tarihsel eksik
   veri sifir/basari sayilmaz. Modulleri yalniz hedefte kanitlanan kapsamla acin.

## Son UI kapisi: izinli test ortami

Onceki ozel UI host baslatmasi tool policy tarafindan reddedildi. Daha ayrintili
bir neden kanit kaydinda yok; port/komut/launcher/izin degistirerek tekrar denenmedi.
Son modal goruntusu onceki source'tandir. Asagidaki islem yalniz normal yetkili
test runner/operator ortaminda yapilir; burada yapildi iddiasi yoktur.

Bagimliliklar: .NET8 SDK, mevcut SecureOpsResourcesV1 LocalDB, sqlcmd, Node,
kurulu Chrome, mevcut Playwright modulu (journey-support.cjs ve test export'u).
Birbirinden ayrilmis taze kanit/publish dizinleri; fixture disinda endpoint yok.
Mevcut `Test-ResourceCatalogueSql.ps1 -DatabaseSuffix OcoContinuationUi20`
taze 001-024 DB olusturur. API/UI/Worker Release publish'ini tek incelenmis
source SHA'dan `payload/api`, `payload/ui`, `payload/worker` altina alin.
SHA, hash ve publish loglarini kaydedin; numbered ZIP uretmeyin.

Mevcut host proseduru (serbest port oldugu dogrulanmadan calistirmayin):

```powershell
powershell -NoProfile -File tests/browser/announcement-hosts.ps1 `
  -EvidenceRoot C:\SecureOpsBuild\validation\continuation-ui20\hosts `
  -PayloadRoot C:\SecureOpsBuild\validation\continuation-ui20\payload `
  -DatabaseSuffix OcoContinuationUi20 -Port 64731 -OperationalRecordSimulation
node tests/browser/inuse-rc626-repair.cjs $playwrightModule `
  https://localhost:64732 https://localhost:64731 `
  C:\SecureOpsBuild\validation\continuation-ui20\browser
$env:WASAS_NATIVE_ZOOM = '1'
node tests/browser/inuse-rc626-repair.cjs $playwrightModule `
  https://localhost:64732 https://localhost:64731 `
  C:\SecureOpsBuild\validation\continuation-ui20\zoom
Remove-Item Env:\WASAS_NATIVE_ZOOM
```

Host kanit dizini onceden ayrilir, publish DLL'leri mevcut olmali; script eksik
dizin/DLL veya dolu portta durur. Baska hosta dokunmayin. Onceki test verisini
silip tekrar etki uretmeyin; taze DB kullanin. $playwrightModule yerel kurulu
modulun gercek yoludur, repo yeni paket indirmeyi gerektirmez.
Betik fare/dokunma, klavye Enter/Escape, focus return, iki tema, 390/1366 reflow,
reset/discard/reload/restart, katalog ve ayni hash'li indirmeyi dogrular.
Native mod Chrome zoom ayarini 2 yapar, DPR=2 ve visual scale=1 kontrol eder;
viewport daraltma ayni kanit degildir. result.json ancak tum assertion'lar gecer.
OCO kaynak-review ve readiness icin mevcut source acceptance yolculugu ayrica
calistirilir; bu In Use betigi OCO kabulunu iddia etmez.

## SQL farki ve geri donus

024 katalog icin gerekceli yeni additive farktir; onceki lifecycle/export tek
basina migration gerektirmiyordu. Sonraki paket `-UpgradeFromRc626` ile sadece
024 delta uretebilir. 022/023 kurulu bildirimini object/DBA kanitiyla teyit edin;
runtime PrepareSchema hep false. Genis grant veya migration replay yoktur.
Bu kaynakta final paket kapisi gecilmediginden hedefte 024 uygulama talimati yoktur.

Rollback basit DLL dusurme degildir: yeni yazilari durdurun, foreground Worker'i
kontrollu durdurup tum bilesenleri koordine edin. Arsiv, intent/artifact, review,
event ve audit korunur. Eski serializer yeni lifecycle/EvidenceSheets/karar
alanlarini korumayabilir; yalniz backup ve geri-yazma uyumlulugu incelenmis
prosedurle devam edilir. Whole-server IIS reset veya yeni Windows Service yok.

Worker sorumlusu, oturum adi, calisma penceresi, queue takibi ve restart
devralani hedefte **henuz bildirilmedi**. Takim surekli hizmet bekliyorsa bu
foreground model icin sorumluluk/pencere karari zorunludur; unattended denemez.

## Bu devamda kosulan kapilar

Kaniti korunan kok: `C:\SecureOpsBuild\validation\continuation19`.
Release build sifir uyari/hata; format verify ve OpenAPI snapshot karsilastirmasi
gecti. Normal birim: 1382 basarili. Normal entegrasyon: 279 basarili, 56 opt-in
atlandi. `tests/continuation-unit-pass.trx` ve `continuation-integration-pass.trx`
son normal sonuclardir. Ayrik kosulari normal test sayisina ekleyip toplam uretmeyin.

| Onceki 54 opt-in grubu | Simdiki durum / kanit |
|---|---|
| ResourceSql 39 | 2 yeni katalog testiyle 41 opt-in; ayri ResourceSql grubunda normal 1 dahil 42 gecti |
| AnnouncementSourceSql 6 | 6 gecti; ozel LocalDB, yayimlanan Hangfire schema9; runtime PrepareSchema=false |
| AccessAdministrationSql 1 | Gecti; taze OcoAccessGuardsContinuation20 |
| Announcement API 5 | 2 draft/discovery + 2 preparation/role-audit gecti; browser MIME fixture kapisi bekliyor |
| API/Worker process source 1 | Bekliyor; izinli runner gerekli, baska launcher ile denenmedi |
| Mail SQL 1 | Gecti; yalniz loopback SMTP sink, taze OcoMailContinuation20 |
| OIDC SQL role removal 1 | Gecti; taze Rc621DefectsContinuation20 |

Normal kosunun 56 atlamasindan 54'unun **ayni test adi**, ayri gecen TRX'lerle
eslestirildi. `continuation-sql-final.trx`: 50 gecti (42 Resource + 6 source +
2 API draft/discovery); `continuation-access-preparation.trx`: 4 gecti;
`continuation-mail-sql.trx`: 1 gecti. Bunlar yeni paket/browser/kurumsal kanit degil.
Onceki basarisiz TRX'ler silinmedi: eski 23 migration beklentisi ve sentetik DI
adres eksigi duzeltildi; bir SQL kosusunda Hangfire onkosulu eksikti, tekrar
kullanilan draft fixture toplamlari yeni veriyle karisti. Son SQL kosusu ayri taze
OcoSourceContinuation20Final DB'de tamamlandi. Corporate SQL/grant uygulanmadi.

Mevcut In Use arama performans regresyonu: 1000 sentetik kayit, 20 sorgu, 2 cached
plan; bu makinede 231.13-266.40 ms (ortalama 254.14 ms). Ham veri/planlar
`query-load-final/` altinda. Bu **mevcut In Use liste sorgusudur**, yeni rapor
katalogunun olcek SLA'si veya kurumsal kapasite kaniti degildir. Katalog icin
SQL bounded sayim/sayfalama/yetki/tarih/indeks-konflikt testleri gecti.

`node --check tests/browser/inuse-rc626-repair.cjs` gecti; screenshot veya native
zoom kabulunun yerine gecmez. Final UI ve exact-payload source/SMTP replay
beklediginden yeni numarali ZIP ve ekibe tam aktivasyon bildirimi yoktur.
