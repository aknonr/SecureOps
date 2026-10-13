# Servis Hesapları — 037 (web kullanım taraması işleri) için DBA eki

Tarih: 2026-10-13. Kime: kurulu TEST veritabanından sorumlu DBA. Durum: **hazırlık belgesi; kurulu sisteme bu belgedeki hiçbir
şey uygulanmadı.** Uygulama için proje sahibinin ayrı onayı gerekir. Yedeği DBA kendi sürecinde alır; bu belge yedek komutu
içermez. Sözleşme: `USAGE-SCAN-JOB-CONTRACT.md`; karar: ADR-0024 R2-4 ve sahip kararı 2026-10-13.

**Sıra:** 030 (kullanım taramaları) uygulanmış olmalı. 035 ve 036 paralel dallara ayrılmıştır; 037 onlara bağlı değildir ve
yalnız 025 ile 030'u ister. Sürüm aracı (`scripts/release/Get-ReleaseSqlPlan.ps1`) 037'yi henüz tanımıyor (açık konu, Codex).

## Ne uygulanacak

| Sıra | Dosya (repo içinde) | Ne yapar | Ön koşul |
|---|---|---|---|
| 1 | `sql/migrations/037-service-account-usage-scan-jobs.sql` | Üç **yeni** tablo: `svcacct.UsageScanJobs` (iş başlığı, durum, kira), `UsageScanJobAccounts` (işin hesapları; süzülmüş benzersiz dizin ile hesap başına tek etkin iş), `UsageScanJobServers` (planlanan her sunucunun canlı durumu ve doğrulanmış belgesi). Üç koruma tetikleyicisi (silme yok; biten iş/sunucu değişmez). Mevcut tablo, kısıt, satır, rol veya izin değişmez | 025 ve 030 uygulanmış |
| 2 | `sql/pending/service-accounts/SA-008-API-permissions.sql` | API rolüne üç yeni tabloda SELECT, INSERT, UPDATE | `SA-API-permissions.sql` |
| 3 | `sql/pending/service-accounts/SA-008-Worker-permissions.sql` | Worker rolüne iş tablolarında SELECT, UPDATE; 030 kanıt tablolarında SELECT, INSERT; kapsam tablolarında SELECT; `History` SELECT/INSERT; `audit.AuditLog` INSERT | `SA-Worker-permissions.sql`, 030 |

Hiçbir rolde DELETE yok. Kanıt (030 tabloları) eklenip değiştirilmeyen kayıt olarak kalır; iş tabloları yalnız tarama sürerken
değişen işletim durumudur. 037 tek işlemdir; 025/030 yoksa **51410** ile, tekrar çalıştırılırsa **51410** ile hiçbir şey
değiştirmeden durur. Eski API sürümü bu tabloları okumaz; yeni API sürümü 037 olmadan web taramasını "kurulu değil" (503) diye
kapalı gösterir, diğer işlevler çalışır.

## 1. Ön kontrol (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=pre -i <repo>\tests\sql\service-accounts\sa-037-dba-checks.sql
```

Beklenen: `OnKosul_030 = var`, `TaramaIsleri = yok`. Farklıysa durun.

## 2. Uygulama

```powershell
cd <repo>\sql\migrations
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i 037-service-account-usage-scan-jobs.sql
cd <repo>\sql\pending\service-accounts
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i SA-008-API-permissions.sql
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i SA-008-Worker-permissions.sql
```

Her biri çıkış kodu 0 olmalı.

## 3. Uygulama sonrası doğrulama (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=post -i <repo>\tests\sql\service-accounts\sa-037-dba-checks.sql
```

Beklenen: `Tablo = 3`, `Tetikleyici = 3`, `TekCalisanDizin = 1`, `GuvenilmeyenKisit = 0`, `Is = 0`. 037'yi bir kez daha
çalıştırmak **51410 ile reddedilmelidir**.

## 4. Geri dönüş

1. **Birincil yol:** DBA'nın uygulamadan önce aldığı kendi yedeğinden dönüş.
2. **Yalnız hiç iş kaydı yoksa** (`Is = 0`) tablolar kaldırılabilir; iş kaydı varsa bırakın (iş geçmişi ve kanıt bağlantıları
   denetim izinin parçasıdır, 030 kanıtı zaten ayrı ve kalıcıdır):

   ```sql
   SET XACT_ABORT ON;
   BEGIN TRANSACTION;
   IF EXISTS (SELECT 1 FROM svcacct.UsageScanJobs) THROW 51415, 'Usage scan jobs are recorded; keep the 037 tables.', 1;
   DROP TABLE svcacct.UsageScanJobServers;
   DROP TABLE svcacct.UsageScanJobAccounts;
   DROP TABLE svcacct.UsageScanJobs;
   COMMIT TRANSACTION;
   ```

   İzinler tabloyla birlikte kalkar; roller kalır.

## Ne denendi / denenmedi

- **Denenmedi (2026-10-13):** bu dal Linux bulutunda yazıldı; LocalDB/SQL Server yok. 037, `sa-037-guards.sql`, DBA kontrolleri
  ve `ServiceAccountUsageScanJobSqlTests` **hiç çalıştırılmadı**; yalnız C# derlemesi doğrulandı. İlk deneme koordinatörün
  Windows makinesinde `sa-sql-harness.ps1` (001–034 + 037, guard dahil) ile, sonra kurulu sistemin kopyasında yapılmalıdır.
- Harness: `-ThroughMigration 36` 037'yi dışarıda bırakır (kurulu sistem benzeri); varsayılan 37 tümünü uygular, SA-008 tekrarının
  reddini, rol betiklerini ve `sa-037-guards.sql`'i (tek etkin iş 2601, belge yalnız Ok ile 547, biten sunucu/iş/hesap
  değişmez 51411–51413, silme yok) dener.
