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
