# 5. Yol haritası

Küçük, sıralı adımlar. **Sahip:** Claude (modül; 2026-10-03 sahip kararı), Codex (platform: Worker altyapısı, JEA
kurulum/paketleme, güvenlik modeli, entegrasyonlar — net tanımlı, sınırlı işler), Sahip (karar ve kurum yazışması).
**⏸ ONAY** işaretli adım, yazılı onay gelmeden başlamaz. Tahminler geliştirici iş günüdür.

## Aşama 0 — Karar (hemen)

| # | Adım | Sahip | Not |
|---|---|---|---|
| 0.1 | Bu araştırmayı oku; README'deki dört soruyu cevapla | Sahip | Cevaba göre aşama C hiç açılmayabilir |
| 0.2 | Ekibe aracın risk notunu ilet (R1–R8): ITSM parolasını kasaya taşı, parolayı konsola/komut satırına yazma, önbelleği sınırla veya kaldır, yazmadan önce mevcut kimliği kontrol et, erişilemeyeni kaydet | Sahip | Ekibin kendi aracı; biz kod yazmayız, yalnız bulgu veririz |

## Aşama A — Onay gerektirmeyen iyileştirmeler (mevcut ADR'ler içinde)

| # | Adım | Sahip | Tahmin | Bağımlılık |
|---|---|---|---|---|
| A1 | Sayfalama + 031 dalını master'a al (zaten hazır) | Sahip (birleştirme) | — | — |
| A2 | "Sıradaki adım" kartı: mevcut veriden hesaplanır, yalnız arayüz | Claude | 2 | A1 |
| A3 | Çok hesaba tek yükleme: bir tarama dosyasını seçili hesaplara bağla, her hesap için ayrı kapsam/dayanak kontrolü | Claude | 2–3 | A1 |
| A4 | Tarama farkı ve "eksik/erişilemeyen sunucuları yeniden planla" (planlı sunucu listesini indir) | Claude | 2 | A1 |
| A5 | Sunucu kümesi kaydı (adlandırılmış liste, CSV/yapıştırma, sentetik testler; yeni migration) | Claude | 2–3 | A1 |
| A6 | Toplu plan (M1): seçili hesaplara talep + eylem planı + bileşen kontrol listesi + OCO; önizleme → onay → kayıt deseni | Claude | 5–7 | A3, A5 |
| A7 | Rehberli manuel değişiklik ekranı: kontrol listesini işaretle, ardından gMSA kontrol taraması kanıt | Claude | 3 | A6 |
| A8 | ADR-0027 eki + toplayıcıya salt okunur COM+ ve kullanıcı hakkı okuması (geçici dosyasız yol bulunamazsa yapılmaz) | Claude | 2–3 | — |

Aşama A sonunda: aracın "bulma" işlevi kişi çalıştırmalı yolla, toplu plan ve rehberle birlikte modülde; yazma yok.

## Aşama B — Ürün tarafı salt okunur tarama (⏸ onaylar)

| # | Adım | Sahip | Tahmin | Onay |
|---|---|---|---|---|
| B1 | Worker erişim yolu kararı (BeyondTrust aracılı / doğrudan WinRM + Kerberos + JEA) | Sahip | — | ⏸ PAM ekibi, Bilgi Güvenliği, takım lideri |
| B2 | Tanılama izin listesi bulgusunu kapatan ADR (ham `Get-WebConfigurationProperty` / `Get-Content`) | Codex | 1–2 | ⏸ Bilgi Güvenliği |
| B3 | ADR-0024 güncellemesi ve onay paketi — **taslak yazıldı 2026-10-06** (ADR-0024 R2, 06-onay-paketi.md); sahip götürür | Claude | ✓ | ⏸ Bilgi Güvenliği, Siber Güvenlik |
| B4 | Worker'da genel JEA çalıştırıcı: bağlantı, zaman aşımı, sınırlı paralellik, iptal, sonuç türleri, sahte uygulama (Faz 1 tanılama da kullanır) | **Codex** | 4–6 | B1 |
| B5 | JEA uç nokta kurulum betiği ve paketleme (`proposed/` → sürüm) | **Codex** | 2–3 | B3 |
| B6 | Modül tarama işi: Hangfire işi, sunucu bitince sonucu tarama kaydına yaz (`tool = Jea`), canlı ilerleme | Claude | 5–7 | B4 |
| B7 | Pilot 10–15 sunucu, Windows PowerShell 5.1, süre ölçümü | Sahip + Claude | 2 | ⏸ sunucu sahipleri |
| B8 | ITSM (RFS) ve vCenter sunucu listesi: önce sözleşme + sahte bağdaştırıcı | **Codex** | 3–5 | ⏸ Turuncuhat sözleşmesi, vSphere okuma onayı |

## Aşama C — gMSA geçişini ürün uygular (⏸ en ağır onay; isteğe bağlı)

| # | Adım | Sahip | Tahmin | Onay |
|---|---|---|---|---|
| C1 | Katalog ADR'si — **taslak yazıldı 2026-10-06** (ADR-0028); **Faz 8 ön koşulu istisnası** sahip + yönetim kararı | Claude ✓ / Sahip | — | ⏸ Sahip + yönetim + Bilgi Güv. + Siber Güv. + değişiklik kurulu |
| C2 | Yazma JEA uç noktası tasarımı (beklenen-mevcut kimlik kontrolü, önce/sonra dönüşü) | Claude | 3 | C1 |
| C3 | Yazma uç noktası kurulumu, ayrı yazma kimliği, imza/paket | **Codex** | 3–4 | C2 |
| C4 | Değişiklik planı veri modeli, iki kişi onayı, acil durdurma, ekleme-yalnız sonuç tabloları | Claude | 8–10 | C1 |
| C5 | Uygulama işi: dalgalar, eşik, `Idempotency-Key`, doğrulama taraması | Claude | 6–8 | B4, C3, C4 |
| C6 | Pilot: 1 sunucu → 5 → genel; geri dönüş tatbikatı (PAM'daki parolayla manuel) | Sahip | 3+ | ⏸ sunucu sahipleri, değişiklik kurulu |

## Aşama D — Klasik hesap parolaları (PAM)

| # | Adım | Sahip | Tahmin | Onay |
|---|---|---|---|---|
| D1 | PAM ekibine soru: bu hesapları yönetebilir mi, bağımlı servis/görev/havuzu güncelleyebilir mi | Sahip | — | ⏸ PAM ekibi cevabı |
| D2 | Modülde "PAM tarafından döndürüldü" referansı ve kanıtı (eylem kaydının parçası) | Claude | 3–5 | D1 |

Ürünün kendi klasik parola döndürmesi (M3) yol haritasında **yok**; sahip açıkça isterse ayrı ADR ve tüm onaylarla
yeniden değerlendirilir.

## Sıra ve paralellik

```text
0.1 ─┬─ A1 → A2..A5 → A6 → A7          (onaysız, hemen)
     ├─ B1, B2, B3 (onay yazışmaları paralel) → B4, B5 → B6 → B7
     ├─ D1 → D2
     └─ (B tamam + C1 onayı) → C2..C6
```

Onay yazışmaları uzun sürer; aşama A beklemeden başlar ve onaylar gelmese de tek başına değer üretir.
