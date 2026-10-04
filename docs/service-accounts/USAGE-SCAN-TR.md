# Kullanım taraması — operatör kılavuzu (ADR-0027)

Bir servis hesabının hangi sunucuda **Windows servisi, zamanlanmış görev, IIS uygulama havuzu, IIS sitesi/uygulaması veya
sanal dizini** olarak çalıştığını bulup hesaba **kanıt** olarak bağlamak için. gMSA dönüşümünden önce kontrol listesi,
dönüşümden sonra "artık gMSA ile mi çalışıyor" kanıtı verir. Örnekler sentetiktir (`SYN`, `SYN-APP01`).

## Ne yapar, ne yapmaz

| Yapar | Yapmaz |
|---|---|
| Kişi taramayı kendi yetkisiyle çalıştırır; sistem çıktıyı içe alır ve hesaba bağlar | Sistem sunuculara bağlanmaz, tarama başlatmaz, zamanlanmış tarama yoktur |
| Servis/görev/IIS kimliklerini **okur** | Hiçbir şeyi değiştirmez: servis, görev, IIS, dosya, kayıt defteri, hesap, parola |
| Bulunan bileşenleri listeler; kişi tek tek kullanım kaydına alır | Otomatik kullanım kaydı, otomatik bağlama, otomatik dönüşüm yok (Phase 8) |
| Taranamayan sunucuyu "bilgi yok" diye gösterir | "Bulunmadı"yı "kullanılmıyor" saymaz; hiçbir sonuç hesabı kapatmaz/serbest bırakmaz/doğrulamaz |
| Parola okumaz | Parola veya gizli değer benzeri alan içeren dosyayı **bütünüyle reddeder ve saklamaz** |

Taramanın göremedikleri: COM+ kimlikleri, kullanıcı hakları (ör. hizmet olarak oturum açma), uygulama/bağlantı dizesi
içindeki kimlikler, veritabanı oturumları, dosya paylaşımı izinleri, Linux/Oracle sunucuları ve planlanmamış sunucular.

## 1. Plan

Taranacak sunucuları bir metin dosyasına yazın (satır başına bir ad; `#` ile başlayan satır yorumdur):

```text
# SYN uygulaması, 2026-10 gMSA hazırlığı
SYN-APP01
SYN-APP02
SYN-APP03
```

En çok 500 sunucu ve tek seferde en çok 20 hesap. Planlanan her sunucu yükleme dosyasında görünür; sonuç gelmeyen sunucu
"sonuç yok" veya "erişilemedi" olarak kalır, kaybolmaz.

## 2. Her sunucuda toplayıcıyı çalıştırın (kendi yetkinizle)

`scripts/powershell/Get-ServiceAccountUsage.ps1` tek dosyadır, Windows PowerShell 5.1 ile çalışır, salt okunurdur,
dosya yazmaz ve ağ bağlantısı açmaz. Çıktısı **tek satır JSON**'dur.

```powershell
.\Get-ServiceAccountUsage.ps1 -Account 'SYN\svc_synapp'
# gMSA dönüşümünden sonra kontrol:
.\Get-ServiceAccountUsage.ps1 -Account 'SYN\svc_synapp' -ExpectedAccount 'SYN\gmsa_synapp$'
```

- `applicationHost.config` ve tüm zamanlanmış görevler için sunucuda yönetici yetkisi gerekir; yetki yoksa o kaynak
  "okunamadı" olur ve tarama **kısmi** sayılır.
- Çıktıyı **kendi makinenize** kaydedin (sunucuya dosya yazmayın). Her sunucu için ayrı `.json` dosyası ya da her satırı
  bir sunucu olan tek bir JSON-Lines dosyası kullanabilirsiniz.
- Sunuculara nasıl ulaştığınız (konsol veya kurumunuzun sizin hesabınız için onaylı uzaktan çalıştırma yöntemi) sizin
  uygulamanızdır; ürün bu adımı yapmaz ve repoda varsayılan WinRM ucuna bağlanan kod yoktur.

## 3. Yükleme dosyasını birleştirin (kendi makinenizde)

```powershell
.\Invoke-ServiceAccountUsageScan.ps1 -CombinePath .\sonuclar.jsonl -ComputerListPath .\planli.txt `
    -UnreachableComputerName SYN-APP03 -Account 'SYN\svc_synapp' -OutputPath .\tarama.json
```

- Her sunucu belgesi olduğu gibi (bayt bayt) aktarılır; planlı olup belgesi olmayan sunucu `NoResult`, sizin
  `-UnreachableComputerName` ile belirttiğiniz `Unreachable` olur.
- Plan dışı sunucu, aynı sunucunun iki belgesi, başka hesabı aramış belge veya parola benzeri alan içeren belge burada
  durdurulur; dosya yazılmaz.
- gMSA kontrolünde `-ExpectedAccount` toplayıcıdakiyle aynı olmalıdır.

## 4. Hesaba bağlayın

Hesap sayfası → **Kullanım taraması** sekmesi → **Tarama dosyası yükle ve bu hesaba bağla**:

1. `tarama.json` dosyasını seçin (en çok 4 MB).
2. **Çalıştırma beyanı** yazın (zorunlu): nerede ve hangi yetkiyle çalıştırdığınız, varsa değişiklik kaydı.
3. Hesaptan sorumlu ekip değil de ekibinize atanmış bir talep üzerinden çalışıyorsanız (ör. gMSA yürütücü ekip) **o talebi**
  seçin; tarama o talebin kanıtı olur.

Dosya bu hesabı aramış olmalıdır (aynı ad; iki tarafta da domain varsa aynı domain). Aynı dosyayı tekrar yüklemek yeni kayıt
oluşturmaz. Birden çok hesabı aramış bir dosyayı her hesabın sayfasından ayrı ayrı bağlayabilirsiniz; her hesap yalnız
kendi bileşenlerini görür.

## 5. Sonucu okuyun

| Sunucu için gösterilen | Anlamı |
|---|---|
| Bu sunucuda çalışıyor | En az bir bileşen bu hesapla yapılandırılmış |
| Taranan kaynaklarda bulunmadı (kullanılmıyor demek değildir) | Üç kaynak da okundu, eşleşme yok — yalnız bu kaynaklar için |
| Belirsiz: kısmi tarama | En az bir kaynak okunamadı; orada olabilir |
| Bilgi yok: sunucu taranamadı | Kaynaklar okunamadı, erişilemedi veya sonuç verilmedi — yeniden tarayın |

## 6. Bulunan bileşenler için karar verin (yalnız sorumlu ekip)

**Karar ver** → kullanım türü (öneri seçili gelir; siz değiştirebilirsiniz) → **Kullanım kaydı oluştur**, ya da gerekçe
yazıp **Kayda almadan kapat**. Karar bileşen ve hesap başına bir kez verilir, geçmişte kalır. Oluşan kullanım kaydı normal
kullanım kaydıdır: bilgi bankası kuralını besler, sonra gerekçeyle kaldırılabilir. Yanlışlıkla "kayda almadan kapat"
derseniz kullanımı "Kullanım ve kural" sekmesinden elle ekleyin.

## 7. gMSA dönüşümünden sonra

Dönüşümü ekip sistem dışında, değişiklik kaydıyla yapar. Ardından aynı sunucularda `-ExpectedAccount` ile tarayıp yükleyin.
Hesap için türetilen sonuç:

| Sonuç | Anlamı |
|---|---|
| Dönüşüm tamamlanmamış | Eski hesap hâlâ en az bir bileşende |
| Kanıt eksik | Eski hesap bulunmadı ama taranamayan veya kısmi taranan sunucu var |
| Taranan tüm sunucularda dönüşmüş (kanıt) | Planlı her sunucu tam tarandı, eski hesap yok, gMSA en az bir bileşende |
| Kanıt yok | Ne eski hesap ne gMSA bulundu |

Bu bir **kanıttır, doğrulama değildir**: gMSA dönüşümünü doğrulayıcı, mevcut işlem doğrulamasında (tarih, doğrulayan,
kanıt) onaylar. Son kontrolün sonucu "Devir ve gMSA" sekmesinde de görünür.

## Yetki

| İşlem | Gereken |
|---|---|
| Taramayı görmek | Hesabı görme kapsamı (yalnız o hesabın bileşenleri ve sunucu kapsamı) |
| Taramayı hesaba bağlamak | `ServiceAccounts.Work` + hesaptan sorumlu kapsam, ya da ekibinize atanmış açık talep |
| Kullanım kaydı oluşturmak / kayda almamak | `ServiceAccounts.Work` + hesaptan sorumlu kapsam |

Kapsam dışı hesap "bulunamadı" gibi görünür. Yüklenen dosyanın kendisi API'den indirilemez.

## Dosya reddedilirse

| Mesaj | Ne yapmalı |
|---|---|
| Parola veya gizli değer gibi görünen alan / değer | Dosyayı kullanmayın; toplayıcıyı yeniden çalıştırıp yeniden birleştirin. Dosyayı elle düzenlemeyin |
| Beklenen biçimde değil, geçerli JSON değil, UTF-8 değil | Yalnız `-CombinePath` çıktısını değiştirmeden yükleyin |
| Planlanan sunucu sayısı tutmuyor / plan dışı sunucu | Planlı listeyi ve sonuç dosyalarını kontrol edip yeniden birleştirin |
| Bir belge çelişiyor / ileri tarih | Taramayı yeniden üretin; sunucu saatini kontrol edin |
| Bu dosya bu hesabı aramamış | Doğru hesabın sayfasından yükleyin ya da doğru `-Account` ile yeniden tarayın |
| Veritabanı güncellemesi 030 uygulanmamış | Ortam yöneticisine bildirin; hesabın diğer bilgileri çalışır |

Sınırlar: 4 MB dosya, 500 sunucu, 20 hesap, sunucu başına 2.000 ve toplam 10.000 bileşen, sunucu başına 20 uyarı.
