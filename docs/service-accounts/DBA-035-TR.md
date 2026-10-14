# Servis Hesapları — 035 (plandan OR gönderimi) için DBA eki

Tarih: 2026-10-13. Kime: kurulu TEST veritabanından sorumlu DBA. Durum: **hazırlık belgesi; kurulu sisteme bu belgedeki hiçbir
şey uygulanmadı**; betikler Windows yerel LocalDB harness'ında prova edildi (aşağıda "Doğrulama durumu"). Uygulama için proje sahibinin ayrı onayı gerekir.
Tasarım: `docs/adr/ADR-0030-service-account-plan-or-creation.md` (**Proposed**, kabul edilmedi) ve OR-2 (yalnız sahte adaptör;
yapılandırma varsayılanı kapalı). Yedeği DBA kendi sürecinde alır; bu belge yedek komutu içermez.

**Sıra:** 029 → 030 → 031 → 032 → 033 → 034 → **035** → 036. 035, 033 ve 034'e bağlıdır; 036'ya bağlı değildir (036 da 035'e
bağlı değildir). 036 daha önce uygulanmışsa 035 sonradan uygulanabilir. Paketleme aracı (`scripts/release/Get-ReleaseSqlPlan.ps1`)
035'i henüz tanımaz (sahibi ayrı günceller).

## Ne uygulanacak

| Sıra | Dosya (repo içinde) | Ne yapar | Ön koşul |
|---|---|---|---|
| 1 | `sql/migrations/035-service-account-plan-operational-records.sql` | İki yeni tablo. `svcacct.ChangePlanRecordSubmissions`: onaylı gMSA planındaki bir hesap için her açık "OR aç" denemesi bir satır (plan, hesap, onaylı önizleme, işlem anahtarı, sunucu parmak izi, sağlayıcı, ortam, TH kullanıcı kimliği, gönderilen kısa açıklama ve açıklama, durum, OR iç kimliği/numarası). Satır, uzak çağrıdan **önce** `Sending` durumunda yazılır. Yalnız durum ve sonuç sütunları değişebilir, yalnız izinli geçişlerle; içerik değişimi ve silme **51403**, izinsiz geçiş **51404**. Filtreli tekil indeksler: plan+hesap başına tek canlı gönderim, OR numarası başına tek canlı sahip. `svcacct.ChangePlanRecordSubmissionEvents`: her durum değişimi; ekleme-yalnız (**51402**). Parola, oturum, adres, uzak yanıt gövdesi veya hata metni sütunu **yoktur**. Mevcut tablo, kısıt, satır, rol veya izin değişmez (`ChangePlanApprovals.OcoNumber` aynen kalır) | 033 ve 034 uygulanmış |
| 2 | `sql/pending/service-accounts/SA-008-API-permissions.sql` | API rolüne (`svcacct_api_runtime`) iki tabloda `SELECT, INSERT`; gönderim tablosunda **yalnız** `State, ExternalId, ExternalNumber, LinkVerification, OutcomeReason, UpdatedBy, UpdatedAt` sütunlarında `UPDATE`. DELETE verilmez. Rol üyesi atanmaz; Worker rolüne bir şey verilmez | 1 ve `SA-API-permissions.sql` |

Geçmiş (`svcacct.History`) ve audit (`audit.AuditLog`) API rolünün **mevcut** izinleriyle yazılır. 035 tek işlemdir; tablolardan
biri zaten varsa (tekrar) **51400** ile hiçbir şey değiştirmeden durur. 033/034 yoksa da **51400** ile durur.

Eski API sürümü bu tablolara dokunmaz. Yeni API sürümü 035 olmadan çalışır: plan ekranındaki OR uçları 503
`ServiceAccountOperationalRecordsNotInstalled` döner, diğer her şey çalışır. 035 kurulu olsa bile OR açma, sunucu yapılandırması
`ItsmRequestCreation:Provider` varsayılanı `Disabled` olduğu için kapalıdır; gerçek Turuncu Hat adaptörü yoktur.

## 1. Ön kontrol (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=pre -i <repo>\tests\sql\service-accounts\sa-035-dba-checks.sql
```

Beklenen: `OnKosul_033 = var`, `OnKosul_034 = var`, `OrTablolari = yok`, `ApiRolu = var`. Biri farklıysa durun.

## 2. Uygulama

```powershell
cd <repo>\sql\migrations
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i 035-service-account-plan-operational-records.sql
cd <repo>\sql\pending\service-accounts
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i SA-008-API-permissions.sql
```

İki komutun da çıkış kodu 0 olmalı. `-I` (QUOTED_IDENTIFIER) zorunludur: filtreli indeksler bunu ister.

## 3. Uygulama sonrası doğrulama (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=post -i <repo>\tests\sql\service-accounts\sa-035-dba-checks.sql
```

Beklenen: `OrTablosu = 2`, `Tetikleyici = 2`, `FiltreliTekilIndeks = 2`, `GuvenilmeyenKisit = 0`, `ApiIzni_SelectInsert = 4`,
`ApiIzni_SutunUpdate = 7`, `BaskaIzin = 0`, `GonderimSatiri = 0`. 035'i bir kez daha çalıştırmak **51400 ile reddedilmelidir**.

## 4. Geri dönüş

1. **Birincil yol:** DBA'nın uygulamadan önce aldığı kendi yedeğinden dönüş.
2. **Yalnız hiç gönderim kaydı yoksa:** iki tablo kaldırılır (tetikleyiciler, indeksler ve izinler tabloyla birlikte gider).
   Gönderim varsa tabloları bırakın: satırlar dış sisteme yapılmış (veya yapılmış olabilecek) bir yazmanın tek kanıtıdır; silinirse
   "Belirsiz" bir OR'un mutabakatı kaybolur. Eski API sürümü bu tabloları okumaz ve yazmaz.

   ```sql
   SET XACT_ABORT ON;
   BEGIN TRANSACTION;
   IF EXISTS (SELECT 1 FROM svcacct.ChangePlanRecordSubmissions) THROW 51405, 'OR submissions are stored; keep the 035 tables.', 1;
   DROP TABLE svcacct.ChangePlanRecordSubmissionEvents;
   DROP TABLE svcacct.ChangePlanRecordSubmissions;
   COMMIT TRANSACTION;
   ```

## Doğrulama durumu (dürüst)

- **2026-10-14, Windows yerel LocalDB'de koşuldu** (kurulu TEST'e hiçbir şey uygulanmadı): `sa-sql-harness.ps1` 001–036
  (`sa-035-guards.sql` dahil) geçti. `-ThroughMigration 34` kopyasında: ön kontrol `var/var/yok/var`; 035 ve SA-008 izinleri çıkış 0;
  son kontrol `2/2/2/0/4/7/0/0`; 035 tekrarı 51400; geri dönüş komutu `COMMIT` yerine `ROLLBACK` ile çalıştı (tablolar geri geldi);
  gönderim satırı olan veritabanında geri dönüş 51405 ile durdu.
- `sa-035-guards.sql` şunları dener: olay tablosunda UPDATE/DELETE 51402; gönderim silme ve içerik değişimi (harf büyüklüğü, sondaki
  boşluk dahil) 51403; izinsiz geçiş ve saklı numaranın değişimi 51404; aynı plan+hesapta ikinci canlı gönderim ve aynı OR numarası
  iki canlı satırda 2601; aynı hesapta aynı işlem anahtarı 2627; kod sütunlarında yanlış yazım ve durum biçimi 547; API rolü DELETE
  229, içerik UPDATE 230.
- Kurulu TEST'in gerçek API havuz hesabı ve gerçek SQL Server sürümü/harmanlaması ile **denenmedi**.
