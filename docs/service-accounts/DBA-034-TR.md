# Servis Hesapları — 034 (plan kanıtı) için DBA eki

Tarih: 2026-10-12. Kime: kurulu TEST veritabanından sorumlu DBA. Durum: **hazırlık belgesi; kurulu sisteme bu belgedeki hiçbir
şey uygulanmadı.** Uygulama için proje sahibinin ayrı onayı gerekir. `DBA-033-TR.md` önce gelir. **Sıra: 029 → 030 → 031 →
032 → 033 → 034.** Yedeği DBA kendi sürecinde alır; bu belge yedek komutu içermez. Tasarım: `CHANGE-PLAN-DESIGN.md`
("Kontrol listesi, kanıt ve kapanış").

## Ne uygulanacak

| Sıra | Dosya (repo içinde) | Ne yapar | Ön koşul |
|---|---|---|---|
| 1 | `sql/migrations/034-service-account-plan-evidence.sql` | Mevcut `svcacct.Evidence` tablosundaki `CK_SaEvidence_Owner` kısıtını **aynı adla** yeniden kurar: izinli sahip türlerine `'ChangePlan'` eklenir (gMSA değişiklik planının kanıt dosyaları). `WITH CHECK`: mevcut satırlar doğrulanır, kısıt güvenilir kalır. Başka tablo, sütun, satır, tetikleyici, rol veya izin değişmez | 025 ve 033 uygulanmış |

İzin betiği **yok**: API rolünün `Evidence` üzerindeki mevcut izinleri yeterli. 034 tek işlemdir; kısıtın şimdiki tanımı 025'in sekiz
sahip türü değilse veya `'ChangePlan'` zaten varsa (tekrar) **51396** ile hiçbir şey değiştirmeden durur. Eski API sürümü
`'ChangePlan'` yazmaz; yeni API sürümü 034 olmadan plan kanıtı yüklemesinde 503 `ServiceAccountChangePlanEvidenceNotInstalled` döner,
diğer plan işlevleri çalışır.

## 1. Ön kontrol (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=pre -i <repo>\tests\sql\service-accounts\sa-034-dba-checks.sql
```

Beklenen: `OnKosul_033 = var`, `KanitKisiti = eski`, `SahipTuru = 8`. Biri farklıysa durun.

## 2. Uygulama

```powershell
cd <repo>\sql\migrations
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i 034-service-account-plan-evidence.sql
```

Çıkış kodu 0 olmalı.

## 3. Uygulama sonrası doğrulama (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=post -i <repo>\tests\sql\service-accounts\sa-034-dba-checks.sql
```

Beklenen: `KanitKisiti = yeni`, `SahipTuru = 9`, `GuvenilmeyenKisit = 0`, `PlanKaniti = 0`. 034'ü bir kez daha çalıştırmak
**51396 ile reddedilmelidir**.

## 4. Geri dönüş

1. **Birincil yol:** DBA'nın uygulamadan önce aldığı kendi yedeğinden dönüş.
2. **Yalnız hiç plan kanıtı yüklenmediyse:** eski kısıt geri kurulur. Plan kanıtı varsa kısıtı bırakın (dosyalar onay ve uygulama
   kanıtıdır; eski API sürümü onları yazmaz ve okumaz):

   ```sql
   SET XACT_ABORT ON;
   BEGIN TRANSACTION;
   IF EXISTS (SELECT 1 FROM svcacct.Evidence WHERE OwnerEntityType = 'ChangePlan') THROW 51397, 'Plan evidence is stored; keep the 034 check.', 1;
   ALTER TABLE svcacct.Evidence DROP CONSTRAINT CK_SaEvidence_Owner;
   ALTER TABLE svcacct.Evidence WITH CHECK ADD CONSTRAINT CK_SaEvidence_Owner CHECK (OwnerEntityType IN ('Account','Request','Action','Communication','Finding','Handover','Transition','Ownership'));
   COMMIT TRANSACTION;
   ```

## Sentetik kopyada ne denendi (2026-10-12, LocalDB, gerçek TEST veritabanında DEĞİL)

- `sa-sql-harness.ps1 -ThroughMigration 33` ile 033'te bırakılmış kopya: ön kontrol `var / eski / 8`; 034 çıkış 0; son kontrol
  `yeni / 9 / 0 / 0`; 034 tekrarı 51396 ile reddedildi.
- Geri dönüş komutu `COMMIT` yerine `ROLLBACK` ile denendi: işlem içinde kısıt eski haline döndü, geri alınınca yeni kaldı. Plan
  kanıtı olan kopyada koruma 51397 ile durdurdu.
- Kısıtı yedi türe indirilmiş (beklenmeyen tanım) kopyada 034, 51396 ile durdu ve dış işlemle birlikte hiçbir şey değişmedi.
- Tam harness (001–034) her çalıştırmada `sa-034-guards.sql` ile dener: kısıt güvenilir, `'ChangePlan'` sahipli kanıt kabul,
  `'ChangePlanItem'` ve bilinmeyen tür 547, kanıt satırı değiştirilemez (51304).
