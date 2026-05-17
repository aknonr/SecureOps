# Bilgi Güvenliği Ön İnceleme Toplantısı

**Belge türü:** Toplantı öncesi bağlam, soru ve karar listesi  
**Proje:** Secure Ops Automation & AI Analysis Hub  
**Şirket adı:** CONTOSO  
**Toplantı sahibi:** project owner  
**Katılımcılar:** Bilgi Güv lead, ilgili Bilgi Güvenliği temsilcileri  
**Amaç:** Phase 1 başlamadan önce Bilgi Güvenliği açısından zorunlu güvenlik kararlarını yazılı ve izlenebilir hale getirmek

---

## 1. Toplantı amacı ve beklenen çıktılar

Bu toplantının amacı, SecureOps projesinin MVP güvenlik modelini Bilgi Güvenliği bakış açısıyla ön incelemeye sunmak ve Phase 1 başlangıcını etkileyen kararları toplantı sırasında netleştirmektir. Toplantı bir genel tanıtım oturumu değil, belirli güvenlik başlıklarında karar alma oturumu olarak yürütülmelidir.

Toplantı sonunda aşağıdaki çıktılar alınmış olmalıdır:

1. Read-only + JEA güvenlik modelinin Bilgi Güvenliği tarafından kabul edilip edilmediği.
2. JEA whitelist yaklaşımının yeterli görülüp görülmediği ve ek kontrol gerekip gerekmediği.
3. Servis hesabı, kimlik doğrulama ve yetkilendirme yaklaşımının uygunluğu.
4. Audit bütünlüğü, retention ve meta-audit yaklaşımının yeterliliği.
5. "Audit is not surveillance" çerçevesinin resmi olarak kabul edilip edilmediği.
6. Onaylanan inbound model webhook ise HMAC, source IP allowlist ve replay protection modelinin yeterliliği.
7. Secrets yönetimi ve PAM bağımlılığına ilişkin kabul, çekince veya ek şartlar.
8. Phase 1 başlamadan önce tamamlanması zorunlu maddelerin açık listesi.
9. SecureOps kapsamı dışında tutulacak taleplerin netleştirilmesi.

Her karar için toplantı sonunda aşağıdaki dört sonuçtan biri yazılacaktır:

- `Onay`
- `Revizyon gerekli`
- `Reddedildi`
- `Ek kanıt gerekli`

## 2. İncelenecek mimari özet

Bu ön inceleme, `docs/05-security-model.md` içindeki aşağıdaki başlıklar esas alınarak yapılacaktır:

- `Authentication`
- `Authorization (RBAC)`
- `JEA (Just Enough Administration)`
- `Whitelist (Canonical)`
- `Service Account`
- `Audit`
- `Audit Is Not Surveillance — Canonical Language`
- `Secrets Management`
- `Network Boundaries`

İncelenecek MVP modeli şu şekildedir:

| Alan | Önerilen yaklaşım |
|---|---|
| Hedef sunucular üzerindeki işlem modeli | Phase 1-6 boyunca yalnızca read-only tanılama |
| Uzak PowerShell erişimi | JEA constrained endpoint zorunlu; Worker privileged-access path'i BeyondTrust / direct WinRM seçenekleri arasında açık karardır |
| Kullanıcı kimlik doğrulama | Windows Authentication via Active Directory |
| Yetkilendirme | AD grup tabanlı RBAC: Operator, TeamLead, Admin, Auditor |
| Webhook güvenliği | Turuncuhat entegrasyonu webhook olarak onaylanırsa HMAC-SHA256 imza, source IP allowlist, 5 dakikalık replay protection |
| Servis hesabı | `CONTOSO\svc-secureops`, PAM tarafından yönetilen parola, hedef sunucularda local admin yok |
| Audit | Append-only `audit.AuditLog`, UPDATE/DELETE bloklu, minimum 36 ay retention |
| Audit erişimi | Auditor/Admin erişimi; privileged read işlemleri de audit edilir |
| Secrets | PAM öncelikli; geliştirme ortamı istisnaları kaynak kontrolü dışında |
| Ağ sınırları | Internal-only, public ingress yok, public AI çıkışı yok |

Bu modelin dışındaki remediation, write operation, public AI, genel internet çıkışı ve mevcut SolarWinds/PAM/Ansible yapılarını değiştirme talepleri MVP kapsamında değildir.

Not: Worker'ın hedef sunuculara erişiminde BeyondTrust broker mı, direct WinRM + Kerberos + JEA mı, yoksa mevcut direct-JEA modelinin açık kabul ile sürmesi mi kullanılacağı henüz kararlaştırılmamıştır. Bu toplantı direct-JEA modelini önceden onaylanmış varsaymamalıdır.

## 3. Karar gerektiren başlıklar

| Karar başlığı | Neden bu toplantıda karara bağlanmalı | İstenen karar |
|---|---|---|
| Read-only + JEA modeli | Phase 1 teknik temelini belirler; güvenlik ekibi kabul etmeden ilerlemek doğru değildir | Güvenlik modeli kabul ediliyor mu |
| JEA whitelist | Hedef sunucularda fiili komut sınırını belirler | Whitelist yeterli mi, daraltma veya ek kontrol gerekli mi |
| Servis hesabı | PAM, AD ve hedef sunucu erişim modelinin merkezindedir | Yetki modeli yeterli mi |
| Kimlik doğrulama ve RBAC | UI/API güvenliğinin temelidir | Windows Auth + AD group mapping yaklaşımı uygun mu |
| Webhook güvenliği | Onaylanan inbound model webhook ise ilk giriş noktasını korur | HMAC + allowlist + replay protection yeterli mi |
| Audit bütünlüğü ve meta-audit | İç denetim ve olay sonrası ispat için temel gereksinimdir | Append-only model ve audit query audit yaklaşımı yeterli mi |
| "Not surveillance" çerçevesi | Yönetim taahhüdü ve kullanıcı güveni için zorunludur | Kullanılacak dil ve sınırlar kabul ediliyor mu |
| Secrets yönetimi | Servis hesabı, HMAC secret ve ileriki faz bağımlılıkları için kritik | PAM ağırlıklı yaklaşım kabul ediliyor mu |
| Phase 1 ön koşulları | Güvenlik ekibinin go/no-go etkisini netleştirir | Başlamadan önce zorunlu ek madde var mı |
| Kapsam dışı talepler | Scope creep'i önler | Hangi beklentiler SecureOps sorumluluğu dışında kalacak |

## 4. Bilgi Güvenliği’nden net cevap alınması gereken sorular

### 4.1 Read-only + JEA güvenlik modeli

**Referans:** `docs/05-security-model.md` → `JEA (Just Enough Administration)`  
**Karar sorusu:** Phase 1-6 boyunca hedef sunucularda yalnızca read-only tanılama yapılması ve tüm WinRM işlemlerinin zorunlu JEA constrained endpoint üzerinden geçmesi Bilgi Güvenliği tarafından MVP için yeterli ana güvenlik modeli olarak onaylanıyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.2 JEA whitelist yaklaşımı

**Referans:** `docs/05-security-model.md` → `Whitelist (Canonical)`  
**Karar sorusu:** Aşağıdaki JEA whitelist, MVP tanılama kapsamı için Bilgi Güvenliği tarafından yeterli ve kabul edilebilir bulunuyor mu; bulunmuyorsa Phase 1 başlamadan önce hangi cmdlet veya cmdlet grubu için revizyon şarttır?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

**Mevcut whitelist:**

```text
Get-Disk
Get-PSDrive
Get-Volume
Get-Partition
Get-PhysicalDisk
Get-CimInstance
Get-WmiObject
Get-Process
Get-Service
Get-WinEvent
Get-EventLog
Get-ChildItem
Get-Item
Get-Content
Test-Path
Get-Counter
Get-NetTCPConnection
Get-NetAdapter
Get-ScheduledTask
Get-Website
Get-WebApplication
Get-WebAppPoolState
Get-WebBinding
Get-WebConfigurationProperty
Measure-Object
Select-Object
Sort-Object
Where-Object
ForEach-Object
Format-List
Format-Table
Out-String
ConvertTo-Json
```

### 4.3 Servis hesabı modeli

**Referans:** `docs/05-security-model.md` → `Service Account`  
**Karar sorusu:** Tek servis hesabı (`CONTOSO\svc-secureops`), PAM yönetimli parola, hedef sunucularda local admin yetkisi olmaması, yalnızca JEA endpoint erişimi ve SecureOps veritabanı erişimi kombinasyonu Bilgi Güvenliği açısından yeterli midir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.4 Kimlik doğrulama ve yetkilendirme

**Referans:** `docs/05-security-model.md` → `Authentication`, `Authorization (RBAC)`  
**Karar sorusu:** UI ve API için Windows Authentication, AD grup tabanlı RBAC ve `Operator / TeamLead / Admin / Auditor` rol ayrımı MVP için uygun mudur; Phase 1 başlamadan önce ek rol, ayrı görev ayrımı veya zorunlu policy değişikliği gerekli midir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.5 Webhook giriş noktası güvenliği

**Referans:** `docs/05-security-model.md` → `Authentication`, `Network Boundaries`  
**Karar sorusu:** Turuncuhat entegrasyonu için webhook modeli seçilirse HMAC-SHA256 imza, source IP allowlist ve 5 dakikalık replay protection kombinasyonu MVP için yeterli midir; Phase 1 öncesi zorunlu ek kontrol gerekiyor mu? API-pull modeli seçilirse hangi eşdeğer kontrol seti gerekir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.6 Audit bütünlüğü ve retention

**Referans:** `docs/05-security-model.md` → `Audit`  
**Karar sorusu:** Append-only `audit.AuditLog`, UPDATE/DELETE bloklama trigger'ı, minimum 36 ay retention ve her state-changing operation için audit kaydı yaklaşımı Bilgi Güvenliği gereksinimlerini karşılıyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.7 Meta-audit yaklaşımı

**Referans:** `docs/05-security-model.md` → `Audit`; `docs/01-current-operations-context.md` → `Audit Reframing (Critical)`  
**Karar sorusu:** Audit sorguları gibi privileged read işlemlerinin de audit edilmesi ve per-operator sorguların yalnızca audit/compliance rolleriyle sınırlandırılması kabul ediliyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

**Korunacak çerçeve:**

> "Audit captures what was done in response to an alarm — operationally — for the purposes of incident response verification, SLA evidence, and compliance review. It does not measure individual operator performance."

**Pratik sınırlar:**

- No leaderboards.
- No per-operator response time charts in the default UI.
- Filter UI defaults to "by alarm" or "by time", not "by operator".
- Reports aggregate to team level, not individual.
- Per-operator queries are possible for audit and compliance roles only, and produce an audit entry of their own.

### 4.8 "Audit is not surveillance" resmi çerçevesi

**Referans:** `docs/05-security-model.md` → `Audit Is Not Surveillance — Canonical Language`  
**Karar sorusu:** Aşağıdaki resmi dilin tüm kullanıcı arayüzü, rapor ve paydaş iletişimlerinde aynen korunması Bilgi Güvenliği tarafından kabul ediliyor mu?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

> "Operational response verification, SLA evidence, and audit. This is process auditing, not personnel monitoring."

> "Kişi takibi amacıyla değil; kritik alarmlarda operasyonel müdahale doğrulama, SLA ve audit amacıyla."

### 4.9 Secrets yönetimi

**Referans:** `docs/05-security-model.md` → `Secrets Management`  
**Karar sorusu:** Servis hesabı parolasının PAM'de tutulması, webhook shared secret'ın PAM veya geliştirme ortamında source control dışı konfigürasyonda tutulması ve production connection string'lerinde integrated auth önceliği Bilgi Güvenliği için yeterli midir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.10 Ağ sınırları

**Referans:** `docs/05-security-model.md` → `Network Boundaries`  
**Karar sorusu:** Internal-only erişim, public ingress olmaması, her Worker privileged-access senaryosunda JEA'nın zorunlu kalması ve public AI çıkışının tamamen yasaklanması yaklaşımı MVP için yeterli midir? BeyondTrust broker / direct WinRM seçeneklerinden biri seçildiğinde ek ağ veya kontrol şartı var mıdır?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.11 Phase 1 öncesi zorunlu koşullar

**Referans:** `docs/05-security-model.md` bütünü; `plans/PHASE-0-discovery-and-project-setup.md`  
**Karar sorusu:** Bilgi Güvenliği açısından Phase 1 başlamadan önce tamamlanması zorunlu ek şartlar var mıdır; varsa bunlar `Must comply` olarak kayda geçirilecek kesin maddeler halinde nelerdir?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

### 4.12 Kapsam dışı talepler

**Referans:** `docs/00-project-brief.md`, `docs/02-roadmap.md`, `docs/05-security-model.md`  
**Karar sorusu:** Aşağıdaki başlıklardan hangileri Bilgi Güvenliği açısından SecureOps MVP kapsamı dışında kabul edilmeli ve ayrı iş akışı olarak ele alınmalıdır: mevcut PAM ürün mimarisini değiştirme, SolarWinds konfigürasyon yönetimi, Ansible/AWX dönüşümü, SIEM işlevleri, public AI entegrasyonu, write remediation?  
**Beklenen çıktı:** `Onay / Revizyon gerekli / Reddedildi / Ek kanıt gerekli`

## 5. Karar matrisi

Toplantı sırasında her satır doldurulmalıdır. Karar sonucu boş bırakılan satır, toplantıdan çıkmış karar sayılmaz.

| # | Karar maddesi | Önerilen SecureOps yaklaşımı | Bilgi Güv kararı | Gerekçe / koşul | Aksiyon sahibi | Son tarih |
|---|---|---|---|---|---|---|
| 1 | Read-only + JEA modeli | Phase 1-6 boyunca write operation yok; tüm WinRM JEA üzerinden |  |  | Bilgi Güv lead / project owner |  |
| 2 | JEA whitelist | `Whitelist (Canonical)` listesinin kullanılması |  |  | Bilgi Güv lead / project owner |  |
| 3 | Servis hesabı | PAM yönetimli tek servis hesabı, local admin yok |  |  | Bilgi Güv lead / PAM admin |  |
| 4 | Kimlik doğrulama ve RBAC | Windows Auth + AD group-based roles |  |  | Bilgi Güv lead / project owner |  |
| 5 | Webhook güvenliği | HMAC-SHA256 + source IP allowlist + replay protection |  |  | Bilgi Güv lead / monitoring admin |  |
| 6 | Audit bütünlüğü | Append-only audit, trigger koruması, 36 ay retention |  |  | Bilgi Güv lead / project owner |  |
| 7 | Meta-audit | Privileged read sorgularının da audit edilmesi |  |  | Bilgi Güv lead / project owner |  |
| 8 | Not-surveillance dili | Canonical language'ın korunması |  |  | Bilgi Güv lead / project owner |  |
| 9 | Secrets yönetimi | PAM öncelikli, integrated auth tercihli yaklaşım |  |  | Bilgi Güv lead / PAM admin |  |
| 10 | Ağ sınırları | Internal-only, no public ingress, no public AI |  |  | Bilgi Güv lead / network admin |  |
| 11 | Phase 1 ön koşulları | Ek zorunlu güvenlik maddelerinin listelenmesi |  |  | Bilgi Güv lead / project owner |  |
| 12 | Kapsam dışı talepler | SecureOps MVP sorumluluk sınırlarının korunması |  |  | Bilgi Güv lead / project owner |  |

Son tarih kolonu boş kalan kararlar için varsayılan süre: toplantı tarihinden itibaren 5 iş günüdür. Daha uzun süre gerekiyorsa Bilgi Güv tarafından gerekçesiyle yazılı belirtilmelidir.

## 6. Toplantı sonrası beklenen artefaktlar

Toplantıdan sonra aşağıdaki çıktılar üretilmelidir:

1. Bilgi Güvenliği'nin yazılı geri bildirimi veya toplantı notu onayı.
2. Karar matrisinin tamamlanmış sürümü.
3. `Must comply` maddelerinin sahibi ve son tarihi belirlenmiş aksiyon listesi.
4. `Should comply` ve `Could comply` maddelerinin Phase 1-2 backlog veya ADR sürecine aktarılması.
5. Gerekirse `docs/05-security-model.md` güncellemesi.
6. Mevcut mimari kararı değiştiren bir talep çıkarsa ilgili ADR taslağı.
7. Phase 1 go/no-go değerlendirmesinde kullanılacak güvenlik durumu özeti.

Bu artefaktlar olmadan toplantı "bilgilendirme yapıldı" kabul edilebilir, ancak "güvenlik ön inceleme kararı tamamlandı" kabul edilmez.

## İtiraz ve anlaşmazlık yönetimi

Bir karar `Reddedildi` olarak kaydedilirse, kararın gerekçesi yazılı biçimde karar matrisine eklenir. project owner, reddedilen başlık için alternatif yaklaşım, kapsam daraltma veya anlaşmazlığı belgeleyen bir ADR önerir. Gerekirse ilgili paydaşlarla yeniden inceleme toplantısı planlanır. Çözüm tamamlanmadan Phase 1 başlangıcı bloke kabul edilir.

## 7. Toplantı öncesi bağlam özeti

SecureOps, Windows ağırlıklı operasyon ortamında gelen alarmlar için yapılan manuel kontrolleri standartlaştırmak amacıyla tasarlanmış bir platformdur. Mevcut iş akışında vardiya mühendisi alarm aldıktan sonra hedef sunucuya bağlanır, disk, servis, IIS, event log ve benzeri kontrolleri manuel olarak çalıştırır, sonucu ticket ve vardiya raporuna elle işler. SecureOps bu süreci otomatikleştirerek aynı alarm türü için aynı read-only tanılama adımlarının tutarlı biçimde çalışmasını, sonuçların yapılandırılmış kaydedilmesini ve operasyonel görünürlüğün artmasını hedefler.

MVP sınırı bilinçli olarak dardır. Phase 1-6 boyunca hedef sunucular üzerinde write operation yapılmayacaktır; servis başlatma, app pool recycle, dosya silme, reboot veya izin değiştirme gibi işlemler sistem tarafından gerçekleştirilmeyecektir. Tüm uzak PowerShell işlemleri JEA constrained endpoint üzerinden yürütülecek, mevcut SolarWinds, PAM/BeyondTrust ve Ansible/AWX altyapıları değiştirilmeden korunacaktır. Bu yaklaşımın amacı, operasyonel faydayı hızlı üretirken hedef sistemlerdeki risk yüzeyini kontrollü tutmaktır.

Audit kabiliyeti çalışan performansı ölçmek için değil, alarm karşısında hangi operasyonel adımların atıldığını doğrulamak için tasarlanmıştır. Projede yönetimle taahhüt edilen çerçeve şudur: "Audit captures what was done in response to an alarm — operationally — for the purposes of incident response verification, SLA evidence, and compliance review. It does not measure individual operator performance." Kullanıcı arayüzü ve raporlarda korunacak resmi ifade de şöyledir: "Operational response verification, SLA evidence, and audit. This is process auditing, not personnel monitoring." Türkçe yönetim dili: "Kişi takibi amacıyla değil; kritik alarmlarda operasyonel müdahale doğrulama, SLA ve audit amacıyla."

Bu toplantıdan beklenen, genel proje onayı değil; Phase 1 başlangıcını doğrudan etkileyen güvenlik kararlarının netleşmesidir. Özellikle read-only + JEA modelinin yeterliliği, whitelist kabulü, servis hesabı yaklaşımı, audit/meta-audit ilkeleri, webhook güvenliği, secrets yönetimi ve Phase 1 öncesi zorunlu ek koşullar üzerinde yazılı karar beklenmektedir. Toplantı sonucunda hangi taleplerin zorunlu, hangilerinin sonraki fazlara bırakılabilir, hangilerinin proje kapsamı dışında olduğu açık biçimde ayrıştırılmalıdır.

## 8. Kapsam sınırı matrisi

Bu matrisin amacı, toplantı sırasında ortaya çıkan talepleri güvenlik değeri ile faz etkisini karıştırmadan sınıflandırmaktır. Her başlık için yalnızca bir sınıf seçilmelidir.

| Karar maddesi | Varsayılan sınıf | Sınıf tanımı | Toplantıda netleştirilecek sonuç |
|---|---|---|---|
| Read-only + JEA modelinin kabulü | Must comply | Phase 1 başlamadan önce onaylanması zorunlu | Kabul edilmediği sürece Phase 1 başlamaz |
| JEA whitelist'in kabulü | Must comply | Hedef sunucularda fiili komut sınırıdır | Revizyon gerekiyorsa Phase 1 öncesi kapanır |
| Servis hesabı modeli | Must comply | Erişim temelidir | PAM/AD gereksinimleri kesinleşir |
| Windows Auth + RBAC yaklaşımı | Must comply | UI/API yetkilendirmesinin temelidir | Ek rol veya separation-of-duty şartı varsa karara bağlanır |
| Webhook HMAC + allowlist + replay protection | Must comply | Webhook modeli seçilirse ilk giriş noktasını korur | Ek zorunlu kontrol varsa başlangıç ön koşulu olur |
| Append-only audit ve 36 ay retention | Must comply | Denetim bütünlüğünün temelidir | Eksik varsa Phase 1 öncesi revize edilir |
| Meta-audit | Must comply | Privileged read izlenebilirliği sağlar | Audit sorgularının da audit edilmesi kesinleşir |
| Not-surveillance dili | Must comply | Yönetim taahhüdü ve kullanım sınırıdır | Resmi çerçeve korunur |
| Secrets yönetimi | Must comply | Kimlik bilgisi güvenliğinin temelidir | PAM veya kabul edilen eşdeğer yöntem kesinleşir |
| Ağ segmentasyonu detayları | Should comply | Güvenliği güçlendirir, ancak mevcut MVP topolojisine göre bazı detaylar Phase 1-2'ye planlanabilir | Phase 1 öncesi zorunlu minimum ile sonraya kalabilecek ayrıştırılır |
| Ek rapor watermarking gereksinimleri | Should comply | Compliance değerini artırır | Phase 1 mi Phase 2 mi uygulanacağı belirlenir |
| Ek güvenlik dashboard'ları | Could comply | Faydalı olabilir, ancak MVP çekirdeği için zorunlu değildir | Ayrı backlog maddesi veya ADR olarak değerlendirilir |
| SIEM fonksiyonu ekleme | Out of scope | SecureOps SIEM değildir | Ayrı ürün/süreç sorumluluğunda kalır |
| Mevcut PAM mimarisini değiştirme | Out of scope | SecureOps PAM replacement değildir | PAM ekibinin ayrı yol haritasında kalır |
| SolarWinds konfigürasyon yönetimi | Out of scope | SecureOps monitoring replacement değildir | Monitoring platform ekibinin sorumluluğunda kalır |
| Ansible/AWX dönüşümü | Out of scope | MVP mevcut altyapıyı değiştirmez | Phase 6+ opsiyonel yeniden değerlendirme dışında tutulur |
| Public AI kullanımı | Out of scope | Public AI üretim verisiyle yasaktır | Phase 7 yalnızca self-hosted olarak ele alınır |
| Write remediation | Out of scope | Phase 8 öncesi yasaktır | Ayrı onay akışı ve gelecek faz konusu olarak kalır |

### Sınıflandırma kuralları

- `Must comply`: Sağlanmadan Phase 1 başlatılamaz.
- `Should comply`: Güvenlik değeri yüksektir; Phase 1-2 planına alınır, ancak başlangıcı tek başına bloke etmez.
- `Could comply`: Faydalıdır; eklenecekse ADR veya backlog maddesi olarak ele alınır.
- `Out of scope`: SecureOps sorumluluğu değildir veya mevcut faz sınırını ihlal eder.

Bu sınıflandırma toplantı sırasında kapsam genişlemesini engellemek için kullanılacaktır. Yeni bir talep ortaya çıktığında önce güvenlik gerekçesi yazılmalı, ardından yukarıdaki dört sınıftan yalnızca biri seçilmelidir.
