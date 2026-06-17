# 14 — Yönetim Özeti (Türkçe)

Bu doküman, repoda Türkçe dilinde tek dokümandır. Yönetim seviyesi paydaşlar (Uğur Bey vb.) için projeyi tek sayfada özetler. Diğer tüm dokümanlar İngilizcedir çünkü yapay zeka kod ajanları ile İngilizce daha verimli çalışır.

---

## Proje Adı

**Secure Ops Automation & AI Analysis Hub** — Windows operasyon otomasyonu ve analiz platformu.

## Tek Cümle Özet

Mevcut izleme platformundan gelen Windows alarmlarını alıp, kısıtlı bir PowerShell uç noktası üzerinden **sadece okuma** modunda tanılama yapan, sonuçları yapılandırılmış audit ile saklayan ve vardiya mühendislerine Web arayüzü ile sunan; sonraki fazlarda kural tabanlı analiz, kurum içi yapay zeka asistanı ve onay tabanlı müdahale yetenekleri ekleyen bir platform.

## Şu Anki Durum

| Konu | Durum |
|---|---|
| Proje aşaması | Uygulama öncesi — doküman ve iskelet üretimi tamamlandı |
| Sunum durumu | Yönetim sunumu yapıldı, "güzel öneri" geri bildirimi alındı |
| Bir sonraki adım | Faz 0 (keşif ve onaylar), ardından Faz 1A kimlik sorgulama MVP ve Faz 1 tanılama MVP |

## Neden Yapıyoruz

Vardiya operasyonunda:
- Alarm başına 8–15 dakikalık manuel inceleme yapılıyor.
- Her mühendis farklı komutlar çalıştırıyor; çıktı standart değil.
- Tekrar eden alarmlar vardiya geçişlerinde gözden kaçabiliyor.
- Yeni mühendis yetişme süresi 6–8 ay.
- Audit kanıtı toplamak saatler sürüyor.

Bu sistem:
- Standartlaştırılmış tanılama → her alarm için aynı kalitede inceleme.
- Yapılandırılmış audit → "geçen Salı APPSRV-12'de ne oldu?" 30 saniyede yanıtlanır.
- Tekrar eden problemleri otomatik yüzeye çıkarır.
- Yeni mühendis yetişme süresini 2–3 aya indirir (hedef).
- Vardiya raporlarını taslak olarak otomatik üretir.
- PAM/AD hesabı görüldüğünde, yetkili lider/admin kullanıcının ilgili hesabı manuel uğraşmadan doğrulamasını sağlar.

## Neyi Değildir

- SIEM **değildir**.
- APM aracı **değildir**.
- İzleme platformunun yerini almaz; üzerine bina edilir.
- PAM'i değiştirmez; sadece audit korelasyonu için **read-only** entegre eder.
- AD veya PAM üzerinde kullanıcı/parola/grup değişikliği yapmaz; kimlik sorgulama sadece okuma modundadır.
- MVP'de **hiçbir yazma işlemi yapmaz**.
- Yapay zekâ ürünü **değildir**; yapay zekâ Faz 7'de ayrı bir karar/bütçe ile değerlendirilir.
- **Kişi takibi sistemi değildir.** Audit, kritik alarmlarda operasyonel müdahale doğrulama, SLA ve denetim amaçlıdır.

## Faz Planı (Üst Seviye)

| Faz | Konu | Süre |
|---|---|---|
| 0 | Keşif, paydaş onayları, pilot listesi | 1–2 hafta |
| 1A | PAM / AD kullanıcı kimlik sorgulama | 1–2 hafta |
| 1 | Sadece okuma tanılama MVP | 4–6 hafta |
| 2 | Web arayüzü + panel | 3–5 hafta |
| 3 | Bildirim + ticket zenginleştirme | 2–4 hafta |
| 4 | Audit + alarm müdahale doğrulama | 3–5 hafta |
| 5 | PAM / AD / local admin uyum | 4–6 hafta |
| 6 | Kural tabanlı analiz ve raporlama | 3–5 hafta |
| 7 | Kurum içi yapay zekâ / RAG PoC | 6–10 hafta (ayrı bütçe) |
| 8 | Onay tabanlı müdahale | 6–8 hafta (gelecek) |

**Gerçekçi takvim:** Tek geliştirici, haftada 16–20 saat çalışma ile:
- **2026 sonu:** Faz 0–4 tamamlanır (operasyonel değer görülebilir).
- **2027 Q1:** Faz 5–6 tamamlanır.
- **2027 ortası:** Faz 7 (yapay zekâ) yönetimle tekrar değerlendirilir.

## Pilot Kapsam

- **10–15 düşük kritiklikteki Windows sunucusu**, tercihen üretim dışı.
- **1–2 alarm tipi** ile başlanır (önerilen: Disk + Servis).
- **4–6 hafta** insan operatörle paralel çalışma.
- **6–8 hafta** sonunda MVP demosu ve yönetim değerlendirmesi.

## Maliyet (Tahminî)

| Kalem | Tahmin |
|---|---|
| MVP doğrudan maliyet | ~0 TL (mevcut altyapı: .NET, SQL Server, IIS, AD; açık kaynak: Hangfire, MudBlazor, Serilog) |
| Faz 7 yapay zekâ donanımı (opsiyonel, gelecek) | ~300–650K TL bir kerelik GPU sunucu (NVIDIA A4000/A6000 sınıfı) |
| Ticari muadiller (kıyas amaçlı) | BMC TrueSight, Splunk ITSI, Datadog, ServiceNow ITOM, Dynatrace: ~500K–3M TL/yıl lisans |

## Operasyonel Kazanım (Pilot Kapsam, Yıllık Tahmin)

| Kalem | Tasarruf |
|---|---|
| Alarm analiz süresi (~8–15 dk → 1–2 dk, %85 azalma) | ~200 saat/yıl |
| Vardiya raporu hazırlık (~30–45 dk → 5–10 dk) | ~134 saat/yıl |
| Vardiya geçiş bilgi aktarımı | ~85 saat/yıl |
| Audit sorgu süresi (saatler → saniyeler) | ~30–40 saat/yıl |
| Mesai / nöbetçi yükü azalması | ~120–240 saat/yıl |
| **Toplam saat tasarrufu** | **~770–940 saat/yıl ≈ 300–500K TL operasyonel kazanım** |
| Önlenen kesintiler (beklenen değer) | 100–300K TL/yıl |
| Onboarding hızlanması (2 yeni vardiya alımı planlı) | 150–200K TL/yıl |

## Riskler (Üst 5)

| # | Risk | Azaltma |
|---|---|---|
| 1 | Tek geliştirici (bus factor 1) | Doküman öncelikli, mainstream teknoloji, küçük commit'ler |
| 2 | İzleme platformu webhook gecikmeleri | Mock öncelikli yaklaşım; alternatif SWIS yolu |
| 3 | Güvenlik onay gecikmeleri | Faz 0'da erken paydaş katılımı |
| 4 | "Kişi takibi" algısı | Çerçeveleme arayüze, rapora, iletişime işlenmiş |
| 5 | Faz 7 yapay zekâ bütçesi reddi | Proje yapay zekâsız değerli; ayrı karar |

Not: Faz 1A kimlik sorgulama özelliği sadece TeamLead/Admin yetkisinde, tek hesap ve gerekçe ile çalışır. Geniş arama, wildcard, parola sıfırlama, hesap açma/kilidi kaldırma veya grup değişikliği yoktur.

## Onay İstenenler (Faz 0 için)

1. Pilot sunucu listesi (10–15) — sahip onayı.
2. PAM servis hesabı talebi.
3. İzleme platformu webhook erişimi.
4. Bilgi Güvenliği + Siber Güvenlik mimari ön incelemesi.
5. Test ortamı tahsisi.
6. Faz 1 başlangıç onayı.

## Audit Çerçevelemesi (Kritik)

Sistemin audit özelliği:

> *"Kişi takibi amacıyla değil; kritik alarmlarda operasyonel müdahale doğrulama, SLA ve audit amacıyla."*

Bu çerçeveleme:
- Tüm arayüzde uygulanır (kişi bazlı varsayılan sıralama yok, kişi bazlı leaderboard yok).
- Tüm raporlarda uygulanır (varsayılan ekip düzeyinde, kişi bazlı sadece denetim rollerinde).
- Tüm yönetim ve İK iletişiminde tutarlı kullanılır.

Bu, mailimde yönetime verdiğim sözdür ve repoda her yerde korunur.

## Soru ve Geri Bildirim

Detay sorular için:
- Teknik dokümanlar: `docs/` klasörü (İngilizce).
- Karar kayıtları: `docs/adr/` klasörü.
- Faz planları: `plans/` klasörü.
- Yol haritası: `docs/02-roadmap.md`.
