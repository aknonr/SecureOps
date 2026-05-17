# PAM Ekibi Ön Görüşme Toplantısı

**Belge türü:** Toplantı öncesi bağlam, soru ve talep listesi  
**Proje:** Secure Ops Automation & AI Analysis Hub  
**Şirket adı:** CONTOSO  
**Toplantı sahibi:** project owner  
**Katılımcılar:** PAM admin, ilgili PAM ekip temsilcileri  
**Amaç:** Phase 0 ve sonraki fazlar için PAM ekibinden gerekli hesap, secret, erişim ve süreç kararlarını somut ticket/request çıktısına dönüştürmek

---

## 1. Toplantı amacı ve beklenen çıktılar

Bu toplantının amacı, SecureOps'un PAM ekibinden hangi somut operasyonel girdilere ihtiyaç duyduğunu netleştirmek ve bu ihtiyaçları takip edilebilir ticket/request maddelerine dönüştürmektir. Bilgi Güvenliği ve Siber Güvenlik görüşmelerinden farklı olarak bu toplantının ana çıktısı genel prensip uyumu değil; kim neyi, hangi sırayla ve hangi tarihe kadar açacak sorusunun cevabıdır.

Toplantı sonunda aşağıdaki çıktılar alınmış olmalıdır:

1. `CONTOSO\svc-secureops` servis hesabının oluşturulma süresi ve sahibi.
2. Servis hesabı parola rotasyon sıklığı ve SecureOps tarafındaki etkisi.
3. Uygulamanın startup sırasında secret alması için tercih edilen yöntem.
4. PAM tarafından bugün yönetilmeyen ama SecureOps'un ihtiyaç duyacağı secret türlerinin listesi.
5. Phase 4 PAM session correlation için read-only API erişimi alma süreci.
6. SecureOps'un PAM üzerinden yaptığı sorguların PAM tarafında loglanıp loglanmadığı.
7. SecureOps'un PAM altyapısına oluşturacağı beklenen yük ve kabul edilen sorgu sınırları.
8. Worker'ın hedef sunuculara erişiminde BeyondTrust broker, direct WinRM + JEA veya açık kabul verilmiş mevcut direct-JEA modelinden hangisinin beklendiğine dair yönlendirme.
9. Phase 8'de remediation başladığında PAM'ın oturum broker'ı olarak rol alıp almayacağına dair ilk yönlendirme.
10. Açılması gereken ticket/request listesinin sahibi, bağımlılığı ve hedef tarihi.

Her madde için toplantı sonunda aşağıdaki dört sonuçtan biri yazılacaktır:

- `Onay`
- `Revizyon gerekli`
- `Reddedildi`
- `Ek bilgi gerekli`

## 2. İncelenecek mimari özet

Bu görüşme, aşağıdaki kaynak başlıklar esas alınarak yapılacaktır:

- `docs/05-security-model.md` → `Authentication`
- `docs/05-security-model.md` → `Service Account`
- `docs/05-security-model.md` → `Worker Privileged-Access Path — Pending Stakeholder Input`
- `docs/05-security-model.md` → `Secrets Management`
- `docs/05-security-model.md` → `Network Boundaries`
- `docs/06-integrations.md` → `PAM Integration (BeyondTrust-style, Phase 4+)`
- `plans/PHASE-4-audit-and-alarm-response-verification.md` → `Pre-Conditions`, `Sprint 1 — PAM Integration`
- `plans/PHASE-8-approval-based-remediation.md` → `Hard Constraints (Reaffirm from ADR-0006)`

PAM açısından incelenecek model:

| Alan | Önerilen yaklaşım |
|---|---|
| Servis hesabı | `CONTOSO\svc-secureops` |
| Parola yönetimi | PAM tarafından yönetilen ve döndürülen parola |
| Uygulama çalışma modeli | API ve Worker startup sırasında gerekli secret'ları çeker |
| Hedef sunucu erişimi | Açık karar: BeyondTrust broker, direct WinRM + Kerberos + JEA veya açık kabul verilmiş mevcut direct-JEA modeli |
| Phase 4 PAM kullanımı | Yalnızca read-only session correlation |
| Phase 4 authentication | PAM sisteminde read-only scoped service account |
| Mevcut PAM altyapısı | Değiştirilmez; SecureOps üstüne oturur |
| Gelecek secret ihtiyaçları | Webhook HMAC secret, gerekirse SQL secret, Phase 3 SMTP credential |
| Phase 8 durumu | Write remediation gelecek fazdır; PAM rolü bugünden yalnızca yönlendirme seviyesinde netleştirilir |

SecureOps, PAM'ın yerine geçmez ve mevcut PAM konfigürasyonunu değiştirmez. Ancak Worker'ın hedef sunuculara hangi onaylı privileged-access yolu ile gideceği henüz açık karardır; bu belge direct-JEA modelini önceden kabul edilmiş varsaymaz. İlk faz için PAM ekibinden beklenen temel çıktı, servis hesabı ve secret yönetim sürecinin başlatılmasıdır. Phase 4'te ek beklenti, alarm yanıt doğrulaması için read-only session verisine erişimdir.

## 3. Karar gerektiren başlıklar

| Karar / talep başlığı | Neden bu toplantıda netleşmeli | İstenen çıktı |
|---|---|---|
| Servis hesabı oluşturma takvimi | Phase 1 başlangıcı için ön koşuldur | Ticket açılışı, sahibi ve hedef tarih |
| Parola rotasyon sıklığı | Uygulama startup ve operasyon tasarımını etkiler | Kabul edilen rotation schedule |
| Secret retrieval yöntemi | Uygulamanın PAM ile nasıl konuşacağını belirler | API / file drop / manuel süreç kararı |
| PAM dışı secret ihtiyacı | Yeni secret onboarding talebi gerektirir | Yönetilecek secret listesi |
| Phase 4 read-only API erişimi | PAM correlation geliştirmesinin ön koşuludur | Approval süreci ve ön koşullar |
| PAM query audit'i | İki sistem arasındaki izlenebilirliği etkiler | PAM tarafında logging davranışı |
| PAM altyapı yükü | Sorgu sıklığı ve limitleri belirler | Rate limit / kullanım sınırı |
| Worker privileged-access path'i | Phase 1 mimarisini ve servis hesabı tasarımını etkiler | BeyondTrust broker mı, direct WinRM + JEA mı, açık kabul mü |
| Phase 8 PAM rolü | Gelecek tasarım varsayımını erken netleştirir | Broker rolü gerekir mi, ileride yeniden bakılacak mı |
| Ticket/request listesi | Görüşmenin operasyonel çıktısıdır | Açılacak kayıtların net listesi |

## 4. PAM ekibinden net cevap alınması gereken sorular

### 4.1 Servis hesabı oluşturma takvimi

**Referans:** `docs/05-security-model.md` → `Service Account`; `plans/PHASE-0-discovery-and-project-setup.md` → PAM service account request deliverable  
**Karar sorusu:** `CONTOSO\svc-secureops` servis hesabı için ticket hangi ekip tarafından açılacak, beklenen SLA nedir ve hesap en erken hangi tarihte hazır olabilir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.2 Parola rotasyon sıklığı

**Referans:** `docs/05-security-model.md` → `Service Account`, `Secrets Management`  
**Karar sorusu:** `CONTOSO\svc-secureops` için PAM ekibinin tercih ettiği parola rotasyon sıklığı nedir ve SecureOps'un bu rotasyona uyum sağlaması için beklenen teknik veya operasyonel koşul var mıdır?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.3 Secret retrieval yöntemi

**Referans:** `docs/05-security-model.md` → `Secrets Management`; `docs/03-architecture.md` → `Configuration`  
**Karar sorusu:** SecureOps'un startup sırasında secret alması için PAM ekibinin desteklediği ve önerdiği yöntem hangisidir: PAM API, kontrollü dosya teslimi, manuel süreç veya başka bir standart yöntem?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.4 PAM dışında bugün yönetilmeyen secret ihtiyaçları

**Referans:** `docs/05-security-model.md` → `Secrets Management`  
**Karar sorusu:** Webhook HMAC secret, gerekirse SQL credential ve Phase 3 SMTP credential gibi SecureOps secret'larından hangileri bugün PAM tarafından yönetilmiyor ve bunların onboarding'i için hangi request/ticket türleri açılmalıdır?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.5 Phase 4 PAM session correlation erişimi

**Referans:** `docs/06-integrations.md` → `PAM Integration (BeyondTrust-style, Phase 4+)`; `plans/PHASE-4-audit-and-alarm-response-verification.md` → `Pre-Conditions`  
**Karar sorusu:** Phase 4'te alarm yanıt doğrulaması için historical session ve active session verilerine read-only erişim alma süreci nedir; hangi approval, form, rol veya test ortamı ön koşul olarak gerekir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.6 PAM sorgularının PAM tarafında loglanması

**Referans:** `docs/06-integrations.md` → `PAM Integration (BeyondTrust-style, Phase 4+)`; `docs/05-security-model.md` → ``Audit (See `docs/08-audit-model.md` for Detail)``  
**Karar sorusu:** SecureOps, PAM session correlation için read-only sorgu yaptığında bu erişimler PAM tarafında kim, ne zaman, hangi sorgu kapsamında görülebilecek şekilde loglanıyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.7 PAM altyapısına beklenen yük

**Referans:** `docs/06-integrations.md` → `PAM Integration (BeyondTrust-style, Phase 4+)`  
**Karar sorusu:** Phase 4'te alarm başına veya zaman penceresi bazında yapılacak session correlation sorguları için PAM ekibinin kabul ettiği sorgu hacmi, rate limit veya batch kullanım beklentisi nedir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.8 Phase 4 veri alanları

**Referans:** `docs/06-integrations.md` → `PAM Integration (BeyondTrust-style, Phase 4+)`  
**Karar sorusu:** Alarm yanıt doğrulaması için Phase 4'te hangi minimum PAM veri alanları alınabilir: kullanıcı, hedef sunucu, session başlangıç/bitiş zamanı, session ID ve gerekiyorsa başka alanlar?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.9 Worker privileged-access path'i

**Referans:** `docs/05-security-model.md` → `Worker Privileged-Access Path — Pending Stakeholder Input`; `docs/15-system-landscape.md` → `Open Decisions`  
**Karar sorusu:** Worker'ın hedef sunuculara erişimi için PAM ekibinin beklediği model hangisidir: BeyondTrust API / broker üzerinden oturum açma, direct WinRM + Kerberos + JEA, yoksa mevcut direct-JEA modelinin açık kabul ile sürmesi? Her seçenek için zorunlu teknik veya operasyonel koşullar nelerdir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.10 Phase 8 remediation ve PAM rolü

**Referans:** `plans/PHASE-8-approval-based-remediation.md` → `Hard Constraints (Reaffirm from ADR-0006)`  
**Karar sorusu:** Phase 8'de approval-based remediation başladığında PAM ekibi, write işlemlerine ait oturumların broker edilmesini veya ayrıca PAM üzerinden ilişkilendirilmesini bekler mi; yoksa bu konu Phase 8 öncesi ayrı tasarım görüşmesine mi bırakılmalıdır?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.11 Açılacak ticket/request listesi

**Referans:** `plans/PHASE-0-discovery-and-project-setup.md`; `plans/PHASE-4-audit-and-alarm-response-verification.md`  
**Karar sorusu:** Bu toplantı sonucunda hemen açılması gereken ticket/request kayıtları hangileridir ve her biri için PAM tarafındaki owner kimdir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

### 4.12 Phase 1 öncesi zorunlu PAM koşulları

**Referans:** `plans/PHASE-0-discovery-and-project-setup.md`; `plans/PHASE-1-readonly-diagnostic-mvp.md`  
**Karar sorusu:** PAM ekibi açısından Phase 1 başlamadan önce mutlaka tamamlanmış olması gereken maddeler nelerdir; hangileri yalnızca ticket açılmış ve owner atanmış haldeyken başlanabilir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek bilgi gerekli`

## 5. Karar ve talep matrisi

Toplantı sırasında her satır doldurulmalıdır. Bu belge için başarı ölçütü, soyut mutabakat değil somut talep ve sahiplik üretmektir.

| # | Talep / karar maddesi | Önerilen SecureOps yaklaşımı | PAM kararı | Açılacak kayıt / çıktı | Aksiyon sahibi | Son tarih |
|---|---|---|---|---|---|---|
| 1 | `svc-secureops` oluşturulması | Tek servis hesabı, PAM yönetimli parola |  | Hesap oluşturma ticket'ı | PAM admin / project owner |  |
| 2 | Parola rotasyon sıklığı | PAM standardına uyumlu çalışma |  | Rotation schedule kararı | PAM admin |  |
| 3 | Secret retrieval yöntemi | Startup sırasında otomatik çekim |  | Entegrasyon yöntemi / onboarding notu | PAM admin / project owner |  |
| 4 | HMAC secret onboarding | Webhook secret'ın PAM'de tutulması tercihli |  | Secret onboarding ticket'ı | PAM admin / monitoring admin |  |
| 5 | SMTP credential onboarding | Phase 3 ihtiyacı olarak ön kayıt |  | Gerekirse gelecek faz ticket'ı | PAM admin / project owner |  |
| 6 | Phase 4 read-only API erişimi | Session correlation için read-only PAM adapter |  | Approval süreci ve test erişimi | PAM admin / project owner |  |
| 7 | PAM query logging | SecureOps sorgularının PAM tarafında görünürlüğü |  | Logging teyidi / örnek kayıt | PAM admin |  |
| 8 | PAM sorgu hacmi | Kontrollü read-only kullanım |  | Rate limit / sorgu politikası | PAM admin / project owner |  |
| 9 | Phase 4 minimum veri alanları | Session correlation için gerekli alanlar |  | Veri alanı listesi | PAM admin / project owner |  |
| 10 | Worker privileged-access path'i | BeyondTrust broker / direct WinRM + JEA / açık kabul senaryolarının netleşmesi |  | Karar + koşullar | PAM admin / project owner |  |
| 11 | Phase 8 PAM rolü | Gelecek faz için yön kararı |  | Deferred / ayrı tasarım görüşmesi | PAM admin / project owner |  |
| 12 | Phase 1 ön koşulları | Başlangıç için minimum PAM gereksinimi |  | Must comply listesi | PAM admin / project owner |  |

Son tarih kolonu boş kalan kararlar için varsayılan süre: toplantı tarihinden itibaren 5 iş günüdür. Daha uzun süre gerekiyorsa PAM ekibi tarafından gerekçesiyle yazılı belirtilmelidir.

## 6. Toplantı sonrası beklenen artefaktlar

Toplantıdan sonra aşağıdaki çıktılar üretilmelidir:

1. Açılmış servis hesabı ticket'ı veya ticket referansı.
2. Parola rotasyon standardının yazılı teyidi.
3. Secret retrieval yöntemi ve gerekiyorsa onboarding adımları.
4. Webhook HMAC secret için PAM onboarding kararı.
5. Phase 3 SMTP credential için gerekiyorsa ileri tarihli hazırlık notu.
6. Phase 4 read-only API erişimi için approval süreci ve gerekli başvuru listesi.
7. PAM query logging davranışına ilişkin teyit.
8. Phase 4 sorgu hacmi ve kullanılabilir alanlar hakkında kısa teknik not.
9. Worker privileged-access path'ine ilişkin karar veya açık koşul listesi.
10. Phase 8 PAM rolünün `kararlaştırıldı` veya `Phase 8'e ertelendi` şeklinde kaydı.
11. Phase 1 go/no-go değerlendirmesinde kullanılacak PAM durum özeti.

Bu artefaktlar olmadan toplantı "görüşüldü" kabul edilebilir, ancak "PAM tarafı hazırlandı" kabul edilmez.

## İtiraz ve anlaşmazlık yönetimi

Bir karar `Reddedildi` olarak kaydedilirse, kararın gerekçesi yazılı biçimde karar ve talep matrisine eklenir. project owner, reddedilen başlık için alternatif yaklaşım, kapsam daraltma veya anlaşmazlığı belgeleyen bir ADR önerir. Gerekirse ilgili ekiplerle yeniden değerlendirme toplantısı planlanır. Çözüm tamamlanmadan Phase 1'i etkileyen madde varsa başlangıç bloke kabul edilir.

## 7. Toplantı öncesi bağlam özeti

SecureOps, SolarWinds → monthly.thy.com / HPE OpsBridge → Turuncuhat zincirinden gelen Windows alarm bağlamını alıp read-only tanılama işlerine dönüştüren bir operasyon platformudur. Hedef sunuculara erişimde JEA zorunludur; ancak Worker'ın BeyondTrust broker mı kullanacağı, direct WinRM + JEA mı kullanacağı, yoksa mevcut direct-JEA modelinin açık kabul ile mi süreceği henüz kararlaştırılmamıştır. PAM ekibi açısından ilk kritik ihtiyaç, SecureOps servis hesabının, gerekli secret yaşam döngüsünün ve bu erişim modelinin netleştirilmesidir.

Bu görüşmenin ana konusu güvenlik prensiplerinin soyut değerlendirmesi değil, uygulanabilir operasyonel hazırlıktır. Phase 1 için `CONTOSO\svc-secureops` hesabının ne zaman açılacağı, parolanın hangi sıklıkla döneceği ve uygulamanın secret'ları hangi yöntemle alacağı netleşmelidir. Ayrıca webhook HMAC secret ve ileride Phase 3 SMTP credential gibi secret'ların PAM kapsamına alınıp alınmayacağı karara bağlanmalıdır.

Phase 4'te SecureOps, alarm yanıt doğrulaması amacıyla PAM session verilerini read-only okuyacaktır. Bu özellik, "kişiyi izleme" amacı taşımaz; mevcut projedeki resmi çerçeve operasyonel müdahale doğrulama, SLA ve audit'tir. PAM ekibinden bu faz için beklenen katkı; read-only API erişim sürecini, alınabilecek veri alanlarını, sorgu hacmi beklentilerini ve SecureOps sorgularının PAM tarafında nasıl izleneceğini netleştirmektir.

Phase 8 remediation bugünün konusu değildir; yine de PAM ekibinin ileride write işlemleri için broker rolü bekleyip beklemediğini erken anlamak faydalıdır. Bununla karıştırılmaması gereken mevcut açık karar, Worker'ın bugünkü read-only otomasyon erişiminin hangi yoldan kurulacağıdır. Bu toplantıdan beklenen çıktı; ticket referansları, owner'lar, tarihler ve sonraki fazlara taşınacak açık bağımlılıklardır. Toplantı sonunda kimin hangi talebi açacağı belirsiz kalırsa, görüşme amacına ulaşmış sayılmayacaktır.

## 8. Kapsam sınırı matrisi

Bu matrisin amacı, PAM görüşmesindeki talepleri aciliyet ve faz etkisine göre sınıflandırmaktır. Her başlık için yalnızca bir sınıf seçilmelidir.

| Karar / talep maddesi | Varsayılan sınıf | Sınıf tanımı | Toplantıda netleştirilecek sonuç |
|---|---|---|---|
| `svc-secureops` servis hesabı ticket'ı | Must comply | Phase 1 hazırlığının temel bağımlılığıdır | Ticket referansı ve hedef tarih belirlenir |
| Servis hesabı parola rotasyon standardı | Must comply | Uygulama çalışma varsayımını etkiler | Rotation schedule yazılı hale gelir |
| Secret retrieval yöntemi | Must comply | Uygulama startup entegrasyonunu belirler | Teknik yaklaşım kesinleşir |
| Webhook HMAC secret onboarding | Must comply | Phase 1 webhook güvenliği için gereklidir | PAM kapsamı veya kabul edilen alternatif belirlenir |
| Phase 1 için minimum PAM ön koşulları | Must comply | Go/no-go kararını etkiler | Başlangıç öncesi zorunlu liste kapanır |
| Worker privileged-access path'i | Must comply | Phase 1 erişim mimarisini belirler | BeyondTrust broker / direct WinRM + JEA / açık kabul kararı netleşir |
| PAM query logging teyidi | Should comply | İzlenebilirliği güçlendirir | PAM tarafındaki kayıt davranışı netleşir |
| Phase 4 read-only API approval süreci | Should comply | Phase 4 hazırlığını önden başlatır | Süreç ve owner belirlenir |
| Phase 4 minimum veri alanları | Should comply | Korelasyon tasarımını netleştirir | Alan listesi doğrulanır |
| Phase 4 sorgu hacmi / rate limit | Should comply | PAM altyapı yükünü yönetir | Kullanım sınırı belirlenir |
| Phase 3 SMTP credential onboarding | Could comply | Faydalıdır ama Phase 1 başlangıcını bloke etmez | İleri faz hazırlığı olarak kaydedilir |
| Phase 8 PAM broker rolü | Could comply | Gelecek tasarım için faydalı erken sinyaldir | Şimdilik yön belirlenir veya ertelenir |
| PAM ürün mimarisini değiştirme | Out of scope | SecureOps PAM replacement değildir | PAM ekibinin ayrı yol haritasında kalır |
| Phase 4'ten önce gerçek PAM korelasyonu uygulama | Out of scope | Faz sınırını ihlal eder | Mock adapter yaklaşımı korunur |
| Otomatik remediation broker tasarımı | Out of scope | Phase 8 öncesi write operation yoktur | Ayrı gelecek faz konusu olarak kalır |

### Sınıflandırma kuralları

- `Must comply`: Sağlanmadan Phase 1 başlatılamaz.
- `Should comply`: Operasyonel hazırlık değeri yüksektir; erken netleşmesi faydalıdır ama başlangıcı tek başına bloke etmez.
- `Could comply`: Faydalıdır; sonraki fazlara hazırlık sağlar.
- `Out of scope`: SecureOps sorumluluğu değildir veya mevcut faz sınırını ihlal eder.

Bu sınıflandırma toplantı sırasında talep listesinin şişmesini önlemek için kullanılacaktır. Yeni bir istek ortaya çıktığında önce hangi fazı etkilediği yazılmalı, ardından yukarıdaki dört sınıftan yalnızca biri seçilmelidir.
