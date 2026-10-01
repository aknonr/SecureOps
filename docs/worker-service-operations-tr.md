# Worker Windows Service isletimi

Guncel giris: [operator rehberi](post-rc626-continuation-tr.md).
Tek kabul kaydi: [ana matris, OPS-02](integrated-test-activation.md).
Bu prosedur yeni Worker kaynak davranisi icindir; rc6.26 binary'sine uygulanmaz.
Windows Service destek paketi zaten vardi; yeni host baglantisi Microsoft'un
[AddWindowsService modelini](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)
kullanir. Yerel kaynak/binding testi SCM, hedef kurulum veya is sonucu degildir.
Asagidaki hedef degisiklikleri yalniz yetkili operator, kabul edilmis eslenik
payload ve onayli bakim penceresiyle yapar. Burada calistirilmadi.

E-06 guvenlik kapisi: ayri SCCM tanilama prosesi Falcon tarafindan sonlandirildi;
inceleme acik, paket henuz sunulmadi, karar yok. Hedefte tanilama veya yeni servis
start/recovery adimlari ilgili tam payload/context icin izin olmadan uygulanmaz.
Tanilama izni yeni Worker hash'lerine devrolmaz; farkli launcher/hesap/host/yol ile
engeli asmayin. Yerel inceleme ZIP'leri kurulum onayi degildir.

## Ayar farki ve haklar

- ServiceName sabit `SecureOps.Worker`; servis ImagePath'inde mutlak EXE yolu ve
  `--environment Test` bulunur. UI/API kimlik veya key ring'i degismez.
- Tek yeni Worker anahtari `WorkerHosting:DataDirectory` (duz JSON) veya
  `WorkerHosting__DataDirectory` (ortam). Degeri isletim sahibinin onayli, mevcut,
  mutlak yerel dizinidir; yol uydurulmaz. JSON'da ters egik cizgileri escape edin.
  Normal konsol da ayni dizini kullanir. Bu alan API/UI'ya eklenmez.
- Dizin SQL audit veya In Use arsivi degildir. `.secureops-worker.lock` ve
  JSON `worker.log`/boyuta gore donen dosyalar buradadir: dosya basina 10 MiB,
  en fazla 14 dosya. UTC-offset'li zaman, PID ve yasam dongusu olaylari kaydedilir;
  exception govdesi, kimlik bilgisi veya tum config yazilmaz. Is ayrintilari mevcut
  SQL command/audit kaydindadir. Log yazilabilirligi ve disk dolulugu izlenmelidir.
  Servis kipinde otomatik Windows Event Log source yaratimi kullanilmaz; SCM'nin
  sistem olaylari ayridir. Konsol kipinin mevcut logging provider'lari korunur.
- Servis hesabi: operatorun dogruladigi mevcut hesap; parola kod/komut satiri/
  dosyada bulunmaz. Gerekirse kurumsal guvenli credential istemi kullanilir.
- Windows sahibi etkin GPO'da `Log on as a service` hakkini ve `Deny log on as a
  service` yoklugunu, hesap/grup kapsami dahil dogrular. LocalSystem veya local
  Administrators uyeligiyle sorun gizlenmez. Uygulama bu haklari otomatik vermez.
- Binary/config icin Read/Execute; yalniz onayli data dizini icin gerekli yazma/
  donen log silme haklari. SQL/Hangfire mevcut runtime haklari, SCCM/WMI ve TH
  erisimi servis kimliginde dogrulanir. UI arsivi/branding haklari kopyalanmaz.
- .NET 8 x64 runtime, mevcut Hangfire schema 9 ve paylasilan DB/queue korunur.
  `PrepareSchema=false`; uc AnnouncementMail bayragi ve InUseCompletion kapali
  kalir. Servis kurulumunun SQL deltasi yoktur; eslenik urundeki katalog 024 ayridir.
- appsettings.json zorunlu degildir: yalniz appsettings.Test.json desteklenir.
  Uc kipte content root EXE/DLL dizinidir, oturumun current directory'si degildir.
  Ortam/CLI yapraklari JSON'u ezer; eski override'lar guvenli kayda alinmalidir.

## Kurulum ve konsoldan devir

1. Son eslenik API/UI/Worker manifest/hash ve kaynak kimligini dogrulayin. Entry
   DLL ProductVersion tek basina tam payload kaniti degil. Yanlis/eksik hash'te DUR.
2. Yeni kaynak taleplerini bakim penceresinde durdurun. Kuyruktaki tam isleri,
   Dispatching/Unknown/Partial ve In Use lease durumlarini kaydedin; onaysiz is
   varsa normal Worker acmayin. Servis ilk acilista recovery islerini de baslatir.
3. Eski rc6.26 konsoluna Ctrl+C verin, PID'nin bittigini ve kalan isleri dogrulayin.
   Eski binary yeni dosya kilidini tanimaz. Kilit farkli data dizinlerini/hostlari
   da birlestirmez; SQL lease ve insan kontrolu gereklidir. Lock dosyasini silmeyin.
4. Servis kaydi varsa once mevcut ImagePath/account/recovery/start mode'u guvenli
   kaydedin; ikinci servis olusturmayin. Payload/config yedeklerini webroot disinda
   koruyun. Baslangic argumanlarinda secret varsa rapora yazdirmayin.
5. API/UI yazilarini ve Worker'i durdurun. DBA 022/023 nesne tanimlarini ve upgrade
   kaydini dogrular; 024 yoksa yalniz kabul edilen paketin 024 deltasi, yedek ve
   SELECT/INSERT runtime hak incelemesiyle uygulanir. Varsa tanimi karsilastirilir,
   replay yok. Eslenik binary'leri yerlestirirken mevcut config/secret/ring/arsivi
   koruyun. API sonra UI; yeni kaynak talepleri henuz kapali kalir.
6. Worker data dizinini haklariyla onaylatin ve tek yeni anahtari birlestirin.
   Normal onayli kimlik/ortam/override ile `--diagnostics` calistirin; bu mod
   SCM/job server/lock/log baslatmaz. Kaynak kontrolu SQL heartbeat okuyabilir.
   API raporuyla mevcut Compare-OperationsReadiness.ps1 karsilastirmasini yapin.
   Worker duruyorken heartbeat olmamasi beklenebilir; profil/queue hatasindan ayirin.
7. Yalniz yeni servis icin yonetici PowerShell'de onayli EXE yolunu secin:

```powershell
$exe = Read-Host 'Onayli Worker EXE mutlak yolu'
if (-not [IO.Path]::IsPathRooted($exe) -or -not (Test-Path -LiteralPath $exe -PathType Leaf) -or $exe.Contains('"')) { throw 'Gecersiz EXE yolu' }
if (Get-Service -Name 'SecureOps.Worker' -ErrorAction SilentlyContinue) { throw 'Servis mevcut; upgrade prosedurunu kullanin' }
$credential = Get-Credential -Message 'Onayli Worker servis hesabi'
New-Service -Name 'SecureOps.Worker' -DisplayName 'WASAS Worker' -BinaryPathName ('"' + $exe + '" --environment Test') -StartupType Manual -Credential $credential
```

8. Servis Log On hesabini ve ImagePath/Test'i yerel guvenli konsolda dogrulayin.
   Onayli ilk start: `Start-Service -Name 'SecureOps.Worker'`. Running tek basina
   yeterli degil: log Started, PID/hesap, API queue/tek matching Worker ve SQL is
   durumu birlikte kaydedilir. Hata halinde kor tekrar start degil, tanilama gerekir.
9. Sentetik izole SCM kabul kapilari ve hedef stop/start kontrolu gectikten sonra
   ayri onayla otomatik gecikmeli baslangic/recovery ayarlanir:

```powershell
sc.exe config SecureOps.Worker start= delayed-auto
if ($LASTEXITCODE -ne 0) { throw 'Startup ayari basarisiz' }
sc.exe qfailure SecureOps.Worker
```

Services -> SecureOps.Worker -> Recovery: first failure Restart/1 dakika, second
failure Restart/5 dakika, subsequent failures Take No Action, reset count 1 gun.
Kaydettikten sonra qfailure ile etkin degerleri tekrar kaydedin.
Crash recovery proses icindir, bir mail/upload retry izni degildir. Iki basarisiz
yeniden baslatmadan sonra operator uzlastirmasi gerekir; sonsuz dongu yok. Normal
Stop-Service sonrasi otomatik geri kalkma beklenmez. Non-crash failure davranisi
SCM kabulunde ayrica kaydedilir; burada dogrulanmis sayilmaz.

## Zorunlu servis kabul kaniti

Yetkili Windows runner'da eslenik staging, taze sentetik DB ve fixture/sink disinda
endpoint olmadan once asagidaki kapilari kosun. Mevcut UI host reddini baska port,
launcher veya executor ile asmayin. Native SCM icin yonetici yetkisi gerekir.

1. appsettings.json olmadan yalniz Test dosyasi; baska current directory'den konsol
   diagnostics; ayni etkin config/fingerprint, yeni log/lock veya job olusmamasi.
2. Servis start/stop: SCM durumlari, PID/hesap, log zamanlari, dogru queue heartbeat;
   Stop-Service ile kapanma suresi (host butcesi 90 saniye, Hangfire bounded stop).
   Bitmeyen isin sonucu varsayilmaz; lease/Unknown kaniti korunur, zorla kill rutini yok.
3. Ayni data diziniyle ikinci yeni konsol/servis normal hostunun SQL/job oncesi
   reddi. Diagnostics halen kullanilabilir. Yeni loglar ve eski dosyalar korunur.
4. Operator tam logoff yapar; baska yetkili oturum SCM PID/heartbeat surekliligini
   gozler. Sadece RDP pencere kapatma tam logoff kaniti degildir.
5. Yalniz fixture isinde kontrollu crash/restart: ilk/son PID, SCM recovery olayi,
   source attempts/terminal proposal; ayni komut iki defa etki uretmez. Mevcut
   kaynak process acceptance testi de yeni Worker payload'iyla kosulur.
6. Izole SMTP sink/fixture In Use icin kayip cevap ve crash: belirsiz dispatch
   Unknown kalir, yeni key veya otomatik retry ile tekrar gonderilmez. Hicbir
   fixture SMTP Accepted sonucu inbox veya gercek OR closure diye raporlanmaz.
7. Yeni payload UI/MIME/200% kapilari mevcut rehberdeki runner ile ayrica kosulur.
   Native servis testi bunlarin yerine gecmez. Tum dosyalar hash/source'a baglanir.

## Upgrade, geri donus ve kaldirma

- Upgrade: is girisini durdur, isi/lease'i kaydet, `Stop-Service` sonrasi Stopped
  ve PID sonunu bekle; eslenik API/UI/Worker ile config yedegi al. Calisan dizin
  ustune kopyalama yok. Ayni onayli yol/hesap/ortam/data dizini korunur; yeniden
  New-Service gerekmez. Yeni hedef yol gerekiyorsa ImagePath ayrica incelenir.
- Geri donus: tum ilgili yazarlari durdur. Eski serializer yeni alanlari dusurebilir;
  sadece Worker DLL downgrade guvenli kabul edilmez. Eslenik bilesenler, DB verisi,
  intent/audit/arsiv ve config birlikte incelenir. 024 tablo/arsiv silinmez. Eski
  konsola donulecekse servisi once durdurup Disabled yapin; tek konsol kontroluyle
  baslatin. Unknown/Partial etkileri uzlastirmadan yeniden is yaratmayin.
- Kaldirma: kuyruk sorumlulugunu devret, servisi Stop-Service ile durdurup PID
  sonunu dogrula; `sc.exe delete SecureOps.Worker` yalniz servis kaydini kaldirir.
  Dizin, log, config, SQL, audit ve arsiv silinmez. Services konsolu aciksa kayit
  silinmek uzere isaretli kalabilir; ikinci kayit yaratmayin. Corporate ACL/GPO
  kaldirilmasi burada otomatik yapilmaz; hak sahibi ayri degerlendirir.

## Is akislarinin kalan girisleri

Ana matristeki IU-05 tek kaynak istegi halen gecerlidir: attachment parent/ID ile
otoritatif byte/hash readback ve kayip-upload uzlastirmasi; 4463/4464 keyed
target/value/version ve gercek conditional-update/conflict cevabi; tek uygun BPM
aktivitesiyle iliskili OR terminal/remaining-state sorgusu. Mevcut upload/BPM wire
bilgisi yeterli diye readback veya CAS uydurulmaz. Kaynak sahibi destek yok derse
manual source-UI/uzlastirma alternatifi karar kaydina alinir, ayni endpoint yeniden
istenmez. Gerekiyorsa per-server servis iliskisi ve RFC source reference ayridir.
OCO icin tam secili OCO/profil; SMTP icin onayli relay/TLS/envelope ve kayitli Mail;
SDM icin tam OR/destination/actor/ServerRequest niyeti beklenir. Mevcut rehberdeki
tek command/preparation/attachment kimligi ve ayni dashboard/export kesitiyle
kabul yapilir; bu servis degisikligi hicbir kaydi secmez veya mail gondermez.
