# 1. Eski ve yeni ekip aracı: fark, yavaşlık, risk

Girdi: ekibin yeni WPF PowerShell aracı ve XAML eki (repo dışında, kopyalanmadı). Eski sürüm için yalnız ADR-0024'teki
sanitize özet var; özette olmayan eski davranış için "bilinmiyor" yazıldı. Bu belgede gerçek sunucu, alan adı, IP, hesap
veya kişi adı yoktur; örnekler `CONTOSO` / `SYN-*` ile verilmiştir.

## Çalışma prensibi (yeni sürüm)

Tek pencere, her şey düğme olaylarında ve aynı GUI iş parçacığında çalışır:

1. **Açılış:** ITSM'e (Turuncuhat REST) script içine gömülü kullanıcı/parola ile giriş yapar, yönetici grubuna ait hizmet
   gruplarını (RFS) listeye doldurur.
2. **Sunucu listesi** (üç kaynak):
   - seçili RFS → ITSM sorgusu (her sorguda yeniden giriş);
   - tüm RFS'ler → her RFS için ayrı giriş + sorgu, sırayla;
   - vCenter → iki sabit vCenter'a bağlanır, açık Windows VM'leri alır, **adında "SQL" geçenleri dışarıda bırakır**.
3. **WinRM ön filtresi:** 5985 portuna paralel TCP denemesi (200 eşzamanlı, 4 sn). Sonuç kullanıcı profilinde
   **90 gün** saklanır; kapalı görünen sunucu 90 gün boyunca listeye hiç girmez.
4. **Bağlan:** `New-PSSession` tüm listeye tek çağrıda (açılış 3 sn, işlem 5 sn), varsayılan uç nokta, kısıtsız oturum.
5. **Ara:** hesap adı sabit bir NetBIOS önekiyle birleştirilir, SID'e çevrilir; açık oturumlarda toplayıcı betik
   `Invoke-Command -ThrottleLimit 10` ile çalışır. Hesap bazlı önbellek: daha önce taranan ve "başarısız" sayılan
   sunucular **bir daha hiç taranmaz**; eski ve yeni sonuçlar birleştirilip tabloya basılır.
6. **AD parolası:** eski + yeni parola girilir (göz düğmesiyle açık metin gösterilebilir), `Set-ADAccountPassword`.
7. **Kaynaklarda güncelle:** sonuç tablosundaki her satır için sırayla, her satırda yeni bir `Invoke-Command`
   (varsayılan uç nokta) ile kaynağın kimliği ve parolası yeniden yazılır.

## İşlem listesi

| # | İşlem | Okur / yazar | Nasıl |
|---|---|---|---|
| 1 | RFS listesi, RFS sunucuları, tüm RFS sunucuları | ITSM okur | REST, her sorguda giriş |
| 2 | vCenter'dan sunucu listesi | vCenter okur | PowerCLI, sabit adresler |
| 3 | WinRM erişilebilirlik ön filtresi | ağ | TCP 5985, 90 gün önbellek |
| 4 | Hesap AD'de var mı | AD okur | `Get-ADUser` |
| 5 | Oturum açma | uzak | `New-PSSession`, kısıtsız |
| 6 | Kullanım arama: Windows servisi, zamanlanmış görev, IIS uygulama havuzu, IIS sitesi / uygulaması / sanal dizini, COM+ kimliği, kullanıcı hakları (hizmet/toplu iş olarak oturum açma) | uzak okur | CIM, ScheduledTasks, WebAdministration, COMAdmin, `secedit` |
| 7 | Hesap parolasını AD'de değiştir | **AD yazar** | `Set-ADAccountPassword` (eski parola gerekli) |
| 8 | Servis: durdur → kimlik + parola yaz → başlat | **sunucu yazar** | WMI `Win32_Service.Change` |
| 9 | Uygulama havuzu: kimlik + parola yaz → yeniden başlat | **sunucu yazar** | `Set-ItemProperty`, `Restart-WebAppPool` |
| 10 | IIS site / uygulama / sanal dizin: "connect as" kimlik + parola yaz | **sunucu yazar** | `Set-WebConfigurationProperty` |
| 11 | Zamanlanmış görev: kimlik + parola yaz | **sunucu yazar** | `schtasks /change /ru /rp` |
| 12 | COM+: kimlik + parola yaz | **sunucu yazar** | COMAdmin `SaveChanges` |
| 13 | Kullanıcı hakkı satırı | — | desteklenmez; ne başarılı ne başarısız sayılır |

## Eski → yeni: ne değişti

| Konu | Eski (ADR-0024 özeti) | Yeni |
|---|---|---|
| Sunucu kaynağı | Tek hizmet grubu (CMDB) | + tüm RFS'ler, + vCenter |
| Oturum açma | Sunucu sunucu, varsayılan zaman aşımı, oturum kapanmaz | Tek çağrı, paralel, 3/5 sn zaman aşımı (oturumlar yine kapanmaz) |
| Ön filtre | Yok | Paralel TCP denemesi + 90 gün önbellek |
| Tarama | GUI'de sırayla | Açık oturumlarda paralel (10) |
| Tekrar tarama | Bilinmiyor | Hesap önbelleği: taranmış sunucu bir daha taranmaz |
| SID çevirisi, IIS üç kez gezme, COM+/`secedit` | Var | **Aynen var** |
| ITSM'e her sorguda giriş | Var | Aynen var, "tüm RFS" ile N kez |
| Görev parolasının konsola yazılması | Var | **Aynen var** |
| IIS okumada parola | `Get-WebConfiguration` tüm öğeyi (çözülmüş parola dahil) çağırana döndürüyordu | Öğe uzak tarafta okunuyor, yalnız kullanıcı adı dönüyor (parola uzak bellekte çözülüyor ama gelmiyor) |
| Yazma yolu | AD + kaynaklar | Aynı; ayrı panel ve düğme, onay/önizleme yine yok |

## Yavaşlık nedenleri (yeni sürüm, etkisine göre)

1. **GUI iş parçacığı:** her şey düğme olayında; pencere donar, ilerleme yok, aynı anda iki iş yapılamaz.
2. **Öğe başına SID çevirisi:** her servis/görev/havuz/sanal dizin kimliği ayrı ayrı `NTAccount.Translate` → hedef
   sunucudan etki alanı denetleyicisine birer gidiş-dönüş; çözülemeyen (silinmiş hesap) kimlik zaman aşımıyla düşer.
3. **IIS üç kez geziliyor;** her uygulama ve sanal dizin için ayrı `Get-WebConfiguration` (her biri yapılandırmayı
   yeniden okur). Çok siteli sunucuda baskın maliyet budur.
4. **COM+ kataloğu ve `secedit` dışa aktarımı** her sunucuda (geçici dosya yazılıp silinir).
5. **"Tüm RFS":** RFS sayısı kadar sıralı ITSM girişi + sorgu.
6. **Tarama `ThrottleLimit 10`:** oturumlar açıkken bile 10'dan fazla sunucu aynı anda taranmaz.
7. **Parola uygulama tamamen sıralı:** satır başına yeni WinRM bağlantısı, servis başına 2 sn bekleme + durdur/başlat,
   havuz yeniden başlatma; 100 bileşen = 100 ardışık bağlantı.

Hızlı olan kısımlar: TCP ön filtresi ve tek çağrıda oturum açma. Önbellek de hızlandırır ama sonucu bayatlatır (risk R6).

## Risk bulguları

Önem: **Y** yüksek, **O** orta, **D** düşük.

| # | Önem | Bulgu | Neden önemli |
|---|---|---|---|
| R1 | Y | ITSM entegrasyon kullanıcısı ve parolası script metninde (paylaşılan kopyada yer tutucu). | Dosyayı gören herkes entegrasyon hesabını kullanır; gizli değer kasada olmalı. |
| R2 | Y | Parola açık metin dolaşıyor: göz düğmesi parolayı `TextBox`'a kopyalar; görev dalı `write-host` ile parolayı yazar (uzak çıktı yerel konsola gelir); `schtasks /rp <parola>` komut satırında. | Konsol dökümü, PowerShell modül/transkript günlüğü ve "komut satırını süreç oluşturma olayına ekle" açıksa güvenlik günlüğü (4688) parolayı saklar. |
| R3 | Y | Parola her hedef sunucuya düz `string` argüman olarak gider; `Set-WebConfigurationProperty -Value`, `Set-ItemProperty` parametre olarak alır. | Hedefte modül günlüğü açıksa parametre değerleri olay günlüğüne düşebilir. |
| R4 | Y | Onay, önizleme, değişiklik kaydı (OCO) bağlantısı, denetim izi yok; tablodaki ne varsa ona yazar. | Yanlış hesap/satır seçimi doğrudan üretime gider; kim ne yaptı izlenemez. |
| R5 | Y | **Bayat veriyle yazma:** hesap önbelleği hiç dolmaz; tabloya eski çalıştırmaların sonuçları da gelir. Yazma dalı kimliği koşulsuz yeniden yazar (önce "şu an hâlâ bu hesap mı" diye bakmaz). | Bileşen sonradan başka hesaba geçtiyse araç onu bu hesaba geri çevirir. |
| R6 | Y | **Kısmi başarısızlık / geri dönüş yok:** AD parolası önce değişir, kaynaklar tek tek güncellenir; biri düşerse o bileşen eski parolayla kalır (bir sonraki yeniden başlatmada çöker). Servis durdurulup `Change` başarısız olursa servis **durmuş** kalır. | Sessiz arıza, gecikmeli kesinti. |
| R7 | O | **Yanlış "başarılı":** `schtasks` çıkış kodu kontrol edilmez; COM+ uygulaması bulunamazsa hiçbir şey yapmadan başarılı sayılır; kullanıcı hakkı satırları sayılmaz; değişiklikten sonra doğrulama (yeniden okuma, servis çalışıyor mu) yok. | Ekran "tamam" der, sunucu farklıdır. |
| R8 | O | **Kapsam sessizce daralıyor:** erişilemeyen sunucu listesi hiç doldurulmuyor (değişken atanmıyor); TCP filtresi kapalı sunucuyu 90 gün listeden çıkarıyor; vCenter dalı adında "SQL" geçen sunucuları atıyor; tek etki alanı öneki sabit. | "Bulunamadı" ile "bakılamadı" karışır (SPEC kural 15); veritabanı ekibi hesaplarının çalıştığı sunucular atlanır. |
| R9 | O | Varsayılan WinRM uç noktası, yönetici yetkili kişisel oturum, kısıtsız komut. | Kişinin kendi pratiği olarak kalabilir; ürüne bu haliyle taşınamaz (AGENTS kural 3). |
| R10 | O | Eski parola zorunlu (`-OldPassword`). | Parolanın insanlar tarafından bilindiğini gösterir — gMSA'nın ortadan kaldırdığı asıl risk. |
| R11 | D | COM+ dalında kimlik etki alanı öneki olmadan yazılıyor (diğer dallar önekli). | Tutarsız sonuç / başarısız oturum. |
| R12 | D | WQL/XPath filtreleri ad birleştirerek kuruluyor (servis, site, uygulama adları). | Ad içindeki tırnak sorgu bozar; kaynak tarama sonucundan geldiği için düşük risk. |
| R13 | D | Önbellek dosyaları (sunucu, hesap, bileşen envanteri) kullanıcı profilinde şifresiz, temizlenmiyor. | Envanter bilgisi kişisel makinede birikir. |
| R14 | D | Script, kişisel masaüstü yolundan XAML okuyor; vCenter adresleri sabit. | Taşınabilir değil; kişiye bağlı. |

## Sonuç

Yeni sürüm **sunucu listesini bulma ve oturum açmada** belirgin hızlı; **tarama** hâlâ öğe başına SID çevirisi ve IIS'in
tekrar tekrar okunması yüzünden yavaş. **Yazma yolu** eski sürümle aynı riskleri taşıyor ve iki yeni risk ekliyor:
bayat önbellekle koşulsuz yazma (R5) ve sessizce daralan kapsam (R8). Ürüne alınabilecek olan: sunucu listesi kaynakları,
paralellik ve kısa zaman aşımı fikri, bileşen listesi. Alınmaması gereken: kısıtsız oturum, düz metin parola, önbelleğe
güvenip koşulsuz yazma, doğrulamasız "başarılı".
