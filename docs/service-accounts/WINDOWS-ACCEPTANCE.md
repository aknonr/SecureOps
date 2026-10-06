# Service Accounts — Windows Acceptance Runner (NOT EXECUTED)

Status: **corporate authentication acceptance not executed.** The original Linux
runner and baseline counts in sections2/3 below are historical; actual combined
Windows build/SQL evidence is in the canonical `../integrated-test-activation.md`.
Combined 2026-10-02 source review is [COMBINED-INTEGRATION-20261002.md](COMBINED-INTEGRATION-20261002.md).
The current local harness installs numbered 001-026 (retained SA-002 is included by 026,
not separately applied). It does not assign runtime principals or establish target acceptance.
The current `../post-rc626-continuation-tr.md` distinguishes a minimum API/UI
pilot with EXISTING approved TEST users and reminders/Worker module disabled from
the later full four-role acceptance in section4. No four new accounts are required.
Neither local bridge nor isolated SQL evidence substitutes for normal OIDC acceptance.
No production server, flag or data
is used; all business data is synthetic. The only non-local input is the set of approved TEST
identities of section 4.

Source: the pinned handoff `b4fdf8d` (`HANDOFF.md`), the follow-up `7e227ed` (`FOLLOWUP-20260930.md`) and the
knowledge-base/report branch `feature/service-accounts-kb-rules-reports-20261001` (`KB-RULES-20261001.md`, delivery
checkpoint `4e6a4ef`) and its continuation on current master, `feature/service-accounts-continuation-20261002`
(adds the bounded directory name search, ADR-0025); run this procedure at the continuation branch's HEAD. **That code requires SQL candidate 2 (`SA-002-usage-rules.sql` and
`SA-002-API-permissions.sql`)**: account detail, reports and import read `svcacct.AccountUsages` and `svcacct.TeamRoles`.

## Why a Windows runner

- The API host enforces Integrated Security for the platform's SQL stores
  (`SqlPersistenceConfigurationValidator`). Role bundles that carry `ServiceAccounts.*` actions exist
  only in the SQL access store, so allowed HTTP journeys need Windows authentication to SQL.
- On Linux these parts ran: module SQL tests (least-privilege runtime role), a DI-level
  persisted-access composition test, and HTTP denial tests through the real `Program`
  (`ServiceAccountApiCompositionTests`). The allowed HTTP/UI journey below did not run.
- Knowledge-base branch, on Linux: the persisted-access SQL test `ServiceAccountKbPersistedAccessSqlTests` (rows 17–28
  at service level, without HTTP) and a browser boundary journey on the real API `Program` and UI host
  (`tests/browser/service-accounts-kb-boundary.cjs`: anonymous 401 and 403 for both demo actors on all new routes, module
  screens refuse, navigation hides the module, also with `Provider=Disabled`). A local Kerberos/AD surrogate for
  Integrated Security was not run (not permitted in that environment), so rows 17–28 in the browser remain Windows steps.

## 1. Prerequisites

- Windows host with the pinned .NET SDK (`global.json`), `sqlcmd`, `SqlLocalDB`, and the isolated
  per-user instance `SecureOpsResourcesV1` (same instance the platform's SQL harnesses use).
- A clean clone at the recorded HEAD. `git status` must be clean.

## 2. Build and non-SQL tests

Use the repository's pinned SDK (master's G-30 gate: `global.json` 9.0.317, `rollForward: disable`,
`Directory.Build.props` `LangVersion` 12.0) with **no `LangVersion` override**; record `dotnet --version`. The continuation
branch keeps these files identical to master. On Linux it was built and tested with the Microsoft SDK 9.0.317 and no
override (results in `PROGRESS-HISTORY.md`); the Windows G-30 gate itself is still to be run by Codex. Older notes measured with
SDK 10.0.112 and `-p:LangVersion=13` do **not** count for the gate.

```powershell
git rev-parse HEAD            # must equal the HEAD in HANDOFF.md
dotnet build SecureOps.sln -c Release
dotnet test tests\SecureOps.Tests.Unit -c Release --logger "trx;LogFileName=unit.trx" --results-directory .\evidence
dotnet test tests\SecureOps.Tests.Integration -c Release --logger "trx;LogFileName=integration-nosql.trx" --results-directory .\evidence
```

Expected: build 0 warnings/0 errors. On Linux 2 unit failures and 12 integration failures are
environment-specific and identical to `e997c5b`; on Windows record the actual set and compare it
with `e997c5b` built on the same host.

## 3. Fresh module database and SQL tests

```powershell
powershell -NoProfile -File tests\sql\service-accounts\sa-sql-harness.ps1 -DatabaseSuffix Pilot01
$env:SECUREOPS_SA_SQL_TEST_CONNECTION = 'Server=(localdb)\SecureOpsResourcesV1;Database=SecureOps_SaPilot01;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15'
$env:SECUREOPS_SA_SQL_DIAGNOSTICS = "$PWD\evidence\sa-diagnostics.log"
dotnet test tests\SecureOps.Tests.Integration -c Release --no-build --filter "FullyQualifiedName~ServiceAccounts" --logger "trx;LogFileName=sa-sql.trx" --results-directory .\evidence
```

Expected: all module tests pass (45 at the combined source, including the two LocalDB-only
`ServiceAccountRoleSqlTests`, which now also check `svcacct.AccountUsages` and `svcacct.TeamRoles` from SA-002: API role
SELECT/INSERT/UPDATE only, no DELETE/ALTER/CONTROL; Worker role no permission. These two cannot run on Linux; record
their Windows result). The diagnostics file should contain only the
intentional fault-probe entries (`Number=51091`, plus synthetic unavailable/timeout provider failures).
Unexpected SQL diagnostics are failures to investigate; keep every TRX and the log.

Migration 031 (requested gMSA name): the harness applies it by default; `-ThroughMigration 30` leaves it out so a second
database can stand in for an installed 030 system. Set `SECUREOPS_SA_SQL_TEST_CONNECTION_030` to that database to run the
"new binaries before 031" SQL test (skipped otherwise), and rehearse 031 on a local copy at 030 with
`tests\sql\service-accounts\sa-031-copy-rehearsal.ps1 -Database SecureOps_Sa<copy> [-SeedSynthetic]` (rows 79–81).

Optional least-privilege rerun: map a second local Windows principal to a database user that is only
a member of `svcacct_api_runtime` (role scripts SA-API, then SA-002-API), set `SECUREOPS_SA_SQL_RUNTIME_CONNECTION` to a connection that runs
as that principal, and rerun the filter on a *new* database. If no second principal is available,
record "NOT RUN" (the Linux run covered it with a SQL login).

## 4. Later Full Four-Role Acceptance: Normal Authentication

This complete authorization matrix is NOT a prerequisite for the separately bounded
first API/UI pilot in the current operator entry. It uses the API's normal
authentication (OIDC), persisted approval, role bundles and module scope grants — nothing else.

**Precondition — existing approved TEST identities.** Four identities issued by the organization's
approved TEST OIDC provider, each with its real claims (issuer, subject, login name), approved for
this test: a platform administrator, a module coordinator, a team member, and an outsider. Do not
create them, share their credentials or put their names in the repository; record only the
role each played.

Use existing approved users; do not create four new accounts. If the distinct
role actors are unavailable, this FULL matrix is blocked, not necessarily the
separately approved minimum pilot. Record "BLOCKED: approved TEST
identities unavailable" in the evidence and stop here. The local smoke test in section 4A never
replaces this section.

Configuration: the platform's normal Test/pilot settings from
`docs/25-real-user-pilot-management-reporting-and-dotnet10.md` (OIDC enabled, `DemoAuth__Enabled=false`,
`Access__DemoCompatibilityEnabled=false`, SQL access/session/audit stores with Integrated Security,
first-admin bootstrap only through the documented `BootstrapAdmin__*` gate), plus the module:

```text
ConnectionStrings__SecureOpsDb=<Integrated Security connection to the database from step 3>
ServiceAccounts__Provider=SqlServer
ServiceAccounts__Reminders__Enabled=false
```

Rights are given only through the product:

a. The platform administrator is the documented first OIDC Admin (bootstrap gate) or an already
   approved administrator. No role assignment by SQL.
b. The administrator creates the module bundles with `POST /api/v1/access/roles/preview` and
   `PUT /api/v1/access/roles` (preview token), and approves each pending identity with
   `POST /api/v1/access/requests/{id}/approve` giving the bundle code and its reviewed version:

   | Pilot role | Bundle | Actions |
   |---|---|---|
   | module administrator | `sa-pilot-admin` | View, Administer |
   | coordinator | `sa-pilot-coord` | View, Work, Assign, Verify, Import, Report |
   | team member | `sa-pilot-member` | View, Work |
   | out-of-scope user | `sa-pilot-member` | View, Work; separate unrelated Team scope |

c. The module administrator creates `SYN PILOT ORG` and `SYN PILOT TEAM`
   (`POST /api/v1/service-accounts/organizations`, `.../teams`) and the scope grants
   (`POST /api/v1/service-accounts/scope-grants`): coordinator → Organization `SYN PILOT ORG`,
   member → Team `SYN PILOT TEAM`; out-of-scope user → a different synthetic team
   with no owner/request/handover link to the account. These are reviewed product
   grants, not imported-person matching. Before adding its module bundle verify
   the out-of-scope user's module call returns 403; afterwards test SQL data scope.
d. The coordinator creates `SYNPILOT_A1` in `SYN PILOT ORG`, a request targeted at
   `SYN PILOT TEAM`, and imports a synthetic coordination list with coverage `Complete`.

Expected results (capture request, status and response body for each):

| # | Caller | Call | Expected |
|---|---|---|---|
| 1 | no token | `GET /api/v1/service-accounts/me` | 401 |
| 2 | platform administrator without a module bundle | any module route | 403 (no platform role carries module actions) |
| 3 | out-of-scope user | `GET .../accounts`, guessed account/evidence ID, `.../accounts/export` | 200 empty scoped list; hidden detail/evidence 404; export 403 (no Report) |
| 4 | team member | `POST .../scope-grants` | 403 |
| 5 | coordinator | `GET .../accounts` | 200, only `SYN PILOT ORG` accounts |
| 6 | coordinator | `GET .../accounts/export` | 200 XLSX; audit `ServiceAccount.AccountsExported` with the row count; 4th call within a minute → 429 |
| 7 | team member | `GET .../work-summary` | 200, `TeamOpenRequests` = 1 |
| 8 | team member | `GET .../accounts/{A1}` | 200, `permissions.basis` = `Participant` |
| 9 | team member | update own request notes | 200 |
| 10 | team member | retarget own request / edit account / create request | 403 |
| 11 | team member | account of another organization by id | 404 |
| 12 | coordinator | import commit twice with the same `Idempotency-Key`, then re-stage the same file | same result; re-stage returns `replay: true`; no duplicates |
| 13 | platform administrator | remove Work from `sa-pilot-member` (preview + apply), then row 9 again | 403 on the next call |
| 14 | coordinator | report a `Review` action with record kind `Closure` | 400 `ClosureKindNotAllowed` |
| 15 | coordinator | weekly live report, sent `Manager` snapshot, month and date-range reports, snapshot XLSX/PDF | 200; snapshot unchanged after a late entry; creator shown |
| 16 | any bundle holder | module routes with `ServiceAccounts__Provider=Disabled` | 503 `ServiceAccountsNotConfigured` |

Knowledge-base rules, gMSA routing and report v2 (same identities; add a bundle `sa-pilot-verify` = View, Verify for the
coordinator, or a fifth TEST identity as verifier). Setup: the module administrator creates teams `SYN PILOT SQL` and
`SYN PILOT WASAS` in `SYN PILOT ORG` and accounts `SYNPILOT_G1`, `SYNPILOT_G2`, `SYNPILOT_G3` in `SYN PILOT ORG`.

| # | Caller | Call / screen | Expected |
|---|---|---|---|
| 17 | team member, coordinator | `POST .../team-roles` | 403; module administrator → 200 |
| 18 | module administrator | Admin → "gMSA yönlendirme": `SYN PILOT SQL` = SQL ekibi, `SYN PILOT WASAS` = yürütücü; a second executor | 200, 200; second executor 400 `role` |
| 19 | coordinator | revoke the executor, stage a DBA list (target `SYN PILOT WASAS`) naming G1 with Ekip `SYN PILOT SQL` | preview has no "gMSA yönlendirme" line; commit opens no request |
| 20 | coordinator | create a manual GmsaHandover request for G2, restore the executor, stage a list with G1, G2, G3 | "gMSA yönlendirme (SQL-EKIP)" only for G1 and G3; after commit exactly one GmsaHandover request each for G1, G2, G3 and no ownership change |
| 21 | coordinator | stage the same accounts again in a later list | no routing line, no new request |
| 22 | coordinator | detail of G1 → "Kullanım ve kural": add Database/Oracle | "Manuel inceleme bekliyor", rule `KB-VT-ORACLE`, reason says unverified; no removal recommended |
| 23 | coordinator | add ScheduledTask; try "İstisna kaydet" | badge "Kurala aykırı (plansız)"; exception button absent; direct `POST usages/{id}/exception` → 403 |
| 24 | verifier | record the exception with a reason | 200; badge returns to manual review; history and audit rows present |
| 25 | team member | G1 detail / `POST accounts/{G1}/usages` | 404 / 404 (out of scope is indistinguishable from missing) |
| 26 | coordinator | Reports: live report, save snapshot A, change data, save snapshot B, "Nüsha karşılaştırma" A→B | directorate view, rules list, gMSA funnel, 12-week trend and risk candidates visible; comparison shows the change; reopened A unchanged (same SHA-256 prefix) |
| 27 | coordinator | snapshot A XLSX and PDF | sheets include "Direktörlük görünümü", "Bilgi bankası kuralları", "İncelenecek hesaplar", "gMSA hunisi", "Trend (son haftalar)", "Risk adayları"; **desktop Excel opens without a repair prompt**; PDF opens |
| 28 | coordinator | a snapshot created with the pre-branch build (`7e227ed`) before upgrading, after upgrading | opens with the "bu sürümde yoktur" notice; XLSX/PDF without the new sheets; hash unchanged |

Bounded directory name search (ADR-0025). Needs the real Active Directory provider (`IdentityLookup:Provider=ActiveDirectory`,
TEST domain), a module bundle with View and a platform bundle with `Identity.Lookup` for the coordinator only. Pick two
TEST users that share a first name (or note "no same-named pair available").

| # | Caller | Call / screen | Expected |
|---|---|---|---|
| 29 | team member (no `Identity.Lookup`) | `/service-accounts` / `POST .../directory/name-search` | panel absent / 403 |
| 30 | coordinator | panel "Dizinde ada göre ara": `ay`, `ay*`, `*)(cn=*` | button disabled for `ay`; API 400 `NameQueryTooShort` / `NameQueryCharacters`; no provider call (Requested audit absent) |
| 31 | coordinator | a TEST first name in lower case, ASCII form and Turkish form (İ/ı) | same people each time; at most 10 rows; only name, account, department |
| 32 | coordinator | the shared first name plus surname | both people listed with the "aynı adlı" note, distinguished by account and department |
| 33 | coordinator | a name whose account has a module record in scope / out of scope | "Kaydı aç" opens the record / "Kapsamınızda kayıt yok", no id in the response |
| 34 | coordinator | an over-broad prefix (e.g. a common first name) | 10 rows and the "daha fazla eşleşme" notice |
| 35 | coordinator | `RateLimiting:IdentityLookup:PermitLimit` + 1 searches within its window | 429 on the call over the limit |
| 36 | coordinator | stop network access to the DC (or point `IdentityLookup:DomainName` at an unreachable TEST name) | 503 `ServiceAccountDirectoryUnavailable`, audit `DirectoryNameSearchFailed`; no partial answer |
| 37 | auditor | `SELECT Action, DetailsJson FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.DirectoryNameSearch%'` | Requested/Completed/Failed rows with hash, length, word and result counts; **no name or account text** |
| 38 | coordinator | `/identity-lookup` exact account | unchanged behaviour (ADR-0008) |

Row 35 uses the configured `IdentityLookup` rate-limit window; record the configured value with the result.

Scope setup, import guidance and report layout (2026-10-03). Use the installed system (migrations 024–028 already
applied; nothing is re-applied). Identities: the operator who will import (holds the module bundle, **no** scope grant
yet) and a different module administrator.

| # | Caller | Call / screen | Expected |
|---|---|---|---|
| 39 | operator without scope | `/service-accounts/imports` | panel "Önce veri kapsamınız tanımlanmalı" with the four steps; no red access error; `GET .../imports` is not called (no `scope` 403 in the API log) |
| 40 | operator | Modül yönetimi → Kapsam yetkileri → grant to own identity | 403 with the Turkish `selfGrant` explanation; no grant row, audit unchanged |
| 41 | other module administrator | same tab, empty organization list | info "İlk kurulum…", scope kind preselected "Tüm kurum"; the operator is **picked from the list** of approved users (own entry disabled, users without module access marked); grant → 200, audit `ServiceAccount.ScopeGranted` |
| 42 | operator | "Kapsamımı yeniden kontrol et" | the import page opens; step line shows 1–3 once each with hints (no "1. 1."); guide shows three cards, "İlk kurulum" marked "Henüz yapılmadı" |
| 43 | operator | pick card 1, choose a TEST copy of the tracking workbook, leave provenance empty | "Önizleme oluştur" disabled and "Önizleme için eksik: Tarihin kaynağı seçilmedi…" listed; the date block is highlighted |
| 44 | operator | choose "Dosyayı ileten e-postanın tarihi", preview, commit | preview and commit as rows 12/20; card 1 then shows "Son aktarım: <date>" |
| 45 | operator | card 2 with a TEST weekly list (Book1 headers), report date, coverage "Bilinmiyor" | new / observation-update counts; no "bu listede yok"; owner, OR/OCO, plan, notes unchanged |
| 46 | operator | switch file type after choosing a `.json` file | file cleared with the warning that it does not fit the type |
| 47 | coordinator | snapshot PDF | A4 landscape; dark title band; shaded section and table header rows; alternating rows; footer "Sayfa n/m"; Turkish letters and dashes correct |
| 48 | coordinator | snapshot XLSX in **desktop Excel** | opens without a repair prompt; table sheets keep the header row frozen and offer AutoFilter; the "Rapor" sheet starts with the title |
| 49 | any | page at 390 px and 1440 px | no horizontal scroll; file picker, provenance options and guide cards wrap; the progress line does not push content down |

One-time first scope grant (ADR-0026) and page guides. `SA-003` is numbered **029** (2026-10-03). The owner applies
029 first on a copy of the installed database, then, after a separate approval, on the installed one. Run rows 50–53 **before** any scope
grant exists (if rows 39–41 already granted scope, run them on a fresh TEST database).

| # | Caller | Call / screen | Expected |
|---|---|---|---|
| 50 | module administrator, no grant exists | Modül yönetimi → Kapsam yetkileri | panel "İlk kurulum: kendinize bir kez…"; reason required; "İlk kapsamı al (bir kez)" → 200; grant row `IsBootstrap = 1`, `ScopeKind = All`; audit `ServiceAccount.ScopeBootstrapped`; history `Bootstrapped` |
| 51 | same administrator | `/service-accounts/imports` | import page opens (scope "Tüm kurum") |
| 52 | any administrator | `POST .../scope-grants/bootstrap` again, also after revoking the bootstrap grant | 403 `bootstrapClosed`; panel no longer shown |
| 53 | any administrator | `POST .../scope-grants` to own identity | 403 `selfGrant` (rule unchanged after bootstrap) |
| 54 | before 029 (`SA-003`) is applied | `GET .../scope-grants/bootstrap`, then `POST` | `schemaReady: false`; POST 403 `bootstrapSchema`; info text on the tab; ordinary grants still work |
| 55 | any | each module page and each admin tab | guide strip with numbered steps, one step highlighted at a time; "Nasıl kullanılır?" opens purpose, steps and "Bu sayfa şunları yapmaz"; with Windows "Show animations" off the highlight does not move |
| 56 | coordinator | snapshot XLSX in desktop Excel | first sheet "Yönetici özeti": title, scope/period, eight tiles, gMSA block, team table, upcoming plans; values match the "Özet" and detail sheets; no formulas; no repair prompt |
| 57 | coordinator | snapshot PDF | first page shows the same eight tiles and the three summary tables before the detail sections |
| 58 | SQL operator, copy of the installed database (024–028) | `sqlcmd -I -b -i 029-service-account-scope-bootstrap.sql` from `sql/migrations` | succeeds once; existing grant rows `IsBootstrap = 0`; `CK_SaScopeGrants_NoSelfGrant` trusted; `UX_SaScopeGrants_OneBootstrap` present; a second run fails with "already applied"; installed API keeps granting/revoking normally **Windows 2026-10-05 (63e6d5d), synthetic copy only (not the real TEST database, no `.bak`):** 029 on a 001–028 copy with synthetic rows succeeded once; the six seeded tables' rows are byte-identical (SHA-256 over the original columns); existing grants `IsBootstrap = 0`; `CK_SaScopeGrants_NoSelfGrant` trusted; `UX_SaScopeGrants_OneBootstrap` present; the second run is refused ("already applied"); at SQL level grant and revoke still work, a self-grant is refused and exactly one bootstrap row is possible (probed inside a rolled-back transaction); an injected failure before `COMMIT` left nothing. The installed API itself was not run against it. **Still NOT RUN on the restored installed database.** |
| 59 | SQL operator | `sa-sql-harness.ps1` (default, through 029) | prints "Scope bootstrap installed through a numbered migration" and "candidate 3 replay refused as expected". **Windows 2026-10-04 (659a666, LocalDB): passed, both lines printed, exit 0.** |
| 60 | user with module View but no scope (e.g. new `sa-ekip-uyesi`) | `/service-accounts`, then `/service-accounts/{id}` | panel "Önce veri kapsamınız tanımlanmalı", current scope "tanımlı değil", team-or-organization step; no filters, no empty table, no not-found error; module administrator without any grant also sees the one-time bootstrap hint |
| 61 | roles from `TEAM-ROLE-SETUP-TR.md` with synthetic TEST users | create the three `sa-*` roles, assign, grant Team / Organization scope | each role sees exactly the links and actions in section 4 of that document; no SQL used |
| 62 | coordinator | `/service-accounts/reports` → live report, then a snapshot | "Görsel özet" first: gMSA funnel, trend, team workload, rule conformance (and gMSA transition when not all zero); each value printed; "Tablo olarak göster" opens the same figures; light/dark and 390 px without horizontal scroll. **Local 2026-10-04: passed with synthetic data (Demo API/UI, LocalDB).** |
| 63 | coordinator | snapshot XLSX and PDF in desktop Excel / Edge / Adobe | XLSX: no repair prompt; charts right after the tiles on "Yönetici özeti", values equal to "Grafik verisi". PDF: page 2 is the chart page; Turkish letters (ğ Ğ ş İ ı) visible in Edge and Adobe. **Local 2026-10-04: XLSX opened in desktop Excel read-only without repair marker, 4 charts rendered (synthetic). PDF Turkish-glyph check in Edge/Adobe NOT done; a preview renderer showed ğ blank (pre-existing standard-font issue).** **Windows 2026-10-04 (659a666): synthetic snapshot XLSX opened in desktop Excel (COM, read-only): no "[Repaired]" marker, first sheet "Yönetici özeti", 17 sheets, the 2 non-empty charts present with series bound to "Grafik verisi" and equal values, no formulas on the summary sheet. PDF in Edge/Adobe: see row 64.** |
| 64 | coordinator | the same snapshot PDF after `ddbea34` (embedded Liberation Mono) in Edge and Adobe Reader; File → Properties → Fonts | every Turkish letter (Ğ ğ İ ı Ş ş Ç ç Ö ö Ü ü) and the dashes render in both viewers; fonts list "LiberationMono" and "LiberationMono-Bold" as *Embedded*; copying a table line into Notepad gives the same Turkish text. **Linux 2026-10-04: passed in PDFium (the Edge/Chrome engine, via pypdfium2), poppler and pypdf with a synthetic report; not yet opened in Edge or Adobe on Windows.** **Windows 2026-10-04 (659a666): structure checked on a synthetic snapshot PDF (5 pages): fonts are only `LiberationMono` and `LiberationMono-Bold`, both `Type0`/`Identity-H` with `FontFile2` and `ToUnicode`; no base-14 Courier left. Viewing: the owner opened it in a Chromium-based viewer (Turkish letters and dashes correct on all pages seen) and Adobe Acrobat Reader 64-bit showed the same on the pages inspected; the Properties → Fonts dialog was NOT opened (structure above is the evidence). A heading left alone at a page bottom was found and fixed in `f1887ae`.** |
| 65 | SQL operator | module SQL tests on a **fresh** 001–029 LocalDB database, first run, with `SECUREOPS_SA_SQL_DIAGNOSTICS` set | 49/49 (now 50 with `ConcurrentDictionarySaves_InTheSameKeyGap_AllCommitWithoutDeadlock`) on the first run; no `Number=1205` in the diagnostics file. If `AccountExport_RowLimitIsExact_AndRefusalIsNotAudited` still fails once, keep the diagnostics line: `Number=1205` means another deadlock site, `Number=-2` a command timeout. **Linux 2026-10-04: two fresh databases, first run 48 passed + 2 LocalDB-only, no 1205.** **Windows 2026-10-04 (659a666, SDK 9.0.317, LocalDB): fresh 001–029 database, first run 50/50; diagnostics only the intentional `Number=51091` probes (4) and synthetic timeout/unavailable provider failures (3); no `Number=1205`.** |
| 66 | SQL operator | `powershell -NoProfile -File tests\sql\service-accounts\sa-sql-harness.ps1 -DatabaseSuffix X -SkipRoleScripts; $LASTEXITCODE` | prints the connection line and `0` (was `1` before the fix). **Windows 2026-10-04 (659a666): passed, `0`.** |
| 67 | operator on a TEST/lab Windows server (synthetic service account configured on one service, one scheduled task and one IIS application pool with a stored password) | `powershell -NoProfile -File .\Get-ServiceAccountUsage.ps1 -Account 'SYN\svc_x'` as local administrator, Windows PowerShell 5.1 | one JSON line; `scanResult` Success; the three components listed; the stored password appears nowhere in the output; no new file on the server (script folder, `%TEMP%`). **NOT RUN (Windows 2026-10-05, 63e6d5d): there is no lab/test server with the synthetic account configured on a service, a scheduled task and an IIS application pool with a stored password, and nothing was installed on the owner's workstation. Needed: such a server, a local administrator, Windows PowerShell 5.1.** Read-only run on the workstation itself, Windows PowerShell 5.1.26100, account `SYN\svc_none` (configured nowhere), not elevated: one line of valid `service-account-usage-v1` JSON, `scanResult` Success (`WindowsServices` Success, `ScheduledTasks` Success, `Iis` NotInstalled), no components, no warnings; the script wrote no file (the saved copy is the operator's own `Out-File`). |
| 68 | same server, non-administrator account | same command | `Partial`; `ScheduledTasks`/`Iis` Failed with the exception type only in `warnings` (no path, no name); never "not used". **NOT RUN (Windows 2026-10-05): no lab server with IIS and no non-administrator context on one.** Note: on the owner's workstation (not elevated, IIS not installed) the collector reported Success, not Partial, because scheduled tasks were readable without elevation and IIS was NotInstalled; the Partial path was therefore not reproduced here and stays covered only by the module's PowerShell unit tests. |
| 69 | operator workstation, Windows PowerShell 5.1 | save two collector lines with `Out-File` (UTF-16), then `Invoke-ServiceAccountUsageScan.ps1 -CombinePath ... -ComputerListPath planned.txt -UnreachableComputerName SYN-APP03 -Account 'SYN\svc_x' -OutputPath scan.json` | `scan.json` is UTF-8 without BOM; every planned server present, the missing one `NoResult`, the named one `Unreachable`; a document with a `Password` property stops the script and writes nothing. **Windows 2026-10-05 (63e6d5d), Windows PowerShell 5.1.26100: passed.** (a) The real collector's line saved with `Out-File` (UTF-16 LE with BOM), combined with `-ComputerName <this workstation>,SYN-APP02,SYN-APP03 -UnreachableComputerName SYN-APP03`: `scan.json` starts with bytes `7B 22 73` (UTF-8, no BOM); Planned 3, Answered 1, NotReached 2 = SYN-APP02 `NoResult` and SYN-APP03 `Unreachable`. (b) The three documents of the checked-in synthetic example as a UTF-16 JSON-Lines file, planned SYN-APP01..04 with SYN-APP04 unreachable: also UTF-8 without BOM, 3 results + 1 `Unreachable`; this is the file uploaded in rows 71–72. (c) A document with a top-level `Password` property stopped the script ("is not a service-account-usage-v1 document": the closed-shape check fires first); a nested `Password` inside `sources` stopped it with "contains a password-like property" (the secret guard); no output file was written in either case. Linux 2026-10-04: passed under the PowerShell 7.4 SDK in `ServiceAccountUsageScanScriptTests`. **Windows 2026-10-05 after the review merge (`a8660cc`, Windows PowerShell 5.1.26100, synthetic documents): value guard passed.** `LogonType=Password; Password Expiry Notification` accepted (1 component written); `C:\syn\run.exe /password:…`, `"Password":"…"`, a full-width and a zero-width-split `password=` refused with "contains a value that looks like a password assignment", a 4 097-character value refused with "longer than 4096 characters"; no output file in any refused case and the message never contains the value. |
| 70 | SQL operator | fresh LocalDB database through `sa-sql-harness.ps1 -DatabaseSuffix X` (001–030 plus role scripts incl. `SA-004-API-permissions.sql`), then the module SQL tests | harness exit 0, "candidate 4 replay refused"; all module SQL tests pass including `ServiceAccountUsageScanSqlTests` (5) and `ServiceAccountRoleSqlTests` (030 tables: SELECT/INSERT only for the API role, nothing for the Worker). **Windows 2026-10-05 (63e6d5d, SDK 9.0.317, LocalDB): passed.** Fresh database 001–030 plus the role scripts including `SA-004-API-permissions.sql`: harness exit 0 and "candidate 4 replay refused as expected"; module SQL tests 56/56 on the first run (including `ServiceAccountUsageScanSqlTests` 5 and the LocalDB-only `ServiceAccountRoleSqlTests` 2, whose 030 checks are API role SELECT/INSERT only and nothing for the Worker); the diagnostics file holds only the 4 intentional `Number=51091` probes and 3 synthetic timeout/unavailable provider failures, no `Number=1205`. Linux 2026-10-04: fresh 001–030 database, 53 passed + 2 LocalDB-only role tests, also as a member of `svcacct_api_runtime` only; grants probed with `HAS_PERMS_BY_NAME`. **Windows 2026-10-05 after the review merge (`9629101`): passed.** Fresh `SecureOps_SaMerge1005b`: harness exit 0, "candidate 4 replay refused as expected"; module SQL 58/58 on the first run (includes the new searched-name test and the 409 assertions); diagnostics 4 × `Number=51091` and 3 synthetic provider failures, no `Number=1205`. |
| 71 | coordinator (browser) | account → "Kullanım taraması" → upload the synthetic `scan.json` with a run statement; then the same file with a `Password` field added | first: attached, coverage line and uncovered servers named, "Karar ver" per match; second: "Tarama dosyası kabul edilmedi" + secret message, no new row in `svcacct.UsageScans`. **Windows 2026-10-05 (63e6d5d), local Demo API + UI on LocalDB with the Demo identity bridge (not corporate OIDC): passed.** `demo:platform-admin` (Admin role; first scope taken through the product): the synthetic combined file attached ("4 sunucudan 2 tanesi tam tarandı"; "4 planlanan sunucu: 1 sunucuda çalışıyor · 1 sunucuda tam taramada bulunmadı · 1 sunucuda belirsiz (kısmi tarama) · 1 sunucu için bilgi yok"; SYN-APP03 "Kısmi tarandı" and SYN-APP04 "Erişilemedi" named as missing; three matches, each with "Karar ver"). The same file with a `Password` property added: "Tarama dosyası kabul edilmedi" and "Dosyada parola veya gizli değer gibi görünen bir alan var…"; `svcacct.UsageScans` stayed at 1 row (servers 4, items 3, links 1, unchanged) and the planted value is in no scan, audit or history row. The file reached the file input by script in the browser pane, not through the OS file dialog. |
| 72 | coordinator | record one match as a usage, dismiss another with a reason | usage appears in "Kullanım ve kural" with the rule recomputed; decision badges; a second decision on the same match is refused. **Windows 2026-10-05 (63e6d5d), local Demo: passed.** The IIS application pool match recorded as a usage (badge "Kullanım kaydı oluşturuldu"; "Kullanım ve kural (1)" lists it; the rule box shows "Manuel karar (KB-YOK)"), the IIS virtual directory dismissed with a reason (badge "Kayda alınmadı" plus the reason); a second usage or dismissal on the recorded item is refused by the API with 400 `alreadyDecided` (the UI no longer offers "Karar ver" there); 2 decisions, 1 usage. **Windows 2026-10-05 after the review merge (`6d54e69`, `9629101`), local Demo: the refusal is now 409.** Two API dismissals on one synthetic match: 200, then 409 `ServiceAccountUsageScanAlreadyDecided` (`stage` `usage-scan-decision`, `retryable` false, `field` `alreadyDecided`). A dismissal sent from a screen opened before that decision: "Karar zaten verilmiş … Güncel durum yüklendi", the first decision is listed, the tab count drops, and the stale decision box closes. |
| 73 | gMSA executor team member (participant through an open GmsaHandover request) | upload a gMSA check through that request | attached via the request; no "Karar ver"; "Devir ve gMSA" shows the latest check as evidence, not verification. **Windows 2026-10-05 (63e6d5d), local Demo: passed.** `demo:team-lead` with a role bundle (`sa-ekip-uyesi`: View, Work; created and assigned through the product's role screens on the local synthetic database, with the owner's approval) and Team scope on the executing team; an open `GmsaHandover` request targets that team. The gMSA check file attached through that request ("Bağlı talep: gMSA ile devir", badge "gMSA: taranan sunucularda dönüşmüş (kanıt)"); no "Karar ver" for the participant ("Karar bekliyor (hesaptan sorumlu ekip)"); "Devir ve gMSA" shows "Son gMSA kontrol taraması … (kanıt; doğrulamayı doğrulayıcı yapar)". Server side: the participant's upload without a request or with a foreign request is 403 `ServiceAccountAccessDenied` (field `requestId`), a decision on an item is 403 (field `scope`); no row was added. |
| 74 | any | the scan tab at 390 px and 200 % zoom, light and dark, keyboard only | no horizontal scroll; Tab reaches both summaries and every "Karar ver"; Enter opens a section; focus visible. **Linux: static render (real component, MudBlazor CSS, theme tokens) at 390/640/1280 px in light and dark: no horizontal scroll, Tab/Enter as expected. Live app: **Windows 2026-10-05 (63e6d5d + 9fc5ee3), Chromium in the Claude desktop browser pane, local Demo: passed after one fix.** 390 × 844 px, dark and light: no horizontal scroll (`scrollWidth` 390), the table becomes stacked cards, nothing beyond the viewport. "200 % zoom" was **emulated** with a 640 × 360 px viewport (not native browser zoom), light and dark: no horizontal scroll. Keyboard, starting from the first summary: Tab visited both help summaries, the upload summary, each coverage summary and "Karar ver"; Enter opened a summary and the decision panel. **Defect found:** "Karar ver" (MudBlazor text button) showed no focus indicator (computed outline none, transparent background); fixed in `9fc5ee3` (2 px ring like the summaries, pinned by a source test) and re-checked after the rebuild (outline 2 px solid). The Tab order from the top of the page was not walked end to end (the tool cannot reset the starting point; the first summary was focused by script).** **Windows 2026-10-05 after the review merge: walked from the page heading, keyboard only — a second defect found and fixed (`7c14ee3`).** The account tabs are MudTabs headers (divs without a tab stop), so the scan tab itself could not be reached by keyboard; Tab went from the first tab's content back to the top. Each header now holds a `SaTabTitle` button: Tab reaches it, Enter or Space opens the tab (`aria-current` moves), then Tab reaches "Tarama neyi göremez?", the upload summary, both coverage summaries and "Karar ver", all with a 2 px ring. 390 × 844 light and dark and 640 × 900 dark with every section open: no horizontal scroll. At 390 px a focused header can be partly outside the MudTabs scroll strip (the scan tab header: about 90 of 310 px visible), never fully hidden. |
| 75 | SQL operator | 030 on the restored copy of the installed TEST database (after 029) | applies in one transaction, replay refused, no existing object changed. **Windows 2026-10-05 (63e6d5d): synthetic copy only — NOT the real TEST database and NOT a restored `.bak`.** A fresh LocalDB database with 001–028 (applied by hand, because `sa-sql-harness.ps1 -ThroughMigration 28` applies candidates 3 and 4 itself), the role scripts and synthetic organization/team/account/scope/team-role/usage/audit rows; then 029, a synthetic bootstrap row, then 030 and `SA-004-API-permissions.sql`: 030 applied (exit 0); the rows of the six seeded tables are byte-identical (SHA-256 over the original columns); the definitions of all 48 pre-existing tables are unchanged (columns, indexes, checks, FKs, defaults, triggers); no object was removed; only `Accounts`, `AccountUsages` and `WorkRequests`, which the new foreign keys reference, got a newer `modify_date`; the new constraints are trusted and all five tables carry the append-only trigger; the second run is refused ("already applied"); grants are `svcacct_api_runtime` SELECT + INSERT only and nothing for the Worker role; an injected `THROW` before `COMMIT` left nothing behind (029 and 030 are each one transaction). **NOT RUN on a restored copy of the installed TEST database (no `.bak`).** DBA notes: [DBA-029-030-TR.md](DBA-029-030-TR.md). |
| 76 | any (browser) | account page and module admin page, keyboard only from the page heading: every tab, every collapsible form (`SaDisclosure`), the buttons inside | every tab and every summary is a tab stop; Enter and Space switch a tab and open a section; the selected tab is announced (`aria-current`); every button and summary shows a focus ring; no horizontal scroll at 390 px with all forms open, light and dark. **Windows 2026-10-05 (`7c14ee3`), local Demo: passed after the tab fix.** Account page: 10 tab buttons and all 13 summaries (edit, new request, action report, usage, scan help, scan upload, two coverage sections, ownership, mail, finding, handover, evidence) reached with Tab and opened with Enter or Space; the only stops without a ring are the app shell brand link and "Hesap menüsü" (outside the module). Admin page: 4 tab buttons, Space switched to "gMSA yönlendirme". 390 × 844 dark, each of the 10 tabs with every form open: `scrollWidth` 390. Note: wait for the Blazor round trip after Enter before pressing Tab, otherwise Tab walks the previous panel. |
| 77 | coordinator (browser) | `/service-accounts` → "Yeni servis hesabı kaydı", type an account name longer than 15 characters (without `DOMAIN\`, UPN suffix or `$`), then the same with `$`, then a short name | a non-blocking hint in a status region (gMSA name limit), a different text for a `$` name, no hint for a short name; "Hesabı kaydet" stays enabled; readable in light and dark. **Windows 2026-10-05 (`0ecc728` merged), local Demo, keyboard only at 390 px: passed.** 23 characters: "Ad 23 karakter. Bu hesap gMSA'ya dönüştürülecekse …"; with `$`: "gMSA adı 23 karakter (sondaki $ hariç) …"; `svc_syn`: no hint; submit enabled throughout; text contrast on the form surface 4.92:1 (light) and 7.40:1 (dark). Nothing was submitted. |
| 78 | SQL operator | module SQL tests on a fresh database: `SearchedName_BareNameHidesNothing_KnownDomainShowsItsOwn_TwoDomainsAreRefused` | an account without a domain is attached under the bare name and shows the SYN and the OTHER match; a SYN account shows only its own match (the other server reads NotFound); a file that searched two domains without a bare name is refused with `accountAmbiguousInScan` and stores neither a scan nor a link. **Windows 2026-10-05 (`c76faf1`, LocalDB): passed; with the selection before `c2601a7` restored temporarily it failed (attached under `SYN\name`).** |
| 79 | SQL operator | fresh LocalDB database through `sa-sql-harness.ps1 -DatabaseSuffix X` (now 001–031), then all module SQL tests with `SECUREOPS_SA_SQL_DIAGNOSTICS` set, first run | harness exit 0, "candidate 5 replay refused"; every module SQL test passes on the first run; no `Number=1205`. **Windows 2026-10-05 (`33c6500`, SDK 9.0.317, LocalDB): passed after one fix.** First fresh run `SecureOps_SaGmsaPage1005b`: 61/62, one `Number=1205` — the 5 000-account export (`ListAccounts`, a wide scoped read) was the deadlock victim of a concurrent `DecideOwnership` in another test (system_health graph: writer X on `PK_SaAccounts` waiting X on `IX_SaOwnership_Account`, reader S the other way). Fix `33c6500`: list, detail and report-fact reads (SELECT only, no transaction) run once more when chosen as the victim; writes are never retried. Fresh `SecureOps_SaGmsaPage1005c`: harness exit 0, candidate 5 replay refused, **62/62 on the first run**, diagnostics only the 4 intentional `Number=51091` probes and 3 synthetic provider failures, no `Number=1205`, and system_health shows no deadlock for that database (the retry was not exercised). |
| 80 | SQL operator | 031 on a copy left at 030 that looks like an installed system: `sa-031-copy-rehearsal.ps1 -Database <copy> [-SeedSynthetic]` | 031 in one transaction; every existing row, definition and permission unchanged; replay refused; an injected failure leaves nothing; no new grant needed. **Windows 2026-10-05 (`a966e57`), synthetic copies only — NOT the installed TEST database and NOT a restored `.bak`.** `SecureOps_SaGmsaUpg1005a` (harness `-ThroughMigration 30` + role scripts + 300 synthetic accounts, 300 requests, 120 transitions) and the local Demo database `SecureOps_SaW1005ui` (030, scans, decisions, history from earlier browser checks): fingerprints of every svcacct table (rows as SHA-256 over all original columns incl. `RowVer`, definition: columns, indexes and their columns, checks, FKs, defaults, triggers) and of all database permissions — 65 lines — identical before and after; a `THROW` injected before `COMMIT` left no column and identical fingerprints; the second run refused; both columns `nvarchar(256) NULL` without default, all values NULL; the API role selects and updates the column through its existing table grants (1/1/1), the Worker role reads requests only (select 1, update 0, transitions 0). A one-row change inside a rolled-back transaction does change the fingerprint (the check is not blind). Then 62/62 module SQL tests on the upgraded `Upg1005a`, no `Number=1205`. |
| 81 | any | new binaries on a database without 031 | the account page, requests, transitions, scans and reports keep working; the requested-name field says the update is missing; a write with a name is refused and writes nothing, a write without a name works. **Windows 2026-10-05: passed.** SQL test `Before031_TheModuleKeepsWorking_AndANameIsRefusedWithNothingWritten` on `SecureOps_SaGmsaOld1005a` (`SECUREOPS_SA_SQL_TEST_CONNECTION_030`): detail `RequestedGmsaNameAvailable` false, request without a name created, with a name 400 `gmsaNameColumnsMissing` and still one row, transition update with a name refused and without one saved, report `GmsaNames` null (unknown, not empty). Local Demo on `SecureOps_SaW1005ui` before 031: choosing "gMSA ile devir" in "Yeni iş" shows "İstenen gMSA adı bu ortamda kaydedilemiyor: veritabanı güncellemesi 031 uygulanmamış. Diğer bilgiler kaydedilir." instead of the field; "Talebi aç" stays enabled. |
| 82 | coordinator (browser) | account → "Yeni iş (talep) aç" → "gMSA geçişi", type a requested gMSA name longer than 15 counted characters, send; then a short one | live counter and warning in a status region, sending stays possible, the server refuses the long name with a Turkish message and keeps the typed input; the short name is saved and listed with its length. **Windows 2026-10-05, local Demo after 031: passed.** `SYN\gmsa_synapp_reports$`: helper "19/15 karakter …", warning "İstenen gMSA adı 19 karakter … Sunucu bu adı kaydetmez …"; "Talebi aç" sent it: API 400, page message "İstenen gMSA adı kaydedilmedi: en çok 15 karakter olabilir …", input kept. `SYN\gmsa_synapp$` (11): saved; the request row shows "İstenen gMSA adı: SYN\gmsa_synapp$ (11/15)". History `RequestCreated` carries `"RequestedGmsaName":"SYN\\gmsa_synapp$"`. Not checked in the browser: the request-update field and its "temizle" option (covered by SQL tests). Side effect of a misplaced Enter during the check: one empty "Değerlendirilecek" request was created on the local Demo database and closed as "İptal edildi" with a reason (synthetic, local only). |
| 83 | assigner (browser) | "Devir ve gMSA" → gMSA transition → requested gMSA name → "İzlemeyi güncelle"; then the live report | the name is saved and shown with its length; a blank field later leaves it unchanged; the report lists requested names. **Windows 2026-10-05, local Demo after 031: passed.** Transition (created through the API with `trackGmsa`) saved with `gmsa_synapp$`: "İstenen gMSA adı gmsa_synapp$ (11/15 karakter)"; history `TransitionUpdated` carries the name; "Canlı raporu göster" → "İstenen gMSA adları (2)": the transition (Uygunluk: Bilinmiyor) and the open request (Açık talep), both 11/15. Blank-keeps-value and the 15-character refusal on transitions are covered by SQL tests, not the browser. XLSX/PDF: the section is in `ReportDocument` (unit test); the generated files were not opened. |
| 84 | coordinator (browser) | account with a scan of 60 matches and 7 attached scans → "Kullanım taraması": pages, "Yalnız karar bekleyenleri göster", a decision on page 2, older scans; keyboard only; 390 px light and dark | totals from the server; 25 items per page undecided first; coverage unchanged by paging or filter; after a decision the page stays and is re-read; older scans reachable; no horizontal scroll. **Windows 2026-10-05, local Demo: passed (one defect found, row 85).** Tab title "Kullanım taraması (7) · 65 karar bekliyor"; "Bu hesaba 7 tarama bağlı; tüm taramalarda 65 bileşen karar bekliyor"; "Taramalar: 1–5 / 7 tarama (sayfa 1 / 2)"; big scan "Bu hesabın bulunduğu bileşenler (60) · 60 karar bekliyor", "Bileşenler: 1–25 / 60 bileşen (sayfa 1 / 3)". Keyboard: Tab to "Sonraki" (2 px ring; disabled "Önceki" skipped), Enter → "26–50 / 60 (sayfa 2 / 3)", focus kept; Shift+Tab to "Karar ver: SYN-APP01 \SynPage50", Enter opened the decision box, dismissal saved → 59 pending, page stayed 2 / 3 and now ends with SynPage51 (the decided one moved to the end); Space on "Yalnız karar bekleyenleri göster (59)" → "Karar bekleyen bileşenler: 1–25 / 59 (sayfa 1 / 3)", the coverage line unchanged; scan pager "Sonraki" → "Taramalar: 6–7 / 7 (sayfa 2 / 2)". 390 × 844 dark and light: `scrollWidth` 390 on the requests and scan tabs, both pagers wrap inside the panel. Scripted focus was used only to place the starting point (browser pane limitation); the scan files were synthetic and uploaded through the API with the Demo actor header. |
| 85 | any (browser) | defects found during rows 82–84 | fixed and pinned. **Windows 2026-10-05 (`ecb1294`).** (a) Keyboard: the dismissal reason committed only on blur, so "Kayda almadan kapat" was still disabled when Tab left the field and focus skipped it; the field now updates while typing (source test). Not re-walked in the browser after the fix (the pane was hidden, keys could not be sent). (b) At 390 px the report trend table was 33 px wider than the page and clipped by the layout; the funnel and trend tables now scroll inside a focusable region: re-checked live, page `scrollWidth` 390, table 395 px inside a 335 px region (`role="region"`, `tabindex="0"`). |
| 86 | coordinator (browser) | list → select 4 accounts → "Tek tarama dosyasını bu hesaplara bağla" → one synthetic file that searched two of them by name, one in two domains (account without domain), and not the fourth → upload; repeat the same file | file checked once; per account Bağlandı / Dosyada yok / Belirsiz; replay "Zaten bağlıydı" and nothing new stored; each account shows only its own component; keyboard, 390 px light and dark without horizontal scroll. **Windows 2026-10-06, local Demo (own ports 5210/5211, `SecureOps_SaW1005ui`, synthetic accounts `svc_synbatch1/2`, `svc_synnodom`): passed after one fix.** Selection with Space, form opened and sent with Enter (focus checked before each Enter); first upload 2 Bağlandı, `svc_synapp` Dosyada yok, `svc_synnodom` Belirsiz; three replays "4 hesaptan 0 tanesine bağlandı, 2 tanesi zaten bağlıydı, 2 tanesine bağlanmadı"; API detail: one scan per account, `SynBatchSvc` only on batch1, `\SynBatchNightly` only on batch2. Fix: the stacked result table was 14 px wider than its container at 390 px (unbreakable searched name beside the text in a flex cell); cells now hold one shrinkable block (335/335). File chosen through a scripted `DataTransfer` in the browser pane, not the OS file dialog. SQL: `ServiceAccountUsageScanBatchSqlTests` 3/3 (scope, basis, replay, refused file, secret guard, 1–20 bounds). |

**Windows run 2026-10-04 at 659a666 (owner's workstation, SDK 9.0.317, LocalDB `SecureOpsResourcesV1`, synthetic data
only).** Build 0/0; integration 301 passed / 107 SQL-gated skipped; `dotnet format --verify-no-changes` clean. Unit
1741 tests at 659a666: two of four full runs failed once in `ServiceAccountUsageModuleTests` with "running scripts is
disabled" — a process-wide execution-policy race with `SccmFailureEvidenceTests` (Restricted runspace), Windows only.
Fixed in `36e6669` (module tests in a non-parallel xUnit collection, prepared by Codex): 1741/1741 in five runs in a row. Rows 59 and 63–66 recorded above. Rows 1–38: **BLOCKED** here — they need approved
TEST OIDC identities (and rows 29–38 the TEST Active Directory). Rows 39–58 and 60–61: **NOT RUN** in this session
(they need product role and scope assignments as different TEST users; row 58 also needs the owner's `.bak`). ADR-0024
module under Windows PowerShell 5.1 (5.1.26100): `Import-Module`, `Test-SoAccountMatch`, `Read-SoIisIdentity`,
`Find-SoAccountUsage`, `Test-SoGmsaConversion` and parameter validation of `Get-SecureOpsAccountUsage` with a synthetic
`applicationHost.config` carrying planted passwords: all checks passed, no password returned, no change needed; no
server was contacted.

**Windows run 2026-10-05 at 63e6d5d (owner's workstation, SDK 9.0.317, LocalDB, synthetic data only; the row 74 fix is `9fc5ee3`).** Baseline: build 0/0; unit 1805 passed, 1 skipped (the collector test that is skipped on Windows on purpose: it would read this machine's services and tasks); integration 302 passed, 112 skipped (SQL-gated and opt-in); `dotnet format --verify-no-changes` clean. None of the Linux-only failures (audit path validator, `SccmFailureEvidenceTests.StagedInvocations_*`) occurred; the SkiaSharp announcement tests are skipped by their own gate. Rows 58 (synthetic copy only), 67–75 recorded above: 69, 70, 71, 72, 73 and 74 passed here, 67, 68 and the `.bak` parts of 58 and 75 were not run. The browser rows used the Demo identity bridge, not corporate OIDC; the role bundle for `demo:team-lead` was created on the local synthetic database with the owner's approval.

**Windows run 2026-10-05 after the review merge, HEAD `9629101` (owner's workstation, SDK 9.0.317, LocalDB, synthetic data only).** The Linux cloud branch `claude/sa-review-20261005` (`0ecc728`, built there with SDK 10 and no SQL Server) merged in `525b5c6`. Build 0/0; unit 1883 passed, 1 intentional skip; integration 303 passed, 113 skipped; `dotnet format --verify-no-changes` clean; rows 69, 70, 72 and 74 extended above, rows 76–78 added and passed. Browser rows again on the local Demo with the identity bridge (not OIDC), on the earlier synthetic database `SecureOps_SaW1005ui`; no new role assignment. Not run: rows 1–38, 39–61 (TEST users), 58/75 on a `.bak`, 67–68 (lab server).

UI (same identities): the team member opens `/service-accounts` and sees "Takibinizdeki işler"
first; the account detail shows the participant notice and only the allowed controls.

## 4A. Local smoke test with the identity bridge (NOT acceptance)

Purpose: check that a local host starts and routes before TEST identities are available.
**It is not corporate or pilot authorization acceptance and must be reported as "local smoke".**
The Test-environment bridge (`DemoAuth__Enabled=true`, `Access__DemoCompatibilityEnabled=false`)
authenticates only two fixed local actors and grants nothing; rights still come from bundles and
module grants as in 4b–4c. Only rows 1–6 of the table can be exercised. The automated equivalent of
rows 1–2 runs without SQL in `ServiceAccountApiCompositionTests`.

## 5. Worker check

With `Hangfire:Enabled=true`, start the Worker once with `ServiceAccounts__Reminders__Enabled=true`
(the recurring job `service-accounts:reminders:v1:<queue>` appears), then restart with
`...Reminders__Enabled=false` and trigger that job from the dashboard. Expected: the job succeeds as a
no-op (no failed or retried jobs, no module SQL), and other recurring jobs are unchanged. The
recurring job is not removed automatically; removing it is an operator decision.

## 6. Evidence to capture

- `git rev-parse HEAD`, `dotnet --info`, SQL Server/LocalDB version.
- All TRX files, `sa-diagnostics.log`, the API and Worker logs for the run.
- For each row in the table: request, status, response body (redact nothing: all data is synthetic).
- `SELECT Action, COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.%' GROUP BY Action;`
- `SELECT program_name, transaction_isolation_level FROM sys.dm_exec_sessions WHERE program_name LIKE '%Service Accounts%';`
  (module sessions use their own pool.)
- `SELECT Role, COUNT(*) FROM svcacct.TeamRoles WHERE RevokedAt IS NULL GROUP BY Role;` and
  `SELECT UsageKind, COUNT(*) FROM svcacct.AccountUsages GROUP BY UsageKind;`
- Screenshots of rows 18–27, 30–34 and 39–49 at 1440 px and 390 px (TEST directory names only; never commit them to the repository).
- A short list of anything that differed from the expected column.
