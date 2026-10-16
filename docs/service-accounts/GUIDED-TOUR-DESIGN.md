# Servis Hesapları — Adım adım rehber turu (REHBER-UI)

Tarih: 2026-10-16 · Sahip: Claude (Servis Hesapları modülü, `docs/service-accounts/README.md` → Authority) · Dal: `feature/service-accounts-guided-tour-20261016`

## 1. Sorun

Sahip geri bildirimi: "Nasıl kullanılır?" (`SaPageGuide`) yalnız yazılı, kalabalık bir metin; kullanıcı ekranda neye basacağını öğrenemiyor.
Çözüm: ekrandaki alanı **vurgulayan**, her adımda tek cümle **ne** + tek cümle **neden** söyleyen, kullanıcının kendisinin başlattığı bir tur.
Uzun metin 3–4 kısa maddeye iner; ayrıntı tura taşınır.

## 2. Sınırlar

- **Otomatik açılmaz.** "Turu gördü" bilgisi tarayıcıda saklanamaz (CLAUDE.md: tarayıcı depolaması yalnız `wasas.appearance`), sunucuda da
  bu modül için bir tercih alanı yok. Tur yalnız "Adım adım göster" düğmesiyle açılır. Sunucu tarafı tercih ihtiyacı:
  `docs/26-ui-backend-contract-gaps.md` **G-41** (uydurulmadı).
- **Yetki sunucudan.** Adımın görünürlüğü `AccessSnapshot.Can(...)` veya hesabın/planın sunucu izinlerinden (`AccountPermissions`,
  `ChangePlanPermissions`) hesaplanır. Yetkisi olmayana o adım hiç gösterilmez ve sayılmaz. Tur hiçbir komut çalıştırmaz, hiçbir şey yazmaz.
- **Dürüst atlama.** Adımın alanı ekranda yoksa (boş liste, henüz oluşmamış önizleme, kapalı sekme, yetkiye bağlı bölüm) adım atlanır ve
  kutuda "Atlanan adım: … — bu alan şu an ekranda yok" yazar; sessizce bozulmaz. Hiç alan yoksa tur bunu söyler ve yalnız "Kapat" sunar.
- **Rapor sayfası hariç** (paralel RAPOR-2 çalışması). Ekibimin işleri ve Yönetim sayfaları bu turda yok; rehberleri değişmedi.
- Dış kütüphane yok; yeni global CSS yok. Bileşen + scoped `.razor.css` (tema değişkenleri) + tek ES modülü (yalnız konum ve kaydırma).

## 3. Bileşen: `SoGuidedTour` (`Shared/Components`)

- Hedef: `data-tour="ad"` özniteliği. Modül görünür (boyutu olan, `hidden`/`aria-hidden`/`inert` altında olmayan) ilk eşleşmeyi alır.
- Durum makinesi saf C#: `GuidedTourState` (`Services/GuidedTour.cs`) — başlat, ileri, geri, kapat, atlananlar; bileşen yalnız onu gösterir.
- Kutu: `role="dialog"` (`aria-modal="false"`, sayfa kullanılabilir kalır), başlık `tabindex="-1"` ve her adımda odak başlığa gelir;
  "Adım 2 / 5", **Ne:** / **Neden:**, Geri / Kapat / İleri (son adımda "Bitir"). Esc kapatır. Kapanınca odak "Adım adım göster" düğmesine döner.
- `aria-live`: yalnız atlama bildirimi (`role="status"`); adım metni odak ile okunur, ayrıca duyurulmaz.
- Vurgu: hedefin çevresinde kalın çerçeve + sayfanın geri kalanı hafif karartılır (renk tek işaret değil: şekil ve karartma; kutu alanın adını yazar).
  `forced-colors` modunda sistem rengi. Çerçeve tıklamayı engellemez.
- Hareket: yalnız açılışta kısa bir belirme, o da yalnız `prefers-reduced-motion: no-preference` altında; çerçeve alanı animasyonsuz izler
  (kaydırırken geride kalmasın). Azaltılmış harekette animasyon yok, kaydırma anlık.
- Dar ekran: 700 px ve altında (390 px telefon, 1280 px'te %200 yakınlaştırma) kutu ekranın altına sabitlenir, en çok yarım ekran yüksekliği,
  içerik kendi içinde kayar; hedef üstte kalacak şekilde kaydırılır ve sayfaya geçici alt boşluk eklenir (sondaki alan da kutunun üstüne
  çıkabilsin; tur kapanınca geri alınır). Yatay kaydırma yok. 640×400 gibi çok alçak ekranda büyük bir alan kutunun altına kısmen girer.
- JS modülü `wwwroot/js/so-guided-tour.js`: `present(adlar)`, `show(ad, kutu, çerçeve)`, `hide(düğmeId)`. Mantık yok; yalnız konum, kaydırma, odak.

## 4. Turlar (her adım: başlık · ne · neden; ayrıntı `ServiceAccountTours.cs`)

| Sayfa | Adımlar (hedef) | Yetkiye bağlı adım |
|---|---|---|
| Servis Hesapları listesi | Takibinizdeki işler (`sa-work-summary`) · Dizinde ada göre ara (`sa-directory-search`) · Süzgeçler (`sa-filters`) · Yeni hesap kaydı (`sa-create`) · Hesap listesi (`sa-results`) · Birden çok hesap seçme (`sa-select`) · Diğer bölümler (`sa-module-links`) | Dizin araması: `IdentityLookup`; yeni kayıt: `Assign`; seçim: `Work` |
| Hesap detayı | Özet (`sa-summary`) · "Bir gelişme mi oldu?" (`sa-event`) · Sıradaki adım (`sa-next`) · İşler: talep (`sa-tab-work`) · Doğrulama (`sa-tab-work`) · "Hangi sunucularda?" (`sa-tab-usage`) · Kaynak ve geçmiş (`sa-tab-history`) | Gelişme: olay akışı kurulu ve `Work` veya atanmış talep; doğrulama: `Verify` |
| İçe aktarım | Akış (`sa-import-steps`) · Dosya türü (`sa-import-kinds`) · Dosya ve rapor tarihi (`sa-import-source`) · Önizleme oluştur (`sa-import-stage`) · Satırlar ve kararlar (`sa-import-rows`) · Aktarımı onayla (`sa-import-commit`) · Geçmiş aktarımlar (`sa-import-history`) | Sayfa zaten `Import` ister; önizleme öncesi/sonrası alanlar dürüstçe atlanır |
| Değişiklik planları | Yeni plan (`sa-plan-create`) · Planlar ve süzgeç (`sa-plans-filter`) · Plana gir (`sa-plans-list`) | Yeni plan: `Work` |
| Değişiklik planı | Planın adımı (`sa-plan-steps`) · Hesaplar ve hedef adlar (`sa-plan-plan`) · Önizleme (`sa-plan-preview`) · Onay (`sa-plan-approval`) · Kontrol listesi (`sa-plan-checklist`) · Doğrulama (`sa-plan-verification`) | Okuma adımları herkese; komutları sunucu karar verir |

Detay sayfasındaki sekme içerikleri (`KeepPanelsAlive`) gizli olduğu için tur **sekme başlığını** vurgular (`SaTabTitle Tour="…"`),
seçili sekmeyi değiştirmez.

## 5. Testler

- `GuidedTourTests` durum bölümü: ileri/geri, atlanan hedef ve bildirimi, hiç hedef yok, yetkisiz adımın sayılmaması, son adımda "Bitir".
- `GuidedTourTests` bileşen bölümü (küçük test renderer, gerçek bileşen yaşam döngüsü, sahte `IJSRuntime`): başlatınca kutu ve odak çağrısı,
  ileri/geri, atlanan adımın metni, Esc ile kapanma ve odağın tetikleyen düğmeye dönmesi (`hide(düğmeId)`), yetkisiz adımın gizlenmesi.
- `ServiceAccountGuidedTourUiTests`: her turun her hedefi sayfa/bileşen kaynağında `data-tour` olarak var; rapor sayfasına tur eklenmedi; kısaltılmış rehberler 3–4 madde.
- Tarayıcı: `tests/browser/service-accounts-guided-tour.cjs` (Demo profili): listede turu başlat, ilerle, Esc, odak dönüşü, 390 px'te yatay kaydırma yok.
  Cloud oturumunda koşulmadı (Demo API + UI gerekir); Windows'ta koşulacak.

## 6. Açık sorular

- Sunucu tarafı "turu gördü" tercihi istenir mi (ilk girişte bir kez önerme)? G-41.
- Ekibimin işleri ve Yönetim sekmeleri de tura alınsın mı?
