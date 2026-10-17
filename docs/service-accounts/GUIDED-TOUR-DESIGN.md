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
- **Rapor sayfası hariç** (paralel RAPOR-2 çalışması). Ekibimin işleri ve Yönetim sekmeleri REHBER-2 (2026-10-17) ile tura alındı (§4b).
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
  çıkabilsin; tur kapanınca geri alınır). Yatay kaydırma yok. 640×400 gibi çok alçak ekranda kutu en çok %42 yüksekliktedir ve **"Açıklamayı gizle / göster"** düğmesiyle katlanır (§4b).
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

## 4b. REHBER-2 (2026-10-17): yeni turlar ve katlanan kutu

Koordinatör kararları: (a) Ekibimin işleri ve Yönetim sekmeleri tura alınsın; (b) %200'de büyük alan kutunun altında kalmasın, kutu küçülsün; (c) otomatik açılış yok (G-41 backend işi).

| Sayfa / sekme | Adımlar (hedef) | Yetkiye bağlı / dürüst atlama |
|---|---|---|
| Ekibimin işleri | Takibinizdeki işler (`sa-work-summary`) · Hazır süzgeçli listeler (`sa-work-links`) · Hatırlatma süzgeci (`sa-reminders-filter`) · Hatırlatmalar (`sa-reminders-list`) · Değerlendirmeyi şimdi çalıştır (`sa-reminders-run`) | Çalıştır: `Administer`; Kapat metni yalnız `Work`; hatırlatma yoksa liste adımı atlanır |
| Yönetim › Kapsam yetkileri | Verilmiş kapsamlar (`sa-admin-grants`) · İlk kurulum (`sa-admin-bootstrap`) · Kapsam ver (`sa-admin-grant-form`) | İlk kurulum yalnız hiç kapsam verilmemişken ekranda; yoksa atlanır |
| Yönetim › Kurum ve ekipler | Kurumlar · Kurum ekle/düzenle · Ekipler · Ekip ekle/düzenle (`sa-admin-orgs`, `-org-form`, `-teams`, `-team-form`) | — |
| Yönetim › gMSA yönlendirme | Yönlendirme kararı · Mevcut ayarlar · Rol ekle (`sa-admin-routing-rule`, `-roles`, `-role-form`) | — |
| Yönetim › Kişiler | Kişi ara · Sonuçlar · Seçili kişi · Kişi kaydı ekle (`sa-people-search`, `-list`, `-edit`, `-add`) | Sonuçlar aramadan, Seçili kişi seçimden önce atlanır |

Yönetim sayfası zaten `Administer` ister; her sekmenin kendi "Adım adım göster" düğmesi ve benzersiz kimliği vardır (`sa-admin-{scope,orgs,routing,people}-tour`). Sekme içerikleri
`KeepPanelsAlive` ile gizli olduğundan tur yalnız görünen sekmenin alanlarını bulur; sekmeyi değiştirmez.

**Katlanan kutu.** Kutuda başlık, "Adım n / m" ve düğmeler sabit kalır; yalnız açıklama (Ne/Neden, ipucu) kendi içinde kayar. **"Açıklamayı gizle / göster"**
(`aria-expanded`, `aria-controls`) açıklamayı katlar; durum yalnız bellekte (sayfa ziyareti), tarayıcıda saklanmaz, adımlar arası korunur, odak basılan düğmede kalır.
`so-guided-tour.js` kutu boyutu değişince (ResizeObserver) alt boşluğu yeniden hesaplar ve alan kutunun altında ya da uygulama çubuğunun üstünde kaldıysa
üstünü çubuğun altına getirir; kaydırmada çalışmaz (kullanıcının kaydırmasıyla yarışmaz). Dar ekranda kaydırma anlıktır. 700 px altı ve 520 px altı yükseklikte kutu en çok %42 vh.
Kutuya sığan alan kutunun üstünde kalır; kutuya sığmayan daha büyük bir alanın **üstü** görünür kalır (katlamak yer açar), altı kutunun altına girebilir — bu bilinen sınırdır.

## 5. Testler

- `GuidedTourTests` durum bölümü: ileri/geri, atlanan hedef ve bildirimi, hiç hedef yok, yetkisiz adımın sayılmaması, son adımda "Bitir".
- `GuidedTourTests` bileşen bölümü (küçük test renderer, gerçek bileşen yaşam döngüsü, sahte `IJSRuntime`): başlatınca kutu ve odak çağrısı,
  ileri/geri, atlanan adımın metni, Esc ile kapanma ve odağın tetikleyen düğmeye dönmesi (`hide(düğmeId)`), yetkisiz adımın gizlenmesi.
- `ServiceAccountGuidedTourUiTests`: her turun her hedefi sayfa/bileşen kaynağında `data-tour` olarak var; rapor sayfasına tur eklenmedi; kısaltılmış rehberler 3–4 madde.
- Tarayıcı: `tests/browser/service-accounts-guided-tour.cjs` (Demo profili): listede turu başlat, ilerle, Esc, odak dönüşü, 390 px'te yatay kaydırma yok.
  Windows 2026-10-17: 6/6. REHBER-2 ile genişledi (9 adım): Ekibimin işleri turu, dört Yönetim sekmesi turu (atlanan adımlar dahil), 640×400'de katlama + "sığan alan kutunun altında kalmaz" + yatay kaydırma yok + alt boşluğun geri verilmesi. Yolculuk her sayfada hedef alanları bekledikten sonra turu başlatır.
- REHBER-2 birim: `GuidedTourTests` katlama, `ServiceAccountGuidedTourUiTests` yeni turların hedefleri/yetkisi/tek cümle kuralı ve Yönetim sekmelerinin benzersiz kimlikleri.

## 6. Açık sorular

- Sunucu tarafı "turu gördü" tercihi istenir mi (ilk girişte bir kez önerme)? G-41.
- ~~Ekibimin işleri ve Yönetim sekmeleri tura alınsın mı?~~ Evet, REHBER-2 (§4b). ~~%200'de kutu küçültülsün mü?~~ Evet, katlanır (§4b).
