# Service Accounts — Current State (start here)

Short on purpose: current state, open work, how to verify. Dated session records and older evidence are in
[PROGRESS-HISTORY.md](PROGRESS-HISTORY.md) (newest first); read it only when a question needs the history.
Design: [README.md](README.md) · rules: [SPEC.md](SPEC.md) · Windows rows: [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md).

## Context

- PR #11 (scope setup, guided import, reports, usage scan) is merged into master (`fb87c20`, 2026-10-05); its branch state is
  in PROGRESS-HISTORY.md. Current branch `feature/service-accounts-gmsa-name-scan-paging-20261005` from master `fb87c20`.
- Owner decision 2026-10-03: Claude owns the module end to end (backend/API/UI, migration numbering, Windows
  verification); the owner merges and deploys. Rules: `AGENTS.md` + `CLAUDE.md`.
- SDK 9.0.317 exactly (`global.json`, `rollForward: disable`, no `LangVersion` override). On the owner's workstation it is
  a user-local install: `$env:LOCALAPPDATA\Microsoft\dotnet` (Program Files has 9.0.318/10.x only).
- Synthetic data only (`syn.*`, `SYN_*`); no corporate SQL/AD/Jira/SMTP writes; nothing applied to the installed TEST
  system without the owner's explicit approval; no write/automatic-conversion code (Phase 8); report in Turkish.
- Push on the owner's workstation: Git Credential Manager holds two GitHub accounts, so name the owner's:
  `git -c credential.https://github.com.username=aknonr push origin <branch>`.

## Ops-research UI work (2026-10-06, separate branches from master, not merged)

Order c, b, a, d from `ops-research/05-yol-haritasi.md`. **c merged** (PR #14, `87df0c3`):
"Sıradaki adım" card on the account page, computed in the UI from the loaded detail only (no API change); missing data reads
"bilgi eksik", never "not used"; a step the caller may not take reads "başkasından bekleniyor".

## Multi-account scan upload (branch `feature/service-accounts-multi-account-scan-20261006`, from master `50c0528`)

- `POST usage-scans` (multipart `file`, `runStatement`, 1–20 distinct `accountIds`), no migration: file validated once (secret
  guard first), then each account on its own (scope, responsible basis, searched by the file); outcomes Attached /
  AlreadyAttached / NotInScan / Ambiguous / Unavailable (no name out of scope) / Failed. UI: list selection → "Tek tarama
  dosyasını bu hesaplara bağla". Rules: SPEC "Usage scans" 9, ADR-0027 §4. Windows row 86.
- Verified 2026-10-06: build 0/0, unit 1919 + 1 intentional skip, integration 361 / 62 skipped with module SQL on fresh
  `SecureOps_SaBatch1006a` (harness 001–031 exit 0), format clean, OpenAPI additions only (+261/−0). One integration run of five
  had one failure that did not repeat; its name and cause were not captured (Open work 9).
- Review fixes 2026-10-07 (two independent reviews of PR #18): per-account failure boundary, names from the scope read (no
  separate name query); per-server account list must equal the bundle's set; scope and basis re-checked inside the link's
  write transaction (both routes); `ServiceAccount.UsageScanBatchRefused` audit (reason and counts only); OpenAPI form
  schema for both uploads; UI: error in the form with retry, old answer cleared, focusable `aria-disabled` send button with
  its reason, `aria-expanded` toggles, badge "Aranmamış", "Hesap bilgisi okunamadı" for an unread failed row. Verified:
  build 0/0, format clean, unit 1933 + 1 intentional skip; before merging master `304788f`: integration 365 / 62 skipped on
  fresh `SecureOps_SaFix1007b` (harness 001–031 exit 0; the 030-copy test 3/3 on `SecureOps_SaFix1007b030`); after the
  merge: five full runs, 419 / 67 skipped in four (two on fresh databases), one failed run (Open work 9). Windows row 87.
  Not run: module SQL as the least-privilege runtime principal (`SECUREOPS_SA_SQL_RUNTIME_CONNECTION`).
- PR #12 (031 + scan paging) is merged (`50c0528`); the section below is its record.

## On this branch (done, 2026-10-05)

- **Requested gMSA name (migration 031, SA-005).** `RequestedGmsaName` on gMSA requests (create, update, reasoned clear) and
  on transitions; one shared 15-character rule (`ServiceAccountGmsaName`) for the registration hint, the new hint and the
  server; the server refuses a longer name or a name on other work; history and the report list ("İstenen gMSA adları")
  carry it; before 031 the screens say so and a name is refused with nothing written. Rules: SPEC "Requested gMSA name".
  031 adds two nullable columns, refuses replay (51370) and needs no new grant. DBA note: [DBA-029-030-TR.md](DBA-029-030-TR.md).
- **Usage scan paging.** The account page carries the newest 5 scans and 25 matches per role (undecided first); counts,
  coverage and outcomes are aggregated in SQL over every match; read-only GET pages for older scans and further matches
  under the detail's scope rule; "Yalnız karar bekleyenleri göster"; server totals in the tab title. SPEC "Usage scans" 8.
- **Fixes found while verifying:** a deadlocked scoped read (list/export, detail, report facts) runs once more instead of a
  503 (`33c6500`); keyboard dismissal of a scan match; the report trend table no longer clipped at 390 px (`ecb1294`).
- Tools: harness 001–031 (`-ThroughMigration 30` stands in for an installed 030 system), `sa-031-copy-rehearsal.ps1`.

## Verified (Windows workstation, 2026-10-05, SDK 9.0.317, LocalDB)

Build 0/0 · unit 1901 passed, 1 intentional skip · integration 303 passed / 117 skipped (SQL-gated and opt-in) · format clean ·
OpenAPI snapshot additions only · harness 001–031 exit 0 on fresh `SecureOps_SaGmsaPage1005c`, module SQL 62/62 on the first
run, no `Number=1205` (the first fresh run, `…1005b`, had one; root cause and fix in WINDOWS-ACCEPTANCE row 79) · 031 rehearsed
on two copies at 030 (rows 80) · new binaries before 031 (row 81) · local Demo (identity bridge, not OIDC): requested name with
warning, server refusal and saved value, transition, report list, scan paging with keyboard, 390 px light and dark
(rows 82–85).

## Open work

1. **Owner approval for 031** on any installed system (after 029/030 and `SA-004-API-permissions.sql`): DBA note above.
2. **Row 58 / 75 / 80:** 029, 030 and 031 on a restored copy of the installed TEST database — waiting for the owner's `.bak`;
   `sa-031-copy-rehearsal.ps1` runs on such a copy without `-SeedSynthetic`.
3. **Row 64:** open the synthetic snapshot PDF in Edge and Adobe; Properties → Fonts shows both fonts as embedded. The new
   report section was not opened in a generated XLSX/PDF (unit test only).
4. **Rows 60–61:** browser check of the landing redirect and the three `sa-*` roles with synthetic TEST users.
5. **Rows 1–38:** blocked until approved TEST OIDC identities (and TEST AD for 29–38) are available.
6. **Rows 67–68 (lab server):** collector where the synthetic account runs a service, a scheduled task and an app pool with a
   stored password, also as a non-administrator; check whether it silently skips scheduled tasks it cannot see.
7. Not re-walked in the browser: the keyboard dismissal after its fix (row 85a, source test only) and the request-update
   name field with "temizle" (SQL tests only).
8. Low priority: the usage-scan attach (both routes) now re-checks scope and basis inside its write transaction
   (2026-10-07); the module's other writes still check before their transaction (unchanged, not this branch); the warning colour is 4.37:1 on the page background (4.92:1 on the form surface where it is used); secret words written
   with Cyrillic look-alike letters are not caught; at 390 px a focused tab header can be partly outside the MudTabs scroll
   strip; the app shell brand link and "Hesap menüsü" have no focus ring (outside the module); the server list of a scan
   (up to 500 rows) is not paged (it sits in a closed section).
9. **Unexplained integration failure (2026-10-06):** on the multi-account scan branch one of five full integration runs
   (module SQL on, `SecureOps_SaBatch1006a`) reported 1 failed / 360 passed; the next four runs passed 361/361 and the failing
   test's name and message were not captured. Cause unknown. Next: run the integration suite repeatedly with a trx logger
   and `SECUREOPS_SA_SQL_DIAGNOSTICS`, record the test name and SQL number, then decide.
   2026-10-07 (trx captured, five runs after merging master `304788f`): the first run on fresh `SecureOps_SaFix1007d` took
   3 m 43 s; `ServiceAccountImportSqlTests.ConfirmingOwnershipInImport_RequiresAssignCapability` passed but took 2 m 16 s
   (normally 0.5 s) while holding the import-commit gate, and two writes waiting behind it failed:
   `ServiceAccountWorkflowSqlTests.ClosureVerification_DuringImportCommit_WaitsInsteadOfDeadlocking` (SqlClient timeout)
   and `ManualAccounts_StayProvisional_EvenWithATypedDomain` (persistence unavailable). system_health had no deadlock in
   that window; the gated access-registration tests were skipped. The next four runs (one on fresh `…1007e`) passed
   419/419, about 1 min each. Also found and fixed that day: the new middle-account test held a raw row lock outside the
   write gate and deadlocked (1205) with a parallel serializable import commit; its class now runs in the serial
   "Service Accounts write gate" collection. Cause of the slow import commit still unknown.

## Decisions (owner, 2026-10-04)

- ADR-0024 read-only discovery: shelved on 2026-10-04; **Revision 2 accepted as an owner decision on 2026-10-06**
  (read-only JEA scan; no server writes, ADR-0028 stays Proposed). Bilgi Güvenliği and server-owner approval are NOT
  obtained and are required before a pilot; the Worker access path (direct WinRM+Kerberos or BeyondTrust) is open.
  Replaced for now by the person-run scan of ADR-0027 (no JEA, no Worker). A product-side fan-out over the default WinRM
  endpoint would be an exception to AGENTS.md rule 3 and is not built.
- JEA allow-list finding (raw `Get-WebConfigurationProperty`/`Get-Content` can return IIS-stored service account
  passwords / any file): decision with the owner and Bilgi Güvenliği; needs its own ADR.
- Automatic password or gMSA change: Phase 8, separate ADR and approval; until then the team changes by hand and the
  module keeps plan, OR/OCO/Jira reference, evidence and verification.
- PDF font subsetting (~350 KB per PDF today): later, only if size becomes a problem.

## Verify each change

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"; $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet build SecureOps.sln
dotnet test tests\SecureOps.Tests.Unit --no-build
dotnet test tests\SecureOps.Tests.Integration --no-build
dotnet format SecureOps.sln --verify-no-changes
powershell -NoProfile -File tests\sql\service-accounts\sa-sql-harness.ps1 -DatabaseSuffix <new>   # 001-031; then WINDOWS-ACCEPTANCE section 3
```

OpenAPI snapshot only when the API changes (`SECUREOPS_UPDATE_OPENAPI=1`, additions only). Linux: module SQL through
`tests/sql/service-accounts/sa-sql-harness.sh` with a disposable SQL Server container.
