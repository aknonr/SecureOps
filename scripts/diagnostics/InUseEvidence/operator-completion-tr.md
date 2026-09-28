# In Use tamamlanma sözleşmesi: salt okunur kanıt

Guncel [operator girisi](../../../docs/post-rc626-continuation-tr.md) kaynak
sozlesmesi ve kabul durumunu tutar. Bu prosedur rc6.26 / 028cbd2 ile paketlenen
completion modunu ve ayni sozlesmedeki successor aracini kapsar. SQL kullanmaz;
API katalog 024 gereksinimi bu salt okunur aracin onkosulu degildir.

Yalnız teslimat metadata/hash listesi bu derlemeyi doğruladığında kullanın.
rc6.24 arşivindeki eski araca `--completion-evidence` eklemek yeterli değildir.
`tool/` bağımlılık ağacını bütünüyle koruyun. Windows x64 üzerinde .NET 8
NETCore.App ve AspNetCore.App gerekir; SDK, Office veya Windows Service gerekmez.

## Hazırlık

1. Entegrasyon sahibi tek bir onaylı TEST OR'sinin **sayısal kaynak kimliğini**
   belirler. OR-... görüntü kodu veya WASAS GUID'si bu argüman değildir.
2. Araç teslimatı, örneğin `D:\SecureOpsTools\InUseEvidence`; özel JSON ayarı
   `D:\SecureOpsPrivate\InUseEvidence\server-config.json`; yeni çıktı için özel
   dizin `D:\SecureOpsPrivate\InUseEvidence\Evidence` olarak ayrılır. Bunlar
   öneridir; otomatik oluşturulmaz veya IIS/webroot altında tutulmaz.
3. Yapılandırma sahibi yalnız `server-config.example.json` şemasındaki mevcut
   TuruncuHat sunucu ayarlarını korumalı yerel süreçle doldurur. Araç **yalnız bu
   JSON'u** yükler; IIS ortam değişkenleri, web.config ve Worker ayarlarını almaz.
   Parola/Authorization/SessionID komuta, rapora veya paylaşılan kanıta yazılmaz.
4. `SourceProvider=TuruncuHat`, `ReadOnlyIntegrationMode=true`,
   `ControlledTestWritesEnabled=false`, `SourceCloseEnabled=false` kalır.
   Normal TLS doğrulaması ve mevcut dar kaynak-okuma izni korunur.
5. `dictionary.json` tam olarak `{}` kalır. Bu mod `ReporterProperty`, RFC
   sözleşmesi veya aday sözlük kullanmaz. Tarihsel A/B reporter modunda
   `ReporterProperty` atlanır veya yalnız `p_rel_requester` olabilir; ayrı bir
   uydurma reporter seçicisi geçerli değildir.

## Çalıştırma

```powershell
$sourceId = Read-Host 'Onaylı sayısal kaynak OR kimliği'
& 'D:\SecureOpsTools\InUseEvidence\tool\InUseEvidence.exe' `
  'D:\SecureOpsPrivate\InUseEvidence\server-config.json' $sourceId `
  'D:\SecureOpsTools\InUseEvidence\dictionary.json' `
  'D:\SecureOpsPrivate\InUseEvidence\Evidence\completion-one-or.json' --completion-evidence
```

Çıktı önceden mevcut olmamalıdır. Sıfır çıkış kodu ve `CollectedNotMapped`
birlikte gerekir. Sadece sınırlı/maskeli `Evidence` bölümünü paylaşın; tam dosya,
ActorSid, LocalComparison, özel JSON ve ham hata paylaşılmaz. Başarısızlıkta
izin genişletmeyin veya kapsamı artırmayın.

## Bu kanıtın sınırı

Araç seçilmiş aktif 4241/68 OR için `id`, `p_code`, `p_emb_dynamic_case_orff`
temsilini ve 103626/103627, durum 1, grup 68, aynı OR filtreli BPM adaylarını
okur. Sıfır/tek/çok aday ayrılır; tek aday yazma onayı değildir. Üst sınır
45 saniye, yanıt başına 64 KiB ve 10 BPM satırıdır. Mutasyon yapılmaz.

Kaynak sahibi ayrıca şu **sözleşmeleri** sağlamalıdır: dinamik vaka kimliğinin
SET/KEY anlamı ve sürüm-koşullu güncelleme; OR ekinin kimliğini ve içerik hash'ini
okuma işlemi; yalnız uygun WASAS aktivitesinin koşullu güncelleme ve yanıt anlamı.
Güncel iş hedefi WASAS adımını ilerletmektir; nihai OR kapanış okuması önkoşul
değildir. Bu aracın status 1 / grup 68 filtresi tamamlanmış aktiviteyi veya sonraki
ekibi sorgulamaz. Sıfır aday dönmesi tamamlanma kanıtı değildir. IU-05-STATUS için
yalnız aynı OR/WASAS aktivitesinin bekleyen/tamamlanmış durumuna ait sınırlı
query/select/yanıt sözleşmesi gerekir; kanıt yoksa doğrulama bekleyen kalır.
Sonraki ekip ayrıntıları ve kapsamlı zaman çizelgesi ertelendi (IU-07-NEXT);
bu teslimatın kaynak isteği veya kabul kapısı değildir. Endpoint tahmin edilmez.
İletilebilir soru metni güncel operatör girişinin IU-05 bölümündedir.
Araç bilinmeyen bir ek API'sini veya kapanış anlamını keşfetmez.
`CollectedNotMapped`, gerçek executor uyarlamasının tamamlandığı anlamına gelmez.
