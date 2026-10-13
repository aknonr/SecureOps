# 2. Yetenek tablosu: ekip aracı ↔ Servis Hesapları modülü

Taban: `master` (`fb87c20`). Sayfalı tarama ve istenen gMSA adı (031) `feature/service-accounts-gmsa-name-scan-paging-20261005`
dalında, henüz master'da değil; ilgili satırda belirtildi.

**Var** = aynı ihtiyacı bugün karşılar · **Kısmen** = bir kısmı var, eksiği yazılı · **Yok** = yok (nedeni yazılı).

## Aracın işlevleri

| # | Araç işlevi | Modül | Modüldeki karşılık | Eksik |
|---|---|---|---|---|
| 1 | Hizmet grubu (RFS) listesi | Yok | Modülde kuruluş/ekip var, CMDB hizmet grubu yok | Turuncuhat okuma sözleşmesi açık karar (AGENTS "Open decisions"); önce sahte bağdaştırıcı |
| 2 | Bir RFS'nin / tüm RFS'lerin sunucuları | Yok | — | Aynı; ayrıca modülde "sunucu kümesi" kaydı yok |
| 3 | vCenter'dan açık Windows sunucular | Yok | — | vSphere okuma "read later" (sistem haritası); sözleşme ve onay yok |
| 4 | WinRM erişilebilirlik ön filtresi | Yok | Ürün sunucuya bağlanmaz (ADR-0027) | Ürün tarafı tarama gelirse gerekir; filtre "dışarıda bırak" değil "erişilemedi" olarak kaydetmeli |
| 5 | Hesap AD'de var mı | Kısmen | Kesin kimlik araması ve sınırlı ad araması (ADR-0008, ADR-0025); hesap kaydında etki alanı/SID alanları | Hesap kaydı sırasında otomatik AD doğrulaması ve gMSA nesnesi okuma yok |
| 6 | Birçok sunucuya oturum açma | Yok (bilerek) | Toplayıcıyı kişi kendi yetkisiyle çalıştırır; JEA paralel modu önerildi, rafta (ADR-0024) | Worker → WinRM → JEA çalıştırıcı, onaylı JEA uç noktası |
| 7 | Servis / görev / IIS havuzu / IIS site-uygulama-sanal dizin kimliği arama | Kısmen | Salt okunur toplayıcı (tek geçiş, SID çevirisi yok, IIS XML bir kez), birleştirme betiği, yükleme, hesaba bağlama, sunucu bazında kapsam, karar (kullanıma al / reddet) | Ürün başlatmıyor; çok sunucuya yayma kişiye kalıyor |
| 8 | COM+ kimliği | Yok | Görünmezlik olarak her sonuçta yazılı | JEA fonksiyonuna isteğe bağlı kaynak (ADR değişikliği) |
| 9 | Kullanıcı hakları (hizmet/toplu iş oturumu) | Yok | Görünmezlik olarak yazılı | Geçici dosyasız okuma yolu + ADR değişikliği |
| 10 | Aynı anda birden çok hesap arama | Kısmen | Toplayıcı ve dosya 1–20 hesap taşır | Yükleme bir kerede tek hesaba bağlanır; aynı dosya her hesap için ayrı bağlanır |
| 11 | Hesap bazlı önbellek / artımlı tarama | Kısmen | Taramalar değişmez kayıt; hesap sayfasında son taramalar ve kapsam (sayfalama dalda) | "Yalnız eksik/erişilemeyen sunucuları yeniden planla" ve iki tarama farkı yok |
| 12 | Sonuç tablosu | Var | Hesap sayfası "Kullanım taramaları" sekmesi; sunucu bazında Bulundu / Bulunamadı / Belirsiz / Bilgi yok | — |
| 13 | Erişilemeyen sunucu takibi | Var (daha doğru) | `Unreachable` / `NoResult` hiç düşmez; "bulunamadı ≠ kullanılmıyor" | — |
| 14 | AD'de parola değiştirme | Yok (bilerek) | "Parola değişimi" eylemi plan / gerçekleşti / doğrulandı olarak **kaydedilir** | Yazma = Faz 8 (AGENTS kural 1, ADR-0006) |
| 15 | Servis / havuz / IIS / görev / COM+ üzerinde kimlik + parola yazma | Yok (bilerek) | Eylem kaydı, OR/OCO/Jira referansı, kanıt dosyası | Aynı; ADR-0006 başlangıç kataloğu "yapılandırma değişikliği"ni açıkça dışarıda tutuyor |
| 16 | Değişiklik sonrası doğrulama | Kısmen | gMSA kontrol taraması (`expectedAccount`): eski hesap kaldı mı / gMSA çalışıyor mu, kanıt olarak; doğrulayıcı eylemi doğrular | Parola değişimi için "bileşen yeni parolayla ayağa kalktı mı" kanıtı yok (servis durumu, son başlama zamanı) |
| 17 | Başarılı / başarısız özeti | Kısmen | Eylem sonucu ve doğrulama hesap bazında | Bileşen bazında uygulama sonucu yok (uygulama yok) |
| 18 | Toplu işlem (çok hesap) | Kısmen | Toplu içe aktarma (önizleme → karar → onay), listede çok hesaba tek e-posta kaydı, raporlar | Seçili hesaplara toplu talep/eylem planı, toplu tarama planı, toplu durum görünümü yok |
| 19 | Rapor | Var (araçtan fazla) | Haftalık, anlık görüntü, XLSX/PDF, grafikler, gMSA ad listesi (dalda) | Tarama sonuçları rapora girmiyor (bilerek, ADR-0027 §7) |

## Modülde olup araçta olmayanlar

Kapsam yetkisi (kim hangi hesabı görür), sahiplik ve devir, talep/eylem/doğrulama ayrımı, bilgi tabanı kullanım
kuralları, hatırlatmalar, kanıt saklama, değişmez geçmiş ve denetim, OR/OCO/Jira referansları. Araç bunların hiçbirini
tutmuyor; bu yüzden "aracı ürüne taşımak" değil "aracın işini ürünün süreç kayıtlarına bağlamak" doğru çerçeve.

## Özet

| Grup | Durum |
|---|---|
| Bulma (hangi sunucuda, hangi bileşen) | **Kısmen** — doğruluk ve kanıt modülde daha iyi; **hız ve ölçek** (çok sunucuya yayma) eksik |
| Sunucu envanteri (RFS, vCenter) | **Yok** — entegrasyon sözleşmesi açık |
| Değiştirme (AD parola, bileşen kimliği) | **Yok** — Faz 8 ve onay gerektiriyor |
| Doğrulama | **Kısmen** — gMSA için var, parola değişimi için yok |
| Toplu çalışma ve rapor | **Kısmen / Var** — rapor güçlü; çok hesaplı plan ve izleme eksik |
