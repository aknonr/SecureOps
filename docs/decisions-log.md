# Decisions Log

## 2026-05-16 — PAM pre-review meeting focus

**What we discussed:** Bilgi Güvenliği ve Siber Güvenlik ön incelemeleri politika, risk ve kontrol çerçevesi üretirken PAM görüşmesinin doğası daha operasyoneldir. PAM ekibi için asıl değer, servis hesabı, parola rotasyonu, secret retrieval yöntemi, Phase 4 read-only session correlation erişimi ve gelecek faz bağımlılıklarının somut ticket/request çıktısına dönüşmesidir.

**What was decided:** PAM toplantı belgesi karar disiplini korunarak hazırlanacak, ancak ana başarı ölçütü soyut mutabakat değil açık ticket, owner ve hedef tarih üretmek olacaktır. `svc-secureops` hesabı, secret onboarding, Phase 4 read-only API erişimi ve PAM query logging başlıkları öncelikli ele alınacaktır.

**What was deferred:** Phase 8'de PAM'ın remediation oturumlarını broker edip etmeyeceği nihai karar olarak bugünden bağlanmayacak; konu erken yönlendirme almak için sorulacak ve gerekiyorsa Phase 8 öncesi ayrı tasarım görüşmesine bırakılacaktır.

## 2026-05-17 — Gerçek operasyon zincirinin netleşmesi

**What we learned:** Turuncuhat yalnızca genel bir ticketing sistemi değil; EVT oluşturan, PR/OR/OCO kayıtlarını açabilen, mail/IVR gönderen ve acknowledge/close akışının gerçek operasyonel merkezi olan sistemdir. SolarWinds alarm kaynağı, monthly.thy.com / HPE OpsBridge ise event-detail katmanıdır.

**What was decided:** SecureOps tasarımı Turuncuhat'ı organizasyonel kayıt sistemi olarak ele alacak; entegrasyon yönü tek taraflı varsayılmayacak ve ihtiyaç halinde EVT verisini alma ile EVT kapanış alanlarını geri yazma ihtimali birlikte değerlendirilecektir.

**What was deferred:** Worker'ın hedef sunuculara erişiminde BeyondTrust broker kullanıp kullanmayacağı bugünden bağlanmadı. Nihai karar PAM ekibi, Bilgi Güvenliği ve ekip lideri girdisi geldikten sonra verilecektir.

## 2026-05-17 — Batch 4 ve Batch 5 kapanışı

**What changed:** Batch 4 ile gerçek sistem manzarası repo geneline işlendi: Turuncuhat merkezi workflow sistemi olarak netleştirildi, monthly.thy.com / HPE OpsBridge event-detail katmanı olarak işlendi ve Worker-BeyondTrust erişimi için X / Y / Z senaryoları açık karar olarak belgelendi. Batch 5 ile Phase 0 süre varsayımı yaklaşık 45 saatlik güncel efora uygun biçimde 2–3 hafta olarak yeniden bazlandı.

**What remains open:** Turuncuhat entegrasyon yöntemi hâlâ netleşmedi; webhook mu API-pull mu olacağı ve read-only mi read-write mı ilerleyeceği paydaş yanıtı bekliyor. Worker'ın BeyondTrust ile hangi senaryoda çalışacağı da henüz açık karardır.

**Mail status:** Turuncuhat ve BeyondTrust / PAM soruları taslaklandı; yönetici onayı bekleniyor, henüz gönderilmedi.

## 2026-06-18 — Phase 1A IdentityLookup kararı

**What we decided:** IdentityLookup / PamAdUserLookup, Phase 1'den önce gelen backend-only Phase 1A olarak eklenecektir. Amaç, yetkili TeamLead/Admin kullanıcıların tek bir PAM hesabını veya AD kullanıcı adını incident response verification bağlamında read-only çözebilmesidir.

**Security framing:** Bu özellik kişi arama veya performans izleme aracı değildir. Geniş arama, wildcard, bulk search, AD/PAM write, parola reset, unlock ve grup değişikliği kapsam dışıdır. Her sorgu gerekçe ister ve auditlenir.

**Implementation direction:** İlk sürüm direct read-only AD lookup kullanır. BeyondTrust/PAM metadata çözümleme mock interface olarak hazırlanır; gerçek PAM API kullanımı paydaş onayı ve ayrı karar olmadan etkinleştirilmez.

## 2026-06-19 - Repository consistency audit after Phase 1A hardening

**What changed:** Repository memory was synchronized with the current state: Phase 1A backend IdentityLookup and audit hardening are implemented and tested, while Phase 1 read-only diagnostics remain not started. `SecureOps.Shared` is documented as an accepted contracts/config/auth constants layer, not an infrastructure or UI layer.

**What remains open:** Real AD smoke testing with an approved read-only account is still pending. Phase 0 stakeholder replies are also still pending; Turuncuhat and BeyondTrust/PAM inquiry mails remain drafted and awaiting manager approval.

**Scope guard:** No new application feature was added in this audit. AI/RAG, notifications, PAM correlation, remediation, diagnostic modules, JEA scripts, and SQL schema remain later-phase or Phase 1 planned work as documented.

## 2026-06-19 - Phase 1A closure hardening decisions

**What changed:** Phase 1A now uses `ConnectionStrings:SecureOpsDb` as the single SQL audit/data store connection string name. The old `SecureOps` key is not treated as a compatibility alias, so missing production SQL configuration fails clearly when `Audit.Provider=SqlServer`.

**Audit behavior:** File/SQL audit persistence remains queued so request threads do not perform file IO. If a queued persistent write later fails, audit-store health becomes unhealthy with a safe code such as `AuditSinkUnavailable`; with fail-closed enabled, later IdentityLookup calls stop before AD provider access.

**Error taxonomy:** Directory provider timeout is separated from generic provider failure. Timeout returns `DirectoryProviderTimeout` and audits `IdentityLookupProviderTimeout`; generic provider exceptions remain `ProviderUnavailable` / `IdentityLookupFailed`.

**What remains open:** Real AD smoke testing with an approved read-only account is still pending in Test/UAT. Phase 1 diagnostic MVP has not started.
