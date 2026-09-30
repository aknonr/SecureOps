# rc6.26 sonrasi kalan isler ve TEST kabul adimlari

## Durum ve kapsam

Bu belge tek guncel Turkce operator girisidir. Son durum: 30 Eylul 2026.
Onceki snapshot'lar asagida tarihsel kanit olarak korunur.

**Siradaki tek SQL kapisi:** 022/023 icin verdiginiz dar tanim karsilastirmasi
eslesiyor; tekrar sorgulanmayacak. 024 halen `NotVisibleOrAbsent`.
Operator oturumu sonucundan API izni cikarilmaz. Hazir dar sorgu:
[`Read-SdmApiEffectivePermissions.sql`](../scripts/diagnostics/Read-SdmApiEffectivePermissions.sql).
Yalniz normal API surecinin mevcut SQL baglantisinda 13 etkili izni okur:
alti 022/023 nesnesinde SELECT/INSERT, executions tablosunda ayrica UPDATE.
1 izin var, 0 izin yok, NULL belirsizdir. Sonuclar ve normal surec/ayar kaynagi
kaniti ozel kanalda tutulur. Sorguyu kendi SSMS/sqlcmd oturumunuzda calistirmak
API kaniti olmaz; runas/EXECUTE AS veya kimlik/grant degisikligi yapmayin.
Kurulu API ve muhurli adayda bu sorguyu calistiran endpoint yoktur;
`health/persistence` yalniz SELECT 1 yapar. **Once** normal API sureci icinde,
mevcut baglantiyi degistirmeyen salt okunur destek yurutme yolunun ayri inceleme
ve onayi gerekir. Bu belge helper kurma veya hedef degisikligi talimati degildir.

SQL uygulama operatoru sizsiniz; baska bir kisiden varsayilan DBA makbuzu
beklenmiyor. Gecmis execution log/change notu verilmedigi icin mevcut degil
olarak kayitli. Daha sonraki 024-only uygulama ancak normal API izin kaniti,
kabul edilmis eslesik payload, ayri onayli change, yedek/recovery noktasi,
incelenmis 024/grant ve durdurulmus yazilarla yetkilendirilir. 023 farki veya
kismi/mevcut 024 durumunda durun; 022/023 tekrar uygulanmaz. Su anda SQL
uygulama, bayrak acma veya deploy yetkisi yoktur.

Service Accounts pin'i ayri yerel integration dalina alindi; yeni Windows
kaniti ve kalan sinirlar [tek durum kaydinda](integrated-test-activation.md)
ve [integration kanitinda](service-accounts/INTEGRATION-20260930.md).
Bu, kurulu rc6.26'yi veya muhurli deda848 paketlerini degistirmez. Sonraki
follow-up alinmadi; Service Accounts pilotu, Jira-only tek OR kabulunden ayridir.

**Guncel In Use karari (IU-07 / E-08):** Hedef yalniz uygun WASAS aktivitesini
onaylayip sureci ilerletmektir; tum OR'yi kapatmak degildir. Sonraki ekip beklerken
OR acik kalabilir. Guncel [operator adimlari](#wasas-aktivitesi-operator-adimlari)
bu ayrimi, kaydetme/rapor/kimlik duzeltmelerini ve kalan gercek tasima sinirini aciklar.
**Guncel daraltma:** Yalniz islem yapilabilir / WASAS adimi tamamlanmis /
dogrulama bekleyen ayrimi icin asgari kaynak durumu kapsamdadir. Sonraki ekip
ayrintilari ve kapsamli surec zaman cizelgesi ertelendi; teslimat/kabul kosulu degildir.
Yerel inceleme urunu `0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.f530ba994f6c`;
HEAD + commitlenmemis degisikliklerdir, kurulu rc6.26 degildir. Kaynak/hash/test/paket
kaniti tek kayit `integrated-test-activation.md` E-08 bolumundedir. Hedefte bayrak,
SQL veya kurulum degisikligi yapilmadi. E-05/E-06/E-07 korunur.

E-08 sonrasi arsiv incelemesi: verilen v19 XLSX'in zarfla birebir eslesmesi
dogrulandi. Bozuk zarf icin hata siniflandirma duzeltmesi ayri yerel build
`wip.4179fca3c22c` olarak test edildi (204 In Use + 1 API); paketlenmedi/kurulmadi.
E-08 degistirilmedi. [Arsivden ayni Excel'i indirme](#excel-arsivde-nerede-ayni-surum-nasil-indirilir)
ve [kaynak sahibine iletilecek metin](#kaynak-sahibine-iletilecek-kisa-metin-iu-05)
asagidadir; tam kanit tek durum kaydinin E-08 follow-up bolumundedir.

**Tarihsel In Use manuel OR kontrolu (IU-06 / muhurli E-07):** Otomatik son OR okumasi ertelendi.
Yeni kaynak davranisi, ek dogrulanip BPM istegi kabul edilince operatorden kaynak
sistemde OR durumunu kontrol etmesini ister. Zaman asimi/belirsizlik tekrar istek
uretmez; acik ret basarisizliktir. Kaynakta kontrol ettim eylemi ayri onayla kimlik
ve UTC zamanini manuel kanit olarak kaydeder; sistemce dogrulanmis OR kapanisi
degildir ve tekrar gonderim kilidini kaldirmaz. Onaydan once OR, rapor surumu,
arsiv hash'i ve eklenecek dosya gosterilir. Kaynak URL'si uydurulmaz.

Bu davranis kurulu rc6.26 veya muhurli E-06 adayinda yoktur. E-07 ayri kaynak/hash
kimligi tasir; onceki aday/tanilama degistirilmez. Bu degisiklik yeni SQL istemez;
katalog icin onceki 024 geregi surer. IU-05 ek icerigi ve gercek kosullu guncelleme
sozlesmeleri halen eksik oldugundan hedef bayraklari kapali kalir. Son OR okuma
sozlesmesi tek basina engel degildir. Ayrinti ve yeni test/paket kaniti tek kayitta:
[integrated-test-activation.md](integrated-test-activation.md). Kurumsal kapatma,
guvenlik izni ve son tarayici kabulunun yapildigi ileri surulmez.

**Guncel guvenlik beklemesi (E-06 / SEC-01):** Operator SCCM console kurulumunu
bildirdi; uyumluluk ve collection erisimi halen dogrulanmadi. Ayri SCCM tanilamasi
normal Worker hesabinda JSON uretmeden sonlandi. Cyber Defense Falcon process-killed
olayini teyit etti. Inceleme/izin talebi acildi, fakat uygulama paketi guvenlik
ekibine henuz verilmedi; Detection ID, teknik neden ve karar yok. False positive
veya basarili SCCM okumasi denmez. Asagidaki onceki tanilama komutunu onay gelmeden
tekrar calistirmayin; launcher/hesap/host/yol veya policy degistirerek asmayin.

Eski tanilama payload'i/hash'leri korunur. Birlesik Worker ayri build/hash tasir;
tanilama izni farkli final binary veya servis kipi izni degildir. Iki paket icin de
guvenlik teslim/alindi/onaylandi iddiasi yoktur. Operator once olay kimligi/zamani,
etkilenen dosya hash'i ve teknik ayrintilari istemeli; korunan tanilama ile ayri
birlesik aday manifestlerini onayli kanaldan incelemeye vermelidir. Kurulum yoktur.

**Somut birlesik inceleme teslimati:**
`C:\SecureOpsBuild\delivery-review\2026-09-21-consolidated-test`.
API/UI/Worker ayni `0.1.0+5c986a96f1f6639e47bf1432a87c3ac55e98054d-wip.9fa51778d1de`
surumundedir. `packages/` altinda API, UI, Worker ve 024-only DBA inceleme ZIP'leri;
`candidate-metadata.json`, `review-artifacts.sha256` ve `manifests/` altinda tam
hash'ler bulunur. Bu HEAD arti kayitli uncommitted urun girdisidir, yeni commit
veya numarali release degil. Temiz commit isteyen release kurali degistirilmedi.

Bu build'in yeni format/build kontrolu gecti (0 uyari/hata); 1425 birim ve
279 entegrasyon testi gecti, 56 opt-in atlandi. Onceki izole kanitlar bu sayiya
eklenmedi. Tam dosya/ZIP taramalari gecti; browser/SCM/kurumsal kabul degildir.
OCO'da whitespace template ile literal aciklama secimi validator ile esitlendi;
bakim bitisi/restart ve servis/kapsam ayrimi odakli testte korundu.
`configuration/review-tr.md` ayar farkini, `DBA/review-tr.md` 024 sinirlarini,
export edilmis servis proseduru devir/geri donusu aciklar. Kurulum yapmayin.

`security/preserved-diagnostic.zip` eski yerel tanilamanin degismeyen 531 dosyasini
tasir; eski dizin degismedi. Bu yeni tasima ZIP'inin hash'i orijinal gonderim
container'inin hash'i degildir. `security/worker-comparison.json` yeni Worker ile
bes dosya farkini verir; farkli binary izni ayrica gerekir. Hedefteki dosyalarin
yerel manifest ile eslesmesi halen operator kaniti bekler. Iki paket de henuz
guvenlik ekibine verilmis veya onaylanmis olarak kayitli degildir.

**Guncel kaynak hatasi (E-05 / OCO-02):** Kuyruk hazirligi kapanmistir; tekrar
kurulum veya profil bayragi acma adimi degildir. Sahibin yapistirdigi konsolda
ActionPreferenceStopException / AnnouncementSourceCollectionUnavailable var,
fakat asil ErrorRecord ve komut yok. Bes ayri JobId tek isin retry'lari sayilmaz.
Hedef kok neden ve basarili cihaz/servis sayisi henuz kanitlanmadi. Yeni dar
tanilama ve yerel PowerShell host bagimlilik onarimi mevcut teslim kapsamindadir;
eski rc6.26'ya kurulmus davranis veya yeni ara release degildir.

## SCCM: bir sonraki kontrollu kontrol

**SEC-01 nedeniyle beklemede:** Bu komut dizisi izin sonrasi prosedurdur; simdi
yeniden deneme talimati degildir. EXE apphost hash'i ayni kalsa bile managed DLL ve
bagimlilik manifesti degisen aday icin ayri guvenlik kapsam teyidi gerekir.

Yerel tanilama girdisi `C:\SecureOpsBuild\validation\sccm-diagnostics-20260921\staging\worker`.
Kimlik `5c986a9...-sccm-diagnostic-wip`; commit'li urun veya kurulum paketi degil.
531 dosya payload taramasindan gecti. Guncel 1422 birim/279 entegrasyon testi gecti,
56 opt-in atlandi; published runspace ve sentetik missing-module tanilamasi da
gecti. Tam hash/TRX listesi ayni kokte `local-evidence.json` icindedir. Asagidaki
hedef adimlari operator tarafindan henuz uygulanmadi.

1. Mevcut Worker'i, kuyrugu, kimligi ve Test ayarlarini degistirmeyin. Bes JobId'yi
   ayri kayitlar olarak saklayin. Secilen NonProd okumasini is sahibiyle belirleyin;
   yeni OCO isleri acarak hata aramayin. Salt kuyruk hazirligini tekrar sinamayin.
2. Yerel `sccm-diagnostics-20260921` kaniti, eski engine-only payload'da yerlesik
   Management modulunun yuklenemedigini gosterdi. Yeni kaynak Microsoft.PowerShell.SDK
   7.4.18 ile bu modulleri tasir. Bu hedef kok nedeni olarak kabul edilmez: hata
   Import-Module asamasinda daha once de olusabilir. ExecutionPolicy, PSModulePath,
   SCCM izinleri veya sunucu dosyalari tahminle degistirilmez.
3. Incelenmis tanilama payload'ini normal Worker'in uzerine degil, ayri korunan
   dizine alin. Kaynak kimligi/hash listesini muhafaza edin. Eski rc6.26 bu yeni
   switch'i desteklemez. Normal Worker ile ayni etkin hesap, Test ortami ve mevcut
   startup override'larini kullanin; sifre/token'u komuta veya rapora eklemeyin.
   Kurulu Worker'in mevcut ayar dizinini `--contentRoot` ile gosterin; gizli
   dosyalari tanilama dizinine kopyalamayin. Yeni servis/Worker hostu baslatilmaz:

   ```powershell
   & '<reviewed diagnostic directory>\SecureOps.Worker.exe' --environment Test --contentRoot '<installed Worker configuration directory>' --sccm-diagnostics --SccmDiagnosticProfile NonProd
   ```

4. Bu tek, sinirli SCCM collection okumasidir; job/SQL/TH servis sorgusu, mail veya
   kaynak kapatma yapmaz. JSON'daki Stage, Command, ErrorId/ErrorIdHash, Category,
   exception/HResult, line/offset ve PowerShell runtime/hash degerlerini donun.
   Kimlik ve baslangic override eslesmesini operator teyit etsin. Exit 2 partial/
   failed sonucudur; cihaz adlari veya ham kaynak cevabi rapora konmaz. Normal
   `--diagnostics` ile birlikte kullanmayin. Yeni payload engine hash'i kurulu
   System.Management.Automation.dll hash'iyle ayrica karsilastirilmalidir.
5. Legacy script satir 147-159 console manifest'ini SMS_ADMIN_UI_PATH uzerinden
   yukler, mevcut modulu/CMSite surucusunu kullanir ve Get-CMDevice calistirir.
   Yeni provider taze Restricted runspace, modulu adiyla yukleme, Private surucu ve
   ErrorAction Stop kullanir. Script'in calistigi gercek PowerShell surumu/hesabi
   dosyadan cikmaz. SCCM sahibi kurulu console/modul surumunu ve ayni hesapta
   gorunurlugunu teyit etsin; basarisiz asamaya gore yalniz ilgili fark onarilsin.
   ErrorId tanimsiz ise hash ve category yeterli olmayabilir: o zaman sadece ilgili
   ErrorRecord'un kaynak sahibi tarafindan maskelenmis hata kodu/nedeni istenir;
   ham TargetObject, PositionMessage, script satiri veya credentials paylasilmaz.
6. Onarimdan sonra bir acikca secilmis OCO/profile JobId'nin terminal sonucunu,
   gercek cihaz sayisi, tekil servis sayisi, eksik/basarisiz servisler, kaynak tarihleri
   ve ayri restart incelemesini kaydedin. Tanilamadaki cihaz sayisi tek basina tam
   SCCM/TH yolculugu degildir. Mevcut bes isi otomatik tekrar gondermeyin; once her
   birinin durumunu uzlastirin. SMTP uc bayragi false, PrepareSchema=false kalir.

## Bes profil ve tarih duzeltmesi

Ozel inceleme kok dizini:
`C:\SecureOpsBuild\validation\sccm-diagnostics-20260921`.
`profiles-review.json` tek nested aday; `worker-profile-merge-review.json` ayni
adayin flat key'leri; `api-profile-merge-review.xml` eslesen environmentVariable
yapraklaridir. `profile-provenance.json` legacy satirlarini ve onay durumunu verir.
Bunlar **inceleme girdileridir, mevcut ayar dosyasinin yerine konmaz**.

Bes mapping sahibin listesiyle ve legacy 122-126 ile eslesti. Her profil kendi
Impact metnini ve legacy Checks metnini korur (225-252). NonProd Scope onaylidir;
diger dort Scope collection adindan turetilmis acik inceleme onerileridir, eski
script'te sabit Scope metni yoktur. Dinamik servis listesi ayri ve gorunur kalir.
Revision ve To/Cc/HighPriority merge'de atlanir, mevcut etkin degerleri silinmez.
rc6.26 DLL binder/catalog kontrolu bes profili nested/flat/XML icin Configured ve
ayni fingerprint ile dogruladi; bu is metni onayi veya SCCM baglantisi degildir.

**Onceki NonProd DescriptionTemplate onerisi yururlukten kaldirildi:** legacy
EndDate'i restart olarak basar. Sahibin e-postada bildirdigi bakim bitisi 05:00,
restart 03:15 ayrimi bu esitlemenin yanlisligini gosterir; bu oturum ham e-postanin
butunlugunu dogrulamadi. Aday metin WorkEnd'i yalniz planli bakim bitisi olarak
etiketler ve restart'i ayri incelemeye birakir. Yeni bir kaynak alani veya template
token'i uydurulmadi. Gercek restart, mevcut ayri alana onayli kaynaktan/manual
girilir; eski immutable hazirlik/EML yeniden yazilmaz.

Operator sirasi: etkin API web.config ve Worker Test ayarlarini korunan konuma
yedekle; mevcut override/Revision/fingerprint'i kaydet; dort Scope ve duzeltilmis
aciklama icin is sahibi onayi al; degisen icerige yeni ortak revision onaylat.
Sadece incelenmis yapraklari birlestir, ayni dosyada flat/nested duplicate tutma.
JSON/XML syntax ve kurulu binary profil dogrulamasini calistir. API environment
degiskenleri ve Worker JSON/CLI onceligini ayri kontrol et; dosyalarin benzemesi
etkin eslesme degildir. Bakim penceresinde yeni kaynak gonderimlerini durdur,
aktif isleri terminal/unknown durumlariyla uzlastir, Worker'i kontrollu durdur,
API ayar yuklemesini ve Worker Test yeniden baslamasini koordine et. Bes profil
Revision/fingerprint ve source/queue ayarlari eslesmeden gonderimleri acma.
Unknown sonuc yeni anahtarla tekrar edilmez. Bu agent hedef restart yapmadi.

Dinamik profil yonetimi bu onarimin kosulu degildir. Mevcut sozlesme bes isimlik
allowlist ve startup options kullanir. Bugun tek korunan/onayli master'dan iki
yuzeyi uretmek ve etkin revision/fingerprint karsilastirmak mumkundur; ortak
revision deposu veya atomik hot reload uygulanmis degildir. Ileride tek revision'li
kaynak icin dogrulanmis atomik snapshot gerekir; yeni UI/CRUD burada acilmadi.

## Onceki servis teslim kaniti (E-04)

Asagidaki staging ve 1411/279 test kimlikleri yeni SCCM degisikliginden oncedir;
guncel tanilama kaniti ustteki ayri dizindedir. Kabul edilmis servis calismasi
korunur; bu ara kontrol yeni numarali servis release'i degildir.

**Yeni hedef kaniti:** Sahibin dogruladigi uc entry DLL ProductVersion rc6.26 /
028cbd2; tam hedef payload hash'leri yeniden dogrulanmadi. Normal Worker kimligi,
dizini ve Test ortami bildirildi; yalniz appsettings.Test.json var. API/Worker
ayarlari onarilmis NonProd dahil eslesiyor, kuyruk hazir ve bir matching Worker
goruluyor. Yeni ham rapor/zaman/hash burada alinmadi. Bu SCCM/TH is sonucu degil;
mail ve In Use completion halen kapali. Onceki false ayarlar artik guncel hedef
ayari olarak tekrar uygulanmaz. Ekip mevcut WASAS'i kullaniyor.

**Yeni yerel teslim kapsami:** Native Windows Service/konsol/tanilama host duzeltmesi
calisma agacinda; son commit HEAD 5c986a9, son commit'li urun c12abf2 korunur.
Yeni servis kurulmus degil. [Servis devir/kurulum/geri donus proseduru](worker-service-operations-tr.md)
yalniz onayli ardil payload icindir; eski rc6.26 konsoluna uygulanmaz. Yeni tek
Worker ayari WorkerHosting:DataDirectory; onayli mevcut yerel yol operatorce
belirlenir. API/UI ayarlari, SMTP kapali bayraklari, PrepareSchema=false korunur.
Bu host icin SQL migration yok; urun katalog deltasi halen 024 ve hedefte teyitsiz.
IU-05 engeli kapsamli Worker/fixes release'ini tek basina durdurmaz; SCM, UI ve
exact-payload kabul kapilari kapanmadan numarali aday verilmeyecektir.

Yerel dogrulama: Release build 0 uyari/hata, genel format kontrolu gecti.
Son birim kosusu 1411, staged-build entegrasyon 279 gecti/56 opt-in atlandi.
Ilk entegrasyonun FileSystemWatcher.Dispose kapanis hatasi korunur; tam tekrar
ve staged-build kosusu gecti. Yayinlanmis EXE, baska current directory ve yalniz
sentetik Test dosyasiyla diagnostics kontrolunu host/log/lock baslatmadan gecti.
Numarasiz kabul girdisi `C:\SecureOpsBuild\validation\worker-service-20260921\staging`;
kimligi `5c986a9...-worker-service-wip`, commit'li urun veya kurulum ZIP'i degildir.
Tam hash/TRX kaydi ayni kokte local-evidence.json. Bu oturum yonetici degil;
SCM start/stop, tam logoff ve crash/recovery hedef/izinli runner kapilari acik.

20 Eylul 2026 tarihsel kimlik ve kanit ayrimi:

| Kimlik | Deger ve kanit siniri |
|---|---|
| Guncel yerel testli urun/derleme | `c12abf29a60d2087728cd1e34879c0360a57c1c9`; sistem-durumu duzeltmesi dahil. Numarali paket, kurulum veya browser kabul kaniti degil |
| Sonraki belge kapanisi | Yalniz bu giris ve ana matrisin sonraki Git commit'i; derlenmis urun kaynagi degil |
| Tarihsel baslangic | `03b0c048d50cf926b5640116533df39c8685a37d` |
| Gelen testli urun kaynagi | `b596058fa0e1f82b278511f9a9e1c032c1130045`; kurtarma sirasinda uc Release DLL ProductVersion/hash ve TRX eslesti. Sonraki sistem-durumu UI duzeltmesi bu staging'de yok |
| Gelen belge kapanisi | `69a9b839614d97aaed5ea9f600cc4841cf01d24d`; b596058 sonrasinda sadece iki Markdown dosyasi degismis |
| Korunan/kuruldu bildirilen aday | rc6.26, `028cbd2e4ec7068d71a33088d7c651e4df21644a`; alti yerel paket hash/boyutu yeniden eslesti. Hedef kurulum bildirimi bagimsiz kurulum kaniti degil |
| Gereken schema (20 Eylul tarihsel durum) | Yeni katalog icin 024; o tarihte 022/023 yalniz kuruldu bildirimiydi. 29 Eylul nesne kaniti ve SQL yurutme operatorunun guncel adimlari asagidadir |
| Hedef kaniti | Saglanan API JSON: 20.09.2026 00:54:02 UTC capture, 00:54:21 UTC kaynak kontrolu. Kaynak/mail kapali, Worker kontrol edilmemis, 6 asset dogrulanmis, arsiv okuma dogrulanmis/yazma sinanmamis. Canli ajan gozlemi veya Worker raporu degil; urun surumu yok |
| Bu devam | Onceki belge/ihrac/tarayici degisiklikleri korunuyor; sistem-durumu paneline dar UI duzeltmesi eklendi. Yeni host/tarayici/paket kabul sonucu yok; kurulmus davranis degil |

Tek gereksinim/kalan-is kaydi [ana matristir](integrated-test-activation.md).
Onceki [test kaniti](continuation-b596058-evidence.md) urun kaynagini ayri tutar.
Kaynak/SMTP destek proseduru [rc6.26 kilavuzudur](rc626-mail-source-activation-tr.md).
Bu kaynak degisiklikleri rc6.26 icinde yoktur. Yeni numarali ZIP yoktur; final
tarayici/paket kapisi kapanmadan aday hazir denmez. Ekip duyurusu gonderilmedi.

## NonProd profil incelemesi: rc6.26 ve c12abf2

Tarihsel onarim proseduru: E-04 ile profil/API/Worker ayar eslesmesi ve normal
Worker kuyruk hazirligi artik bildirildi. Asagidaki merge adimlarini tekrar
uygulamayin; yeni isletim adimi ustteki servis devir prosedurudur.

20.09.2026 dar inceleme: rc6.26 / `028cbd2` secenekleri, profil dogrulayicisi,
allowlist, kaynak-proposal ve tanilama kodu c12abf2 ile ayni. DI dosyasindaki
sonraki In Use eklemeleri profil binding'ini degistirmemis. Kurulu surum halen
operator bildirimidir; yerel rc6.26 DLL/manifest eslesmesi hedef envanteri degil.
Bu inceleme iki belgeyi calisma agacinda gunceller; HEAD 5c986a9 ve testli urun
c12abf2 korunur. Yeni urun derlemesi, commit, ZIP veya hedef ayar degisikligi yok.

Yalniz CollectionId + Label verilen NonProd, `Unconfigured` ve tam olarak
`Scope, Impact, Checks, Description` eksiklerini verir. Revision varsayilani
metin `"1"` oldugundan eksik degil. Tanilama en az bir `Configured` profil
bulamazsa `ConfigurationMissing / AnnouncementSource:Profiles` dondurur;
collection/fingerprint basmak bundan once yapilir, SCCM sorgulandigi anlamina gelmez.
Diger provider/override degerleri varsa gercek eksikler farkli olabilir.

| Profil alani | JSON/.NET turu ve varsayilan | rc6.26 kontrolu / kaynak |
|---|---|---|
| Revision | string, `"1"` | Bos/whitespace olamaz, en cok 64 karakter. Sayisal enum degil; mevcut etkin revision korunur |
| Label | string, `""` | Bos, kontrol karakterli veya 120'den uzun ise NonProd gorunen adina duser; profili tek basina bozmaz. Paylasilan etiket korunur |
| CollectionId | string, `""` | 1-64 ASCII harf/rakam/`-`/`_`; paylasilan deger legacy script satir 122 ile eslesti, degistirilmedi |
| Scope | string, `""` | Whitespace olmayan, en cok 4000 karakter. **Sahibin 20.09.2026 mesajiyla onaylandi**; ozel merge dosyasinda aynen korunur, dinamik servis listesinin yerine gecmez |
| Impact / Checks | Her biri string, `""` | Her biri whitespace olmayan, en cok 4000 karakter; Checks dizi degil. Legacy NonProd satir 225/227 metinleri ozel inceleme bloguna aynen alindi |
| Description | string, `""` | Kullanilabilir DescriptionTemplate yoksa zorunlu, en cok 4000 karakter. Literal alternatif |
| DescriptionTemplate | string, `""` | En cok 3800 karakter; yalniz tam `{WorkStart}` ve `{WorkEnd}` belirtecleri; bunlar cikarilinca baska `{`/`}` kalamaz. Eski satir 226 donusumu restart anlami nedeniyle yururlukten kaldirildi; ustteki tarih duzeltmesi gecerlidir |
| To / Cc | string dizisi, `[]` | Her biri en cok 50 gecerli, yalniz adres iceren metin; gorunen ad/whitespace yok, adres 4-254 karakter. Bos diziler kaynak profili icin gecerlidir; alici eklenmedi |
| HighPriority | boolean, false | Yalniz inceleme metaverisi; mail baslatmaz. Eklenmesi gerekmiyor |

NonProd istek adi allowlist'te buyuk/kucuk harfe duyarlidir. JSON/property
binding'i case-insensitive olsa da profili yeniden adlandirmayin. String alanlari
metin olarak verin; null, dizi veya dolu yer-tutucu ile dogrulama gecisi uretmeyin.
Template kullanilmiyorsa alani atlayin veya `""` kullanin; whitespace template
kullanmayin. Kurulu rc6.26'da proposal Length ile secilir; birlesik adayda bu fark
duzeltildi ve validator gibi IsNullOrWhiteSpace ile literal Description'a donulur.

Ozel dosya: `C:\SecureOpsBuild\validation\nonprod-profile-review-20260920\nonprod-review-blocked.json`.
Bu eski inceleme blogunda Scope bilerek bostur, **tarihsel/kurulamaz**. Parola,
Authorization, alici, SMTP ayari veya tarih icermez. Revision atlanarak kodun
mevcut `"1"` varsayilani kullanildi; daha yuksek provider'daki revision silinmez.
Script satir 75-86 servisleri toplayip tekillestirir; 291-292 bunlari sistem/uygulama
kapsami olarak basar. Sabit profil Scope metni vermez. `{Services}` eklemek veya
etki metnini Scope'a kopyalamak onay yerine gecmez. Sahip artik sabit Scope
metnini sagladi; dinamik servis listesi halen ayri ve gorunur teklif alanidir.
Legacy aciklamadaki restart ifadesi yeni bir restart zamani kaniti degildir;
WorkStart/WorkEnd OCO'dan ve gereken offset incelemesinden gelir, tarih uydurulmaz.

### Onayli birlestirme ve kontrollu operator sirasi

Yeni ozel kok: `C:\SecureOpsBuild\validation\nonprod-config-repair-20260920`.
`worker-profile-merge.json` tam dort duz `AnnouncementSource:Profiles:NonProd:*`
yapragidir. `api-environment-merge.xml` Announcements/source/provider/site/selectors,
ayni profil (Revision="1"), Hangfire/schema/queue/PrepareSchema ve uc kapali mail
bayragini iceren 23 env girdisidir. Tam dosya veya ortam kapsayicisi yerine konmaz.
Scope sahip mesajindan; Impact/Checks legacy 225/227'den; DescriptionTemplate
226'daki iki tarih ifadesinin desteklenen belirteclere donusumunden gelir.

1. Bakim penceresi ve normal foreground Worker sorumlusunu belirleyin. API fiziksel
   yoluna bagli gercek AppPool'u saptayin; ad tahmin etmeyin. Yeni kaynak istegi
   girisini durdurun; bekleyen/retry/recovery islerini inceleyin. Bos kuyruk veya
   tam olarak onayli isler yoksa normal Worker baslatmayin: mevcut isler otomatik
   tuketilebilir. Unknown/Partial islerde kor yeni anahtarla deneme yapmayin.
2. Mevcut Worker'i Ctrl+C ile kontrollu durdurup bitisini bekleyin; ikinci instance
   acmayin. Yalniz API AppPool'u durdurun; web.config kaydi otomatik recycle
   tetikleyebilir. UI AppPool, IIS genel restart, ACL ve SQL degisikligi yok.
3. API web.config ve Worker appsettings.json/appsettings.Test.json dosyalarini
   webroot disinda erisimi zaten sinirli yedek alanina alin; erisim korumasini ve
   hash'leri dogrulayin. Ortam/CLI override'larini guvenli kaydedin; secretlari
   sohbete/loga cikarmayin. Yedekleri API/UI yayin dizinine koymayin.
4. Worker'in mevcut duz JSON'una dort yapragi birlestirin; CollectionId/Label,
   secretlar ve ilgisiz ayarlar korunur. API'de mevcut
   configuration/system.webServer/aspNetCore/environmentVariables altindaki ilgili
   adlari guncelleyin veya yoksa ekleyin. Kapsayiciyi, processPath/arguments veya
   hostingModel'i degistirmeyin. ':' ve '__' ile ayni anahtarin ikinci kopyasi yok.
5. UTF-8 JSON ve XML sozdizimini dogrulayin. Etkin oncelik:
   appsettings.json < appsettings.Test.json < ortam < CLI; yaprak bazli birlesir.
   Normal baslangictaki eski false CLI degerlerini inceleyin; tum override'lari
   korlemesine kaldirmayin. Uc SMTP bayragi false, PrepareSchema=false kalmali.
   API IIS ortami Worker'a miras degildir; UI'ya secret veya entegrasyon eklenmez.
6. API AppPool'u baslatin, yeni is girisini henuz acmayin. Yetkili salt-okunur API
   tanilamasini alin. Worker'i normal dizin/Test/normal onayli kimlik ve ayni
   override'larla `dotnet .\SecureOps.Worker.dll --environment Test --diagnostics`
   kipinde inceleyin. Bu kip normal job server baslatmaz; profil duzelince SQL
   heartbeat SELECT yapabilir. PAM tanilama kimligi normal Worker kaniti degildir.
7. Mevcut Compare-OperationsReadiness.ps1 ile yeni raporlari karsilastirin:
   source/provider/profile revision-fingerprint, DB/schema/queue ve kapali mail
   degerleri eslesmeli. Worker'in bos asset/arsiv alanlarini API ayarlarini
   kopyalayarak doldurmayin; rol farklari korunur. Audit dizini In Use arsivi degil.
   Worker duruyorken taze heartbeat bulunmamasi beklenebilir; normal baslatma
   oncesinde tum tanilamanin Ready olmasini sart kosmayin, profil hatasini ayirin.
8. Yalniz kuyruk/is yetkisi ve etkin ayar kapisi gectiyse, ayni dizin/kimlik/Test
   ve onayli override'larla tek foreground Worker'i normal baslatin. Konsol
   sorumlusu, oturum/pencere, kuyruk-heartbeat izleyicisi ve restart sahibi kayitli
   olsun. Fingerprint/Ready SCCM/TH/SMTP kabul degil; secili OCO isi ayrica bekler.
9. Eksik profil, beklenmeyen kimlik/kuyruk veya acik SMTP varsa DUR. Iki bilesen
   duruyorken eslenik ayar yedeklerini geri alin; API sonra kuyruk kapisiyle tek
   Worker sirasi korunur. DLL downgrade, yeni release veya migration gerekmez.

Sohbet Worker capture: 20.09.2026 02:14:40.6917339 UTC; kaynak kontrolu
02:14:40.8524852 UTC. Ozgun dosya butunlugu dogrulanmadi. ConfigurationMissing /
AnnouncementSource:Profiles ve NotChecked/0/null okundu; heartbeat kontrolu yok.
Mevcut karsilastirici exit 2 ile 11 ayar farki verdi; DB/TH/mail-policy parmak
izleri eslesiyor, baglanti kaniti degil. API E-02 halen source/Hangfire kapali.
Onayli aday iki surumde 15'er etkisiz assertion gecti: Configured, eksik yok,
API/Worker profil parmak izi ayni. Onceki iki alanli profilin parmak izi de sohbet
raporuyla eslesti; Revision="1" varsayilani dogrulandi. Hedefte uygulanmadi.

Ozel etkisiz kontrol: ayni kokun `check/ProfileCheck.csproj` araci, rc6.26
manifestiyle eslesen DLL'leri ve paket binder'ini yukler; host/DI altyapisi,
SQL, SCCM, Turuncu Hat veya SMTP baslatmaz. Iki surumde ayni 22 assertion gecti.
`packaged-binder-baseline-check.json` ve `packaged-binder-current-check.json`
sonuclari: yalniz iki alanli profil 4 eksik, inceleme blogu yalniz Scope eksik;
sadece bellekte sentetik Scope eklenen kontrol Configured. Bu kontrol metni
operator dosyasina yazilmadi; kurumsal kabul, yeni urun veya release degil.

## Kullanici akisindaki devam

### Excel arsivde nerede, ayni surum nasil indirilir?

Arsiv duzeni `<InUseReports:Directory>/<WASAS-kayit-GUID>/<surum>.json` seklindedir.
JSON icindeki `Content`, XLSX dosyasinin Base64 kodlu **asil baytlaridir**;
`Sha256`, `Size`, `Version`, `PreparedBy` ve `PreparedAt` bunlara eslik eder.
Ayri `.xlsx` dosyasi olusturulmamasi beklenen davranistir. `.json.lock` Excel
degil, eszamanli erisim kilidi dosyasidir; silinmez. JSON'u XLSX diye yeniden
adlandirmayin, Content'i duzeltmeyin veya eski raporu yeniden uretip ustune yazmayin.

1. Kurulu In Use ekraninda ilgili OR'yi acin; `Onceki WASAS raporlari` bolumunde
   istediginiz surumu secip `Arsivden indir` kullanin. Guncel onizlemede arsivlenmis
   rapor icin `Arsivi indir` de ayni yolu kullanir. Surum secimi, yeni rapor uretmek
   veya kaynak sisteme eklemek degildir. Ekran etiketleri kurulu surume gore degisebilir.
2. E-08/katalog 024 kabul edilip kuruldugunda `In Use -> Rapor gecmisi` icinden OR
   ve surumle arayip `Indir` kullanilabilir. Kurulu rc6.26'da katalog oldugunu
   varsaymayin. Katalog eksik kapsami, dosyanin arsivden silindigi anlamina gelmez.
3. Tarayici XLSX'i kendi indirme dizinine yazar. Sunucudaki arsiv yeri degismez.
   Eski rapor indirilince eski sayfalar/hucreler/hash aynen kalir; yeni duzen ancak
   yeni rapor surumunde kullanilir. Indiren kisi asil hazirlayani degistirmez.
4. Destek icin surum, zaman ve SHA-256'yi birlikte saklayin. API yolu mevcut
   yetkili `POST /api/v1/in-use/{record-GUID}/report`; `expectedVersion` guncel
   yerel kayit surumu, `archive=false`, `archivedVersion` istenen eski surumdur.
   Uygulama bunu guncel yetki/surum/audit denetimiyle yapar; elle SQL ya da dosya
   duzenlemek gerekmez. Catismada kaydi yenileyip ayni eski surumu tekrar secin.

22 Eylul'de verilen uc zarf ve indirilen v19 dosyasi ozel dizinde karsilastirildi.
v19, zarftaki 7.653 baytla birebir eslesiyor; dort sunucu/12 cevap mevcut. Bu eski
alti sayfali dosyada ham hazirlayan/inceleyen GUID'leri ve duzensiz kolonlar var;
E-08'in dort sayfali yeni ihracati olarak sunulmaz. Tarihsel dosya degistirilmedi.
Tek kayit IU-07-D ve ozel `inuse-archive-20260922` kaniti ayrintiyi tutar.

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

### Kaynak sahibine iletilecek kisa metin (IU-05)

> Merhaba, WASAS yalniz kendi uygun aktivitesini onaylayip sonraki ekibe ilerletecek;
> tum OR'yi kapatmayacak. Mevcut uploadattachment ve BPM update istekleri biliniyor.
> Yeni yazma denemesi yapmadan, belge veya onceden alinmis maskeli orneklerle lutfen:
>
> **A. Yukleme, zorunlu alanlar ve onay icin gerekli sozlesmeler (IU-05)**
>
> 1. **Ek dogrulama:** Mevcut bir OR ekini hangi desteklenen islemle bulup icerigini
>    okuyabiliriz? Islem yolu/metodu, OR-parent ve attachment kimlik alanlari/turleri,
>    liste/lookup ile download veya otoritatif hash cevabinin birer ornegini paylasin.
>    Upload cevabi kaybolursa ayni baytlarin zaten yuklendigini nasil dogrulariz?
>    Ayni dosya adi yeterli olmadigindan 0/1/cok eslesme kurali da gerekli.
> 2. **Zorunlu alanlar:** p_emb_dynamic_case_orff -> DCM_DynamicCaseProperty
>    p_dc/p_dcctp=4463,4464 icin keyed SET/KEY hedef, p_value ve surum temsilini
>    gosterin. 4463 Application Server ve 4464 TEST/PROD gercek wire degerleri
>    nelerdir? Herhangi bir PROD sunucu varsa PROD, diger bilinen ortamlar icin
>    TEST is kurali onayli; ekran etiketi yerine API degeri gerekli. Beklenen
>    surum/deger ile atomik guncelleme istegi ve basari/konflikt yaniti gerekiyor.
> 3. **WASAS onayi:** BPM_Actvty icin ayni OR, model 103626/103627, status 1,
>    grup 68 kosullariyla bulunan tek aktivitenin m_status=4 guncellemesinde
>    filtreler atomik mi? 0/1/cok eslesen satir, eszamanli ilerlemis aktivite,
>    acik ret ve kabul icin maskeli UpdateResult cevabi/alan tiplerini verin.
>    Success=true yalniz istek kabulunu mu, guncellenmis tek aktiviteyi mi ifade eder?
>
> **B. Ayri asgari durum okumasi (IU-05-STATUS)**
>
> Secilen OR'nin yalniz WASAS aktivitesini bekleyen/uygun ve tamamlanmis olarak
> ayiran desteklenen, sinirli query/select nedir? OR-parent ve aktivite kimligiyle
> eslesen keyed QueryResult icin bir bekleyen ve bir tamamlanmis maskeli ornek,
> alan adlari/turleri ve durum degerlerinin anlamlarini paylasin. Sifir/cok sonuc,
> eksik alan veya yetki/okuma hatasinin anlami da gerekli; "bekleyen satir yok"
> tek basina tamamlanma sayilmayacak. Uygunluk, mevcut yetki/ek/eszamanlilik
> kontrollerinden ayri tutulacak. Kaynak kaniti yoksa dogrulama bekleyen gosterilir;
> manuel teyit kaynakta dogrulandi sayilmaz. Otomatik onay-sonrasi okuma zorunlu
> degil; nihai OR kapanis alani, sonraki ekip ve surec timeline'i bu istekte yoktur.
>
> Istege bagli, ertelenmis iyilestirme: ileride tarih siralamasi istenirse ana OR'nin
> olusturulma select/key, JSON tipi, saat dilimi/offset ve maskeli keyed yanit ornegi
> kullanilir. Bu bilgi teslimat veya WASAS onayinin on kosulu degildir; yanit
> beklenmez. Simdiki siralama OR numarasina goredir, tarih uydurulmaz.
>
> Kimlikleri tutarli takma degerlerle maskeleyin; alan adlari, string/number,
> null/bos ve parent iliskileri korunsun. Parola, token, Authorization, SessionID,
> gercek XLSX/base64 veya kisi verisi gondermeyin. Kosullu guncelleme ya da ek
> icerik dogrulama desteklenmiyorsa bunu ve desteklenen alternatif/kurtarma yolunu
> acikca belirtin. GET + kosulsuz update atomik kontrol sayilmayacak.

Bu metin bir kaynak yazma/probe izni degildir. Diger RFC/servis mapping kabul
istekleri asagida ayridir; cozulmus is hedefi veya son OR kapanisi yeniden onaya sunulmaz.

Bilinen upload istegini yeniden istemiyoruz. Mutation istemcisi gercek kaynak
provider'inda DI ile cozulur, ancak completion transport olarak baglanmaz:
A bolumundeki ek icerik/atomik kosul/yanit kanitlari olmadan dispatcher acilmaz.
B bolumu yalniz asgari kaynak-durum ayrimi icindir; onay yaniti alinmis ama kaynakta
dogrulanmamis islem beklemede kalabilir. Sonraki ekip ve kapsamli timeline
IU-07-NEXT altinda ertelendi; kaynak sahibinden bu teslimat icin istenmez.

| Eksik olgu / sorumlu | Bilinen / en kucuk salt okunur istek | Acilan kod ve kabul |
|---|---|---|
| Ek kimligi ve icerik / Turuncu Hat API sahibi | Secilen tek OR'deki **mevcut** bir ek icin belgelenmis liste/lookup ve download/icerik kaniti islem yolu, kesin parent ve attachment ID alanlari, tipler, tek maskeli yanit; kayip upload cevabinin ayni baytlarla nasil aranacagi. Filename tek basina yetmez. Bilinen upload req.fBase/fId/fName/datastring/SessionID/TenantId yeniden kesfedilmez. | Ek readback adapteri ve kayip-cevap mutabakati; yeni upload yapmadan tekrar dogrulama |
| Dinamik vaka + kosullu yazma / workflow API sahibi | Mevcut completion collector ile tek secili OR'nin p_emb_dynamic_case_orff SET/KEY bicimi; ayrica 4463/4464 icin mevcut anahtar/deger/surum, sunucu destekli atomik kosul parametresi ve konflikt yanitinin belge/maskeli ornegi. GET+kosulsuzPOST veya yerel kilit CAS degildir. | Gercek property hedef/cozumleyici ve kaynak-eszamanlilik testleri |
| Uygun WASAS aktivitesi / workflow API sahibi | Bilinen model 103626/103627, status 1, grup 68, ayni OR filtresi ve m_status=4 update korunur. Beklenen aktivite/surum icin atomik kosul, eslesen/guncellenen satir sayisi, ret/konflikt/kabul yanitinin maskeli ornegi eksik. | Yalniz uygun tek aktivitenin onayi; kayip/belirsiz cevapta tekrar yok |
| Asgari WASAS aktivite durumu / workflow sahibi (IU-05-STATUS) | Ayni OR/aktivite kimligi, sinirli query/select, keyed bekleyen/tamamlanmis yanit ve durum anlamlari. Mevcut collector yalniz status 1/grup 68 adaylarini okur; sifir satir tamamlanma kaniti degildir. | Islem yapilabilir / kaynakta tamamlanmis / dogrulama bekleyen ayrimi; manuel teyit ayri. Nihai OR kapanisi veya sonraki ekip istenmez |
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

API E-02 ve sohbet Worker E-03 tarihsel raporlari korunur. E-04 normal Worker
ve API ayar eslesmesi/queue hazirligini artik dogrular; ayni onarim tekrarlanmaz.
Servis devir kabulunde yeni rapor cifti ayni onayli ortam/override ile alinir.
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
| API/UI/Worker ProductVersion | E-04 uc entry DLL 028cbd2/rc6.26 | Tam hedef payload hash'leri beklenir; yeni onarimlar yalniz successor'da | TEST operatoru; salt okunur envanter |
| SQL 022/023/024 | Operator TEST DB'yi teyit etti; 022/023 envanteri ve dar DDL metadatasi kaynakla anlamsal eslesiyor; 024 gorunmuyor | Yalniz normal API SQL baglamindaki etkin 022/023 izinleri acik. 022/023 tekrari yok; eski calistirma kaydi varsa saklanir, yoksa erisilemez yazilir | SQL yurutme operatoru; operator oturumundaki izin sonucunu API hakki sayma |
| InUseReports:Directory | API: mevcut yol, ReadableWriteNotTested | Mevcut D:\SecureOpsData\InUseReports korunur; yazma ve eski/yeni hash-esit indirme kabul edilir | API operatoru; yetkili rapor olusturma/indirme |
| DB target, Hangfire schema/queue/PrepareSchema | E-04 API/Worker eslesiyor; queue ready, bir Worker | Etkin degerler korunur; PrepareSchema=false. Servis devir sonrasi yeniden gozlem | API/Worker operatoru; hazir kuyruk kaynak baglantisi degil |
| Announcements:Enabled, AnnouncementSource:Enabled | E-04 onarilmis NonProd/provider ayarlari API/Worker'da eslesiyor | Etkin degerleri koruyun; flag onarimini tekrar etmeyin | SCCM/Turuncu Hat sahibi; tek secili OCO/profil terminal isi |
| AnnouncementMail Enabled/SelfTestEnabled/SendEnabled | E-04 API/Worker kapali | Servis onariminda false/false/false korunur; ayri secili self-test onayinda true/true/false | Mesajlasma + Worker operatoru; mail fingerprint + tek self-test |
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
6. In Use: gercek adapter kabulunden sonra secilen OR/rapor surumunu inceleyin ->
   degismez raporun ek ID/parent/bayt dogrulamasi -> gerekli Server Type/Environment
   degerlerinin dogrulanmis kosullu guncellemesi -> yalniz tek uygun WASAS
   aktivitesinin onayi. Yetki, uygunluk, eszamanlilik ve tekrar-gonderim korumalari
   korunur. Kabul yaniti kaynakta tamamlanma kaniti degilse "dogrulama bekleyen"
   gosterin; operator kaynakta WASAS adimini kontrol edebilir, manuel teyit kisi ve
   zamanla ayri kaydedilir. Asgari kaynak sorgusu tamamlanmayi dogrularsa WASAS
   adimi tamamlandi denir; OR kapandi denmez. Acik ret basarisizlik, belirsiz sonuc
   manuel kontrol gerektirir; otomatik tekrar yok. Baslatan insan, opsiyonel
   inceleyici ve Worker ayri kalir. Genel OR kapanisi, sonraki ekip ve kapsamli
   timeline bu kabulun kosulu degildir.
7. Ayni kayitlari /dashboard detay ve ayni veri kesiti Excel'iyle eslestirin.
   Tekrar denemeleri yeni tamamlanmis OR saymayin. Unknown veya tarihsel eksik
   veri sifir/basari sayilmaz. Modulleri yalniz hedefte kanitlanan kapsamla acin.

### SDM-01: secili TEST ServerRequest icin Jira-only kabul (hazir, calistirilmadi)

#### Salt okunur hedef on kontrolu

Tarihsel not: `aknonr/SecureOps` 28.09.2026 denetiminde Public gorundu ve
o anda push bekletildi. Sahip sonradan normal public push'a acikca izin verdi;
bildirilen uzak uc `e997c5b68cebcd23716860a9b06fdc25ebbb4493`.
Bu on kontrol yeni push veya yayin degildir. Yerel inceleme ZIP'leri aynen
korunur; derlenen urun `deda848`, sonraki belge kapanisi ayri kimliktir.

1. IIS envanteri 29.09.2026'da secilen TEST sunucusunda salt okunur
   alindi. Iki baslatilmis site tekil eslesti: API ve UI entry DLL'leri
   `0.1.0+028cbd2e4ec7068d71a33088d7c651e4df21644a` (rc6.26).
   Tam IIS yollari ve DLL hash'leri repo disi ozel kayitta saklanir:
   `C:\SecureOpsBuild\validation\sdm-target-preflight-20260929\iis-entry-evidence.json`.
   Bunlar tum kurulu payload'in hash'i degil; `deda848` adayinin kuruldugu
   iddia edilemez. IIS betiginde `Add-Type -AssemblyName` bu sunucuda DLL'yi
   bulamadi; `Microsoft.Web.Administration.dll` tam `inetsrv` yolundan
   yuklenince rapor olustu. IIS ayari degismedi; ayni kaniti tekrar istemeyin.

2. Salt okunur [Read-SdmTargetSqlPreflight.sql](../scripts/diagnostics/Read-SdmTargetSqlPreflight.sql)
   sorgusunun 29.09.2026 `18:51:01Z` sonuc ekranlari operatorce iletildi;
   SQL yurutme operatoru bu sorguyu kendisinin calistirdigini teyit etti.
   Sunucu/veritabani kimliginin onayli TEST hedefi oldugunu operator teyit etti;
   `CanViewDatabaseDefinition=1`.
   022 icin dokuz, 023 icin on beklenen nesne/kolon/index/trigger satiri
   `Visible`; ilgili index/trigger'lar etkin. 024'un dort beklenen nesnesi
   `NotVisibleOrAbsent`. Olasi harici ledger tablosu sonuclari bostur; bu,
   eski degisiklik kaydi olmadigini kanitlamaz. Tam hedef kimligi
   ve ekran transkripsiyonu ozel yerel kanitta saklanir:
   `C:\SecureOpsBuild\validation\sdm-target-preflight-20260929\sql-object-evidence.json`.
   Sonraki [Read-SdmContractDetails.sql](../scripts/diagnostics/Read-SdmContractDetails.sql)
   sonucunun 1-5 kumeleri `sql/schema/022` ve `023` ile karsilastirildi:
   kolon turleri/nullability, PK/UQ/CHECK/FK, index anahtar sirasi/filtre ve
   alti tetikleyici anlamsal olarak eslesiyor; gereken metadata NULL/gorunmez
   degil. Otomatik constraint adlari, CHECK terim sirasi ve bosluk farklari
   islevsel fark degildir. Sorgu kolasyon, identity seed/increment, FK action
   ve index storage seceneklerini dondurmedi; byte-esit tam DDL iddiasi yoktur.
   024 metadata satiri yok; onceki 4/4 gorunmeme ile
   tutarli. Onceki calistirma logu/degisiklik kaydi saglanmadi; varsa saklanir,
   yoksa tarihsel kanit erisilemez olarak kaydedilir. Ayri DBA makbuzu
   varsayilmaz. Altinci izin sonucu **operatorun SQL oturumuna** aittir; gorunen
   hesap adi benzer olsa da API'nin etkin hakkini kanitlamaz. 024 satirindaki
   sifir haklar da henuz gorunmeyen tablo ve operator oturumu baglamindadir;
   API icin grant sonucu olarak yorumlanmaz. Normal API SQL
   kimliginde onayli salt okunur hak kaniti yoksa tek SQL kapisi acik kalir;
   kimlik degistirme veya yeni yetki verme bu adimda yapilmaz.

3. Mevcut yetkili API `GET /api/v1/diagnostics/operations` raporunun yalniz
   allowlist `Settings` ve kaynak durumu kisimlarini, gizli alan eklemeden
   saklayin. Bu endpoint OperationalRecords/Jira mapping degerlerini vermez.
   API sahibinin normal IIS kimligi ve ortaminda mevcut `appsettings.Test.json`
   ve `web.config` environmentVariables katmanlarindan yalniz su anahtarlarin
   kaynak/degerini redakte ederek eslestirmesi gerekir:
   `OperationalRecords:{SourceProvider,RepositoryProvider,ReadOnlyIntegrationMode,ControlledTestWritesEnabled,SourceCloseEnabled}`,
   `OperationalRecords:Pilot:{RuleSetVersion,SourceRecordId,SourceFingerprint,SourceScope,RequestType,MappingVersion,ApprovalReference,TrackingReason,ExpiresAt}` ve
   `Jira:{Provider,ProjectKey,IssueType,IssueTypeId,MappingVersion,TeamCustomField,TeamValue,Labels,RequesterWatcherCustomField,ReporterMode,AssignmentMode,UnresolvedRequesterPolicy}`.
   Kisi eslemeleri gerekiyorsa kayitli onay referansiyla ayrica incelenir.
   Yalniz izinli anahtar/deger/kaynak ve gerekiyorsa parmak izi paylasilir;
   normal API prosesinin etkili degeri ile dosya/override katmanlari ayrilir.
   `Jira:Authorization`, URL'nin gizli kisimlari ve baglanti
   dizeleri paylasilmaz. Dosya ve IIS katmanlari baska saglayici/override varsa
   etkin degerin kaniti sayilmaz; ayni prosesin etkin degeri dogrulanmadan
   yazma kapisi acilmaz. Bu turde ek bilgi icin Falcon engelli SCCM paketi
   calistirilmaz.
   Kurulu rc6.26 DLL'leri birlesik Jira-only pilot adayindan eskidir;
   `diagnostics/operations` endpoint'i Jira/Pilot anahtarlarini dondurmez.
   Mevcut hedef dosya/override degerleri ile aday icin onaylanacak degisiklik
   ayri listelenir; dosya gorunumu aday prosesinin etkin degeri sanilmaz.
   `ReadOnlyIntegrationMode=true`, `ControlledTestWritesEnabled=false` ve
   `SourceCloseEnabled=false` on izleme boyunca korunur. Kurulum ve yazma
   kapisi ancak ayri izinli pencerede, secili OR ve canli on izleme sonrasi
   degerlendirilir.

4. Is/Jira sahibi bir acik TEST ServerRequest OR kodu/kaynak ID'si ve guncel
   kaynak parmak izini, onayli Jira proje/issue type/mapping, yetkili actor ve
   yalniz Jira niyetini tek kabul kaydina baglar. Canli preview ayni alanlari
   gosterene kadar ikinci asamadaki yazma anahtarlari kapali kalir. Kurulum,
   SQL degisikligi ve Jira create bu on kontrolun parcasi degildir.

Guncel birlesik kaynak: `feature/sdm-integrated-test-20260928`.
Test edilen urun: `deda8486b57c04a23aba203c0e79f96b746102e9`.
Guncel eslesmis API/UI/Worker inceleme adayi:
`C:\SecureOpsBuild\delivery-review\2026-09-28-sdm-integrated-test\final`.
Surum `0.1.0+deda8486b57c04a23aba203c0e79f96b746102e9`; paket hash'leri
`candidate.json`, dosya hash'leri `manifests` altindadir. Bu bir rc numarali
kurulum onayi degildir. Ust dizindeki onceki yayinlar tarayici kusuru nedeniyle
yerini bu adaya birakmistir; kullanilmaz. Yerel 1512 unit, 283 normal integration,
49 izole SQL ve son payload tarayici/restart/yanit-kaybi yolculuklari gecti.
61 opt-in normal kosuda atlandi; 49 SQL bunlarin icindedir, sayilar ust uste
eklenmez. In Use/SMTP/SCCM ve kurumsal Jira kabulu bu sonuctan turetilmez.
Iki kaynak worktree ve muhurlu E-05/E-06/E-07/E-08 degistirilmedi.
Birlesik aday ve test kimligi tek kayitta: `integrated-test-activation.md`.
Asagidaki hedef sirasi **kosullu inceleme taslagidir**, simdi uygulanacak
dagitim/aktivasyon talimati degildir. Secili kurumsal OR, Jira mapping/actor,
hedef SQL ledger ve tam payload onayi henuz alinmadi.

#### Yapilandirma farki ve dagitim incelemesi

| API anahtari | Kabulden once / kontrollu tek OR penceresi |
|---|---|
| Environment | `Test`; normal IIS override'lariyla dogrulanir |
| `OperationalRecords:SourceProvider` / `Jira:Provider` | Onayli `TuruncuHat` / `Corporate`; yereldeki Simulation hedefe tasinmaz |
| `OperationalRecords:RepositoryProvider` | `SqlServer`; mevcut onayli DB ve kalici command/audit/access depolari |
| `OperationalRecords:ReadOnlyIntegrationMode` | Once `true`; yalniz ayri hedef onayindan sonra `false` |
| `OperationalRecords:ControlledTestWritesEnabled` | Once `false`; yalniz ayni onayli pencerede `true` |
| `OperationalRecords:SourceCloseEnabled` | Her asamada `false` |
| `OperationalRecords:Pilot:*` | `RuleSetVersion=WASAS-SDM-PILOT-2026.09-v1`, `RequestType=ServerRequest`; `SourceRecordId`, lowercase SHA256 `SourceFingerprint`, `SourceScope`, `MappingVersion`, `ApprovalReference`, `TrackingReason`, UTC `ExpiresAt` sahiplerce onaylanir |
| `Jira:*` mapping | Mevcut onayli `ProjectKey`, `IssueType`, `IssueTypeId`, `MappingVersion`, `TeamCustomField`, `TeamValue`, `Labels`, `RequesterWatcherCustomField`; `ReporterMode=AuthenticatedOperator`, `UnresolvedRequesterPolicy=Block`; mevcut onayli `AssignmentMode` korunur |
| Ayri kapilar | `InUseCompletion:Enabled=false`; `AnnouncementMail:Enabled`, `SelfTestEnabled`, `SendEnabled=false`; `Hangfire:PrepareSchema=false` |

Jira/Turuncu Hat adresleri ve kimlik bilgileri sunucudaki korumali mevcut
kaynakta kalir; UI'ya tasinmaz. `SourceScope`, mevcut base object, grup ve
sirali dislama listesiyle birebir olmalidir. Bu tabloda hedef degerler veya
kimlikler uydurulmadi. `Default` provider bilgisi dosyada yokluk kaniti degildir.
Kullanici haklari mevcut onayli capability'lerle dogrulanir; testi gecirmek
icin rol verilmez. Yalniz bu tam kaynak ID/fingerprint olumlu pilot olabilir;
SoftwareInstallation/ServerRetirement ServerRequest'e cevrilmez.

Onaylar tamamlaninca operatorun inceleyecegi sira:

1. Secili OR ve asagidaki preview kanitini, tam aday manifest/hash'lerini,
   API/UI/Worker surumlerini ve guvenlik onayini ayni degisiklik kaydina baglayin.
   Hedef API/UI halen rc6.26'dadir; `deda848` inceleme ZIP'leri kurulu degildir
   ve bu belge kurulum onayi vermez.
   Yerel test kurumsal Jira baglantisi/issue kabulunun yerine gecmez.
2. Bakim penceresinde mevcut payload/config/ayri API-UI key ring yedeklerini,
   DB yedegini ve islenmekte/belirsiz komut listesini alin. Yeni yazmalari
   durdurun; mevcut Worker'i sahibiyle koordine edin, ikinci process baslatmayin.
3. SQL yurutme operatoru 022/023 tam tanim ve API izinlerini karsilastirir;
   eski calistirma kaydi mevcutsa ekler, yoksa erisilemez diye kaydeder. 024
   yoksa ve ayri onay verildiyse katalog farki yalniz 024'tur; 024 mevcutsa
   tekrar uygulanmaz. 023 sozlesmesi farkliysa 024 adimi durur.
   Eksik/belirsiz baseline
   varsa durun; yerel 001-024 fixture komutlari kurumsal sunucuda calistirilmaz.
   Bu Jira UI birlesmesi yeni migration eklemez.
4. API/UI ayni kaynakli payload olarak koordine edilir; config/web.config ve
   sirlar ZIP'ten degistirilmez. Matched Worker da teslimata dahildir, fakat
   Jira-only dispatch API icindedir: bu kabul icin SCCM veya yeni servis
   kurulumuna ihtiyac yoktur. Hedefte Worker icin Windows Service kaydi
   bulunmadigi, eski isletimin PowerShell konsolu oldugu bildirildi; bos
   `Win32_Service` sonucu ariza sayilmaz. Worker'in kurulu exe surumu, normal
   konsol sahibi/oturumu ve calisma durumu bu IIS raporuyla dogrulanmadi.
   Worker degisecekse ayri konsol-servis devir, tek proses kilidi, log/geri
   donus ve guvenlik onayi gerekir; burada Worker veya SCCM tanilamasi baslatilmaz.
5. Once yazma kapilari kapaliyken API/UI kimlik/payload, capability, kalici SQL
   ve exact OR onizlemesini dogrulayin. Eksik alan, mapping/fingerprint farki,
   bilinmeyen komut, app health hatasi veya yetki eksiginde durun.
6. Yalniz ayri onayli tek-OR penceresinde iki yazma anahtarini birlikte
   degistirin; `SourceCloseEnabled=false` kalir. Etkin degerleri normal API
   kimligi ve override'larla yeniden dogrulayin, guncel preview alin; asagidaki
   tek create kabulunu yapin. Sonucu kaydedip yazma penceresini kapatin.

Geri donus: once yeni yazmalari durdurun ve belirsiz islemleri Jira sahibiyle
mutabik kilin. DLL geri almak Jira issue'sunu geri almaz. DB audit/link/intent
silinmez; 024 icin otomatik down migration yoktur. Eski serializer yeni
alanlari dusurebileceginden API/UI/Worker surumlerini birlikte koordine etmeden
eski binary ile yazmaya izin vermeyin. Yedekten veri geri alma ayri onayli SQL islem karari
gerektirir; basarili issue icin Jira sahibinin ayri iptal sureci kullanilir.

#### Tek OR kabul kaniti

1. Is sahibi tek bir acik TEST OR'nin kodunu ve sayisal kaynak ID'sini, o anki
   kaynak surumunu, ServerRequest karar/onay referansini, suresini ve takip
   gerekcesini kaydeder. Jira sahibi mevcut onayli proje anahtari, issue type
   ID, mapping version, team alani/degeri, SunucuTalep etiketi, requester/watcher
   alani ve reporter/assignee ilkesini; yetkili uygulama actor'unu dogrular.
   Niyet yalniz Jira olusturmaktir: `SourceCloseEnabled=false` ve onizlemede
   `SourceCloseRequested=false`. Hedef bayraklari bu kilavuzdan acilmaz.
2. Yetkili TEST arayuzunde ayni OR icin normal `jira-preview` yolunu kullanin.
   Engelleyici kosul kalmadiysa OR kodu+basliktan summary, kaynak aciklamasi ve
   onayli takip gerekcesinden description, proje/issue type ID, team, label,
   tekil cozulmus requester ve oturumdaki dogrulanmis reporter ile varsa
   onayli assignee'yi alan alan karsilastirin. Kaynak surumu veya mapping
   degisirse eski onizlemeyle gondermeyin.
3. Ayri yetkili onaydan sonra UI onay penceresinden yalniz bir kullanici
   gonderimi yapin (`POST /api/v1/operational-records/{internal-guid}/jira`).
   UI hostunun HTTP katmani kopan cevabi otomatik yeniden iletebilir; bu nedenle
   agdaki POST sayisinin kesinlikle bir oldugu iddia edilmez. Beklenen, kalici
   komut/transfer korumasi sayesinde Jira'da tek issue ve tek kayitli key'dir.
   Komut kimligi mevcut DTO'da gosterilmez; destek referansi ve UTC zamaniyla
   yetkili destek ekibi kalici komut/audit kanitini eslestirir. Beklenen:
   Jira'da ayni alanlara sahip tek issue, WASAS
   transferinde kalici tek issue key ve OR-Jira iliskisi, `JiraCreated` durumu,
   `SourceCloseRequested=false`. WASAS ayrintisindaki key'i Jira'nin kendi
   onayli arayuzunde acilan issue ile karsilastirin; bu UI key gosterir, yeni
   bir Jira URL formati uydurmaz. Turuncu Hat'ta ayni OR hala Open olmalidir;
   BPM/source close cagrisi beklenmez.
4. Ikinci bir `/jira` POST'u gondermeden WASAS kaydini GET ile yeniden
   okuyun; kalici transfer satiri/key ve tek `JiraCreated` gecmisiyle Jira'daki
   tek issue'yu eslestirin. Ayni veya yeni komut anahtariyla yeniden gonderim
   bu rutin kontrolun parcasi degildir. Islem audit'i ve kaynak Open gozlemini
   birlikte saklayin. API cevabi,
   onizleme, redakte mapping surumu, secilen OR/actor ve Jira issue ekran
   kanitini ayni kabul kaydina baglayin; sifre veya Authorization saklamayin.
5. Zaman asimi, kopan cevap veya `CreatingJira`/`ReconciliationRequired` varsa
   yeni anahtarla gondermeyin. F5 devreye ait yerel belirsizlik mesajini silebilir;
   kayitli key olmamasi veya onizleme dugmesinin yeniden acilmasi tekrar gonderim
   izni degildir. Kalici belirsizlik sunucuda korunur. Jira sahibi secilen OR ve komut iliskisini
   onayli Jira arayuzunde inceleyip var olan issue kimligini veya belirsizligi
   kaydeder; bos arama tek basina yokluk kaniti degildir. Hatali issue icin
   silme/iptal yalniz Jira sahibinin ayri onayli sureciyle yapilir; WASAS SQL
   linki veya audit silinmez. Acik Jira reddi duzeltilmis onizleme ve ayri
   onay gerektirir. Ilk tek-OR kabulunde retry yapilmaz: mevcut retry penceresi
   sabitlenmis alanlari yeniden gosteremez; sonraki deneme icin ayri alan inceleme
   kaniti ve onay gerekir. Kaynak OR kapanisi ve In Use kabulunden bagimsizdir.

Tarihsel not: Onceki 6. maddedeki "otoritatif OR durumu" kosulu eski tum-OR
kapanisi kabulune aitti; guncel WASAS aktivitesi kabulunde yururlukten kalkti.
E-07/E-08 tarihsel kanitlari ve gercek kapanis kayitlari aynen korunur.

## Son UI kapisi: izinli test ortami

Kapsam notu: Asagidaki onceki host kisiti ve In Use kabul proseduru tarihsel
baglamiyla korunur. Yukaridaki SDM-01 bolumundeki yeni Jira-only tarayici
kanitlari ayri, normal yerel runner kosusudur; In Use UI kabulunu kapatmaz.

Onceki ozel UI host baslatmasi tool policy tarafindan reddedildi. Daha ayrintili
bir neden kanit kaydinda yok; port/komut/launcher/izin degistirerek tekrar denenmedi.
Son modal goruntusu onceki source'tandir. Asagidaki islem yalniz normal yetkili
test runner/operator ortaminda yapilir; burada yapildi iddiasi yoktur.

Bagimliliklar: .NET8 SDK, mevcut SecureOpsResourcesV1 LocalDB, sqlcmd, Node,
kurulu Chrome, mevcut Playwright modulu (journey-support.cjs ve test export'u).
Birbirinden ayrilmis taze kanit/publish dizinleri; fixture disinda endpoint yok.
Mevcut `Test-ResourceCatalogueSql.ps1 -DatabaseSuffix OcoCompletion20Ui`
taze 001-024 DB olusturur; burada calistirilmadi. Bu devamda
`C:\SecureOpsBuild\validation\system-status-20260920\payload-mapped` altina c12abf2
DLL'leri release PathMap ile derlenip `--no-build --no-restore` ile yayimlandi.
12 proje DLL hash/surumu testli derlemeyle ayni; ust dizindeki
`staged-payload-manifest.json` 767 dosyayi kaydeder. Uc payload taramasi gecti.
Eski completion-20260920 b596058 staging'i yeni paneli icermez. Yeni kokun
`payload` altindaki ilk deneme kisisel yol taramasinda reddedildi; kullanilmaz.
`payload-mapped` numarasiz runner girdisidir; host/paket kabulunu kanitlamaz. Source degisirse yeni SHA icin taze
staging gerekir. Son teslimatta kabul edilen baytlar ile paket yeniden eslestirilir.

Mevcut host proseduru (serbest port oldugu dogrulanmadan calistirmayin):

```powershell
powershell -NoProfile -File tests/browser/announcement-hosts.ps1 `
  -EvidenceRoot C:\SecureOpsBuild\validation\system-status-20260920\hosts `
  -PayloadRoot C:\SecureOpsBuild\validation\system-status-20260920\payload-mapped `
  -DatabaseSuffix OcoCompletion20Ui -Port 64731 -OperationalRecordSimulation
node tests/browser/inuse-rc626-repair.cjs $playwrightModule `
  https://localhost:64732 http://127.0.0.1:64731 `
  C:\SecureOpsBuild\validation\system-status-20260920\browser
$env:WASAS_NATIVE_ZOOM = '1'
node tests/browser/inuse-rc626-repair.cjs $playwrightModule `
  https://localhost:64732 http://127.0.0.1:64731 `
  C:\SecureOpsBuild\validation\system-status-20260920\zoom
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
basina migration gerektirmiyordu. Sonraki kabul edilmis paketin
`-UpgradeFromRc626` deltasi yalniz 024 icermelidir. SQL yurutme operatoru
bu degisikligi kendi onayli surecinde yapar; ayri DBA makbuzu varsayilmaz.
Repo ledger'i yoktur. Eski calistirma logu/degisiklik kaydi varsa saklanir;
yoksa tarihsel kanit erisilemez olarak kaydedilir. 29.09 envanterinde 022/023
gorunur ve 024 gorunmez; sonraki dar tanim ciktilari 022/023 ile anlamsal
eslesmistir. Bu, migration calistirma gecmisini veya API etkin haklarini kanitlamaz.

Kosullu 024-only degisiklik sirasi (simdi **calistirilmaz**):

1. Onayli TEST hedef kimligi ve 022/023 dar DDL karsilastirmasi operator
   kanitiyla tamamlandi; envanter veya tanim sorgusu simdi tekrarlanmaz.
   Normal API SQL kimliginin etkili 022/023 SELECT/INSERT ve execution UPDATE
   haklari ayri, onayli salt okunur kanitla teyit edilir. Altinci sonucun
   operator oturumuna ait olmasi bu kontrolu kapatmaz. Yeni bir 023 farki,
   eksik API hakki veya asiri/genis izin sorusu varsa **dur**; 024 uygulanmaz.
2. Ayri degisiklik onayi, secili matched API/UI/Worker payload'i ve reviewed
   024 delta hash'leri olmadan ilerlenmez. Gozden gecirilecek tek giris
   `sql/migrations/024-in-use-report-catalogue.sql`; SQLCMD `:r` ile
   `sql/schema/024-in-use-report-catalogue.sql` dosyasini cagirir. Her iki
   dosyanin hash'i kabul edilmis delta manifestiyle eslesmelidir. 024 nesnesi
   zaten varsa veya kismen varsa durulur; yeniden oynatma/otomatik onarim yok.
3. Bakim penceresinde mevcut DB ve uygulama/config yedegi ile geri yukleme
   noktasi kaydedilir, yazilar durdurulur, belirsiz islemler ayiklanir.
   SQLCMD migration dizini baglaminda yalniz 024 girisi, mevcut onayli hedef
   baglantisi ve `-I -b` hata davranisiyla yurutulur; 001-024 fixture veya
   022/023 hicbir zaman hedefte tekrar calistirilmaz. Bu belge baglanti bilgisi
   veya simdi calistirilacak hedef komutu vermez.
4. 024 DDL basarisi ayri dogrulanir: `reporting.InUseReportCatalogue` kolon/PK,
   `reporting.InUseArchiveReceipts` icin FK, `IX_InUseReportCatalogue_Code`
   key sirasi ve etkin `TR_InUseReportCatalogue_Immutable` tam schema dosyasiyla
   ayni dar sozlesme sorgusunun post-change sonucunda karsilastirilir.
   Runtime icin yalniz **mevcut onayli API DB principal'ina**
   bu tabloda SELECT ve INSERT izni ayri incelenir; Worker/UI, DELETE, UPDATE,
   DDL, rol atamasi veya genis grant eklenmez. Normal API SQL kimligindeki
   etkili izinler ayri salt okunur dogrulanir; uygulama yazmasi bu adim degildir.
5. SQL hata/belirsizlik, yedek eksigi, nesne/izin farki veya surum uyumsuzlugunda
   uygulama yazilari kapali kalir; sonucu incelemeden migration tekrar edilmez.
   Otomatik down yoktur. Geri yukleme yalniz onayli recovery kararidir;
   audit, receipt ve arsiv baytlari silinmez. `Hangfire:PrepareSchema=false`
   korunur. SQL kapisi kapaninca bile Jira one-OR kabul ve hedef flag onaylari
   ayrica gerekir.

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
Son c12abf2 PathMap'li derlemenin `tests/mapped-committed-unit.trx` dosyasinda
1399 gecti (17 yeni panel testi dahil); `tests/mapped-committed-integration.trx`:
279 gecti, 56 opt-in atlandi. Release build sifir uyari/hata; scoped format ve
node syntax gecti. Onceki/ortusen kosulara ekleyerek toplam uretilmez.
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
## WASAS aktivitesi operator adimlari

Guncel is hedefi OR'nin tamamini kapatmak degil, raporu ekleyip yalniz uygun
WASAS aktivitesini onaylamaktir. Sonraki ekip beklerken OR acik kalabilir.
Bu belgedeki E-07 kapanis ifadeleri yalniz muhurlenmis onceki aday ve onun tarihsel
kayitlari icindir. Tek durum kaydi: [integrated-test-activation.md](integrated-test-activation.md), IU-07 / IU-05.

1. Sunuculari ve degisen alanlarin eski/yeni degerlerini inceleyin. Surec ilerlemesi
   ayni sunucu cevaplarini silmez; gercek sunucu bilgisi degisikligi yeniden inceleme ister.
2. Cevaplari tamamlayin. Ortam/NMS onerilerini kendi bolumundeki kutuyla onaylayin.
   `Cevaplari kaydet` hem cevaplari hem secilen oneri incelemesini yerelde saklar;
   kaydetmek kurumsal onay gondermez. Secili sunuculara kopyalama once farklari gosterir.
3. Excel onizlemesini ve OR/surum/hazirlayan bilgisini kontrol edin; arsivleyip indirin.
   Yeni surumlerin insan adlari/hesaplari hazirlayan ve inceleyen icin ayridir;
   teknik kimlikler kanitta korunur. Onceki arsivler ve hash'leri degismez.
4. `Raporu ekle ve WASAS adimini onayla` oncesinde OR, rapor hash/surumu, sunucu tipi
   ve ortam gorulur. Is kurali: herhangi bir PROD -> PROD; bilinen diger ortamlar -> TEST.
   Gercek 4464 kaynak degeri eslemesi ve kosullu yazma sozlesmesi henuz eksiktir.
   Gercek tasima bu nedenle kapali kalir; bayrak acmak cozum degildir.
5. Asgari durum: islem yapilabilir (kaynak uygunlugu ve mevcut korumalarla), WASAS
   adimi kaynakta tamamlanmis veya dogrulama bekleyen. Kabul yaniti/manuel teyit
   kaynakta tamamlanma degildir; belirsiz sonucta tekrar gondermeyin. Sonraki ekip
   ve kapsamli timeline ertelendi; genel OR kapanisi onay/kabul kosulu degildir.
   Islem gecmisini acmak kaydetme/onay kosulu degildir. Takip gorunumu yerel tik veya
   manuel bildirimden basari uretmez; kanit yoksa kaynak durumu alinmadi yazar.

E-05/E-06/E-07 degistirilmez. E-08 inceleme ciktisi kurulu rc6.26 davranisi degildir.
Tarayici ve kurumsal kabul, Falcon izni, gercek ek/alan/gorev sozlesmeleri bekler.
Kurumsal sunucuya kopyalama, SQL uygulama veya kaynak onayi bu calismada yapilmaz.
