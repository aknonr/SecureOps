# rc6.26 Kaynak ve SMTP Kontrolu

Destek proseduru: rc6.26 / kaynak 028cbd2 / schema 001-023 ayar sozlesmesi;
b596058 devaminda ayni kaynak/mail anahtarlari korunur, katalog ayrica 024 ister.
Tek guncel [operator girisi](post-rc626-continuation-tr.md) ve onun ana matrisi
surum/kabul durumunu belirler. Buradaki ayar ornekleri kurulum kaniti degildir.

Bu belge koddan dogrulanan ayar adlarini verir; kurumsal aktivasyon kaniti degildir.
Relay ve alici degerlerini mesajlasma sorumlusu onaylamadan uygulamayin.
SQL 022/023 kurulumu ve hesap sorununun cozulmesi operator bildirimidir. Bu
islem migration tekrari, yeni rol/grant veya In Use kaynak yazma izni gerektirmez.

## Etkin Ayari Dogrula

API: Sistem Durumu altindaki OCO/arsiv tanilama raporunu indirin. Worker'i kendi
yayim klasorunden, normal baslatmadaki ayni ortam ve parametrelerle salt okunur
tanilama modunda calistirin. Ciktiyi webroot disindaki ozel operasyon klasorunde
tutun; baglanti bilgilerini veya parolalari paylasmayin.

```powershell
Set-Location -LiteralPath 'D:\secureops_worker'
dotnet .\SecureOps.Worker.dll --environment Test --diagnostics
```

`Host.CreateApplicationBuilder` ile ortam `Test` ise `appsettings.Test.json`
yuklenir. Ortam degiskenleri JSON'u, komut satiri ise bunlari ezer. Onceki
baslatmadaki `--AnnouncementMail:Enabled=false`, `SelfTestEnabled=false` ve
`SendEnabled=false` parametreleri varsa JSON degisikligini gecersiz kilar.
IIS ortam ayarlari Worker'a miras kalmaz. Normal komut farkli override iceriyorsa
tanilamada da ayni override kullanilmadan esitlik iddia edilmez.

API ve Worker tanilamasinda karsilastirin: DB TargetFingerprint, Hangfire schema/
queue/PrepareSchema, uc mail bayragi ve Mail PolicyFingerprint. Kaynak icin ayrica
iki provider, site/provider makinesi, uc Turuncu Hat selector'u ve profil
fingerprint'leri eslesmeli. Tam genel fingerprint'in API arsiv dizini gibi
rol-farkli ayarlar nedeniyle farkli olmasi tek basina hata degildir. Worker
kimligi/heartbeat kuyruk kanitidir; SCCM veya relay baglantisinin kaniti degildir.

## Once Yalniz Kendime Deneme

Gerekli bilgiler: onayli relay host/port; StartTls veya SslOnConnect; gerekiyorsa
sunucuya ozel kimlik bilgileri; EnvelopeMode=Actor veya Configured; ikinci modda
onayli EnvelopeSender; tam izinli alici domain'leri; PolicyRevision. From her
zaman oturumdaki uygulama kullanicisinin kayitli Mail'idir. Entegrasyon hesabi veya
ortak mailbox otomatik From yapilmaz. Secret degerlerini bu belgeye/UI'ye koymayin.

API `web.config` icindeki mevcut `environmentVariables` bolumunde anahtarlari
tekil olarak guncelleyin. Asagidaki BUYUK_HARF yer tutuculari uygulanabilir deger
degildir. `Port` tamsayi, bayraklar boolean, diger alanlar metindir.

```xml
<environmentVariable name="AnnouncementMail__Enabled" value="true" />
<environmentVariable name="AnnouncementMail__SelfTestEnabled" value="true" />
<environmentVariable name="AnnouncementMail__SendEnabled" value="false" />
<environmentVariable name="AnnouncementMail__Host" value="APPROVED_RELAY_HOST" />
<environmentVariable name="AnnouncementMail__Port" value="APPROVED_INTEGER_PORT" />
<environmentVariable name="AnnouncementMail__Security" value="APPROVED_TLS_MODE" />
<environmentVariable name="AnnouncementMail__PolicyRevision" value="APPROVED_REVISION" />
<environmentVariable name="AnnouncementMail__EnvelopeMode" value="APPROVED_ENVELOPE_MODE" />
<environmentVariable name="AnnouncementMail__AllowedRecipientDomains__0" value="APPROVED_EXACT_DOMAIN" />
```

Worker `appsettings.Test.json` icindeki mevcut bolume ayni onayli degerleri
uygulayin. `0` portu kasitli gecersiz yer tutucudur; onayli tamsayi ile degistirin.

```json
{
  "AnnouncementMail": {
    "Enabled": true,
    "SelfTestEnabled": true,
    "SendEnabled": false,
    "Host": "APPROVED_RELAY_HOST",
    "Port": 0,
    "Security": "APPROVED_TLS_MODE",
    "PolicyRevision": "APPROVED_REVISION",
    "EnvelopeMode": "APPROVED_ENVELOPE_MODE",
    "AllowedRecipientDomains": ["APPROVED_EXACT_DOMAIN"],
    "MaxRecipients": 100,
    "TimeoutSeconds": 30
  }
}
```

Configured modu icin her iki surecte `AnnouncementMail:EnvelopeSender` gerekir.
Kimlik dogrulama gerekiyorsa `AnnouncementMail:UserName` ve `Password` birlikte,
mevcut korumali secret mekanizmasinda tanimlanir. Sertifika kontrolu kapatilmaz.
PlaintextLoopback kurumsal relay secenegi degildir. Ayrica mevcut
`Announcements:Enabled=true`, `Hangfire:Enabled=true`, `Hangfire:PrepareSchema=false`,
kurulu sema, ayni DB/queue ve aktif Worker gereklidir. Kaynak toplama bayragi mail
icin zorunlu degildir. OperationalRecords write/source-close bayraklarini acmayin.

Yalniz yetkili degisiklik penceresinde etkilenen API uygulamasini yeniden baslatin;
tum IIS resetlenmez. Worker konsolunu sorumlu operator Ctrl+C ile durdurur, temiz
kapanisi bekler ve celisen false override'lar olmadan ayni onayli oturumda baslatir:

```powershell
dotnet .\SecureOps.Worker.dll --environment Test
```

Bu bir Windows Service degildir; sorumlu operator, oturum ve calisma penceresi
belirlenmelidir. UI'de relay ayari veya secret yoktur; UI yeniden dagitimi gerekmez.
Sayfayi yenileyip yeni onizleme alin. Eski hazirligi otomatik yeniden gondermeyin.

1. Kayitli Mail'i dogrulanmis, mevcut `Announcements.SelfTest` yetkili
   operator kendi duyuru hazirligini acar. Yetki kodunu rol adi varsayimiyla atlamayin.
2. Kendime deneme onizlemesinde From ve tek To kendi Mail'i, Cc bos; hazirlik kimligi,
   konu, OCO ve saniye/UTC farki dahil tarihler dogrulanir. Bir kere onaylanir.
3. Komut kimligi ve kayitli sonuc alin. Accepted SMTP kabuludur, inbox teslimi
   degildir. Outlook'ta gorunen icerik/gorseller ve alici gozlemi ayri kaydedilir.
4. Unknown/Partial halinde otomatik veya yeni komutla tekrar gondermeyin. Relay
   sorumlusu Message-ID ile sonucu arastirir. Restart eski belirsiz gonderimi yinelemez.
5. Dagitim icin ayri onayli hazirlik/To/Cc/etki gerekir. Kabulden sonra her iki
   surecte `SendEnabled=true` ve dar `Announcements.Send` yetkisi kontrol edilir;
   fingerprint degisecegi icin yeni onizleme/onay gerekir. Bu belge gonderim onayi degildir.

## Bos Bakim Profili ve Kaynak Toplama

Kaynak toplama SMTP'den bagimsizdir. `AnnouncementSource:Enabled=false` nedeniyle
reddedilen profil istegi basarili sifir-servis sonucu degildir. Mevcut kod API'de
profil listesini `Announcements.Source` yetkisi ve modulu/kaynak bayraklariyla korur.

Her iki surecte inceleme sonrasi gereken anahtarlar:

| Anahtar | Beklenen tur / anlam |
|---|---|
| Announcements:Enabled / AnnouncementSource:Enabled / Hangfire:Enabled | boolean true |
| Hangfire:PrepareSchema | boolean false; kurulu Hangfire schema 9 yeniden hazirlanmaz |
| AnnouncementSource:CollectionProvider | ConfigurationManager |
| AnnouncementSource:ServiceProvider | TuruncuHat |
| AnnouncementSource:SiteCode / ProviderMachineName | Onayli site ve SMS provider, legacy deger otomatik onay degildir |
| AnnouncementSource:ServiceInstanceBaseObject | Incelenen script: LCSIMS_ServiceInstance |
| AnnouncementSource:ServiceNameSelect | Incelenen script: c_new_SI_major_project; adapterin KEY/SET cevabi ayrica dogrulanir |
| AnnouncementSource:ChangeBaseObject | Incelenen script: SMSS_Operation_Change |
| AnnouncementSource:Profiles:NonProd | Onayli Revision, Label, CollectionId, Scope, Impact, Checks ve Description veya DescriptionTemplate |

API ortam anahtari icin `:` yerine `__` kullanilir. Worker JSON ic ice bolumlerden
okur. Mevcut TuruncuHat sunucu sirlarini koruyun. Profil To/Cc bos diziler olabilir;
SMTP ve alicilar kaynak okumasi onkosulu degildir. Yalniz onayli profilleri tanimlayin;
Prod01/Prod02/ProdSingle/ProdRPA isimleri desteklenir, otomatik secilmez.

Kontrol sirasi: etkili ayarlar -> profil Configured -> dogru queue/Worker -> secilen
tek OCO/profil -> kayitli terminal is sonucu -> cihaz/servis sayilari ve kaynak
tarihleri -> secili onerileri uygula. Missing/ambiguous/partial sonuclar tam kabul
degildir. Kaynak ortaminda bu kontrol bu oturumda yapilmadi.
