# Service Accounts — Current State (start here)

Short on purpose: current state, open work, how to verify. Dated session records and older evidence are in
[PROGRESS-HISTORY.md](PROGRESS-HISTORY.md) (newest first); read it only when a question needs the history.
Design: [README.md](README.md) · rules: [SPEC.md](SPEC.md) · Windows rows: [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md).

## Context

- Branch `feature/service-accounts-scope-import-ux-20261003` (draft PR #11), based on master `77bcfab`. The Linux cloud review
  branch `claude/sa-review-20261005` (`0ecc728`) is merged into it (`525b5c6`, no rebase).
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

**Review merge (2026-10-05):** cloud review — value-guard bypasses closed (quoted keys, token/secret/api-key, full-width and
zero-width spellings; text over 4 096 characters refused, no more 500), `\z` instead of `$`, searched-name rule with
`accountAmbiguousInScan`, "not found" only for fully scanned servers, every `MudExpansionPanel` replaced by `SaDisclosure`,
non-blocking 15-character gMSA name hint. Windows follow-ups: focus ring inside `SaDisclosure`; a second scan decision is
409 `ServiceAccountUsageScanAlreadyDecided` and the stale decision box closes; invisible and bidi characters stripped from
stored file names; the combine script refuses password assignments in values too; MudTabs headers made keyboard reachable
(`SaTabTitle`, account and admin tabs) and every module page gives MudBlazor buttons a focus ring; SQL test for the
searched-name rule.

## Verified (Windows workstation, 2026-10-05, after the review merge, HEAD `9629101`, SDK 9.0.317, LocalDB)

Build 0/0 · unit 1883 passed, 1 intentional skip · integration 303 passed / 113 skipped (SQL-gated and opt-in) · format clean ·
harness 001–030 exit 0 on fresh `SecureOps_SaMerge1005b`, module SQL 58/58 on the first run, diagnostics only the 4
intentional `Number=51091` probes and 3 synthetic provider failures,
no `Number=1205` (also 57/57 earlier on fresh `SecureOps_SaMerge1005a` before the new SQL test) · the new searched-name SQL
test fails on the code before `c2601a7` and passes after it · combine value guard under Windows PowerShell 5.1 · local Demo
(identity bridge, not OIDC) keyboard only: every account tab, admin tab and all 13 `SaDisclosure` summaries reached with Tab,
Enter/Space switch tabs and open sections, focus ring visible; 390 px and 640 px (emulated 200 %), light and dark, all forms
open: no horizontal scroll; gMSA hint (`role="status"`, submit stays enabled, contrast 4.92:1 light / 7.40:1 dark on the
form surface); second decision 409 end to end. Older runs (2026-10-04 Windows and Linux, 2026-10-05 before the merge):
[PROGRESS-HISTORY.md](PROGRESS-HISTORY.md).

## Open work

1. **Merge of PR #11** — owner approved Claude merging once tests are complete; the owner answers "Açık satırlarla merge
   edilsin mi?" first. Open rows: 1–38 blocked (TEST OIDC/AD), 39–61 need TEST users, 58/75 the real `.bak`, 67–68 a lab server.
2. **Row 58 / 75:** 029 and 030 on a restored copy of the installed TEST database — waiting for the owner's `.bak`
   (synthetic-copy rehearsal done; DBA note: [DBA-029-030-TR.md](DBA-029-030-TR.md)). 029, 030 and `SA-004-API-permissions.sql`
   need the owner's approval before any installed system.
3. **Row 64:** open the synthetic snapshot PDF in Edge and Adobe; Properties → Fonts shows both fonts as embedded.
4. **Rows 60–61:** browser check of the landing redirect and the three `sa-*` roles with synthetic TEST users.
5. **Rows 1–38:** blocked until approved TEST OIDC identities (and TEST AD for 29–38) are available.
6. **Rows 67–68 (lab server):** collector where the synthetic account runs a service, a scheduled task and an app pool with a
   stored password, also as a non-administrator. Check there whether the collector **silently skips scheduled tasks it
   cannot see without administrator rights** (it would then print `Success` although the task list is incomplete; on the
   owner's workstation, not elevated, it printed `Success`).
7. **Owner decision — scan size on the account page:** the detail shows every matched component of the 10 newest scans
   (up to 10 000 per scan) without a limit or paging. Proposal: a limit with the total count and paging (API additions only).
8. **Owner decision — requested gMSA name (migration 031):** `CreateWorkRequest` / `TransitionUpdateRequest` have no field for
   the gMSA name a conversion will use, so the 15-character check exists only at account registration. Needs 031 + API.
9. Low priority: participant basis is not re-checked inside the write transaction (same pattern as the rest of the module);
   the warning colour is 4.37:1 on the page background (4.92:1 on the form surface where it is used); secret words written
   with Cyrillic look-alike letters are not caught (the guard is against accidental leaks, not a deliberate attacker); at
   390 px a focused tab header can be partly outside the MudTabs scroll strip (never fully hidden); the app shell brand link
   and "Hesap menüsü" have no focus ring (outside the module).

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
