# Tests

## Toolchain And Rules

SDK 10.0.401 (`global.json`, roll-forward disabled), .NET 10, C# 14, analyzer level 10.0.
Use the pinned SDK without language-version overrides. Warnings are errors.
xUnit v3 (`xunit.v3.mtp-off`) runs through VSTest with runner 4, Test SDK 18, FluentAssertions 7.2,
NSubstitute 6 and coverlet 10. Custom Fact/Theory attributes forward caller file/line;
`xUnit1051` requires the current test cancellation token for ordinary calls.
Blazor render tests use HtmlRenderer, not bUnit.

- Unit: fast, isolated, no I/O; mock external dependencies. Tests mirror source namespaces.
- Integration: in-process API host with deterministic fakes; opt-in SQL/process cases use guarded local fixtures.
- No sleeps waiting for state, shared mutable test state, log-text assertions or real external services.
- No hard-coded user paths. Use synthetic data only; no corporate scripts, archives or workbooks as public fixtures.
- Windows DPAPI cases must execute on Windows; no substituted SDK/platform pass or waived platform failures.
- Never contact corporate AD/PAM/LDAP/SQL, IIS, load balancers, SCCM, Turuncuhat, Jira or real SMTP.
- Report skipped opt-ins as NOT RUN. SQL, rendered UI, browser, published payload, corporate TEST and provider evidence are distinct.

Full quality gate and coverage expectations: [090-testing-quality.md](../docs/agent-guides/090-testing-quality.md).
Domain coverage target 90%+, application 80%+. Projects:
`SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj` and
`SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj`.

## Required Gates

Run from the repo root on a Windows CRLF checkout:

```powershell
dotnet build SecureOps.sln -c Release
dotnet test SecureOps.sln -c Release --no-build
dotnet format SecureOps.sln --verify-no-changes
git diff --check
```

Build must have zero warnings/errors. Repository-wide format is required; scoped formatting
is partial evidence. Linux LF ENDOFLINE failures are not Windows format verification.
Explicit cancellation inputs remain explicit. For Windows PowerShell/runspace fixtures,
only temporary synthetic drives/modules are permitted; never import ConfigurationManager or contact SCCM.

## LocalDB Opt-Ins

Only `(localdb)\SecureOpsResourcesV1`, NEW prefix-protected databases and integrated authentication.
Never reuse an installed/shared database, reset old transfers, disable append-only protection
or apply corporate migrations. Resource fixture code verifies server, prefix and credentials;
SA/other harnesses are hard-bound to the same isolated instance. Keep prior test evidence intact.

| Group | Process environment | Guarded harness / test selection |
|---|---|---|
| Resource SQL, draft/discovery, session concurrency | `SECUREOPS_SQL_TEST_CONNECTION` | `scripts/powershell/Test-ResourceCatalogueSql.ps1`, fresh `SecureOps_ResourcesV1_*`, migrations 001-028; `ResourceSqlTests`, relevant `AnnouncementTests` |
| Source SQL + API/Worker recovery | Above plus `SECUREOPS_SOURCE_HOST_ACCEPTANCE=1`, `SECUREOPS_SOURCE_EVIDENCE` | Fresh `OcoSource*` suffix; `AnnouncementSourceSqlTests` / `AnnouncementSourceAcceptanceTests`; Hangfire schema 9 provisioned separately, runtime PrepareSchema=false |
| Access guards | `SECUREOPS_ACCESS_GUARD_CONNECTION` | Fresh `OcoAccessGuards*` suffix; `AccessAdministrationSqlTests` |
| rc621 actor/permission | `SECUREOPS_RC621_DEFECT_CONNECTION` | Fresh `Rc621Defects*` suffix; `OidcAdmin_SqlRoleRemoval` |
| Mail SQL | `SECUREOPS_MAIL_SQL_CONNECTION` | Fresh `OcoMail*` suffix; `AnnouncementMailSqlTests`, loopback SMTP sink only |
| Preparations | `SECUREOPS_PREPARATION_SQL=1`, `SECUREOPS_PREPARATION_SQL_CONNECTION` | `Test-AnnouncementPreparationsSql.ps1`, fresh `OcoPreparation*`; preparation and role-audit cases |
| Service Accounts SQL | `SECUREOPS_SA_SQL_TEST_CONNECTION`; optional `SECUREOPS_SA_SQL_RUNTIME_CONNECTION` | `tests/sql/service-accounts/sa-sql-harness.ps1`, fresh `SecureOps_Sa*`; `ServiceAccounts` AND `SqlTests`; optional runtime-role run is separate evidence |
| Browser MIME artifact | `SECUREOPS_ANNOUNCEMENT_BROWSER_EVIDENCE` | Actual browser-downloaded representative EML, saved preview/draft; `BrowserDownload_ParsesSavedTurkishContentAndSixMatchingCidImagesWithoutSending` |

With a configured fresh group connection, run the integration project with
`-c Release --no-build --filter 'FullyQualifiedName~<class-or-method>'`,
a TRX logger and a fresh private results directory. Do not count zero matching cases as a pass.
For matched release acceptance, `SECUREOPS_SOURCE_PAYLOAD_ROOT` must identify the reviewed published
payload; without it source process acceptance uses current bin/Release assemblies.
SA 027/028 and upgrade harnesses are in `tests/sql/service-accounts/`; preserve their scope/refusal guards.
Paired-release SQL selection has `tests/release/Test-PairedReleaseSqlSelection.ps1`;
its optional SqlCmd path uses only the hard-coded disposable LocalDB instance and a new evidence directory.

## Feature Evidence On Demand

[Preserved detailed test/fixture/browser history](../docs/archive/agent-context-20261006/tests-README.before.md)
contains every earlier handoff, runner argument, recovery/reporter/workbook case and unexecuted gate.
Search by feature; historical claims do not establish current acceptance.
Current feature contracts are under `docs/contracts/`; the canonical activation register is
`docs/integrated-test-activation.md`. Focused .NET 10/F3 evidence is under `docs/validation/`.
Browser runners are `tests/browser/*.cjs`: use fresh loopback-only Demo/Simulation hosts, synthetic fixtures
and a private evidence root; UI HTTPS/API HTTP follows the runner's profile. Assign synthetic roles through
the product API where the journey requires it. Real browsers, native zoom, screen readers, SCM/logoff/crash
recovery, corporate OIDC/desktop/provider checks and actual delivery require their own evidence/authorization.
