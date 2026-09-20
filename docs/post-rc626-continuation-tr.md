# rc6.26 sonrasi kalan isler ve TEST kabul adimlari

## Durum ve kapsam

Bu belge tek guncel Turkce operator girisidir. 20 Eylul 2026 kontrolu:

| Kimlik | Deger ve kanit siniri |
|---|---|
| Tarihsel baslangic | `03b0c048d50cf926b5640116533df39c8685a37d` |
| Gelen testli urun kaynagi | `b596058fa0e1f82b278511f9a9e1c032c1130045`; kurtarma sirasinda uc Release DLL ProductVersion/hash ve TRX eslesti. Sonraki sistem-durumu UI duzeltmesi bu staging'de yok |
| Gelen belge kapanisi | `69a9b839614d97aaed5ea9f600cc4841cf01d24d`; b596058 sonrasinda sadece iki Markdown dosyasi degismis |
| Korunan/kuruldu bildirilen aday | rc6.26, `028cbd2e4ec7068d71a33088d7c651e4df21644a`; alti yerel paket hash/boyutu yeniden eslesti. Hedef kurulum bildirimi bagimsiz kurulum kaniti degil |
| Gereken schema | Yeni katalog icin 024; 022/023 kuruldu bildirimi var. Hedef nesne/tanim ve DBA kaydi bekleniyor |
| Hedef kaniti | Saglanan API JSON: 20.09.2026 00:54:02 UTC capture, 00:54:21 UTC kaynak kontrolu. Kaynak/mail kapali, Worker kontrol edilmemis, 6 asset dogrulanmis, arsiv okuma dogrulanmis/yazma sinanmamis. Canli ajan gozlemi veya Worker raporu degil; urun surumu yok |
| Bu devam | Onceki belge/ihrac/tarayici degisiklikleri korunuyor; sistem-durumu paneline dar UI duzeltmesi eklendi. Yeni host/tarayici/paket kabul sonucu yok; kurulmus davranis degil |

Tek gereksinim/kalan-is kaydi [ana matristir](integrated-test-activation.md).
Onceki [test kaniti](continuation-b596058-evidence.md) urun kaynagini ayri tutar.
Kaynak/SMTP destek proseduru [rc6.26 kilavuzudur](rc626-mail-source-activation-tr.md).
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

Orijinal ek artik mevcut ve salt okunur dogrulandi: 27,044 bayt/SHA
`2FDB1ABB5837BF292F8912D8ED707AAF9342A96A4804EF8A05D88BB0E5CD828A`.
Content decode sonucu 6,788 bayt/SHA
`11ECD7B916A9A6086D934C107936664CFBC999CB956EB7C3564A9145688C40DC`;
zarf Size/Sha256 ile ayni. Bu tarihsel v13 XLSX alti sayfali; yeniden yazilmadi.
Legacy dort sayfa ile 22 NMS basligi ve 29 Sunucular etiketi sirayla ayni,
iki checklist her ikisinde bos. Iki farkli OR'nin veri degerleri esit sayilmadi.
Orijinaller Desktop'ta ozel inceleme girdisi olarak kalir; repo/fixture/pakete
kopyalanmaz. Onceki bozuk sohbet kopyasi orijinalin yerine kullanilmaz.

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
[completion proseduru](../scripts/diagnostics/InUseEvidence/operator-completion-tr.md).
rc6.26 icindeki `diagnostics/inuse-evidence-win-x64-028cbd2.zip` SHA:
`C1B7C1536276C180D090C25320F24358ACD68F6D2455D76518189F29A1EC16BF`.
Tam `tool/` agaci, .NET 8 NETCore.App ve AspNetCore.App korunur. JSON-only ozel
yapilandirma IIS'ten miras alinmaz. Yalniz bounded masked Evidence paylasilir;
CollectedNotMapped runtime esleme degildir. Bilinmeyen attachment endpoint'i
veya terminal durum anlamini bu arac kesfedemez. Kaynak sahibi yukaridaki belgeyi
verir; eski yazma scripti probe olarak calistirilmaz. ID'ler tutarli takma
degerlerle maskelenir; alan adi, tur, null/empty ve iliski esitligi korunur.

Kaynak sahibi kosullu yazma veya otoritatif okuma imkani olmadigini bildirirse,
yaniti referans/tarih ve desteklenen alternatifle ana matrise kaydedin; ayni
olmayan endpoint'i tekrar istemeyin. Bu durumda otomatik tamamlama kapali kalir.
Desteklenen secenekler: WASAS arsivini indirip mevcut kaynak UI'sinde operatorun
ayri yurutmesi (WASAS verified-complete saymaz), veya kaynak sahibinin saglayacagi
kosullu sunucu islemi. Yerel kilit/GET+kosulsuz yazma kabul edilen eszamanlilik
garantisini saglamaz. Daha zayif bir akis ancak owner'in acik risk/sozlesme karari
ve yeni inceleme ile ele alinabilir; bu devam o karari vermiyor.

## Etkin degerden gereken degere

API raporu artik alindi; hash ve kesin UTC zamanlari ana matriste E-02'de.
Siradaki eksik girdi: `rc626-mail-source-activation-tr.md` adimlariyla normal Worker
oturumunda ayni ortam/override ile `--diagnostics` JSON'u. Teklif edilen
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
| InUseReports:Directory | API: mevcut yol, ReadableWriteNotTested | Mevcut D:\SecureOpsData\InUseReports korunur; yazma ve eski/yeni hash-esit indirme kabul edilir | API operatoru; yetkili rapor olusturma/indirme |
| DB target, Hangfire schema/queue/PrepareSchema | API raporu var: Hangfire Enabled=False, PrepareSchema=False; Worker yok | Iki surecin onayli hedef/queue'su eslesir; PrepareSchema=false, schema9 kurulu | API/Worker operatoru; karsilastirici + heartbeat; API kapali olmak SQL arizasi degil |
| Announcements:Enabled, AnnouncementSource:Enabled | API: True / False; collection/service Disabled; site/provider ve uc selector bos | Yalniz onayli kaynak icin etkinlestirme; onayli profil revision/collection/template degerleri iki surecte eslesir. Raporda profil gorunmemesi bos sozluk kaniti degil | SCCM/Turuncu Hat sahibi; tek OCO/profil terminal isi |
| AnnouncementMail Enabled/SelfTestEnabled/SendEnabled | API: False/False/False, Default; Worker bilinmiyor | Ilk onayli kabulde true/true/false; once Worker startup karsilastirmasi. Default, acik operator false override kaniti degil | Mesajlasma + Worker operatoru; mail fingerprint + tek self-test |
| AnnouncementMail: Host/Port/Security/EnvelopeMode/EnvelopeSender/AllowedRecipientDomains/PolicyRevision | Onayli deger alinmadi | Onceki kilavuzdaki alan adlari gecerli; host/port/TLS/envelope/domain degerleri **mesajlasma sahibinden**. Secret sunucuda kalir | Mesajlasma sahibi; TLS + SMTP kayitli sonuc + ayri inbox/Outlook gozlemi |
| InUseReporterMapping | Yeni kaynak, bos varsayilan | Tek onayli scope/userref -> mevcut UserId; revision/reviewReference/ValidUntil | Kaynak kimlik sahibi + WASAS admin; matched ve yetkisiz hesap testi |
| InUseCompletion | Gercek readback contract yok | Bayrak acilmaz; yukaridaki belgelerden sonra adapter/test tamamlanir | API/workflow sahibi + gelistirici |
| Duyuru gorselleri | 6 Validated; main.jpg icerigi PNG; paket PresentNotValidated | Baytlar korunur; paket/MIME ve Outlook kabul kapilari ayridir | Test operatoru |
| TuruncuHat:InUseAspectLookupEnabled | API False, Default | Servis sahibi alanlari veya RFC mapping sorunu yalniz bu bayrakla aciklanmaz; kaynak iliski sozlesmesi gerekir | Kaynak sahibi |

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
Mevcut `Test-ResourceCatalogueSql.ps1 -DatabaseSuffix OcoCompletion20Ui`
taze 001-024 DB olusturur; burada calistirilmadi. Bu devamda
`C:\SecureOpsBuild\validation\completion-20260920\payload` altina mevcut testli
b596058 DLL'leri `--no-build --no-restore` ile yayimlandi. Uc giris DLL hash'i
ayni; `staged-payload-manifest.json` tum dosyalari kaydeder. Bu numarasiz runner
girdisidir; host/paket kabulunu kanitlamaz. Source degisirse yeni SHA icin taze
staging gerekir. Son teslimatta kabul edilen baytlar ile paket yeniden eslestirilir.

Mevcut host proseduru (serbest port oldugu dogrulanmadan calistirmayin):

```powershell
powershell -NoProfile -File tests/browser/announcement-hosts.ps1 `
  -EvidenceRoot C:\SecureOpsBuild\validation\completion-20260920\hosts `
  -PayloadRoot C:\SecureOpsBuild\validation\completion-20260920\payload `
  -DatabaseSuffix OcoCompletion20Ui -Port 64731 -OperationalRecordSimulation
node tests/browser/inuse-rc626-repair.cjs $playwrightModule `
  https://localhost:64732 http://127.0.0.1:64731 `
  C:\SecureOpsBuild\validation\completion-20260920\browser
$env:WASAS_NATIVE_ZOOM = '1'
node tests/browser/inuse-rc626-repair.cjs $playwrightModule `
  https://localhost:64732 http://127.0.0.1:64731 `
  C:\SecureOpsBuild\validation\completion-20260920\zoom
Remove-Item Env:\WASAS_NATIVE_ZOOM
```

Host kanit dizini onceden ayrilir, publish DLL'leri mevcut olmali; script eksik
dizin/DLL veya dolu portta durur. Baska hosta dokunmayin. Onceki test verisini
silip tekrar etki uretmeyin; taze DB kullanin. $playwrightModule yerel kurulu
modulun gercek yoludur, repo yeni paket indirmeyi gerektirmez.
API bu launcher'da HTTP loopback, UI HTTPS'tir; API'ye HTTPS verilmez.
Betik var olan browser/zoom kanit dizinini reddeder; basarisiz eski sonuc silinmez.
Betik fare/dokunma, Enter ile acik Vazgec, focus return, iki tema, 390/1366 reflow,
reset/discard/reload/restart, katalog ve ayni hash'li indirmeyi dogrular.
Mevcut dialog CloseOnEscapeKey=false kullanir; Escape kendi basina kapatmaz.
Klavye kabulunde acik Vazgec dugmesi kullanilir; bu davranis degistirilmedi.
Native mod once Chrome ayarini 1 yapip ayni pencerede baseline olcer; sonra 2
yapip getDefaultZoom ile gercek ayari geri okur. result.json ayar, baseline ve
sonraki viewport/DPR/visual scale olcumlerini saklar. DPR=2 tek basina kanit
degildir. Atama kaydet/atanmamis secimi de kalici API sonucuyla karsilastirilir.
Bu degisen betik henuz izinli runner'da kosulmadi; syntax gecisi kabul degildir.
Son UI duzeltmesi ayni izinli betige sistem-durumu kartini da ekler: iki tema,
masaustu/mobil, klavye/touch/fokus, gercek zoom, acik kontrol oncesi/sonrasi
goruntuler ve iki JSON indirmenin bayt/zaman esitligi. Sayfa Yenile dugmesi
ilk is-akisi kontrolunu baslatmaz. Bu goruntuler henuz uretilmedi.
OCO kaynak-review ve readiness icin mevcut source acceptance yolculugu ayrica
calistirilir; bu In Use betigi OCO kabulunu iddia etmez.

Ayri runner kapilari (ayni incelenmis kaynak/publish; yalniz taze sentetik DB):

| Kapi | Mevcut giris ve zorunlu bag | Geri gelecek kanit |
|---|---|---|
| API/Worker source process | Integration filter `FullyQualifiedName~AnnouncementSourceAcceptanceTests`; `SECUREOPS_SOURCE_HOST_ACCEPTANCE=1`, `SECUREOPS_SOURCE_PAYLOAD_ROOT` ayni `payload` dizini, `SECUREOPS_SOURCE_EVIDENCE` yeni ozel kok; `SECUREOPS_SQL_TEST_CONNECTION` yalniz yeni `SecureOps_ResourcesV1_OcoSource...` LocalDB fixture'i. Hangfire 9 onceden bu fixture'da; PrepareSchema=false | TRX, acceptance.json, host loglari, job/attempt/recovery ve olusan MIME; payload hash'leri. Payload degiskeni yoksa bin/Release'e duser, exact-payload kabul sayilmaz |
| OCO tarayici/MIME | `announcement-hosts.ps1 -SourceReview -FinalPresentation` ve incelenmis orijinal asset dizini; `announcement-source-review.cjs <playwright> <ui> <api> <denied-ui> <fresh-output>`, ayrica `announcement-continuity.cjs <playwright> <ui> <api> <fresh-output> after` | result/evidence, saved-draft.json, saved-preview.html, representative.eml; v2 tarihsel MIME ve v3 guncel gorunum ayridir |
| MIME parser opt-in | `SECUREOPS_ANNOUNCEMENT_BROWSER_EVIDENCE` yukaridaki uc dosyanin dizini; `SECUREOPS_ANNOUNCEMENT_ORIGINAL_ASSETS` onayli yerel asset/manifest dizini; integration filter `FullyQualifiedName~BrowserDownload_ParsesSavedTurkishContentAndSixMatchingCidImagesWithoutSending` | Bir passing TRX; alti CID/bayt/MIME ve 155 servis. Gonderim kaniti degil |
| API/Worker/SMTP payload | `local-mail-sink.cjs <port> <private-sink>` ve `operations-mail.cjs <playwright> <ui> <api> <fresh-output> <private-sink>`, yalniz fixture kompozisyonu; ayni DB/payload ile task Worker restart sonrasi `resumed` | command/preparation kimligi, Accepted/Rejected/Unknown, ikinci gonderim olmamasi ve ayni MIME; kurumsal relay/inbox degil |

Bu tablo host baslatma engelini asma talimati degildir. Yetkili runner/operator
mevcut source ve mail prosedurlerini kullanir; burada hicbir host denenmedi.
Salt syntax ve onceden kalan goruntuler bu kapilari kapatmaz.

## SQL farki ve geri donus

024 katalog icin gerekceli yeni additive farktir; onceki lifecycle/export tek
basina migration gerektirmiyordu. Sonraki paket `-UpgradeFromRc626` ile sadece
024 delta uretebilir. 022/023 kurulu bildirimini object/DBA kanitiyla teyit edin;
runtime PrepareSchema hep false. Genis grant veya migration replay yoktur.
Bu kaynakta final paket kapisi gecilmediginden hedefte 024 uygulama talimati yoktur.

DBA paket oncesi 023 makbuz tablosu ve kuruldu bildirilen 022/023 nesne, kolon,
index, trigger tanimlarini kurulum kaydiyla karsilastirir; repo migration ledger
olusturmaz. 024 tek metadata tablosu, receipt foreign key, code index ve immutable
trigger ekler. 024 zaten varsa script durur; tanim farki otomatik onarilmaz.
Dar runtime farki yalniz mevcut onayli API principal'ina bu tabloda SELECT/INSERT;
Worker/UI icin yeni SQL veya arsiv yetkisi yok. Migration dosyasi SQLCMD `:r`
ile schema dosyasini cagirir, iki dosyanin paket hash'i birlikte dogrulanir.
Kabul edilmis successor, dogrulanmis 023, geri yukleme noktasi ve DBA/bakim
penceresi olmadan uygulanmaz. Eksik/farkli nesne, farkli hash, yedek eksigi,
surum uyumsuzlugu veya devam eden yazilar durma nedenidir. 001-024 test fixture
komutu corporate SQL icin kullanilmaz; bu devam yeni hedef komutu vermiyor.

Rollback basit DLL dusurme degildir: yeni yazilari durdurun, foreground Worker'i
kontrollu durdurup tum bilesenleri koordine edin. Arsiv, intent/artifact, review,
event ve audit korunur. Eski serializer yeni lifecycle/EvidenceSheets/karar
alanlarini korumayabilir; yalniz backup ve geri-yazma uyumlulugu incelenmis
prosedurle devam edilir. Whole-server IIS reset veya yeni Windows Service yok.

Worker sorumlusu, oturum adi, calisma penceresi, queue takibi ve restart
devralani hedefte **henuz bildirilmedi**. Takim surekli hizmet bekliyorsa bu
foreground model icin sorumluluk/pencere karari zorunludur; unattended denemez.

## Bu devamda kosulan kapilar

Sistem-durumu duzeltmesi icin yeni yerel kanit:
`C:\SecureOpsBuild\validation\system-status-20260920`.
`tests/system-status-unit-accepted.trx`: 1399 gecti (17 yeni panel testi dahil);
`tests/system-status-integration.trx`: 279 gecti, 56 opt-in atlandi.
Release build sifir uyari/hata. Onceki sayilara ekleyerek toplam uretilmez.
Iki eski birim TRX basarisizligi, artik arsivlenen rehber/runbook adini bekleyen
paket testiyle ilgiliydi; guncel ihrac beklentisiyle duzeltildi ve kanit korundu.
Bu kaynak duzeltmesi kurulmadi; tarayici once/sonra goruntusu yoktur.
Asagidaki b596058 test/staging kaniti tarihsel olarak korunur; yeni paneli icermez.

20 Eylul devaminda: orijinal JSON/XLSX ve alti rc6.26 paket hash'i dogrulandi;
uc numarasiz no-build publish ve payload taramasi gecti. Paket SQL secimi icin
dort baseline ve iki reddetme kontrolu, PowerShell parse ve node syntax kontrolu
yapildi. Guncel tarayici/host/SMTP kosusu yok; DB olusturulmadi. Ayni .NET
testlerini yeniden kosup sayi artirilmadi; asagidaki TRX'ler tekrar okunarak
1382/279 ve 54/56 eslesmesi teyit edildi. Kanit ayrintisi ana matriste.

Kaniti korunan kok: `C:\SecureOpsBuild\validation\continuation19`.
Release build sifir uyari/hata; format verify ve OpenAPI snapshot karsilastirmasi
gecti. Normal birim: 1382 basarili. Normal entegrasyon: 279 basarili, 56 opt-in
atlandi. Bu b596058 kanitinin son normal dosyalari `tests/committed-unit.trx`
ve `tests/committed-integration.trx` dosyalaridir; eski `continuation-*-pass.trx`
adlari son committed kosu olarak kullanilmaz. Ayrik kosulari toplama eklemeyin.

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
