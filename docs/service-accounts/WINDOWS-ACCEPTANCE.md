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
override (results in `PROGRESS.md`); the Windows G-30 gate itself is still to be run by Codex. Older notes measured with
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
| 58 | SQL operator, copy of the installed database (024–028) | `sqlcmd -I -b -i 029-service-account-scope-bootstrap.sql` from `sql/migrations` | succeeds once; existing grant rows `IsBootstrap = 0`; `CK_SaScopeGrants_NoSelfGrant` trusted; `UX_SaScopeGrants_OneBootstrap` present; a second run fails with "already applied"; installed API keeps granting/revoking normally |
| 59 | SQL operator | `sa-sql-harness.ps1` (default, through 029) | prints "Scope bootstrap installed through a numbered migration" and "candidate 3 replay refused as expected" |
| 60 | user with module View but no scope (e.g. new `sa-ekip-uyesi`) | `/service-accounts`, then `/service-accounts/{id}` | panel "Önce veri kapsamınız tanımlanmalı", current scope "tanımlı değil", team-or-organization step; no filters, no empty table, no not-found error; module administrator without any grant also sees the one-time bootstrap hint |
| 61 | roles from `TEAM-ROLE-SETUP-TR.md` with synthetic TEST users | create the three `sa-*` roles, assign, grant Team / Organization scope | each role sees exactly the links and actions in section 4 of that document; no SQL used |
| 62 | coordinator | `/service-accounts/reports` → live report, then a snapshot | "Görsel özet" first: gMSA funnel, trend, team workload, rule conformance (and gMSA transition when not all zero); each value printed; "Tablo olarak göster" opens the same figures; light/dark and 390 px without horizontal scroll. **Local 2026-10-04: passed with synthetic data (Demo API/UI, LocalDB).** |
| 63 | coordinator | snapshot XLSX and PDF in desktop Excel / Edge / Adobe | XLSX: no repair prompt; charts right after the tiles on "Yönetici özeti", values equal to "Grafik verisi". PDF: page 2 is the chart page; Turkish letters (ğ Ğ ş İ ı) visible in Edge and Adobe. **Local 2026-10-04: XLSX opened in desktop Excel read-only without repair marker, 4 charts rendered (synthetic). PDF Turkish-glyph check in Edge/Adobe NOT done; a preview renderer showed ğ blank (pre-existing standard-font issue).** |
| 64 | coordinator | the same snapshot PDF after `ddbea34` (embedded Liberation Mono) in Edge and Adobe Reader; File → Properties → Fonts | every Turkish letter (Ğ ğ İ ı Ş ş Ç ç Ö ö Ü ü) and the dashes render in both viewers; fonts list "LiberationMono" and "LiberationMono-Bold" as *Embedded*; copying a table line into Notepad gives the same Turkish text. **Linux 2026-10-04: passed in PDFium (the Edge/Chrome engine, via pypdfium2), poppler and pypdf with a synthetic report; not yet opened in Edge or Adobe on Windows.** |
| 65 | SQL operator | module SQL tests on a **fresh** 001–029 LocalDB database, first run, with `SECUREOPS_SA_SQL_DIAGNOSTICS` set | 49/49 (now 50 with `ConcurrentDictionarySaves_InTheSameKeyGap_AllCommitWithoutDeadlock`) on the first run; no `Number=1205` in the diagnostics file. If `AccountExport_RowLimitIsExact_AndRefusalIsNotAudited` still fails once, keep the diagnostics line: `Number=1205` means another deadlock site, `Number=-2` a command timeout. **Linux 2026-10-04: two fresh databases, first run 48 passed + 2 LocalDB-only, no 1205.** |
| 66 | SQL operator | `powershell -NoProfile -File tests\sql\service-accounts\sa-sql-harness.ps1 -DatabaseSuffix X -SkipRoleScripts; $LASTEXITCODE` | prints the connection line and `0` (was `1` before the fix) |

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
