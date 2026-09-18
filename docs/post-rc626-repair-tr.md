# rc6.26 sonrasi duzeltme ve kabul durumu

Bu bir TEST aktivasyonu veya yeni paket kabul raporu degildir. Baslangic
`34c8837c2837b9a3ad61a432aa121cf41ac3e0c7`; rc6.26 build kaynagi
`028cbd2e4ec7068d71a33088d7c651e4df21644a`. Yeni ZIP uretilmedi.
Mevcut rc6.26 arsivleri degistirilmedi. SQL 022/023 kurulum ve hesap kilidinin
cozulmesi operator bildirimidir. Bu makineden belirtilen D: hedefleri erisilemiyor.
Etkili IIS/Worker ayari, kimligi veya hedef SQL baglantisi dogrulanmis sayilmaz.

## Bu devamda degisen davranis

- Yeni Excel dort kurumsal sayfayi kullanir: NMS, CheckList_THY,
  CheckList_TEKNIK, Sunucular. Kaynak/inceleme kaniti arsiv zarfindaki ayri
  EvidenceSheets alaninda ve yetkili uygulama gorunumunde korunur.
- Sunucular ve NMS: metin hucreleri, kalin baslik, genislik, satir yuksekligi,
  uzun metni kaydirma ve sabit baslik. Birlesik veri hucresi/formul eklenmedi.
- Indirme adi: `InUse_<OR>_<hazirlayan>-<aktor-kisa-kimlik>_<yyyyMMdd_HHmmss>Z_v<surum>.xlsx`.
  `Z` UTC'dir. Sonradan indiren veya atanan inceleyici hazirlayanin yerini almaz.
  Eski arsivlerde kod yalniz kendi Provenance verisinden okunur; yeni kayittan
  uydurulmaz. Eski dosya baytlari degismez.
- Atama gorunur MudBlazor penceresinde, acik Kaydet/Vazgec ile yapilir. Atama
  zorunlu degildir. RFC bildireni, inceleyici ve tamamlama baslatani ayri rollerdir.
  Kaynak kullanici referansi ile uygun WASAS hesabi arasinda dogrulanmis sabit
  esleme yoksa ad benzerligiyle onerilen kisi atanmaz; bu eksik acikca gosterilir.
- Ortam kaynak degeri ve gozlem zamani soru alaninda gorunur. PROD icin NMS,
  CPU/bellek/disk onerisi Evet; DEV/TEST/UAT/NonProd icin Hayir. Ham deger aynen
  korunur; UAT, TEST olarak yeniden adlandirilmaz. Bilinmeyen ortam karar uretmez.
  Up/Down onerisi kurulum kaniti degildir. Uc soru yanitlanmamis baslar.
- OCO kaynak hazirlik bilgisi, profil istegi basarisiz olunca kaybolmaz. Kapali
  kaynak, eksik profil ve istek hatasi ayri mesajlardir. SMTP kaynak toplamayi acmaz.

## Taslak kurtarma yolu

1. Kaydedilmemis degisiklikleri geri al: son kayitli cevaplara doner, uzak cagri yapmaz.
2. Kayitli cevaplari sifirla: OR ve sunucu sayisini onaylayin. Yeni surumde cevaplar,
   notlar ve kabul edilmis politika temizlenir; eski incelemeler ve arsivler kalir.
3. Taslagi kaldir: aktif listeden cikar, kaynak OR'yi silmez. Kaldirilmis taslaklar
   filtresinde bulunur. Kaynak yenileme bunu kendiliginden yeniden acmaz.
4. Yeniden basla: kaldirilmis kayitta yeni bos inceleme acilir. Deneme cevaplari
   gecmisten tekrar kullanilabilir onaylara donusmez.

Her islem tam gosterilen surume baglidir, mevcut yetki ve audit kaydi gerektirir.
Eszamanli kayit veya tamamlama baslatma cakismasi reddedilir. Kuyrukta/calisan/
kismi/belirsiz aktif kaynak komutu varsa once onun sonucu uzlastirilir; bu tuslar
komutu silmez, eki geri cekmez veya OR'yi yeniden acmaz. Eski tarihsel indirmeler
yetki kontrolunden sonra ayni arsiv baytlarini dondurur. Tamamlanmis uzak gercekler
ayri saklanir. Pano aktif is sayimindan kaldirilmis taslagi cikarir; kaldirilmis
taslak ve tarihsel arsiv ayri kalemdir, sahte tamamlama toplami uretilmez.

Yeni SQL migration yoktur: mevcut JSON aggregate ve 022/023 nesneleri kullanilir.
Eski binary ile yeni lifecycle alanlarina yazmak desteklenen rollback degildir;
eslesen bilesenler ve yazmalari durduran geri donus proseduru gerekir.

## JSON ve XLSX nerede

| Nesne | Konum / anlam |
|---|---|
| Legacy script Excel | Scripti calistiran makinede `C:\InUse\InUse_<OR>_<yyyyMMdd_HHmm>.xlsx` |
| WASAS ozel arsivi | Etkili `InUseReports:Directory` altinda `<record-GUID>\<version>.json`; Base64 XLSX, hash, surum ve aktor tasir |
| Operatorun gosterdigi hedef | `D:\SecureOpsData\InUseReports`; ekran goruntusu var, bu agent API kimligiyle etkili yol/okuma dogrulamadi |
| Tarayici indirmesi | Kullanicinin tarayici/indirme secimi; API arsiv dizini degildir |
| Turuncu Hat eki | `<OR>_InUse.xlsx`; dogru sayisal OR kimligine uzak ek, yerel klasor degildir |

JSON'u XLSX diye yeniden adlandirmayin. Uygulamanin arsiv indirmesi zarftaki
baytlari cozer, bugunku kaynakla eski dosyayi yeniden uretmez. `.json.lock`,
FileShare.None ile yazma/okuma koordinasyonunda kullanilan kalici kilit dosyasidir;
sifir bayt olmasi takilmis surec kaniti degildir. Taslak sifirlamak icin klasor,
JSON veya lock silinmez. UI/Worker'a arsiv icin dogrudan dosya izni gerekmez;
Worker, komutla SQL'e dondurulmus rapor baytlarini kullanir.

Mevcut bulma yolu: In Use listesinden OR ara, kaydi ac, Onceki WASAS raporlarindan
surumu sec. OR/kullanici/sunucu ile tum raporlarda sinirli yetkili katalog aramasi
bu devamda tamamlanmadi; dosya adi duzeltmesi bu eksigin yerine gecmez.

## Gercek dosya karsilastirmasi

Legacy ek: 12.718 bayt, SHA-256
`B3979BBC3A5F58EC7A824F7199F92ACF744D0AAED72243E00B75ABB762AAF2EF`.
Yerel orijinal dosyadan ZIP/XML okunarak dogrulandi; dosya degistirilmedi.

| Kontrol | Sonuc |
|---|---|
| Sunucular alan sirasi/yazimi | 29 satir, yeni sentetik ciktiyla ayni |
| NMS baslik sirasi/yazimi | 22 sutun, yeni sentetik ciktiyla ayni |
| Sayfa sirasi | Yeni dort sayfa legacy sirasinda; eski alti sayfali arsiv degismez |
| Hucre turu | Legacy kimlikleri sayisal olabilir; yeni cikti kimlikleri kayipsiz metin tutar |
| Gorunurluk | Legacy NMS genislik/kalin baslik var; Sunucular acik genislik yok. Yeni cikti her ikisini iyilestirir |
| Yazi, formul, birlestirme | Yerlesim/alanlar korunur; formul ve birlesik veri hucresi yok |
| Sayfa yonu | Legacy acik baski yonu belirtmiyor; yeni cikti da belirtmiyor |
| Checklist | Iki legacy sayfa bos; yeni cikti bunlari doldurulmus kontrol gibi gostermez |
| Kaynak degerleri | Farkli OR/sunucular; deger esitligi iddia edilmez, kisi/DEV/sahip bilgisi kopyalanmaz |
| Sonraki tuketici | Dort sayfa varsayilan kurumsal sozlesme; gercek ek/import kabulunun yerine gecmez |

Yeni sentetik XLSX kurulu Microsoft Excel COM ile salt okunur normal Open'da
basariyla acildi; NMS 3x22, Sunucular 29x3, baslik/width/wrap incelendi.
Onarim yolu kullanilmadi, kaydedilmedi. Bu, kurumsal kullanicinin gorsel Excel
kabulunun veya Outlook testinin yerine gecmez. Pasted current JSON sonu bozuk;
orijinal 13.json/ek dosya istendi. Ekran goruntusunden byte-level karsilastirma yok.

## Acik kalan kabul

Paylasilan current rapordaki eksikler, canli yeniden sorgu yapilmadan su sekilde
ayrilir: COUNTRY/Department/Sub_Department/Contact_email/ITMC_Event_Owner_Group
icin kaynak gozlemi yoksa onayli `InUsePolicy:Proposals` ve `Revision` gerekir.
Servis unsuru adi/kimligi icin her sunucunun kendi servisine bagli kaynak eslemesi
gerekir. OS RELEASE ve UY_Owner Mail Address icin gercek kaynak gozlemi gerekir;
script sabiti veya RFC bildireni bunlarin yerine kullanilmaz. KONTROL'un bilinmiyor
kalmasi kasitlidir: Excel varsayilani kurulum/dogrulama kaniti degildir.

Tek gereksinim/kanit matrisi ve dar kaynak-sozlesmesi tablosu:
[integrated-test-activation.md](integrated-test-activation.md).
SMTP API/Worker ayarlari ve asamali self-test proseduru:
[rc626-mail-source-activation-tr.md](rc626-mail-source-activation-tr.md).
Salt okunur arac komutu:
[operator-completion-tr.md](../scripts/diagnostics/InUseEvidence/operator-completion-tr.md).

Gercek In Use transport/DI, ek identity/content readback, kosullu guncelleme ve
yetkili son OR durumu halen tamamlanmadi. OR-SDM pozitif politika ServerRequest;
diger turlerin eslemesi varsayilmaz. OCO profili/relay/Outlook hedef kabulune ve
tek secilmis OCO/profil/aktor/preparation'a ihtiyac var. Genel gorev onayi, kayit
secme veya belirtilmemis aliciya gonderme onayi degildir. Son UI touch/200% zoom,
tam payload SMTP/browser ve rapor katalogu da acik gelistirme/kabul kalemleridir.

## Gonderilmemis ekip duyurusu taslagi

"WASAS In Use yerel inceleme ve Excel arsiv duzeltmeleri kabul surecindedir.
Arsivleme, Turuncu Hat'a ek yuklendigini veya OR'nin kapandigini gostermez.
OCO kaynak/mail ve kaynak tamamlama icin kontrollu TEST kabulunu bekleyiniz.
Tum is akislarinin devrede oldugu henuz dogrulanmamistir."

## Yerel kanit dosyalari

Kanit koku: `C:\SecureOpsBuild\validation\post-rc626-20260918`.
`tests/post-rc626-commit-gate*.trx`: son solution testleri, 1.367 birim ve
278 entegrasyon basarili; 54 istege bagli test varsayilan kosuda atlandi.
Izole SQL ayri TRX'tedir; sayilar ust uste toplanmaz. Corporate SQL, LDAP,
SCCM, Turuncu Hat, Jira veya relay kabulunun yerine gecmez.
`tests/post-rc626-checkpoint-sql.trx`: 40 izole SQL testi basarili.
1.000 yeni sentetik OR senaryosunda capture 4.177 ms, capture/sayfa/export
4.474 ms, filtrelenmis 6.173 satir gozlenmistir; bu bir SLA degildir.
Son build (0 uyari/hata), tam format kontrolu ve OpenAPI snapshot kontrolu gecti.

`browser4/synthetic-corporate.xlsx`: yeni ihracat ornegi, kurulu Excel'de salt
okunur acildi. `browser5/assignment-open-1366.png`: fareyle acilmis modal,
son font/erisebilir etiket duzeltmelerinden ONCE; final ekran kabul resmi degildir.
Yeni UI sureci baslatma cagrisi arac politikasi tarafindan reddedildi; baska bir
yolla asilmasi denenmedi. Son kodun browser/touch/zoom/tema kabul testi aciktir.
Yalniz bu gorevin 64731/64732/64733 yerel hostlari kimlik/port/zaman kontrolunden
sonra durduruldu. Onceki gorevin hostlari ve kurumsal surecler degistirilmedi.

Yetkili izole kabul ortaminda guncel API/UI/SQL kurulup yerel fixture hesabi
acildiktan sonra `tests/browser/inuse-rc626-repair.cjs` calistirilir. Argumanlar:
Playwright modul dizini, loopback UI URL, loopback API URL, yeni kanit dizini.
Bu script kurumsal adres kabul etmez; kaynak refresh ve deneme taslagi mutasyonlari
yalniz sentetik fixture'a yoneliktir. Ekran goruntusu tek basina test sonucu degildir;
son `result.json` ancak butun assertion'lar gectiginde yazilir.

Korunan rc6.26 icin `release-artifacts.sha256` listesindeki alti ZIP'in hash'i
tekrar dogrulandi. Kaynak kanit araci da bu listede
`diagnostics/inuse-evidence-win-x64-028cbd2.zip`, SHA-256
`C1B7C1536276C180D090C25320F24358ACD68F6D2455D76518189F29A1EC16BF`.
Bu, yeni duzeltmelerin rc6.26'da oldugu anlamina gelmez; yeni paket yoktur.
