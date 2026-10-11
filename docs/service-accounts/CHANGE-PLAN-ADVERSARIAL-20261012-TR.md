# E dalına karşı ek güvenlik testleri (G, 2026-10-12)

## 1. Verdict

T8'de bir kayıt bütünlüğü açığı doğrulandı; iki regresyon testi kırmızı bırakıldı. Üretim kodu, SQL şeması,
izin betikleri ve mevcut testler değiştirilmedi. Merge/deploy yapılmadı.
Taban: `feature/service-accounts-change-plan-api-20261007`, `db5685656fcdc2c517100d807154ae6cf5de671f`.
Çalışma kopyası: `C:\SecureOpsBuild\sa-g-tests`; ayrı dal: `tests/service-accounts-change-plan-adversarial-20261012`.

## 2. Evidence

Eklenenler: `tests/SecureOps.Tests.Integration/ServiceAccounts/ServiceAccountChangePlanAdversarialSqlTests.cs`
(10 SQL testi) ve `tests/SecureOps.Tests.Unit/ServiceAccounts/ChangePlanAdversarialDigestTests.cs` (6 birim vakası).

| Tehdit | Ek saldırı ve sonuç |
|---|---|
| T1–T2 | Hesap çıkarıldıktan sonra eski onay; aynı özetle yeni önizlemenin eski sürümü; iki farklı doğrulayıcıyla tekrar: ret ve dokuz tablo/audit görünümü değişmiyor. |
| T3 | PATCH yapan kişinin değişikliği başka kişi tarafından geri alınsa ve yeniden önizlense de onayı reddediliyor; planlayan ve eski editör doğrudan runtime SQL INSERT ile de 51393 alıyor. |
| T4 | İki servis örneği ve bağımsız SQL bağlantılarıyla eşzamanlı başlangıç kapısı, 32 tur: 13 onay / 19 iptal kazandı. Her tur bir başarı, bir 409; durum/olay/üç history/üç audit tutarlı; tekrar istekleri yazmıyor. |
| T5 | İki hesaplı planda yalnız bir organizasyonun kapsamı kaldırılıyor. Görme, kalem okuma, PATCH, önizleme, onay, iptal ve hesap filtreli liste kapsamı yeniden denetliyor. Kapsam dışı hesap PATCH ile sızmıyor; adı dönmüyor. |
| T6 | Başka planın özetiyle onay, başka plana bağlı PreviewId ile doğrudan SQL onayı, item/preview GUID'ini plan kimliği olarak kullanma ve başka hesabın RequestId'sini bağlama reddediliyor. |
| T7 | Ayraç/satır sonu enjeksiyonu, null/boş değer, eşit sıralama anahtarı ve tekrar sayısı: 6 birim vakası geçti. Tetikleyici kapatmadan eklenen sahte önizleme kalemi onayda özet yeniden hesaplanarak reddediliyor. |
| T8 | Altı ekleme-yalnız tabloda dolu ve gerektiğinde FK tarafından referanslanmayan satırlar: owner UPDATE/DELETE = 51392; runtime UPDATE/DELETE = 229. Toplam 24 deneme, tüm satırlar aynı. Sabit başlıkta iki atlatma bulundu. |

SDK `9.0.317`, Windows, hedef `net8.0`; `global.json` değiştirilmedi.
Harness iki kez yeni soneklerle çalıştı: `GAttack20261012A`, `GAttack20261012B`; ikisi de 001–033 ve rol/guard kontrollerini tamamladı.
Son SQL koşusu, B harness çıktısındaki `SECUREOPS_SA_SQL_TEST_CONNECTION` değerini kullandı; yalnız `SecureOpsResourcesV1` LocalDB.

| Koşu | Sonuç |
|---|---|
| `dotnet build SecureOps.sln --no-restore` | Son koşu: 0 uyarı, 0 hata. |
| Tam birim süiti | 2014 geçti, 0 başarısız, 1 atlandı. Windows'ta makinenin servis/görevlerini okuyacak collector testi tasarım gereği atlandı. Yeni 6 vaka geçti. |
| Seri change-plan + API composition | 42 toplam: 40 geçti, 2 T8 regresyonu başarısız, 0 atlandı. Yeni SQL testleri: 8 geçti, 2 başarısız. |
| Repo geneli format kapısı | `dotnet format SecureOps.sln --verify-no-changes --no-restore`: geçti, çıkış 0. |
| Geniş paralel Service Accounts denemesi | Tamamlanmadan durduruldu; 5 mevcut testte SQL timeout (-2). Aynı 5 test ayrı seri koşuda geçti (5/5). Başarılı tam entegrasyon koşusu sayılmaz; paralel timeout kök nedeni doğrulanmadı. |

Kanıt dizini: `C:\SecureOpsBuild\sa-g-evidence-20261012` (`unit-full.trx`, `change-plan-final.trx`,
`build-final.log`, `harness-B.log`, `sql-final-diagnostics.log`, `format-final.log`, `service-accounts-all.log`, `timeout-cases-serial.trx`).
Son SQL tanılamasında yalnız beklenen audit-hata enjeksiyonu 51399 var; 1205 yok.
Yarış ayrıca SqlClient komut hata olaylarını dinler: okuma tekrarının gizlediği 1205 de testi düşürür.
T8 matrisinde aynı gözlemci 51392/229 gördü. Son DB kontrolü: geçici `sa_attack_*` kullanıcı sayısı 0, kapalı change-plan tetikleyicisi 0.

## 3. Blockers

**T8: sabit Title değiştirilebiliyor.** Kaynak: `sql/pending/service-accounts/SA-006-change-plans.sql:144`;
runtime rolünün tablo düzeyinde UPDATE izni: `sql/pending/service-accounts/SA-006-API-permissions.sql:9`.
Tetikleyicinin `i.Title <> d.Title` karşılaştırması, test DB'sinin `SQL_Latin1_General_CP1_CI_AS` collation'ında
yalnız büyük/küçük harf farkını ve sondaki boşluğu değişiklik saymıyor. Runtime rolüyle başlık önce küçük harfe,
ayrı testte bir boşluk eklenerek değiştirildi; satırın gerçekten değiştiği işlem içinde okunarak doğrulandı.
Beklenen 51394 gelmedi. İki işlem de finally içinde geri alındı; düzeltme yapılmadı.

Açık sorular: düzeltmeyi E sahibi hangi dalda yapacak? Geniş paralel SQL koşusunun timeout nedeni nedir?
PR 2'de item/check/evidence rotaları için saldırı vakaları ne zaman çalıştırılabilecek?

## 4. Minimal Safe Next Step

E sahibi, sabit kolon kontrolünü tam değer/uzunluk değişimini algılayacak şekilde ayrı düzeltmede ele almalı;
runtime rolünün yalnız gerekli kolonları güncelleyebilmesi de değerlendirilmelidir. Buradaki iki kırmızı testi
yeşile çevirdikten sonra yeni harness DB ile aynı seri koşu tekrarlanmalı. Merge kararı sahibinde kalır.

## 5. Risks

Açık doğrudan SQL runtime rolüyle gösterildi; HTTP üzerinden başlık değiştirme atlatması kanıtlanmadı.
PR 1'de `items/{itemId}/checks`, complete ve plan kanıt rotaları yok; bunlar için IDOR/kapsam kabulü yapılmış sayılmaz.
SQL yolculuğunda kimlik/yetenek kaynağı sentetik fixture'dır; API composition mevcut Test host kontrolleridir.
Gerçek OIDC/AD/JEA/PAM, IIS/F5, kurulu TEST, UI tarayıcı yolculuğu ve tüm entegrasyon süiti doğrulanmadı.
İki yerel test DB'si kanıt için bırakıldı; mevcut DB/süreçlere ve kurumsal sistemlere işlem yapılmadı.

Tekrar: yeni bir harness sonekiyle DB oluşturun, çıktının `SECUREOPS_SA_SQL_TEST_CONNECTION` değerini süreç ortamına alın;
`dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj --filter "FullyQualifiedName~ServiceAccountChangePlan|FullyQualifiedName~ServiceAccountApiCompositionTests"`
koşusunu xUnit `MaxParallelThreads=1`, `ParallelizeTestCollections=false` ayarlarıyla çalıştırın.
İki `FixedTitle_Runtime*Update_MustBeRejected` testi, E tabanında düzeltme yapılana kadar başarısız olmalıdır.
