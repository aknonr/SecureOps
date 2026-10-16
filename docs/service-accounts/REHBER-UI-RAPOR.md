# REHBER-UI — Rapor (2026-10-16)

Dal: `feature/service-accounts-guided-tour-20261016` (master `cf16a63`'ten). Tasarım: [GUIDED-TOUR-DESIGN.md](GUIDED-TOUR-DESIGN.md).

## Ne değişti

- **"Adım adım göster" turu**: Servis Hesapları listesi, hesap detayı, içe aktarım, değişiklik planları ve plan detayında "Nasıl kullanılır?"
  yanında. Alanı çerçeveler, her adımda **Ne:** + **Neden:** (birer cümle), "Adım n / m", Geri / Kapat / İleri (son adımda Bitir), Esc kapatır,
  odak kutu başlığına gider ve kapanınca düğmeye döner. Otomatik açılmaz.
- **Yetki**: adımın görünmesi sunucunun cevabına bağlı (`AccessSnapshot.Can`, hesap/plan izinleri). Örn. dizin araması yalnız Identity.Lookup,
  yeni hesap yalnız Assign, çoklu seçim yalnız Work, "Bir gelişme mi oldu?" yalnız olay akışı kuruluysa ve Work/atanmış talep varsa,
  "Doğrulama" yalnız Verify. Yetkisiz adım gösterilmez, sayılmaz.
- **Dürüst atlama**: alanı ekranda olmayan adım (boş liste, henüz önizleme yok, dar ekranda gizli tablo başlığı) atlanır ve kutu adını söyler.
- **Kısa rehber**: bu beş sayfanın "Nasıl kullanılır?" metni 3–4 kısa maddeye indi; ayrıntı turda. Ekibimin işleri, Yönetim ve **Raporlar
  değişmedi** (rapor sayfasına dokunulmadı).
- Kod: `SoGuidedTour` (Shared), `GuidedTourState` (saf C#), `ServiceAccountTours`, `wwwroot/js/so-guided-tour.js` (yalnız konum/kaydırma/odak),
  scoped CSS (tema değişkenleri). `SaPageGuide`'a `Tour`, `SaTabTitle`'a `Tour` parametresi. API, SQL, migration değişikliği yok.

## Nasıl doğrulandı / ne doğrulanmadı

Ortam: Linux cloud oturumu. SDK 9.0.317 indirilemedi (proxy `builds.dotnet.microsoft.com`'u reddetti); Ubuntu paketinden **SDK 10.0.112** +
net8 çalışma zamanı kuruldu, `global.json` **yalnız yerelde** 10.0.112'ye çevrildi (commit edilmedi).

- `dotnet build SecureOps.sln`: 0 uyarı, 0 hata.
- Birim: 3021/3024 geçti. Kalan 3 hata master'da aynı ortamda da var, bu işle ilgisiz: `BuildToolchainContractTests` (global.json geçersiz
  kılma), `AuditConfigurationValidatorTests.Validate_WhenProductionFailOpen_Throws` (Linux yol), `SccmFailureEvidenceTests` (PowerShell
  execution policy Linux'ta desteklenmiyor). Yeni: `GuidedTourTests` (13), `ServiceAccountGuidedTourUiTests` (11).
- `dotnet format SecureOps.sln --verify-no-changes`: temiz — repodaki dosyalar LF, `.editorconfig` CRLF istediği için **CRLF kopyada** koşuldu.
- JS modülü + CSS: headless Chromium'da sahne sayfası (gerçek uygulama değil): 1440 px (kutu hedefle çakışmıyor), 390 px + azaltılmış hareket
  (kutu altta tam genişlik, animasyon yok, hedef kutunun üstünde), 640×400; yatay kaydırma yok, odak dönüşü doğru.
- **Doğrulanmadı**: gerçek sayfalar tarayıcıda (Demo API + SQL burada yok), `tests/browser/service-accounts-guided-tour.cjs` (yazıldı, sözdizimi
  kontrol edildi, koşulmadı), ekran okuyucu, açık/koyu temayı gözle, SDK 9.0.317, Windows/IIS, kurumsal TEST.

## Belgeler

`GUIDED-TOUR-DESIGN.md` (yeni), `PROGRESS.md`, `src/SecureOps.Ui/README.md` (tarihli not), `docs/26-ui-backend-contract-gaps.md` **G-41**
(sunucu tarafı "turu gördü" tercihi yok). Master'da son numara G-39; görevde G-40'ın başka dalda kullanıldığı söylendiği için G-41 seçildi.

## Açık sorular

1. İlk girişte turu bir kez önermek istiyor musunuz? Bunun için sunucu tarafı tercih gerekir (G-41); şu an yalnız düğmeyle açılıyor.
2. Ekibimin işleri ve Yönetim sekmeleri de tura alınsın mı?
3. G-40 gerçekten başka dalda mı? Değilse G-41 → G-40 yapılabilir.
4. 640×400 (1280×800'de %200) gibi çok alçak ekranda büyük bir alan alttaki kutunun altına kısmen giriyor; kutu küçültülsün mü, yoksa kabul mü?

## Sonraki adım

Windows'ta SDK 9.0.317 ile build + birim + format; Demo profilinde `node tests/browser/service-accounts-guided-tour.cjs …`; beş sayfada turu
klavyeyle, açık/koyu temada, 390 px ve %200'de gözle deneme.
