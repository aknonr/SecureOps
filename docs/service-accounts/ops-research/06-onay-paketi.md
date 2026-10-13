# 6. Onay paketi — Bilgi Güvenliği / Siber Güvenlik

Kime: Bilgi Güvenliği, Siber Güvenlik; bilgi: PAM / BeyondTrust ekibi, AD ekibi, değişiklik kurulu. Kimden: proje sahibi.
Ayrıntı: ADR-0024 revizyon 2 (okuma) ve ADR-0028 (gMSA'ya geçiş). İki onay **ayrıdır**; okuma tek başına onaylanabilir.

**Durum (2026-10-06):** ADR-0024 R2 sahip kararıyla *Accepted*. Bilgi Güvenliği / Siber Güvenlik ile sunucu sahibi onayı
(JEA uç noktası kaydı, okuma gMSA'sı) **ALINMADI** ve pilot öncesi şarttır; Worker erişim yolu (doğrudan WinRM+Kerberos
mı, BeyondTrust mı) **açık soru**. Sahip kararı gereği sunucuda yazma yoktur: aşağıdaki 2. ve 3. talepler (ADR-0028,
*Proposed*) bu pakette **beklemededir**, şu an istenmiyor; AGENTS.md kural 1 ve 3 değişmez.

## Ne istiyoruz

| # | Talep | Sunucuda ne değişir | Karar |
|---|---|---|---|
| 1 | **Okuma uç noktası** (ADR-0024 R2): servis hesabının hangi servis, görev, IIS havuzu/sitesi, COM+ uygulamasında çalıştığını salt okunur bulmak | Tek seferlik JEA uç noktası kaydı; sonra hiçbir şey | Onay / ret / koşullu |
| 2 | **gMSA'ya geçiş uç noktası** (ADR-0028): onaylı değişiklikte servis, IIS havuzu, zamanlanmış görev kimliğini gMSA'ya çevirmek | Yalnız onaylı çalıştırmada, yalnız o bileşenin kimliği; yeniden başlatma | Onay / ret / koşullu (yönetimin Faz 8 istisnasıyla birlikte) |
| 3 | ADR-0006 "anlık görüntü" koşulu yerine yapılandırma yedeği (yalnız #2 için) | Havuz öğesinin yedeği aynı sunucuda, 30 gün | Onay / ret |

## Bugünkü durum (değişmeden kalırsa)

Ekip aynı işi kendi aracıyla yapıyor: kişisel yönetici oturumu, kısıtsız uzak komut, parolanın her sunucuya düz metin
argüman olarak gitmesi ve bazı yollarda konsola/komut satırına yazılması, önizleme/onay/kayıt yok, bayat önbellekle
koşulsuz yazma (bulgular: [01-script-farki.md](01-script-farki.md), R1–R8). Bu talep riski **eklemiyor, azaltıyor**.

## Tehdit → kontrol

| Tehdit | Kontrol |
|---|---|
| Platform kimliği çalınır, sunucuda keyfi komut çalıştırılır | JEA `RestrictedRemoteServer`, `NoLanguage`; okuma ucunda tek, yazma ucunda üç görünür fonksiyon; betik parametresi yok; sanal hesapla çalışır; transkript açık |
| Okuma kimliği yazar | Okuma ve yazma ayrı gMSA, ayrı AD grubu, ayrı uç nokta |
| Parola sızar | gMSA'da parola yok; yazma fonksiyonlarında **parola parametresi yok**; okuma IIS'te parola özniteliğini seçmez; COM+ parolası katalogda zaten okunamaz; ürün parola değiştirmez (PAM'ın görevi) |
| Yazma ucu sıradan bir hesabı veya yerleşik hesabı ayarlamak için kullanılır | Yeni kimlik yalnız gMSA biçimi (`…$`); yerleşik/yerel hesaplar reddedilir |
| Bileşen bu arada başka hesaba geçmiştir, araç üzerine yazar | Karşılaştır-ve-değiştir: beklenen mevcut kimlik değilse dokunmaz (`PreconditionChanged`) |
| Yanlış ya da geniş değişiklik | 1–20 hesap; bileşenler yalnız son 24 saatlik taramadan; önizleme sürümü; **onaylayan ≠ planlayan**; OCO numarası; bakım penceresi; kanarya → dalgalar; hata eşiğinde durma; acil durdurma |
| Kesinti, geri alınamaz durum | Havuz: sunucudaki yedekten geri alma. Servis/görev: eski hesap değişmeden kalır, geri dönüş PAM'daki parolayla kişi tarafından; çalıştırma sonrası otomatik doğrulama taraması |
| İz kaybı | Ekleme-yalnız kayıtlar (plan, önizleme, onay, adım sonucu, durdurma) + `audit.AuditLog`; her çağrının kimliği sunucu olay günlüğünde de |
| "Bulunamadı" yanlış yorumlanır | Erişilemeyen / uç noktası olmayan / sonuç vermeyen sunucu ayrı ayrı "bilgi yok"; hiçbir sonuç hesabı kapatmaz |
| Kişi izleme algısı | Kayıtlar operasyonel kanıt; kişi bazlı karşılaştırma ekranı yok |

## Yapılmayanlar (açıkça)

Parola değiştirme veya taşıma · AD'de yazma (gMSA oluşturma, izinli sunucu grubu — AD ekibi) · kullanıcı hakkı verme ·
hesap kapatma/silme · zamanlanmış veya otomatik tarama/değişiklik · BeyondTrust'a dokunma · varsayılan WinRM uç noktası.

## Sizden beklediğimiz kararlar

1. Okuma uç noktası ve okuma gMSA'sı: onay / koşul.
2. Kullanıcı hakkı okuma yöntemi: derlenmiş kod (`Add-Type`) mı, geçici dosyaya `secedit` dışa aktarımı mı, hiç mi?
3. Worker erişim yolu: doğrudan WinRM + Kerberos + JEA kabul mü, BeyondTrust aracılığı mı şart? (PAM ekibiyle)
4. Yazma uç noktası ve yazma gMSA'sı: onay / koşul; yapılandırma yedeği mi, VM anlık görüntüsü mü?
5. Mevcut tanılama izin listesindeki ham `Get-WebConfigurationProperty` / `Get-Content` bulgusunun kapatılma biçimi.

## Pilot önerisi

Okuma: 10–15 sunucu (sunucu sahipleri imzalı), 2 hafta, süre ve kapsam ölçümü. Yazma (onaylanırsa): önce üretim dışı bir
sunucu, sonra tek bir gerçek hesap ve kanarya; her çalıştırma OCO ile.
