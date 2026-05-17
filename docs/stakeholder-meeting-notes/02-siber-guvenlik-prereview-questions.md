# Siber Güvenlik Ön İnceleme Toplantısı

**Belge türü:** Toplantı öncesi bağlam, soru ve karar listesi  
**Proje:** Secure Ops Automation & AI Analysis Hub  
**Şirket adı:** CONTOSO  
**Toplantı sahibi:** project owner  
**Katılımcılar:** Siber Güv lead, ilgili Siber Güvenlik temsilcileri  
**Amaç:** Phase 1 başlamadan önce saldırı yüzeyi, tehdit modeli, veri akışı, tespit kabiliyeti ve olay müdahale hazırlığı açısından gerekli kararları yazılı ve izlenebilir hale getirmek

---

## 1. Toplantı amacı ve beklenen çıktılar

Bu toplantının amacı, SecureOps projesinin MVP mimarisini Siber Güvenlik bakış açısıyla ön incelemeye sunmak ve Phase 1 başlangıcını etkileyen saldırı yüzeyi kararlarını netleştirmektir. Odak, politika uyumu veya retention ayrıntılarından çok sistemin nasıl suistimal edilebileceği, hangi yollarla veri sızabileceği, hangi kontrollerin bunu sınırladığı ve bir saldırı halinde CONTOSO'nun neyi ne kadar erken görebileceğidir.

Toplantı sonunda aşağıdaki çıktılar alınmış olmalıdır:

1. Mevcut tehdit modelinin Siber Güvenlik tarafından yeterli görülüp görülmediği.
2. Webhook'un MVP'deki tek dış giriş noktası olarak kabul edilip edilmediği.
3. Alarm, tanılama ve audit verisinin geçtiği yolların ve olası sızıntı noktalarının onaylanması.
4. `CONTOSO\svc-secureops` hesabının ele geçirilmesi senaryosunda tasarımın kabul edilebilir risk seviyesinde olup olmadığının değerlendirilmesi.
5. Audit log üzerinde tamper veya exfiltration riskine karşı mevcut kontrollerin yeterliliği.
6. Internal-only yaklaşımının ağ düzeyinde nasıl doğrulanacağına ilişkin karar.
7. Phase 7 self-hosted AI aşaması için gelecekte geçerli olacak ana tehdit modeli beklentilerinin belirlenmesi.
8. SecureOps'a yönelik saldırı veya kötüye kullanımın Siber Güvenlik tarafından nasıl tespit edileceğine ilişkin minimum gereksinimlerin netleştirilmesi.
9. SecureOps audit/security loglarının organizasyon SIEM'ine aktarılıp aktarılmayacağına ilişkin karar.
10. Phase 1 başlamadan önce kapanması zorunlu güvenlik maddelerinin açık listesi.

Her karar için toplantı sonunda aşağıdaki dört sonuçtan biri yazılacaktır:

- `Onay`
- `Revizyon gerekli`
- `Reddedildi`
- `Ek kanıt gerekli`

## 2. İncelenecek mimari özet

Bu ön inceleme, `docs/05-security-model.md` içindeki aşağıdaki başlıklar esas alınarak yapılacaktır:

- `Threat Model (Summary)`
- `Authentication`
- `JEA (Just Enough Administration)`
- `Service Account`
- ``Audit (See `docs/08-audit-model.md` for Detail)``
- `Network Boundaries`
- `Data Masking (Phase 7 Requirement)`
- `Configuration Hardening`
- `Incident Response`

Siber Güvenlik ön incelemesinde ayrıca veri akışının anlaşılması için `docs/03-architecture.md` içindeki `Data Flow: Receive Alarm → Surface Result` ve `Network Topology` bölümleri yardımcı referans olarak kullanılacaktır.

İncelenecek MVP modeli şu şekildedir:

| Alan | Önerilen yaklaşım |
|---|---|
| Dış giriş noktası | Monitoring platformundan `/api/v1/alerts/webhook` çağrısı |
| Webhook koruması | HMAC-SHA256 imza, source IP allowlist, 5 dakikalık replay protection |
| Kullanıcı erişimi | Internal-only UI/API, Windows Authentication |
| Worker erişimi | SQL'e TLS, hedef sunuculara WinRM HTTPS 5986 + Kerberos + JEA |
| Hedef sunucu işlemleri | Phase 1-6 boyunca yalnızca read-only tanılama |
| Servis hesabı | `CONTOSO\svc-secureops`, PAM yönetimli parola, local admin yok |
| Audit koruması | Append-only `audit.AuditLog`, UPDATE/DELETE bloklu, ayrı audit write rolü |
| Dışa veri çıkışı | Public AI yok; public internet uygulama trafiği yok |
| Phase 7 yaklaşımı | Self-hosted LLM, masking zorunlu, izole subnet, no outbound internet |
| Olay müdahalesi | Worker kill switch, webhook disable flag, audit preserve, Bilgi Güv + Siber Güv bildirimi |

Bu modelde SecureOps, monitoring platformundan alarm alır; API doğrular, normalleştirir ve job kuyruğa alır; Worker JEA üzerinden read-only tanılama çalıştırır; sonuçlar SQL ve audit kayıtlarına yazılır; UI yalnızca API üzerinden veri okur. MVP'de target server üzerinde write operation yoktur, public ingress yoktur ve mevcut PAM/SolarWinds/Ansible altyapıları değiştirilmez.

## 3. Karar gerektiren başlıklar

| Karar başlığı | Neden bu toplantıda karara bağlanmalı | İstenen karar |
|---|---|---|
| Tehdit modeli | Tasarımın hangi saldırıları ele aldığını ve hangi riskleri kabul ettiğini belirler | Mevcut model yeterli mi |
| Saldırı yüzeyi | Phase 1'de hangi giriş noktalarının korunacağını kesinleştirir | Webhook tek dış giriş noktası mı |
| Veri akışı ve sızıntı yolları | Alarm, log ve tanılama verisinin nerede maruz kalabileceğini belirler | Veri akışı kabul edilebilir mi |
| Servis hesabı ele geçirilmesi | En kritik kötüye kullanım senaryolarından biridir | Blast radius kabul edilebilir mi |
| Audit tamper / exfiltration | Güvenlik delilinin güvenilirliğini ve veri kaybı riskini etkiler | Mevcut kontroller yeterli mi |
| Ağ segmentasyonu | "Internal-only" iddiasının teknik olarak uygulanmasını belirler | Ek ağ kontrolü gerekli mi |
| Phase 7 AI riski | Gelecek fazın bugünden yanlış varsayımla tasarlanmasını önler | Beklenen tehdit modeli nedir |
| Tespit kabiliyeti | Saldırı olduğunda görünürlük ve alarm üretimi gerekir | Hangi olaylar izlenmeli |
| SIEM entegrasyonu | Güvenlik operasyonunun merkezi görünürlüğünü etkiler | Log forward zorunlu mu |
| Phase 1 ön koşulları | Güvenlik ekibinin go/no-go etkisini netleştirir | Başlangıç öncesi zorunlu ek madde var mı |

## 4. Siber Güvenlik’ten net cevap alınması gereken sorular

### 4.1 Tehdit modeli yeterliliği

**Referans:** `docs/05-security-model.md` → `Threat Model (Summary)`  
**Karar sorusu:** Mevcut tehdit modeli; compromised service account, SQL injection, webhook spoofing, audit tampering, privilege escalation, Phase 7 AI leakage ve employee surveillance misuse senaryolarını Phase 1 için yeterli asgari tehdit seti olarak kapsıyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.2 Saldırı yüzeyi ve dış giriş noktaları

**Referans:** `docs/05-security-model.md` → `Authentication`, `Network Boundaries`  
**Karar sorusu:** MVP için monitoring webhook'un tek dış giriş noktası olduğu, UI/API'nin yalnızca internal network üzerinde tutulduğu ve public ingress bulunmadığı kabul ediliyor mu; aksi durumda ek giriş noktaları veya tehditler ayrıca tanımlanmalı mı?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.3 Webhook kötüye kullanım senaryosu

**Referans:** `docs/05-security-model.md` → `Authentication`  
**Karar sorusu:** HMAC-SHA256 imza, source IP allowlist ve 5 dakikalık replay protection birleşimi; spoofed alarm, replayed request ve yetkisiz tetikleme senaryolarını Phase 1 için kabul edilebilir seviyede azaltıyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.4 Veri akışı ve sızıntı yolları

**Referans:** `docs/05-security-model.md` → `Network Boundaries`; yardımcı referans: `docs/03-architecture.md` → `Data Flow: Receive Alarm → Surface Result`  
**Karar sorusu:** Alarm payload, tanılama sonucu ve audit kaydının monitoring platformu → API → SQL/Hangfire → Worker → hedef sunucu → SQL/UI hattındaki hareketi kabul ediliyor mu; Siber Güvenlik açısından Phase 1 öncesi kapatılması gereken ek sızıntı yolu var mı?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.5 Servis hesabı ele geçirilmesi senaryosu

**Referans:** `docs/05-security-model.md` → `Threat Model (Summary)`, `JEA (Just Enough Administration)`, `Service Account`  
**Karar sorusu:** `CONTOSO\svc-secureops` hesabı ele geçirilirse blast radius'ın JEA whitelist, local admin olmaması ve ayrı servis rolü sayesinde kabul edilebilir seviyede sınırlandığı değerlendiriliyor mu; Phase 1 başlamadan önce ek containment kontrolü gerekli mi?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.6 Audit log tamper ve exfiltration riski

**Referans:** `docs/05-security-model.md` → `Threat Model (Summary)`, ``Audit (See `docs/08-audit-model.md` for Detail)``  
**Karar sorusu:** Append-only trigger, ayrı audit write rolü, privileged read audit'i ve minimum retention yaklaşımı audit log tampering riskini yeterince azaltıyor mu; ayrıca audit verisinin exfiltration riskine karşı ek kontrol gerekiyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.7 Ağ segmentasyonu doğrulaması

**Referans:** `docs/05-security-model.md` → `Network Boundaries`  
**Karar sorusu:** "No public ingress" ve "outbound to public internet blocked except explicit allow-list" yaklaşımı için Siber Güvenlik'in Phase 1 öncesinde görmek istediği doğrulama kanıtı nedir: firewall rule review, network diagram approval, scan sonucu veya başka bir kanıt?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.8 Phase 7 AI risk çerçevesi

**Referans:** `docs/05-security-model.md` → `Threat Model (Summary)`, `Data Masking (Phase 7 Requirement)`, `Network Boundaries`; yardımcı referans: `docs/10-ai-rag-strategy.md`  
**Karar sorusu:** Phase 7'de self-hosted LLM, mandatory masking, isolated subnet ve no outbound internet yaklaşımı gelecekteki AI risk incelemesi için doğru başlangıç çerçevesi midir; Siber Güvenlik bu aşama için bugünden kayda geçirilmesini istediği ek tehdit sınıfı veya zorunlu ön koşul görüyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.9 Tespit kabiliyeti

**Referans:** `docs/05-security-model.md` → `Incident Response`; yardımcı referans: `docs/03-architecture.md` → `Logging and Observability`  
**Karar sorusu:** Siber Güvenlik'in SecureOps'a yönelik saldırı veya kötüye kullanımı fark edebilmesi için Phase 1'de en az hangi olayların alarm üretmesi gerekir: başarısız webhook doğrulama, anormal webhook hacmi, başarısız JEA çağrıları, yetkisiz audit sorguları, beklenmeyen servis hesabı kullanımı veya başka olaylar?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.10 SIEM aktarım gereksinimi

**Referans:** `docs/05-security-model.md` → ``Audit (See `docs/08-audit-model.md` for Detail)``, `Incident Response`; yardımcı referans: `docs/03-architecture.md` → `Logging and Observability`  
**Karar sorusu:** SecureOps audit/security loglarının organizasyon SIEM'ine Phase 1'de aktarılması zorunlu mudur; değilse hangi log sınıfları sonraki faza bırakılabilir ve hangi tetikleyiciler ayrı alarm olarak yine de izlenmelidir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.11 Olay müdahale hazırlığı

**Referans:** `docs/05-security-model.md` → `Incident Response`  
**Karar sorusu:** Worker kill switch, webhook disable flag, audit preserve, Bilgi Güvenliği ve Siber Güvenlik bildirimi, şüpheli zaman aralığı incelemesi ve fix sonrası reactivation adımları Phase 1 için yeterli incident response minimumu mudur?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.12 Phase 1 öncesi zorunlu koşullar

**Referans:** `docs/05-security-model.md` bütünü; `plans/PHASE-0-discovery-and-project-setup.md`  
**Karar sorusu:** Siber Güvenlik açısından Phase 1 başlamadan önce tamamlanması zorunlu ek şartlar var mıdır; varsa bunlar `Must comply` olarak kayda geçirilecek kesin maddeler halinde nelerdir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

## 5. Karar matrisi

Toplantı sırasında her satır doldurulmalıdır. Karar sonucu boş bırakılan satır, toplantıdan çıkmış karar sayılmaz.

| # | Karar maddesi | Önerilen SecureOps yaklaşımı | Siber Güv kararı | Gerekçe / koşul | Aksiyon sahibi | Son tarih |
|---|---|---|---|---|---|---|
| 1 | Tehdit modeli | `Threat Model (Summary)` tablosunun Phase 1 minimumu olarak kullanılması |  |  | Siber Güv lead / project owner |  |
| 2 | Dış giriş noktaları | Monitoring webhook tek dış giriş noktası; UI/API internal-only |  |  | Siber Güv lead / network admin |  |
| 3 | Webhook kötüye kullanımı | HMAC + allowlist + replay protection |  |  | Siber Güv lead / monitoring admin |  |
| 4 | Veri akışı | Monitoring → API → SQL/Hangfire → Worker → target server → SQL/UI akışı |  |  | Siber Güv lead / project owner |  |
| 5 | Servis hesabı blast radius | JEA whitelist + local admin yok + PAM yönetimi |  |  | Siber Güv lead / PAM admin |  |
| 6 | Audit tamper / exfiltration | Append-only audit + ayrı rol + privileged read audit'i |  |  | Siber Güv lead / project owner |  |
| 7 | Ağ segmentasyonu | Internal-only, no public ingress, kontrollü outbound |  |  | Siber Güv lead / network admin |  |
| 8 | Phase 7 AI risk çerçevesi | Self-hosted only + masking + isolated subnet |  |  | Siber Güv lead / project owner |  |
| 9 | Tespit kabiliyeti | Güvenlik açısından izlenecek minimum olay setinin belirlenmesi |  |  | Siber Güv lead / project owner |  |
| 10 | SIEM entegrasyonu | Phase 1 için gerekli log forwarding kararının verilmesi |  |  | Siber Güv lead / SIEM owner |  |
| 11 | Olay müdahale hazırlığı | Kill switch + disable flag + preserve + notify + review |  |  | Siber Güv lead / project owner |  |
| 12 | Phase 1 ön koşulları | Ek zorunlu güvenlik maddelerinin listelenmesi |  |  | Siber Güv lead / project owner |  |

Son tarih kolonu boş kalan kararlar için varsayılan süre: toplantı tarihinden itibaren 5 iş günüdür. Daha uzun süre gerekiyorsa Siber Güv tarafından gerekçesiyle yazılı belirtilmelidir.

## 6. Toplantı sonrası beklenen artefaktlar

Toplantıdan sonra aşağıdaki çıktılar üretilmelidir:

1. Siber Güvenlik'in yazılı geri bildirimi veya toplantı notu onayı.
2. Karar matrisinin tamamlanmış sürümü.
3. `Must comply` maddelerinin sahibi ve son tarihi belirlenmiş aksiyon listesi.
4. Gerekirse güncellenmiş tehdit modeli veya ek saldırı senaryosu listesi.
5. Gerekirse güncellenmiş veri akışı / ağ topolojisi diyagramı.
6. SIEM aktarımı gerekli görülürse kapsamı ve sorumlusu tanımlanmış aksiyon maddesi.
7. Gerekirse `docs/05-security-model.md` veya ilgili ADR güncellemesi.
8. Phase 1 go/no-go değerlendirmesinde kullanılacak Siber Güvenlik durumu özeti.

Bu artefaktlar olmadan toplantı "bilgilendirme yapıldı" kabul edilebilir, ancak "Siber Güvenlik ön inceleme kararı tamamlandı" kabul edilmez.

## İtiraz ve anlaşmazlık yönetimi

Bir karar `Reddedildi` olarak kaydedilirse, kararın gerekçesi yazılı biçimde karar matrisine eklenir. project owner, reddedilen başlık için alternatif yaklaşım, kapsam daraltma veya anlaşmazlığı belgeleyen bir ADR önerir. Gerekirse ilgili paydaşlarla yeniden inceleme toplantısı planlanır. Çözüm tamamlanmadan Phase 1 başlangıcı bloke kabul edilir.

## 7. Toplantı öncesi bağlam özeti

SecureOps, Windows ağırlıklı operasyon ortamında gelen alarmlar için yapılan manuel tanılama adımlarını standartlaştırmak üzere tasarlanmış bir platformdur. Monitoring platformundan gelen alarm doğrulanır, normalize edilir, read-only tanılama işi olarak kuyruğa alınır ve hedef sunucuda JEA constrained endpoint üzerinden çalıştırılır. Sonuçlar SQL Server'da saklanır ve operasyon ekiplerine yapılandırılmış görünürlük sağlanır.

MVP sınırı kasıtlı olarak dardır. Phase 1-6 boyunca hedef sunucularda write operation yapılmayacak, tüm uzak PowerShell erişimi JEA üzerinden geçecek, public ingress olmayacak ve public AI kullanılmayacaktır. Bu nedenle Siber Güvenlik ön incelemesinin ana sorusu yalnızca "hangi kontroller var" değil; "hangi saldırı yolları kalıyor, bunlar nasıl tespit edilecek ve bir kompromizasyon halinde blast radius ne kadar sınırlı kalacak" olmalıdır.

Veri akışı monitoring platformu, API, SQL/Hangfire, Worker, hedef sunucular ve UI arasında gerçekleşir. Potansiyel risk alanları; spoofed webhook, compromised service account, audit log'a yetkisiz erişim, yanlış yapılandırılmış outbound erişim, ileride Phase 7 AI katmanında maskeleme boşluğu ve yeterli tespit sinyali olmamasıdır. `docs/05-security-model.md` içindeki `Threat Model (Summary)` ve `Incident Response` bölümleri bu risklere karşı mevcut başlangıç yaklaşımını tanımlar.

Bu toplantıdan beklenen, mimarinin genel olarak beğenilip beğenilmediği değil; Phase 1 başlamadan önce Siber Güvenlik açısından hangi saldırı senaryolarının kapatılmış sayılacağı, hangilerinin ek kanıt gerektirdiği, hangi olayların izlenmesi gerektiği ve SIEM aktarımının zorunlu olup olmadığı konusunda yazılı karar çıkmasıdır. Ayrıca Phase 7 AI aşaması henüz başlamayacak olsa da self-hosted LLM, masking ve isolated subnet yaklaşımının ilerideki Siber Güvenlik incelemesi için doğru temel olup olmadığı bu toplantıda kayda geçirilmelidir.

## 8. Kapsam sınırı matrisi

Bu matrisin amacı, toplantı sırasında ortaya çıkan talepleri güvenlik değeri, faz etkisi ve SecureOps sorumluluk sınırı açısından sınıflandırmaktır. Her başlık için yalnızca bir sınıf seçilmelidir.

| Karar maddesi | Varsayılan sınıf | Sınıf tanımı | Toplantıda netleştirilecek sonuç |
|---|---|---|---|
| Tehdit modelinin kabulü | Must comply | Tasarımın minimum güvenlik temelidir | Eksik senaryo varsa Phase 1 öncesi modele eklenir |
| Webhook'un tek dış giriş noktası olduğunun doğrulanması | Must comply | Saldırı yüzeyinin temelidir | Ek giriş noktası varsa risk modeli güncellenir |
| Webhook HMAC + allowlist + replay protection | Must comply | Dış tetikleme güvenliğinin temelidir | Ek kontrol gerekiyorsa Phase 1 ön koşulu olur |
| Veri akışı ve sızıntı yolları | Must comply | Veri nerede açığa çıkabilir sorusunu cevaplar | Kapatılması gereken yol varsa Phase 1 öncesi belirlenir |
| Servis hesabı blast radius değerlendirmesi | Must comply | Kompromizasyon senaryosunun ana kontrolüdür | Ek containment gerekiyorsa başlangıç ön koşulu olur |
| Audit tamper koruması | Must comply | Güvenlik delilinin güvenilirliğini sağlar | Eksik varsa Phase 1 öncesi revize edilir |
| Internal-only ağ doğrulaması | Must comply | Public exposure iddiasını teknik olarak doğrular | Kanıt formatı ve sahibi belirlenir |
| Olay müdahale minimumu | Must comply | Kompromizasyon halinde ilk eylemleri belirler | Eksik adım varsa Phase 1 öncesi tamamlanır |
| Phase 1 tespit olay seti | Should comply | Erken görünürlük sağlar; detay seviyesi fazlara göre genişleyebilir | Minimum olay seti ile sonraya kalabilecek ayrıştırılır |
| SIEM log forwarding | Should comply | Merkezi güvenlik görünürlüğünü güçlendirir | Phase 1 zorunluluğu mu, sonraki faz mı kararlaştırılır |
| Gelişmiş anomali tespiti | Could comply | Değerlidir fakat MVP çekirdeği değildir | Sonraki backlog veya ADR olarak ele alınır |
| Phase 7 model seçimi | Out of scope | Phase 7 başlamadan kesinleşmez | Ayrı AI ADR güncellemesine bırakılır |
| Public AI entegrasyonu | Out of scope | Public AI üretim verisiyle yasaktır | Değerlendirme dışı kalır |
| SIEM ürünü geliştirme | Out of scope | SecureOps SIEM değildir | Kurumsal SIEM ekibinin sorumluluğunda kalır |
| Mevcut PAM mimarisini değiştirme | Out of scope | SecureOps PAM replacement değildir | PAM ekibinin yol haritasında kalır |
| SolarWinds konfigürasyon yönetimi | Out of scope | SecureOps monitoring replacement değildir | Monitoring platform ekibinin sorumluluğunda kalır |
| Write remediation | Out of scope | Phase 8 öncesi yasaktır | Ayrı onay akışı ve gelecek faz konusu olarak kalır |

### Sınıflandırma kuralları

- `Must comply`: Sağlanmadan Phase 1 başlatılamaz.
- `Should comply`: Güvenlik değeri yüksektir; Phase 1-2 planına alınır, ancak başlangıcı tek başına bloke etmez.
- `Could comply`: Faydalıdır; eklenecekse ADR veya backlog maddesi olarak ele alınır.
- `Out of scope`: SecureOps sorumluluğu değildir veya mevcut faz sınırını ihlal eder.

Bu sınıflandırma toplantı sırasında kapsam genişlemesini engellemek için kullanılacaktır. Yeni bir talep ortaya çıktığında önce tehdit veya saldırı senaryosu yazılmalı, ardından yukarıdaki dört sınıftan yalnızca biri seçilmelidir.
