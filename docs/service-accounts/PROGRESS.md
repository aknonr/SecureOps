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

## OR creation OR-2, Fake only (branch `feature/service-accounts-or-creation-20261013`, from master `e79ac0b` + ADR-0030 branch merged in, not merged)

- ADR-0030 stays **Proposed**; owner asked for OR-2 with a Fake adapter only (AGENTS rule 8). Migration **035** (SA-008): per plan account OR
  submissions (Sending written before any call, fixed content, allowed transitions only, one live submission per plan account and per OR number,
  idempotency key unique per account) + append-only events; `DBA-035-TR.md`, harness step and `sa-035-guards.sql`.
- `Infrastructure/ItsmRequests/`: `IItsmRequestCreator` (module-independent), shared insert rule (vendor `Success=true` + `InsertedId` decides; error
  text next to them is ignored), Fake and Disabled providers; `ItsmRequestCreation:Provider` defaults to Disabled, Fake only in Development/Demo/Test,
  `TuruncuHat` refused at startup (no real adapter). `ItsmRecords/` and `TuruncuHat*` files untouched.
- API (Work for commands, View for reads, whole-plan scope): preview, submit (fingerprint + idempotency key; OutcomeUnknown on any doubt, never resent),
  candidate lookup, link (verified only when the ADR-0031 lookup finds it), not-created, live tracking (ADR-0032; null = unknown). Requester = the signed-in
  user via `IItsmUserMapping.MatchAsync(Uid, LoginName)`; anything but Matched with a home location stops. OpenAPI: 7 paths + 15 schemas added only.
- UI: `SaPlanOperationalRecords` on the plan page after approval (preview → separate confirm, Belirsiz and reconciliation, tracking "bilinmiyor").
- Verified 2026-10-13 in the **Linux cloud session** (SDK 8.0.131 in a build copy; the repo pins 9.0.317, unavailable there): build 0/0; unit 2713 passed,
  3 failed (environment: SDK pin, Linux PowerShell, publish path; also failing on master there); API composition 15/15 and the OpenAPI snapshot test pass;
  integration 380 passed / 208 skipped / 7 failed (the same 7 `AnnouncementTests` codec tests fail on master there); format gate: 346 findings on both
  master and this branch under SDK 8 + CRLF, none in changed lines. 2026-10-14 on Windows LocalDB (SDK 9.0.317), master `6f6b472` merged in:
  harness 001–037 with all guards passed (two 035 guard expectations fixed, no guard relaxed), SA SQL suite serial 159 passed / 2 skipped, DBA-035
  rehearsed on a 034 copy, unit 2944, format clean. **Not run:** browser journey, Windows/IIS, corporate TEST. Report: [OR-2-RAPOR.md](OR-2-RAPOR.md).

## Web usage scan, TARAMA-S (branch `feature/service-accounts-web-usage-scan-20261013`, from master `b64068a`, master `e79ac0b` merged in, not merged)

- Owner decision 2026-10-13: the product runs the same read-only scan from the web; Worker is a Windows service; access is
  direct WinRM + Kerberos + JEA (rules 1 and 3 unchanged). Contract first (`616fa0e`): `USAGE-SCAN-JOB-CONTRACT.md`,
  `IServiceAccountUsageCollector` + deterministic fake. The Worker JEA runner, service setup and endpoint package are TARAMA-C
  (Codex, separate branch).
- Migration 037 (SA-010; SA-008 and 035 are left to OR-2, 036 is SA-009; `DBA-037-TR.md`): job/account/server tables, one active scan per account (filtered unique index),
  guard triggers, API/Worker grants; harness 001–034, 036, 037 + `sa-037-guards.sql`; errors 51430–51435. Runner on the existing Hangfire queue,
  lease-fenced; each document validated by the ADR-0027 parser; final evidence in the 030 tables (`tool = Jea`). API: six
  routes (OpenAPI snapshot: additions only). UI: live page + start panel. Off by default (`Collector=None`).
- Verified here (Linux cloud, SDK 9.0.317): build 0/0, unit tests, format gate on a CRLF copy, the OpenAPI snapshot test.
  Not verified: SQL (037, guards, SQL tests), Windows/IIS, browser, real JEA. Report: `TARAMA-S-RAPOR.md`.
- Open: release selector `Get-ReleaseSqlPlan.ps1` does not know 037 (Codex); 035 belongs to OR-2.
## Event guidance UI OLAY-3 (branch `feature/service-accounts-event-guidance-ui-20261013`, from master `4a7ea77`, master `0556459` merged in, not merged)

- Account page: "Bir gelişme mi oldu?" stepped flow (Ne oldu? → Ayrıntılar + optional mail paste → Önizleme "Şunlar kaydedilecek / Kaydedilmeyecek" →
  separate Kaydet) against the OLAY-2a/2b/2c routes; hidden with a short honest text when `eventGuidanceAvailable` is false (036 missing). Close hints
  from `close-readiness` on "Talebi kapat" (aria-disabled + the server's list + remedy buttons), closure sub-codes on actions, "Bildir" on the next-step
  card, role line in the page guide. UI/tests/docs only; contract gaps [G-39](../26-ui-backend-contract-gaps.md). Decisions: EVENT-GUIDANCE-DESIGN §4 note.
- Verified 2026-10-13 locally (Windows, SDK 9.0.317): build 0/0; full unit 2622 passed / 1 skipped (existing), new `ServiceAccountEventFlowUiTests` (24); repository-wide format gate clean; journey
  `tests/browser/service-accounts-event-flow.cjs` 7/7 on the local Demo (API :5630, UI :5631, synthetic LocalDB `SecureOps_SaOlay3Ui1013` built by the
  harness 001–036 without role scripts, `sa-demo-bootstrap.sql`; database dropped afterwards): keyboard + heading focus, mail hints cleared from the box,
  one save per event, 409 stale and 422 refused close writing nothing, close hint aria-disabled, next-step Bildir, 390 px and dark without overflow.
  The journey found one defect, fixed: the page did not re-render after a delegate call, so a refreshed preview reused the stale account version.
- Not verified: unknown outcome and missing 036 in a browser (unit-tested only; the API call is server-side), participant in a browser (unit-tested),
  real 200 % zoom by eye, screen reader, Windows/IIS, corporate TEST (036 not installed there).

## Close readiness OLAY-2a (branch `feature/service-accounts-close-readiness-20261013`, from master `633eed9` + OLAY-1 design merged in, not merged)

- `ServiceAccountRules.EvaluateClose` lists every missing close condition (`CloseGap`: code, remedy, sub-codes of the closest deletion
  candidate, expected action type, related action); `ValidateClose` returns its first code (equivalence to the previous implementation
  is a unit test). New read-only `GET requests/{id}/close-readiness?outcome=` (View, account-detail scope, 404 out of scope, strict
  outcome names, no write/audit); OpenAPI snapshot additive only (one path, `CloseReadiness`, `CloseMissing`). No migration, no UI.
- Not in this PR: `RequestView.closeReadiness` (detail lacks the verifier/evidence facts; see EVENT-GUIDANCE-DESIGN §6 note) and
  `EvaluateVerification`.

## Mail hints OLAY-2b (branch `feature/service-accounts-mail-hints-20261013`, from master `6ee5d26`, not merged)

- `POST accounts/{id}/events/mail-hints` (Work + account in scope, else 403/404): pasted reply text in, **suggestions only** out
  (event kinds, OR/OCO numbers, dates); deterministic rules in one table (`EventMailHintRules`), pure function `EventMailHints`, no
  language model, no network, nothing written, text never stored, logged or echoed. Own controller file; OpenAPI snapshot additive only.
  Details and limits: EVENT-GUIDANCE-DESIGN §7 "OLAY-2b uygulama notu". No migration, no UI.

## Event guidance design OLAY-1 (branch `docs/service-accounts-event-guidance-design-20261013`, from master `633eed9`, not merged)

- Design only, no code: [EVENT-GUIDANCE-DESIGN.md](EVENT-GUIDANCE-DESIGN.md) ("Ne oldu?" flow, event-to-record table, preview/commit contract,
  close-readiness, deterministic mail hints, migration 036 reserved, threat table E1-E14, split OLAY-2a/2b/2c/3). Owner questions S1-S8 open.
  Reported is not verified: the flow never writes Verified. OLAY-3 (UI) waits for F4.

## First TEST acceptance — UI fixes (F4; branch `feature/service-accounts-first-acceptance-ui-20261013`, merged #42 → master `101c1f5`)

- List shortcut buttons did nothing while the list was open (Blazor updates parameters, `OnParametersSet` only set `_status`). Now a changed
  query string re-applies exactly that shortcut's filter (all other filters cleared), returns to page 1 and re-reads; no change, no re-read.
- Counts: the list says what its total counts ("kapsamınızdaki tüm hesaplar" vs the applied filter) and, for `status=proposed` from the card,
  whether it equals the card's number. Cards carry hints ("farklı hesap", "talep/bildirim sayısı, hesap değil"); the shortcut shows the count.
  Both numbers count distinct in-scope accounts with a Proposed ownership; no API gap.
- List search field is "Hesap adı" (the API never searched SID); filters apply on Listele/Enter, said on the page. Directory name search has
  distinct states: waiting, could not search (unknown, may be not set up or unreachable), no match for the searched name, matches, truncated.
- Usage-scan texts: the system does not scan; read-only JEA scanning is a separate capability without Information Security approval (ADR-0024 R2).
- Short guide fix (list "Bul" step). Import rows: "Excel satırı N", a one-line reason for Conflict/Invalid/OutOfScope, error lines name sheet and row,
  and a missing server detail is stated. The event-oriented guide ("Ne oldu?") is OLAY-1, not here.
- Gaps recorded: `docs/26-ui-backend-contract-gaps.md` G-37. Tests: `ServiceAccountFirstAcceptanceUiTests` (renders the real list page with a
  synthetic API; reverting the fix makes the shortcut tests fail). Not verified: the real browser journey and corporate TEST.

## Change plan d, PR 2 (branch `feature/service-accounts-change-plan-checklist-20261012`, from the PR 1 branch `db56856`, not merged)

- Contract first (`a737d2b`, pushed for F and G): `GET/POST change-plans/{id}/items/{itemId}/checks`, `POST .../evidence`,
  `POST .../complete`; additive DTO members (latest mark per row, checklist summary, plan evidence, completion outcome,
  `Check/Complete/UploadEvidence` permissions). Design section "Kontrol listesi, kanıt ve kapanış", threat rows T4b–T13.
- Migration 034 (SA-007, `DBA-034-TR.md`): only `CK_SaEvidence_Owner` gains `'ChangePlan'` (same name, `WITH CHECK`); 51396
  on replay/missing 033/unexpected definition. Harness 001–034 + `sa-034-guards.sql`.
- Marks are the person's statement (`Done/Skipped/Failed/RolledBack`, note required except Done, `expectedCheckId` against
  lost updates, outside-window accepted and flagged). Completion records one unverified `GmsaConversion` Performed action per
  account whose rows say Done with nothing Failed/RolledBack; never verifies, never closes the linked request, never touches
  the transition record. Plan evidence: whole-plan scope for download, never listed on one account.
- Open: release selector must learn 034 too (Codex, H); `sa-sql-harness.sh` (Linux counterpart) still ends at 032.

## Change plan d, PR 1 (branch `feature/service-accounts-change-plan-api-20261007`, from master `78a94b1`, not merged)

- Contract first (`0400d58`, pushed for the UI (F) and security-test (G) sessions): `/api/v1/service-accounts/change-plans` list/detail/
  items, create, PATCH (full account list), preview, approve, cancel; DTOs in `ServiceAccountChangePlanContracts.cs`.
- Migration 033 (SA-006, `DBA-033-TR.md`): seven tables, append-only guards, approval separation trigger, `UQ(PreviewId)`;
  grants SELECT/INSERT (+UPDATE ChangePlans), no DELETE. Harness 001–033 + `sa-033-guards.sql`. Design deviations and threat
  test names: `CHANGE-PLAN-DESIGN.md`. Added flag `NothingFound` (scan answered, account found nowhere).
- Open: release tool `Get-ReleaseSqlPlan.ps1` refuses a 001–033 chain (by design, needs a reviewed update before merge);
  `Evidence.OwnerEntityType` cannot hold plan evidence without changing an existing CHECK (PR 2 question).

## Change plan d, PR 4 — UI checklist, plan evidence, closing (F3; branch `feature/service-accounts-change-plan-checklist-ui-20261012`, from master `d3eed21`, not merged)

- Replaces the "Henüz sunulmuyor" states with the real flow against the routes merged in #36: per-row marks (`expectedCheckId`; 409 `checkStale`
  adopts the server's plan, re-reads rows, keeps the draft), "pencere dışında" label, counters, plan evidence upload/download (503 without 034 shown as
  "kurulu değil"), closing form with the "Yapıldı is a statement, not a verification" confirmation, per-account closing outcome. Buttons follow
  `permissions` only. UI/docs/tests only; [G-36](../26-ui-backend-contract-gaps.md) item 1 closed. Details: `src/SecureOps.Ui/README.md` ("F3").
- Verified 2026-10-12 on the local Demo (API :5430, UI :5431, synthetic DB `SecureOps_SaF31012` on LocalDB `SecureOpsResourcesV1`, migrations 001–034 by the
  harness, no role scripts; the second demo actor got the Admin role by a direct insert into the synthetic DB): mark Yapıldı / Atlandı (note required,
  button waits); a mark made by another actor in between → 409 `checkStale`, row refreshed to the other mark, nothing saved, "Geri alındı" now offered;
  evidence listed (uploaded through the API with curl; the browser file picker was NOT driven); close with the confirmation → outcome per account
  ("eylem kaydedilmedi" for Failed/Skipped, "gMSA geçiş eylemi 'yapıldı' olarak kaydedildi, doğrulama ayrı" for an all-Done plan); 390 px and 320 px
  without horizontal overflow. Not verified: light appearance and 200 % zoom by eye, the outside-window label against a real window (unit-tested only),
  Windows/IIS, corporate TEST (034 is not installed there), a Playwright journey (none written).
- Found by that run and fixed: the child forms call back through plain delegates, so the page was not re-rendered after a save (marks reached the server
  but the screen stayed stale); the page now renders after each command.

## Change plan d, PR 3 — UI (branch `feature/service-accounts-change-plan-ui-20261012`, from the API branch `db56856`, not merged)

- `/service-accounts/plans` (scoped list, status and `?accountId=` filter, create a Draft plan from searched accounts) and
  `/service-accounts/plans/{id}` (Plan → Önizleme → Onay → Kontrol listesi → Doğrulama). Details and wording rules:
  `src/SecureOps.Ui/README.md` ("gMSA change plan UI"). Checklist marking, evidence and closing are **not offered** (no API until PR 2: G-36).
- F2 (2026-10-12): PR 1 branch (E-fix `2c245ad`) merged into this branch without conflict; OCO text corrected to the server rule
  (validated, upper-cased; 400 `ocoNumber` shown under the field). Verified on a fresh 001–033 DB: invalid OCO refused under the field with the
  draft kept, `oco-9000124` saved as `OCO-9000124`; build 0/0, unit 2052 passed / 1 existing skip, scoped format clean.
- Shared pager `SaPager` replaces the four hand-written pagers (keyboard focus no longer falls to the page when a page changes).
- Verified 2026-10-12 on the local Demo (API :5330, UI :5331/:5332, synthetic LocalDB `SecureOps_SaPlanUi1012`, migrations 001–033 applied
  by the harness without role scripts): create refused → per-account answers and draft kept; create; preview (counters 2/0/1/0/1/2/0:
  found, stale, no info, partial, manual, not found, long name); planner sees the server's `approverIsPlanner` reason and no form; a second actor
  approves; approving a version the planner had just replaced → 409 `previewStale`, current plan adopted, nothing saved; PATCH rename; cancel;
  390 px and 320 px without horizontal overflow, light and dark; account-list pager keeps focus on the last page (31 accounts).
  Build 0/0, unit 2035 passed / 1 skipped (existing), scoped format clean.
- Not verified: integration/SQL suites (no backend change), repo-wide format, real 200 % browser zoom (narrow viewports used), screen reader,
  full Tab traversal of every control, Windows/IIS/corporate TEST, Playwright journey (not written).

## Ops-research UI work (2026-10-06, separate branches from master, not merged)

Order c, b, a, d from `ops-research/05-yol-haritasi.md`. **c merged** (PR #14, `87df0c3`):
"Sıradaki adım" card on the account page, computed in the UI from the loaded detail only (no API change); missing data reads
"bilgi eksik", never "not used"; a step the caller may not take reads "başkasından bekleniyor".
**b** (branch `feature/service-accounts-scan-diff-20261006`, no PR yet): server-side scan comparison (G-33,
`GET accounts/{id}/usage-scans/{linkId}/diff`, read only, no migration) and the "Önceki taramaya göre fark" UI. Verified
2026-10-07 at `b2d02b3` (contains master): build 0/0, format clean, unit 1972 + 1 intentional skip, integration 434/434
(67 skipped) twice on fresh `SecureOps_SaDiff1007a`, local Demo WINDOWS-ACCEPTANCE row 88.

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
   (normally 0.5 s). This is the whole test duration, not a measured commit duration: that test stages a workbook and
   checks assignment denial; it never calls commit. The original gate holder was not captured. Two writes failed:
   `ServiceAccountWorkflowSqlTests.ClosureVerification_DuringImportCommit_WaitsInsteadOfDeadlocking` (SqlClient timeout)
   and `ManualAccounts_StayProvisional_EvenWithATypedDomain` (persistence unavailable). system_health had no deadlock in
   that window; the gated access-registration tests were skipped. The next four runs (one on fresh `…1007e`) passed
   419/419, about 1 min each. Also found and fixed that day: the new middle-account test held a raw row lock outside the
   write gate and deadlocked (1205) with a parallel serializable import commit; its class now runs in the serial
   "Service Accounts write gate" collection. Cause of the slow test still unknown.
   2026-10-07 follow-up from master `13b5703`: 21 full integration runs on 21 fresh synthetic LocalDB databases,
   432 passed / 68 gated skips each; eleven runs also correlated SQL SPIDs with exact test names. No unexpected failure
   or deadlock reproduced. Controlled deadlock capture verified separately. No scheduling, module, UI or migration
   fix inferred. **Open work 9 remains open**; evidence and limitations: [INTERMITTENT-LOCK-20261007.md](INTERMITTENT-LOCK-20261007.md).

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
