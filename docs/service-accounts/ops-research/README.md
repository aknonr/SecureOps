# Servis hesabı operasyonları — araştırma (2026-10-05)

Durum: **araştırma ve öneri**. Kod, SQL, JEA veya script değişikliği yok; hiçbir sunucuya bağlanılmadı; hiçbir karar
kabul edilmiş sayılmaz. Bağlayıcı olan hâlâ `AGENTS.md`, ADR'ler ve [../PROGRESS.md](../PROGRESS.md).

Soru: ekibin PowerShell aracı hesabın nerede kullanıldığını bulur ve parola değişikliğini servis, görev ve IIS'te uygular.
Proje sahibi web sisteminin bunu ve fazlasını (çok hesapta toplu işlem, değişiklik, rapor) hızlı, yönetilen biçimde ve
kullanıcıyı yönlendirerek yapmasını istiyor.

| Belge | İçerik |
|---|---|
| [01-script-farki.md](01-script-farki.md) | Eski/yeni araç: çalışma prensibi, işlem listesi, yavaşlık nedenleri, risk bulguları (R1–R14) |
| [02-yetenek-tablosu.md](02-yetenek-tablosu.md) | Aracın her işlevi için modülde var / kısmen / yok |
| [03-mevcut-kod-yeterliligi.md](03-mevcut-kod-yeterliligi.md) | Kod ve ADR'lerin hedefe uyumu; revize edilmesi gereken ilkeler ve gerekçesi |
| [04-tasarim-secenekleri.md](04-tasarim-secenekleri.md) | Erişim ve değişiklik seçenekleri, toplu akış, parola, denetim, hız, arayüz; güvenlik / onay / tahmin |
| [05-yol-haritasi.md](05-yol-haritasi.md) | Sıralı adımlar, Claude / Codex / sahip, onay bekleyenler işaretli |
| [06-onay-paketi.md](06-onay-paketi.md) | Bilgi Güvenliği / Siber Güvenlik'e verilecek Türkçe onay paketi |
| [ADR-0024 revizyon 2](../../adr/ADR-0024-service-account-usage-discovery.md) | Ürün tarafı salt okunur tarama (Worker → WinRM → JEA), onaya hazır hâli |
| [ADR-0028](../../adr/ADR-0028-service-account-gmsa-switch.md) | Onaylı gMSA'ya geçiş: parola parametresi olmayan yazma uç noktası, plan → önizleme → onay → çalıştırma → doğrulama → geri dönüş |

## Sahip kararları (2026-10-06)

- **Parola değişimi PAM ekibinin yükümlülüğü;** ürün parola değiştirmez, taşımaz. Ürünün yazması (onaylanırsa) yalnız
  servis / IIS havuzu / görev kimliğini gMSA'ya çevirmekle sınırlı önerildi (ADR-0028).
- "Aracın yaptığını en iyi şekilde yapalım": okuma (ADR-0024 R2) ve gMSA geçişi (ADR-0028) onaya hazır taslak olarak
  yazıldı; ikisi de onay gelene kadar **uygulanmaz**.

## Bir paragrafta öneri

Bulmayı hızlandır, değiştirmeyi parolasız yap. Önce onay gerektirmeyen iyileştirmeler (toplu plan, rehberli manuel
değişiklik, çok hesaba tek yükleme, tarama farkı). Paralelde ADR-0024'ü (Worker → WinRM → JEA ile salt okunur tarama)
raftan indirip onaya götür; aracın hız kazancının çoğu buradan gelir ve yazma gerektirmez. Ürün bir gün değişiklik
yapacaksa tek katalog **gMSA'ya geçiş** olsun (parola yok, iki kişi onayı, beklenen-mevcut kimlik kontrolü, dalga dalga
uygulama, yeniden okumayla doğrulama). Klasik parola döndürme ürüne alınmasın; PAM sürecinde kalsın, modül izlesin.

## Proje sahibine sorular (cevaplar yol haritasını belirler)

1. **Yazma** (= yönetilen sunucuda bileşen kimliğini değiştirmek; modülün kendi veritabanı değil): ADR-0028 için
   yönetimden Faz 8 ön koşul istisnası istenecek mi? İstenmezse değiştirme insanda kalır (aşama A + B).
2. ~~Klasik parolalar~~ — cevaplandı: PAM'ın yükümlülüğü. Açık kalan: PAM yalnız AD parolasını mı döndürüyor, yoksa
   bağımlı servis/görev/havuzu da güncelliyor mu (D1)? Güncellemiyorsa o iş hâlâ elle yapılıyor demektir.
3. **ADR-0024 R2 ve onay paketi:** 06-onay-paketi.md ile Bilgi Güvenliği'ne götürülsün mü?
4. **Worker erişim yolu:** PAM / BeyondTrust ekibine yazışma (sistem haritasındaki açık karar) ne durumda (B1)?

## Belirsiz kalanlar (doğrulanmadan karar verilmemeli)

- IIS sanal dizin "connect as" ve COM+ kimliğinde gMSA desteği (bilinen: "connect as" parola ister).
- Servis kimliği WMI ile değiştirildiğinde "hizmet olarak oturum açma" hakkının otomatik verilip verilmediği.
- Hedef sunucularda PowerShell modül günlüğü ve 4688 komut satırı denetiminin açık olup olmadığı (R2, R3'ün etkisi).
- PAM ürününün bağımlı servis/görev/havuz güncelleme yeteneği.
- Toplayıcı ve JEA fonksiyonu Windows PowerShell 5.1'de gerçek sunucuda hiç çalıştırılmadı; hız tahminleri ölçülmedi.
- Eski araç sürümünün ADR-0024 özetinde olmayan davranışı.

## Girdiler hakkında

Girdiler `C:\SecureOpsBuild\inputs\` altında (repo dışı) okundu; kopyalanmadı. Paylaşılan kopyada parolalar yer tutucu;
kişi adı yok. Ancak gerçek altyapı tanımlayıcıları var (ITSM adresi, entegrasyon kullanıcı adı, kiracı/grup kimlikleri,
kişisel PAM hesap kimliği içeren dosya yolu, etki alanı adları, vCenter IP adresleri; ek CSV'de gerçek sunucu adları).
Bunların hiçbiri bu belgelere alınmadı.
