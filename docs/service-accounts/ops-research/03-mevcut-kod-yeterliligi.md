# 3. Mevcut kod ve kararlar bu hedefe yeter mi?

Hedef (proje sahibi, 2026-10-05): web sistemi aracın yaptığı her şeyi yapsın; çok hesapta toplu işlem, değişiklik ve
rapor; hızlı; sunucularla yönetilen biçimde konuşsun; arayüz kullanıcıyı öğretip yönlendirsin.

## Kısa cevap

- **Kayıt, kanıt, kapsam, rapor:** yeterli ve aracın çok önünde. Toplu planlama ve izleme eklenebilir; ilke değişikliği
  gerekmez.
- **Bulma hızı ve ölçeği:** kod hazır (toplayıcı, JEA fonksiyonu, sözleşme, ayrıştırıcı) ama ürün tarafı yayma **kararla
  rafta** (ADR-0024). Açılması ilke değişikliği değil, **onay** meselesi.
- **Değiştirme (AD parola, bileşen kimliği):** mevcut kurallar **açıkça engelliyor**. Hedefin bu kısmı ilke revizyonu ve
  kurum onayı olmadan yapılamaz. Bu belge onu önermez; nerede ve neden değişmesi gerektiğini yazar.

## Modül kodu

| Alan | Yeterlilik | Not |
|---|---|---|
| Hesap, talep, eylem (plan → gerçekleşti → doğrulandı), OR/OCO/Jira | Yeterli | Değişiklik akışının "kâğıt" tarafı zaten burada; uygulama eklenirse bu kayıtlara bağlanmalı, yeni paralel kayıt açılmamalı |
| Kapsam yetkisi (ADR-0026) ve dayanak (sorumlu / katılımcı) | Yeterli | Toplu işlem her hesap için ayrı kontrol etmeli; kapsam dışı hesap önizlemede görünmez, planda reddedilir |
| Kullanım taraması (ADR-0027) | Yeterli (yedek yol olarak kalmalı) | JEA uç noktası olmayan sunucular için kişi çalıştırmalı yol hep gerekir |
| Toplayıcı / JEA fonksiyonu | Hızlı tasarım | Tek CIM sorgusu, SID çevirisi yok, IIS XML bir kez; aracın 2–4. yavaşlık nedenini zaten çözüyor. Windows PowerShell 5.1'de sunucuda henüz çalıştırılmadı |
| Worker | Yetersiz | Yalnız hatırlatma işi var; WinRM/JEA çalıştırıcı, paralel iş, sunucu bazında durum yok (platform genelinde de yok: Faz 1 başlamadı) |
| Toplu işlem | Kısmen | İçe aktarmadaki önizleme → karar → `Idempotency-Key` ile onay deseni toplu değişiklik planına örnek alınabilir |
| Denetim | Yeterli | Aynı işlemde geçmiş + `audit.AuditLog`; uygulama adımları için yeni ekleme-yalnız tablolar gerekir |
| Arayüz | Kısmen | Erişilebilir, dürüst durumlar var; "sırada ne var" rehberliği ve sihirbaz yok |

## İlkeler: hangisi tutuyor, hangisi revize edilmeli

| İlke / karar | Bu hedefle durumu | Öneri | Gerekçe |
|---|---|---|---|
| **AGENTS kural 1** (Faz 8'e kadar salt okunur) | Değiştirme isteğiyle doğrudan çelişir | **Kural kalsın;** dar, adlandırılmış bir istisna ADR'si yazılsın (yalnız servis hesabı kimlik değişikliği kataloğu) — ya da değiştirme ürün dışında kalsın | Kuralın amacı "platform kesinti nedeni olmasın". Dar katalog + iki kişi onayı + geri dönüş planı bu amaca uyar; kuralı genel olarak gevşetmek uymaz |
| **Faz 8 ön koşulları** (Faz 1–6 üretimde 6 ay kararlı) | Bugün Faz 1 başlamadı; koşul yıllar sonra | Proje sahibi + yönetim, yalnız bu katalog için ön koşulu **yazılı olarak** kaldırırsa ilerlenir; aksi halde değiştirme 2027+ | Ön koşul ürün olgunluğunu ölçüyor; istisna kararı teknik değil, kurumsal |
| **ADR-0006 başlangıç kataloğu** ("yapılandırma değişikliği", `Stop-*`, `Restart-*` yok) | Gereken işlemler tam olarak dışarıda bırakılanlar | Ayrı katalog ADR'si: `Set-ServiceIdentity`, `Set-AppPoolIdentity`, `Set-TaskPrincipal` (+ kontrollü yeniden başlatma) | ADR-0006 her yeni eylemin kendi ADR'siyle eklenmesini zaten öngörüyor |
| **AGENTS kural 3** (tüm WinRM JEA ile) | Uyumlu, korunmalı | Okuma ve yazma için **ayrı** JEA uç noktası, ayrı rol dosyası, ayrı AD grubu | Aracın en büyük riski kısıtsız oturum; ürün bunu tekrarlamamalı |
| **ADR-0024** (JEA ile okuma, rafta) | Hız hedefinin anahtarı | **Raftan indirilsin** ve onaya sunulsun; kapsama isteğe bağlı COM+ ve kullanıcı hakkı okuması, gMSA ön koşul kontrolü eklensin | Ürün tarafı paralel tarama başka yolla kuralı bozmadan yapılamaz |
| **ADR-0027** (kişi çalıştırır, yükler) | Doğru, ama tek başına yavaş | Kalsın (yedek yol); çok hesaba tek yüklemede bağlama ve "yalnız eksik sunucuları yeniden planla" eklensin (uygulama değişikliği, ilke değil) | Uç noktası olmayan, ağ dışı veya onay bekleyen sunucular hep olacak |
| **ADR-0026** (kapsam) | Uyumlu | Değişmez | Toplu işlem hesap bazında kapsam kontrolüyle çalışır |
| **SPEC kapsam cümlesi** ("parola döndürmez, gMSA'ya çevirmez, sunucu taramaz") | Hedefle çelişir | Yalnız ilgili ADR kabul edildikten sonra aynı değişiklikte güncellensin | Belgeler kararın önüne geçmemeli |
| **Güvenlik modeli tanılama izin listesi** (ham `Get-WebConfigurationProperty`, `Get-Content`) | Açık bulgu (IIS parolası / herhangi bir dosya okunabilir) | Herhangi bir uç nokta kurulmadan **önce** kapatılsın | Aynı sunuculara yeni uç nokta kurarken eski açığı bırakmak savunulamaz |
| **Worker ayrıcalıklı erişim yolu** (BeyondTrust aracılı mı, doğrudan WinRM + Kerberos + JEA mı) | Açık karar; okuma ve yazma ikisini de bloklar | Önce bu karar | AGENTS bu karar için cevap varsaymayı yasaklıyor |
| **Barındırma** (API ve Worker ortak IIS sunucusunda) | Yazma işi için zayıf | Yazan Worker işi ayrı kimlikle (tercihen gMSA) ve mümkünse ayrı sunucuda | Çok sunucuda yazma yetkisi olan kimlik, ortak sunucuyu yüksek değerli hedef yapar |
| **Kural 7** (BeyondTrust'a dokunulmaz) | Parola döndürme için ilgili | Klasik hesap parolası döndürme PAM ekibinin sürecinde kalsın; modül izlesin | PAM ürünleri bağımlı servis/görev güncellemeyi zaten yapıyor olabilir (doğrulanmalı); ikinci bir parola sistemi kurmak riski artırır |

## Değiştirmeye kadar gitmeden yapılabilecekler (onay gerekmez)

Toplu plan ve izleme, tarama farkı, "eksik sunucuları yeniden planla", çok hesaba tek yükleme, rehberli arayüz. Bunlar
mevcut ADR'ler içinde kalır. Kişi çalıştırmalı toplayıcıya salt okunur COM+ ve kullanıcı hakkı okuması eklemek ADR-0027'ye
kısa bir ek ister (modül sahibi yetkisinde); aynı şeyi JEA fonksiyonuna eklemek ADR-0024 onayının parçasıdır.
