# Service Accounts — Current State (start here)

Short on purpose: current state, open work, how to verify. Dated session records and older evidence are in
[PROGRESS-HISTORY.md](PROGRESS-HISTORY.md) (newest first); read it only when a question needs the history.
Design: [README.md](README.md) · rules: [SPEC.md](SPEC.md) · Windows rows: [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md).

## Context

- Branch `feature/service-accounts-scope-import-ux-20261003` (draft PR #11), based on master `77bcfab`.
- Owner decision 2026-10-03: Claude owns the module end to end (backend/API/UI, migration numbering, Windows
  verification); the owner merges and deploys. Rules: `AGENTS.md` + `CLAUDE.md`.
- SDK 9.0.317 exactly (`global.json`, `rollForward: disable`, no `LangVersion` override). On the owner's workstation it is
  a user-local install: `$env:LOCALAPPDATA\Microsoft\dotnet` (Program Files has 9.0.318/10.x only).
- Synthetic data only (`syn.*`, `SYN_*`); no corporate SQL/AD/Jira/SMTP writes; nothing applied to the installed TEST
  system without the owner's explicit approval; no write/automatic-conversion code (Phase 8); report in Turkish.
- Push on the owner's workstation: Git Credential Manager holds two GitHub accounts, so name the owner's:
  `git -c credential.https://github.com.username=aknonr push origin <branch>`.

## On this branch (done)

One-time first scope grant (ADR-0026, migration 029); approved-user picker; no-scope explanation on list/detail/import;
guided import; page guides; role bundles ([TEAM-ROLE-SETUP-TR.md](TEAM-ROLE-SETUP-TR.md)) and the
Service-Accounts-only landing redirect from `/`; report charts on screen, XLSX (native) and PDF (vector page); XLSX
"Yönetici özeti"; Liberation Mono (OFL) embedded in PDFs; serializable name-check deadlock fixed (`UPDLOCK`);
harness `-SkipRoleScripts` exits 0; Windows-only execution-policy race in the unit tests fixed (module PowerShell tests
in a non-parallel collection). ADR-0024 discovery module exists as PROPOSED only (shelved).

**Usage scan (2026-10-04, ADR-0027, migration 030):** a person runs the read-only collector
(`scripts/powershell/Get-ServiceAccountUsage.ps1`) under their own authority, combines the results on their workstation
(`Invoke-ServiceAccountUsageScan.ps1 -CombinePath`) and uploads the file on the account ("Kullanım taraması" tab). Strict
parser with a secret guard (file refused, nothing stored), append-only scan tables, honest per-server outcomes, per-match
human decision into a usage, derived gMSA evidence (never verification). Operator guide:
[USAGE-SCAN-TR.md](USAGE-SCAN-TR.md).

## Verified (Windows workstation, 2026-10-04, SDK 9.0.317, LocalDB)

Build 0/0 · unit 1741/1741 five runs in a row after the race fix · integration 301 passed / 107 SQL-gated skipped ·
format clean · module SQL 50/50 on a fresh 001–029 database at the first run, no `Number=1205` · harness exit 0 ·
desktop Excel opens the synthetic snapshot XLSX without repair, charts equal "Grafik verisi" · PDF embeds
`LiberationMono` and `LiberationMono-Bold` · ADR-0024 pure helpers pass under Windows PowerShell 5.1.

## Verified (Linux cloud, 2026-10-04, usage scan, SDK 9.0.317, SQL Server 2022 container)

Build 0/0 · unit 1803/1805 (the 2 failures also fail on `e6b3e03`: audit path validator and a Windows-only execution
policy test) · module SQL 53 passed + 2 LocalDB-only on a fresh 001–030 database, also as an `svcacct_api_runtime`-only
user, no `Number=1205` · other integration failures are the 7 SkiaSharp announcement tests that also fail on `e6b3e03` ·
format clean on a CRLF copy (Linux checkouts are LF; `.editorconfig` wants CRLF) · OpenAPI additions only · scan tab
checked on a static render at 390/640/1280 px light/dark and by keyboard. Not run on Windows: rows 67–75.

## Open work

1. **Merge of PR #11** — owner approved Claude merging once tests are complete. Still open before merge: rows 39–61 need
   TEST users (role and scope assignment through the product); the owner decides whether to merge with them pending.
2. **Row 58:** 029 on a restored copy of the installed TEST database — waiting for the owner's `.bak`.
3. **Row 64:** open the synthetic snapshot PDF in Edge and Adobe; Properties → Fonts shows both fonts as embedded.
4. **Rows 60–61:** browser check of the landing redirect and the three `sa-*` roles with synthetic TEST users.
5. **Rows 1–38:** blocked until approved TEST OIDC identities (and TEST AD for 29–38) are available.
6. **Rows 67–75 (usage scan):** collector under Windows PowerShell 5.1 on a TEST/lab server, combine on a workstation,
   030 on LocalDB and on the `.bak` copy, browser upload/decision/participant flows. 030 and `SA-004-API-permissions.sql`
   need the owner's approval before any installed system.
7. Module-wide: MudBlazor 6.16 expansion-panel headers take no keyboard focus (seen while checking the scan tab, which now
   uses `<details>`); the other module panels still use them.

## Decisions (owner, 2026-10-04)

- ADR-0024 read-only discovery: **shelved** (Bilgi Güvenliği approval and the 10–15 server pilot are with the owner).
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
powershell -NoProfile -File tests\sql\service-accounts\sa-sql-harness.ps1 -DatabaseSuffix <new>   # 001-030; then WINDOWS-ACCEPTANCE section 3
```

OpenAPI snapshot only when the API changes (`SECUREOPS_UPDATE_OPENAPI=1`, additions only). Linux: module SQL through
`tests/sql/service-accounts/sa-sql-harness.sh` with a disposable SQL Server container.
