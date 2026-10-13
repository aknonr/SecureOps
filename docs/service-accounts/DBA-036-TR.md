# Servis Hesapları — 036 (olay kaydı) için DBA eki

Tarih: 2026-10-13. Kime: kurulu TEST veritabanından sorumlu DBA. Durum: **hazırlık belgesi; kurulu sisteme bu belgedeki hiçbir
şey uygulanmadı.** Uygulama için proje sahibinin ayrı onayı gerekir. Tasarım: `EVENT-GUIDANCE-DESIGN.md` §5.6. Yedeği DBA kendi
sürecinde alır; bu belge yedek komutu içermez.

**Sıra:** 029 → 030 → 031 → 032 → 033 → 034 → (035, OR-2; ayrı iş, henüz yok) → 036. 036 yalnız 025'e (`svcacct.Accounts`) bağlıdır
ve 035'e bağlı değildir; ama paket sırası numara sırasıdır. 035 yokken 036'yı ayrı uygulamak teknik olarak çalışır, sıra kararı
sahiptedir. Paketleme aracı (`scripts/release/Get-ReleaseSqlPlan.ps1`) 036'yı henüz tanımaz (sahibi ayrı günceller).

## Ne uygulanacak

| Sıra | Dosya (repo içinde) | Ne yapar | Ön koşul |
|---|---|---|---|
| 1 | `sql/migrations/036-service-account-events.sql` | İki yeni tablo: `svcacct.AccountEvents` (olay başlığı: hesap, olay türü, tarih, kanal, işlem anahtarı, özet karması) ve `svcacct.AccountEventItems` (olayın oluşturduğu/güncellediği kayıtların türü ve kimliği). Ekleme-yalnız: UPDATE/DELETE tetikleyiciyle **51411** ile reddedilir. Mail gövdesi, gönderen, ad, adres, ek ya da başka serbest metin sütunu **yoktur**. Mevcut tablo, kısıt, satır, rol veya izin değişmez | 025 uygulanmış |
| 2 | `sql/pending/service-accounts/SA-009-API-permissions.sql` | API rolüne (`svcacct_api_runtime`) iki tabloda yalnız `SELECT, INSERT`. UPDATE/DELETE verilmez. Rol üyesi atanmaz; Worker rolüne bir şey verilmez | 1 ve `SA-API-permissions.sql` |

Olayın ürettiği kayıtlar (yazışma, işlem bildirimi, referans, talep güncelleme/açma/kapatma, sahiplik önerisi, geçmiş, audit) API
rolünün **mevcut** izinleriyle yazılır; yeni izin gerekmez. 036 tek işlemdir; tablolardan biri zaten varsa (tekrar) **51410** ile
hiçbir şey değiştirmeden durur.

Eski API sürümü bu tablolara dokunmaz. Yeni API sürümü 036 olmadan çalışır: `GET me` yanıtında `eventGuidanceAvailable=false`,
olay önizleme/kayıt uçları 503 `ServiceAccountEventsNotInstalled` döner; diğer her şey çalışır.

## 1. Ön kontrol (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=pre -i <repo>\tests\sql\service-accounts\sa-036-dba-checks.sql
```

Beklenen: `OnKosul_025 = var`, `OlayTablolari = yok`, `ApiRolu = var`. Biri farklıysa durun.

## 2. Uygulama

```powershell
cd <repo>\sql\migrations
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i 036-service-account-events.sql
cd <repo>\sql\pending\service-accounts
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i SA-009-API-permissions.sql
```

İki komutun da çıkış kodu 0 olmalı.

## 3. Uygulama sonrası doğrulama (salt okunur)

```powershell
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -W -v Phase=post -i <repo>\tests\sql\service-accounts\sa-036-dba-checks.sql
```

Beklenen: `OlayTablosu = 2`, `EklemeYalnizTetikleyici = 2`, `GuvenilmeyenKisit = 0`, `MetinSutunu = 0`, `ApiIzni_SelectInsert = 4`,
`BaskaIzin = 0`, `OlaySatiri = 0`. 036'yı bir kez daha çalıştırmak **51410 ile reddedilmelidir**.

## 4. Geri dönüş

1. **Birincil yol:** DBA'nın uygulamadan önce aldığı kendi yedeğinden dönüş.
2. **Yalnız hiç olay kaydedilmediyse:** iki tablo kaldırılır (tetikleyiciler ve izinler tabloyla birlikte gider). Olay varsa
   tabloları bırakın: olay satırları audit niteliğinde iz kaydıdır; eski API sürümü onları okumaz ve yazmaz.

   ```sql
   SET XACT_ABORT ON;
   BEGIN TRANSACTION;
   IF EXISTS (SELECT 1 FROM svcacct.AccountEvents) THROW 51413, 'Account events are stored; keep the 036 tables.', 1;
   DROP TABLE svcacct.AccountEventItems;
   DROP TABLE svcacct.AccountEvents;
   COMMIT TRANSACTION;
   ```

   Olayın ürettiği kayıtlar (yazışma, işlem bildirimi vb.) mevcut tablolardadır ve geri dönüşte silinmez.

## Sentetik kopyada ne denendi (2026-10-13, LocalDB, gerçek TEST veritabanında DEĞİL)

- `sa-sql-harness.ps1 -ThroughMigration 34` ile 034'te bırakılmış kopya: ön kontrol `var / yok / var`; 036 ve izin betiği çıkış 0;
  son kontrol `2 / 2 / 0 / 0 / 4 / 0 / 0`; 036 tekrarı 51410 ile reddedildi.
- Geri dönüş komutu `COMMIT` yerine `ROLLBACK` ile denendi: boş kopyada işlem içinde tablolar kalktı, geri alınınca durdu; olay
  satırı olan kopyada koruma 51413 ile durdurdu.
- Tam harness (001–034 + 036) her çalıştırmada `sa-036-guards.sql` ile dener: iki tabloda UPDATE/DELETE 51411; aynı hesapta aynı
  işlem anahtarı 2627; büyük/küçük harf, sondaki boşluk, bilinmeyen tür, tarih biçimi ve büyük harfli karma 547; serbest metin
  sütunu yok; API rolü olay kaydının kullandığı her izne sahip, olay tablolarında UPDATE/DELETE yapamaz (229).
- Kurulu TEST'in (028 tabanı) gerçek API havuz hesabı ve gerçek SQL Server sürümü/harmanlaması ile **denenmedi**.
