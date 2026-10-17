# Combined .NET 10 opt-in validation, 2026-10-06

## Verdict

109 of the 110 opt-in cases skipped by the baseline solution run were executed and passed.
One browser-artifact test remains NOT RUN. This is Windows local validation, not release,
deployment, IIS, corporate authentication, provider or real-send acceptance.

Worktree: `C:\SecureOpsBuild\secure-ops-dotnet10-followups-2`.
Branch: `codex/dotnet10-followups-2`, based on `520b07d9aef4f033c26ca3a37b2b3a7de95d3a96`
(`origin/claude/dotnet10-ui-shell`: combined PR #8/#9/#13, PR #10, harness 028 fix).
SDK 10.0.401, .NET 10, Release; no toolchain substitution.
Private evidence root: `C:\SecureOpsBuild\validation\dotnet10-followups-2-20261006`.

## Evidence

Every group used a NEW prefix-protected database on `(localdb)\SecureOpsResourcesV1`.
All databases and evidence were retained. No installed database was reused or upgraded.
The Resource harness applied 001-028. Source Hangfire schema 9 was installed separately
from the installed Hangfire.SqlServer 1.8.6 `tools/install.sql`; runtime preparation stayed off.

| Group | Environment and filter | Passed opt-ins | Database suffix / TRX |
|---|---|---:|---|
| Source SQL + actual API/Worker | `SECUREOPS_SQL_TEST_CONNECTION`, `SECUREOPS_SOURCE_HOST_ACCEPTANCE=1`, `SECUREOPS_SOURCE_EVIDENCE`; `AnnouncementSourceSqlTests` OR `AnnouncementSourceAcceptanceTests` | 6 + 1 | `OcoSourceF2_20261006`; `source.trx` |
| Access guards | `SECUREOPS_ACCESS_GUARD_CONNECTION`; `AccessAdministrationSqlTests` | 1 | `OcoAccessGuardsF2_20261006`; `access.trx` |
| rc621 canonical actor / effective permission | `SECUREOPS_RC621_DEFECT_CONNECTION`; `OidcAdmin_SqlRoleRemoval` | 1 | `Rc621DefectsF2_20261006`; `rc621-corrected.trx` |
| Mail SQL | `SECUREOPS_MAIL_SQL_CONNECTION`; `AnnouncementMailSqlTests` | 1 | `OcoMailF2_20261006`; `mail.trx` |
| Preparation + role audit rollback | `SECUREOPS_PREPARATION_SQL=1`, `SECUREOPS_PREPARATION_SQL_CONNECTION`; `AnnouncementTests` | 2 | `OcoPreparationF2_20261006`; `preparation.trx` |
| Service Accounts SQL | `SECUREOPS_SA_SQL_TEST_CONNECTION`; `ServiceAccounts` AND `SqlTests` | 42 | `SecureOps_SaF2_20261006`; `sa.trx` |
| Resource SQL + draft/discovery | `SECUREOPS_SQL_TEST_CONNECTION`; `ResourceSqlTests` OR `Discovery_Sql` OR `DraftApi_Persists` | 53 + 2 | `OcoAllF2_20261006`; `resource-draft.trx` |

All `dotnet test` invocations used
`tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-build`,
`--filter FullyQualifiedName~...`, `--logger trx;LogFileName=<above>` and the private
`--results-directory`. Preparation's broader filter also passed 24 ordinary cases and
skipped the two draft/discovery cases (executed separately) and the browser-artifact case.
Resource/draft's 58 passes include three ordinary Resource cases, not 58 SQL opt-ins.

Actual source-process evidence is in `source-e7*` or the unique `source-*` directory
under the evidence root: `acceptance.json`, two API/Worker generations' host logs and
reviewed-v2 HTML/EML. API PID 29276; recovered Worker PID 31140; loopback port 57804;
queue `source-ebdd4585`; recovered job `e3844e38-77cb-4de5-bf3f-f953745307c4`;
`Attempts=2`, draft version 5, 154.2 seconds. HTTP 202, queue readiness, owner isolation,
review/apply, immutable preparation, actual killed-Worker lease recovery, obsolete attempt
rejection and duplicate dispatch were asserted. No automatic apply or send occurred.
This used the exact current `bin/Release/net10.0` assemblies, not a published release payload.

SA PowerShell harnesses ran unchanged on separate fresh databases:
`sa-sql-harness.ps1` through 028 plus candidate replay refusals and unnumbered role scripts;
`admin-navigation-migration.ps1` (`SecureOps_SaNavF2_20261006`), 17 checks;
`admin-operations-harness.ps1` (`SecureOps_SaOpsF2_20261006`), 9 checks;
`sa-upgrade-harness.ps1` (`SecureOps_SaUpgradeF2_20261006`), 001-023 baseline retention,
024/025/026 delta, transactional failure and replay refusals. Logs: `sa-027.log`,
`sa-028.log`, `sa-upgrade.log`, `sa-setup.log`. No runtime-role member was assigned;
`SECUREOPS_SA_SQL_RUNTIME_CONNECTION` was not enabled, so this is not a runtime-role run.

Baseline gates: `dotnet build SecureOps.sln -c Release` (0 warnings/errors),
`dotnet test SecureOps.sln -c Release --no-build` (1,769 unit + 332 integration passed;
110 integration opt-ins skipped), and `dotnet format SecureOps.sln --verify-no-changes`
passed. Task commit gates are also rerun after the code commit and reported separately.

## Blockers

Initial `draft.trx`: discovery failed ONLY at `plans.Should().NotBeEmpty()` after
240 drafts / 1,200 revisions, paging, owner isolation and concurrent-reader assertions passed.
Inspection confirmed no matching cached temporary-table batch. Cache retention is an optional
measurement, not a product correctness contract. The small harness correction logs missing
measurement explicitly, preserving all behavioural and public application-lock assertions.
Fresh `resource-draft.trx` rerun passed. No product code or decision changed.

Initial rc621 filter matched zero cases (`rc621.trx`); it is NOT a pass. The corrected
filter above executed and passed the actual opt-in. No other failure was observed.

`BrowserDownload_ParsesSavedTurkishContentAndSixMatchingCidImagesWithoutSending` remains
NOT RUN: no fresh browser-downloaded `representative.eml`, `saved-preview.html` and
`saved-draft.json` were supplied via `SECUREOPS_ANNOUNCEMENT_BROWSER_EVIDENCE`.
Generated source-test EML is not substituted for that browser evidence.

## Minimal Safe Next Step

Run the permitted browser journey with fresh synthetic evidence, then execute its MIME
artifact test. Owner handles push; this task makes local commits only.

## Risks

Loopback SMTP sink only; no SMTP relay, SCCM, Turuncuhat, Jira, AD, PAM, LDAP, corporate
SQL, TEST, IIS, service installation or deployment was contacted or changed. Actual SQL
audit rollback is tested; no audit protection was disabled and no audit row was updated/deleted.
Linux, published payload acceptance, corporate/desktop/browser gates and real delivery remain unverified.
