# Servis Hesaplari SQL bekleme incelemesi - 2026-10-07

## 1. Karar

Kok neden kanitlanmadi; acik is 9 kapanmadi. Test duzenegi/uretim ayrimi yapilamadi.
Tahmini duzeltme, ek retry, timeout artisi, indeks, koleksiyon serilestirmesi veya modul/UI degisikligi yok.
Dal: `fix/sa-intermittent-lock-20261007`; master tabani `13b57038eff41134bc70f92953c18e1ca5a9e9c1`.

Degisiklikler yalniz tanilama ve kayit duzeltmesi:

- `tests/sql/service-accounts/sa-lock-replay.ps1`: her tur yeni LocalDB, tam suit/TRX, 250 ms DMV kilit/bekleme orneklemesi,
  test-owned Extended Events deadlock oturumu ve tampon saglik kaydi; kurumsal opt-in'ler aciksa reddeder.
- `tests/SecureOps.Tests.Integration/ServiceAccounts/ServiceAccountSqlTraceAttribute.cs`: istege bagli
  `SECUREOPS_SA_SQL_TRACE`; test adi, SPID, baglanti/operasyon kimligi, SQL hash'i ve thread-pool sayaclari.
  SQL metni, parametreler ve baglanti dizesi yazilmaz; havuz ve test siralama ayarlari degismez.
- `tests/sql/service-accounts/sa-lock-summary.ps1`: TRX sayaclari ve UTC/SPID uzerinden bekleyen/kilitleyen test eslestirmesi.
- `PROGRESS.md`: onceki test suresinden commit/kilit sahibi sonucu cikarilamayacagi aciklandi.

## 2. Kanit

Windows, SDK **9.0.317**, yalniz `(localdb)\SecureOpsResourcesV1`, sentetik veri. Her suit icin yeni `SecureOps_SaLock1007AR01..10`
ve `SecureOps_SaLock1007BR01..10`; son kosucu kontrolu icin `SecureOps_SaLock1007CR01`.
Mevcut harness ile 001-031 ve modul rol betikleri. SQL runtime-principal opt-in kapali.

| Dogrulama | Sonuc |
|---|---|
| Tam integration, tanilama kodu eklenmeden 10 tur | Her tur 432 basarili / 68 atlanan / 0 hata |
| Tam integration, test/SPID eslestirmeli 10 tur | Her tur 432 basarili / 68 atlanan / 0 hata, process exit 0 |
| Son kosucu kontrolu, bosluk iceren kanit yolu | Tam suit: 432 basarili / 68 atlanan / 0 hata; trace guard ve cleanup gecti |
| Toplam | 9.072 basarili test calistirmasi; 21 ayri yeni veritabani |
| Solution build | 0 uyari / 0 hata |
| Tam unit suit | 1.961 basarili / 1 bilincli atlama / 0 hata |
| Repository-wide format | `dotnet format SecureOps.sln --verify-no-changes`: exit 0; ilk kapida yeni dosyanin CRLF gereksinimi duzeltildi |
| Deadlock yakalama kontrolu | Ayri `SecureOps_SaLock1007Canary01`: SQL 1205, 1 filtrelenmis grafik, 0 kirpilma/kayip |

20 turdaki test sureleri:

| Test (sinif/kisa ad) | En az - en cok, saniye | Hata |
|---|---|---|
| Import / `ConfirmingOwnershipInImport_RequiresAssignCapability` | 0.117 - 0.486 | 0 |
| Import / `ImportCommit_WaitsForAnInFlightModuleWrite_OnTheGateNotOnRows` | 0.196 - 0.807 | 0 |
| Workflow / `ClosureVerification_DuringImportCommit_WaitsInsteadOfDeadlocking` | 0.205 - 0.635 | 0 |
| PersistedAccess / `ProtectedAdmin_HasModuleOperations_ButNeedsExplicitIndependentScope` | 0.126 - 3.196 | 0 |

Gercek kilit eslestirmesi: B/round-01, `2026-10-07T00:41:24.8328544Z`, SPID 71 -> 70,
`MultipleRequests_ActionVerification_AndClosureRules` -> `CoordinationList_NewPeriodObservations_AbsenceIsNotClosure_AndOlderPeriodNeverMovesLatestBack`,
`LCK_M_S`, `APPLICATION: ... [svcacct:import-commit]`, **1.322 saniye**. Bu kaydedilen en uzun farkli-test APPLICATION beklemesidir;
her iki test de gecti. Kuyruk zincirinin ara oturumlari da kaydedildi; ara blocker her zaman kok kilit sahibi degildir.

30 saniyelik uzun KEY beklemesi: `MiddleAccountFails_EarlierLinkStands_LaterAccountIsTried_AndTheAnswerIs200` kendi ikinci
oturumunda `PK_SaAccounts` uzerinde X kilidi tutuyor; ayni testin okuma oturumu S kilidi bekliyor. Her tur test gecti.
Bu kontrollu timeout ve audit-failure testlerinin SQL 51091 kayitlari beklenmeyen ariza sayilmadi.
Eslesmeli 10 turun ve son kosucu kontrolunun XE kayitlari: **0 deadlock, 0 kirpilma, 0 kayip**.
Goreve ait XE oturumlari sonunda kaldirildi.

Kanit kokleri (`C:\SecureOpsBuild\evidence\`):
- `sa-lock-baseline-20261007-01\analysis.json`, `round-*\integration.trx`, `blocking.jsonl`, `sql-errors.log`.
- `sa-lock-traced-20261007-01\analysis.json`, `manifest.json`, `round-*\test-sql.jsonl`, `correlated-waits.json`, `deadlock-health.json`.
- `sa lock runnercheck 20261007\summary.jsonl`, `round-01\integration.trx`, `test-sql.jsonl`, `deadlock-health.json`.
- `sa-lock-controls-20261007-01\result.json`, `deadlocks.xml`, `capture-canary.ps1` (kontrol, asil arizanin tekrari degil).
- `sa-lock-gates-20261007-01\unit.trx`; `sa-lock-format-20261007-02.log` (son tam format kapisi).
Ilk kosucunun `summary.jsonl` skipped=0 ve exitCode=null alanlari hataliydi; dogru sayilar TRX'ten `analysis.json` icine hesaplandi.
Ilk DMV tarih serilestirmesinin yerel saat kaymasi ozetleyicide duzeltildi; ikinci seri acik UTC ISO-8601 kullaniyor.

## 3. Engeller

Eski 136 saniyelik kosunun ham TRX/SQL bekleme zinciri bu incelemede bulunamadi. Test suresi tek basina hangi SQL isleminin
bekledigini veya hangi oturumun kilidi tuttugunu kanitlamaz. `ServiceAccountImportSqlTests.cs:451` testi commit cagirmiyor;
asagidaki `StageAsync` yardimcisi `StageImportAsync` cagiriyor. Bu kaynak `a7e40e7` ile master arasinda da ayni.
68 atlama kapsaminda ayri 030 veritabani ve diger modul/host/browser SQL opt-in'leri var. En az yetkili runtime SQL,
migration 032, Linux, kurulu TEST, kurumsal OIDC/AD/Jira/SMTP ve browser kabul yolculugu bu is kapsaminda dogrulanmadi.

## 4. Asgari Guvenli Sonraki Adim

Eski ham dosyalar mevcutsa SPID/UTC zincirini incele. Yeni ariza icin ayni kosucuyu yeni prefix/kanit diziniyle kullan;
once build, sonra tam tekrar. Mevcut dal kok neden duzeltmesi veya merge onayi degildir.

```powershell
dotnet build SecureOps.sln
powershell -NoProfile -File tests\sql\service-accounts\sa-lock-replay.ps1 -EvidenceDirectory C:\SecureOpsBuild\evidence\sa-lock-new -Prefix LockNew -Rounds 10
powershell -NoProfile -File tests\sql\service-accounts\sa-lock-summary.ps1 -EvidenceDirectory C:\SecureOpsBuild\evidence\sa-lock-new
```

## 5. Riskler

21 basarili tekrar aralikli hatanin yoklugunu kanitlamaz. 250 ms DMV orneklemesi daha kisa beklemeleri kacirabilir;
XE yakalama kontrolu deadlock kanitinin yolunu dogrular. Izleme ilave I/O ve zamanlama etkisi yaratabilir.
Indeks eksikligi, SERIALIZABLE aralik kilidi, paylasilan fixture veya managed zamanlama problemi bu eski arizanin
nedeni olarak gosterilmedi. Sentetik veritabanlari kanit icin tutuldu; kurulu sistemlere degisiklik yapilmadi.
