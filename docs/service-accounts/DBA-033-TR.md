# Servis Hesapları — 033 (gMSA değişiklik planı) için DBA eki

Tarih: 2026-10-07. Kime: kurulu TEST veritabanından sorumlu DBA. Durum: **hazırlık belgesi; kurulu sisteme bu belgedeki hiçbir
şey uygulanmadı.** Uygulama için proje sahibinin ayrı onayı gerekir. `DBA-029-030-TR.md` (029–031) ve
`docs/access-registration-dba-032.md` (032, Access) önce gelir. **Sıra: 029 → 030 → 031 → 032 → 033.** Yedeği DBA kendi
sürecinde alır; bu belge yedek komutu içermez. Tasarım: `CHANGE-PLAN-DESIGN.md`.

## Ne uygulanacak

| Sıra | Dosya (repo içinde) | Ne yapar | Ön koşul |
|---|---|---|---|
| 1 | `sql/migrations/033-service-account-change-plans.sql` | Yedi yeni tablo: `ChangePlans`, `ChangePlanAccounts`, `ChangePlanPreviews`, `ChangePlanItems`, `ChangePlanApprovals`, `ChangeItemChecks`, `ChangePlanEvents`. `ChangePlans` dışındakiler **salt eklenir** (UPDATE/DELETE tetikleyiciyle engelli, 51392); `ChangePlans`'ta yalnız durum, güncel önizleme sürümü ve güncelleme damgası değişebilir, satır silinemez (51394). Onay tablosunda önizleme başına tek onay (tekil indeks) ve **onaylayan ≠ planlayan / önizlemeyi üreten / planı değiştiren** tetikleyicisi (51393). Mevcut hiçbir tablo, satır, kısıt, rol değişmez; parola/gizli sütun yok | 025, 030, 031 uygulanmış |
| 2 | `sql/pending/service-accounts/SA-006-API-permissions.sql` | `svcacct_api_runtime` rolüne yeni tablolarda `SELECT, INSERT`, `ChangePlans`'ta ayrıca `UPDATE`. **DELETE yok**; Worker rolüne hiçbir şey | 033 uygulanmış, `svcacct_api_runtime` rolü var |

033 tek işlemdir (aradaki `GO`lar işlemin içindedir) ve ikinci çalıştırmada "already applied" diye **reddedilir**. Ortada hata
olursa hiçbir nesne kalmaz (sentetik kopyada kanıtlandı, aşağıda). 033, 032'ye bağlı değildir; yine de sıra korunmalı.

## 1. Ön kontrol (salt okunur)

`tests\sql\service-accounts\sa-033-dba-checks.sql` hiçbir şey değiştirmez:

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=pre -i <repo>\tests\sql\service-accounts\sa-033-dba-checks.sql
```

Beklenen: `OnKosul_025_030_031 = var`, `ApiRolu = var`, `033Tablosu = 0`. Biri farklıysa durun.

## 2. Uygulama

```powershell
cd <repo>\sql\migrations
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i 033-service-account-change-plans.sql
cd ..\pending\service-accounts
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i SA-006-API-permissions.sql
```

Her komuttan sonra çıkış kodu 0 olmalı. Sıra şema → uygulama sürümü: 033 eski API sürümüyle uyumludur (eski sürüm yeni tabloları
kullanmaz). Yeni API sürümü 033 olmadan plan uçlarında 503 `ServiceAccountChangePlansNotInstalled` döner ve hiçbir şey yazmaz;
diğer işlevler çalışır.

## 3. Uygulama sonrası doğrulama (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=post -i <repo>\tests\sql\service-accounts\sa-033-dba-checks.sql
```

Beklenen: `Tablo = 7`, `Tetikleyici = 8`, `GuvenilmeyenKisit = 0`, `ApiIzni = 15`, `BeklenmeyenIzin = 0` (DELETE veya başka
rol/kullanıcıya izin yok), `PlanSatiri = 0`. Ardından 033'ü bir kez daha çalıştırmak **reddedilmelidir**; izin betiğinin tekrarı
zararsızdır (aynı izinler).

## 4. Geri dönüş

1. **Birincil yol:** DBA'nın uygulamadan önce aldığı kendi yedeğinden dönüş.
2. **Yalnız hiç plan oluşturulmadıysa:** tablolar bağımlılık sırasıyla düşürülür (izinler tablolarla gider). Plan satırı varsa
   düşürmeyin: kayıtlar onay ve uygulama kanıtıdır; tabloları bırakın, eski API sürümü onları kullanmaz.

   ```sql
   SET XACT_ABORT ON;
   BEGIN TRANSACTION;
   IF EXISTS (SELECT 1 FROM svcacct.ChangePlans) THROW 51395, 'Change plans are stored; keep the tables.', 1;
   DROP TABLE svcacct.ChangeItemChecks; DROP TABLE svcacct.ChangePlanApprovals; DROP TABLE svcacct.ChangePlanItems;
   DROP TABLE svcacct.ChangePlanPreviews; DROP TABLE svcacct.ChangePlanEvents; DROP TABLE svcacct.ChangePlanAccounts; DROP TABLE svcacct.ChangePlans;
   COMMIT TRANSACTION;
   ```
   Yalnız erişimi kapatmak için: `REVOKE SELECT, INSERT ON OBJECT::svcacct.<tablo> FROM svcacct_api_runtime;` (yedi tablo; `ChangePlans` için `UPDATE` de).

## Sentetik kopyada ne denendi (2026-10-07, LocalDB, gerçek TEST veritabanında DEĞİL)

- `sa-sql-harness.ps1 -ThroughMigration 32` ile 032'de bırakılmış iki kopya: ön kontrol `var / var / 0`; 033 ve izin betiği
  çıkış 0; son kontrol `7 / 8 / 0 / 15 / 0 / 0`; 033 tekrarı "already applied" ile reddedildi.
- Geri dönüş komutu `COMMIT` yerine `ROLLBACK` ile denendi: tablolar düştü (0) ve geri alındı (7). Plan satırı olan kopyada
  koruma 51395 ile durdurdu.
- 033 adayının `COMMIT`'inden hemen önce yapay hata (51399) eklenen kopyasında betik durdu ve **hiçbir** 033 nesnesi kalmadı.
- Tam harness (001–033) her çalıştırmada `sa-033-guards.sql` ile tetikleyicileri doğrudan dener: salt eklenir tablolarda
  UPDATE/DELETE 51392, `ChangePlans` sabit sütun/silme 51394, ikinci onay 2627, ayrım tetikleyicisi 51393 (planlayan,
  önizleyen, düzenleyen, yanlış özet, başka plan), API rolüyle DELETE 229 ve depo için gereken her iznin varlığı.
