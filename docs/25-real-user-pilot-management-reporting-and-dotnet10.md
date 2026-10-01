# Real-User Pilot, Management Reporting, and .NET 10 Readiness

## Pilot Authentication and Persistence

The corporate OIDC path is OIDC validation -> bounded normalized issuer/subject/login claims -> `ICorporatePrincipalResolver` -> persisted SecureOps approval -> role -> capability. Do not add an LDAP password form. Negotiate and Demo compatibility do not enter the real first-Admin bootstrap gate.

For first real-user TEST bootstrap, preserve the current server-owned configuration and apply these exact environment-variable decisions through the controlled deployment process:

| Key | Pilot value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Test` |
| `DemoAuth__Enabled` | `false` |
| `Access__DemoCompatibilityEnabled` | `false` |
| `Access__RepositoryProvider` | `SqlServer` |
| `SessionSecurity__RepositoryProvider` | `SqlServer` |
| `SessionSecurity__IdleTimeoutMinutes` / `AbsoluteLifetimeHours` / `ActivityPersistenceIntervalMinutes` | `30` / `12` / `5` |
| `DataProtection__Mode` / `ApplicationName` | `FileSystemDpapi` / `SecureOps.Api` |
| `DataProtection__KeyRingPath` | absolute server-owned key-ring path outside deployment payload |
| `Access__AutoCreateRequest` | `true` |
| `BootstrapAdmin__Enabled` | `true` only for the controlled first OIDC Admin login; otherwise `false` |
| `BootstrapAdmin__LoginName` | one approved exact runtime-only OIDC login name |
| `BootstrapAdmin__AllowedIssuer` | exact approved HTTPS issuer, equal to `Oidc__Authority` |
| `Audit__Provider` | `SqlServer` |
| `Audit__FailClosed` | `true` |
| `Audit__RequirePersistentStoreInProduction` | `true` |
| `Audit__Queue__Enabled` / `Capacity` / `FullBehavior` / `FlushIntervalSeconds` | `true` / `1000` / `FailClosed` / `1` |
| `OperationalRecords__RepositoryProvider` | `SqlServer` |
| `OperationalRecords__SourceProvider` | `Disabled` for a production-style real-user pilot; `Fake` only for explicitly synthetic TEST |
| `Jira__Provider` | `Disabled` for a production-style real-user pilot; `Fake` only for explicitly synthetic TEST |
| `IdentityLookup__Provider` | `ActiveDirectory` |
| `IdentityLookup__DomainName` | approved AD DNS domain |
| `IdentityLookup__EnableUpnLookup` | approved `true` or `false`; effective capability remains provider-governed |
| `PamProvider__Provider` | `Mock`; this is pass-through metadata only and is not a real PAM connector |
| `ConnectionStrings__SecureOpsDb` | server-owned `SecureOpsDb` connection string using Windows Integrated Security, `Encrypt=True`, `TrustServerCertificate=False` |

The runtime identity remains `DOMAIN\WASAST_YONETIM`. Do not configure SQL credentials or KRON/AAPM for this connection. Migrations 001-008 and grants must be completed before SQL providers are selected. The App Pool identity needs read/write/create permission only on the configured Data Protection key-ring directory; no key material is deployed from Git. After the first audited OIDC Admin grant, set `BootstrapAdmin__Enabled=false` and restart in a controlled window. Historical Admin assignment rows permanently prevent reuse even if configuration is left enabled; revocation does not reopen bootstrap.

## Reporting API and Windows

- `GET /api/v1/reporting/management/summary`
- `GET /api/v1/reporting/management/operators`
- Query `window=today|7d|30d|custom`; custom requires `from` and `to` and is capped at 92 days.
- All boundaries use UTC and `[fromInclusive, toExclusive)` semantics.
- Operator results use `page` and `pageSize`; page size is capped at 100.
- Both routes require `Reporting.ManagementView` and audit their privileged reads.

## Metric Definitions

Identity Lookup:

- Total lookups: terminal exact-account audit events only: succeeded, not found, rejected, failed, provider timeout, or forbidden. Requested/provider/cache events are excluded to prevent double-counting.
- Successful, not found, and rejected: their matching terminal action counts.
- Provider unavailable: failed plus provider-timeout terminal actions.
- Unique active operators: distinct non-system actors with a terminal lookup event.
- Trends: terminal outcomes grouped by UTC day.

Operational Record/Jira:

- Imported, eligible, Jira created, completed: distinct records entering the matching durable workflow state in the selected window.
- Previewed: successful `JiraPreviewGenerated` audit events.
- Source-changed and closed/missing prevented: `OperationalRecordSourceChanged`, split by its persisted error code.
- Failures: audited Jira-create and source-close failures.
- Reconciliation required: records whose persisted transfer is currently reconciliation-blocked and was updated in the window.
- Retry outcomes: audited retry requests correlated to completed or failed terminal events.
- Duplicate/idempotent prevention: explicit prevention audit events recorded from this release forward. No historical value is fabricated.

Adoption and Security:

- Daily/weekly/monthly active users: distinct non-system actors with a reviewed business-operation audit action in trailing 1/7/30-day windows ending at report `to`.
- Operations by user: reviewed business-operation action count, paginated, with no directory profile enrichment.
- Operations by workflow: server-side categories for Identity, Access, and Operational Record/Jira.
- Access activity: requested, approved, rejected, disabled, role assigned, and role removed action counts.
- Authorization failures: `AuthorizationDenied` plus `IdentityLookupForbidden`.
- Concurrency conflicts: audited Operational Record claim/state conflicts. Historical access-version conflicts are unavailable because they were not audited.
- Provider unavailable: identity provider failure/timeout audit events. Operational-source query outages are unavailable historically when no audit event exists.
- Rate-limit events: unavailable until rate-limit rejection auditing is explicitly designed.
- Application sessions: reliable starts, idle/absolute timeouts, logout, administrator revocation, access-disable termination, and access-version termination are aggregated. Heartbeats are excluded.

Durations report sample count, minimum, average, and maximum elapsed seconds for import -> preview, claim -> Jira creation, and claim -> completion. Stable keys are `importToPreview`, `claimToJiraCreation`, and `claimToCompletion`; UI/application behavior must not use English definitions or array order. Durations include waits and retries. They are not manual effort, active handling time, time saved, or operator performance.

Both report routes expose additive evidence coverage. `coverageFromUtc` is the earliest retained persisted event in the known reporting-action catalog. Coverage is complete only when that boundary exists at or before the requested start. An incomplete zero is not historical evidence of zero. See `docs/30-management-reporting-contract-hardening.md` for the algorithm and stable limitation codes.

## Database Impact

Migration 005 creates limited `reporting` views over audit/workflow data and supporting indexes. Migration 007 creates authoritative `security.ApplicationSessions` plus a limited reporting view. Runtime needs `SELECT, INSERT, UPDATE` on the session table and `SELECT` on each reporting view. It still needs no `SELECT` on base `audit.AuditLog` or history tables, no `DELETE`, no DDL, and no schema ownership.

## Known Historical Gaps

- Browser/UI actions are intentionally excluded.
- Existing rows cannot prove manual-process duration or time saved.
- Historical rate-limit rejections, access concurrency failures, source-query outages, and bulk-invalid item outcomes are incomplete or absent.
- A future manual baseline must use an approved, bounded sample: record start/end timestamps and workflow type for the old process, collect no content beyond operational references, aggregate at team level, and compare equivalent work classes and time windows.
- G-14 stable duration keys, G-15 coded limitations, and G-17 evidence coverage are resolved by the additive backend contract. G-16 adoption and Operational Record/Jira trend series remain explicitly unresolved and out of scope.

## Direct Package Inventory

The solution has 38 unique direct package references:

| Package | Version | Used by |
|---|---:|---|
| coverlet.collector | 6.0.0 | tests |
| Dapper | 2.1.28 | Infrastructure |
| FluentAssertions | 6.12.0 | tests |
| FluentValidation | 11.9.0 | API |
| FluentValidation.AspNetCore | 11.3.0 | API |
| Hangfire.AspNetCore | 1.8.6 | API |
| Hangfire.Core | 1.8.6 | Infrastructure, Worker |
| Hangfire.SqlServer | 1.8.6 | Infrastructure, Worker |
| JsonSchema.Net | 6.0.0 | tests |
| Microsoft.AspNetCore.Authentication.Negotiate | 8.0.30 | API, UI |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.30 | integration tests |
| Microsoft.AspNetCore.OpenApi | 8.0.30 | API |
| Microsoft.Data.SqlClient | 5.2.3 | Infrastructure |
| Microsoft.EntityFrameworkCore | 8.0.30 | Infrastructure |
| Microsoft.EntityFrameworkCore.Design | 8.0.30 | Infrastructure |
| Microsoft.EntityFrameworkCore.SqlServer | 8.0.30 | Infrastructure |
| Microsoft.Extensions.Configuration.Abstractions | 8.0.0 | Infrastructure |
| Microsoft.Extensions.Hosting | 8.0.1 | Worker |
| Microsoft.Extensions.Hosting.WindowsServices | 8.0.1 | Worker |
| Microsoft.Extensions.Http.Polly | 8.0.30 | Infrastructure, UI |
| Microsoft.Extensions.Options.ConfigurationExtensions | 8.0.0 | Infrastructure |
| Microsoft.NET.Test.Sdk | 17.8.0 | tests |
| MudBlazor | 6.16.0 | UI |
| NSubstitute | 5.1.0 | tests |
| Polly | 8.3.0 | Infrastructure |
| Serilog.AspNetCore | 8.0.0 | API, UI |
| Serilog.Extensions.Hosting | 8.0.0 | Worker |
| Serilog.Settings.Configuration | 8.0.0 | API, UI, Worker |
| Serilog.Sinks.Console | 5.0.1 | API, UI, Worker |
| Serilog.Sinks.File | 5.0.0 | API, UI, Worker |
| Serilog.Sinks.MSSqlServer | 6.5.1 | API, UI, Worker |
| Swashbuckle.AspNetCore | 6.5.0 | API |
| System.DirectoryServices | 8.0.0 | Infrastructure |
| System.DirectoryServices.AccountManagement | 8.0.1 | Infrastructure |
| System.Management.Automation | 7.4.18 | Infrastructure |
| System.Text.Json | 8.0.6 | integration tests |
| xunit | 2.6.6 | tests |
| xunit.runner.visualstudio | 2.5.6 | tests |

## Security and Deprecation Findings

The 2026-08-23 direct/transitive vulnerability scan reported no vulnerable packages from the configured sources. The deprecation scan flags direct `FluentValidation.AspNetCore` as legacy and xUnit v2 in both test projects as legacy. It also reports transitive legacy/deprecated families including `Polly.Extensions.Http`, IdentityModel 6.35, and older Azure Identity dependencies. These require dependency-owner tracing and bounded upgrades; they are not evidence that arbitrary latest-version upgrades are safe.

## .NET 10 Readiness Verdict

.NET 8 support ends 2026-11-10; .NET 10 is active LTS through 2028-11-14. The current machine has .NET 10.0.11 runtime components but no .NET 10 SDK, so a net10 compile/test proof cannot be produced here. The solution also crosses major versions for ASP.NET Core, EF Core, OpenAPI/Swashbuckle, SQL client, logging, validation, and tests. The UI and MudBlazor migration requires the UI owner's separate review. Therefore migration is required soon but is not low risk enough to combine with this reporting milestone.

Bounded migration sequence:

1. Install an approved current .NET 10 SDK on the build machine and .NET 10 Hosting Bundle on a non-production IIS validation server.
2. Create a dedicated migration branch; change the TFM and align Microsoft.AspNetCore, Microsoft.Extensions, EF Core, DirectoryServices, and test-host packages to the same supported 10.0 patch.
3. Remove/replace deprecated FluentValidation ASP.NET integration and review the Polly resilience migration separately.
4. Review Microsoft.Data.SqlClient major-version behavior, Swashbuckle/OpenAPI output changes, Windows Negotiate, AD exact lookup, PowerShell hosting, Hangfire SQL, Serilog sinks, and xUnit migration.
5. Run complete unit/integration/OpenAPI/package/runtime-publish gates, then validate Windows auth, AD, SQL integrated security, IIS in-process hosting, and rollback in controlled TEST.

Official references: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [.NET 10 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10), [ASP.NET Core 9 to 10 migration](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100), and [.NET 10 IIS Hosting Bundle](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0).

## Historical NuGet Inventory: 2026-09-06

Preserved from the existing ignored, uncommitted
`artifacts/nuget-inventory-sdm-2026-09-06.md`, not reconstructed from memory.
This tracked maintenance record retains the original inspection date, sanitized
A-E version tables, direct/transitive distinctions, deprecations, risk notes,
and recommended batches. The repetitive full resolved-graph appendix stays in
the local artifact; the actionable reviewed tables are preserved below.

Every "current", "latest", compatibility assessment and recommendation below is
**historical metadata observed on 2026-09-06**, not freshly verified by this
documentation preservation. Refresh public package metadata and dependency-owner
bounds before any separately approved update. No proposed version was restored,
no package/target framework changed, and no update is authorized by this record.

Resource backend feature commit: `1bb8fcf6606d1b6a77c6aad72f0a97459f92a97d`.
A separate fresh vulnerability-only check during that task used
`dotnet list SecureOps.sln package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json`
and reported no vulnerable packages across eight projects. Outdated-version and
deprecation metadata were **not re-queried** for this preservation. That clean
advisory result neither refreshes the historical version tables nor removes the
documented deprecation and compatibility risks.

### Preserved Findings

Observed 2026-09-06 from public NuGet metadata after commit `c18d196ba14905df92e57fe231c2e31b95d113de`.

### Scope and evidence

- Read-only package audit: 43 central pins, 60 PackageReference declarations, 40 distinct direct packages, 178 distinct resolved package IDs, 220 distinct package/version/direct-or-transitive rows across eight projects.
- Inspected Directory.Packages.props, every csproj PackageReference, resolved direct/transitive graph, stable outdated versions (unrestricted and highest-minor), deprecations and vulnerabilities. No prerelease recommendations; no package changes, restore of proposed versions, or committed inventory.
- Commands: `dotnet list SecureOps.sln package --include-transitive --format json`; add `--outdated`, `--outdated --highest-minor`, `--deprecated`, or `--vulnerable` with public nuget.org source. Selected latest and conservative package nuspec dependency framework groups were also inspected using the public flat-container API.
- Framework-compatible means package metadata accepts .NET 8, NOT that the complete upgraded dependency graph or application has passed restore/tests. No proposed version was installed. Highest-minor alone is not a framework compatibility check (PowerShell 7.6 is a counterexample).
- Latest compatible stable and recommended conservative target are deliberately separate below. Major upgrades compatible with .NET 8 are review-only options in C, not falsely labeled .NET 10-required. Runtime support and package support are distinct.

### A. Urgent security updates

No vulnerable direct or transitive packages reported by the NuGet vulnerability scan. This is advisory-feed evidence, not a claim that all dependencies are supported or vulnerability-free. Deprecated identity dependencies warrant prioritized B5 review, but deprecation alone is not a confirmed security advisory.

### B. Safe patch candidates

Patch candidates remain conditional on their batch tests; SQL/job/logging patches are not zero-risk.

| Package | Current | Latest .NET 8-compatible stable | Proposed conservative target | Kind | Risk / breaking surface | Batch / required tests |
|---|---|---|---|---|---|---|
| Dapper | 2.1.28 | 2.1.79 | 2.1.79 | Direct | High for majors; medium otherwise; SQL mapping, transactions, encryption/authentication and provider behavior | B2-data: SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |
| Hangfire.Core | 1.8.6 | 1.8.25 | 1.8.25 | Direct | Medium; Job serialization, retries, SQL schema compatibility; no automatic storage upgrade approval | B3-jobs: Worker retry/idempotency and fake storage tests; separately authorized SQL storage validation |
| Hangfire.AspNetCore | 1.8.6 | 1.8.25 | 1.8.25 | Direct | Medium; Job serialization, retries, SQL schema compatibility; no automatic storage upgrade approval | B3-jobs: Worker retry/idempotency and fake storage tests; separately authorized SQL storage validation |
| Hangfire.SqlServer | 1.8.6 | 1.8.25 | 1.8.25 | Direct | Medium; Job serialization, retries, SQL schema compatibility; no automatic storage upgrade approval | B3-jobs: Worker retry/idempotency and fake storage tests; separately authorized SQL storage validation |
| Serilog.AspNetCore | 8.0.0 | 10.0.0 | 8.0.3 | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Serilog.Settings.Configuration | 8.0.0 | 10.0.1 | 8.0.4 | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| FluentAssertions | 6.12.0 | 8.10.0 | 6.12.2 | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| coverlet.collector | 6.0.0 | 10.0.1 | 6.0.4 | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| System.Management.Automation | 7.4.18 | 7.4.19 | 7.4.19 | Direct | Medium; high for majors; Hosting/DI configuration, platform APIs and JSON behavior | B10-platform: Full suite, host startup/fail-closed configuration, JSON hashing and serialization |

PowerShell 7.4.19 is the .NET 8 line; pair Microsoft.PowerShell.CoreCLR.Eventing with it. Coverlet 6.0.4 should be evaluated with the test SDK/runner batch, not assumed compatible with all existing tooling.

### C. Review-required minor updates and separate compatible-major options

The first table proposes conservative minor batches. Compatible-major alternatives shown in the latest column require a separate migration review and are not included in those batches.

| Package | Current | Latest .NET 8-compatible stable | Proposed conservative target | Kind | Risk / breaking surface | Batch / required tests |
|---|---|---|---|---|---|---|
| FluentValidation | 11.9.0 | 12.1.1 | 11.12.0 | Direct | Medium; high for replacement; Validation pipeline, async rules and dependency injection | B6-validation: Invalid request, authorization order and validation response tests |
| FluentValidation.AspNetCore | 11.3.0 | 11.3.1 | 11.3.1 | Direct | Medium; high for replacement; Validation pipeline, async rules and dependency injection | B6-validation: Invalid request, authorization order and validation response tests |
| JsonSchema.Net | 6.0.0 | 9.4.0 | 6.1.2 | Direct | Medium; high for majors; Schema IDs, enum/nullability serialization and schema validation APIs | B4-contracts: OpenAPI equality, API serialization, frozen enums and schema tests |
| Microsoft.NET.Test.Sdk | 17.8.0 | 18.9.0 | 17.14.1 | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| MudBlazor | 6.16.0 | 9.9.0 | 6.21.0 | Direct | High; Claude-owned; Component signatures, forms, dialogs, styles and Blazor dependencies | B7-ui: Claude-owned UI build, interaction/accessibility and desktop/mobile visual tests |
| NSubstitute | 5.1.0 | 6.2.0 | 5.3.0 | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| Polly | 8.3.0 | 8.7.0 | 8.7.0 | Direct | Medium; Retry counts, cancellation, timeout and legacy policy integration | B9-resilience: Fake external-client retries/cancellation, no-write fences and startup validation |
| Serilog.Sinks.MSSqlServer | 6.5.1 | 10.0.0 | 6.7.1 | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Swashbuckle.AspNetCore | 6.5.0 | 10.2.3 | 6.9.0 | Direct | Medium; high for majors; Schema IDs, enum/nullability serialization and schema validation APIs | B4-contracts: OpenAPI equality, API serialization, frozen enums and schema tests |
| xunit | 2.6.6 | 2.9.3 | 2.9.3 | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| xunit.runner.visualstudio | 2.5.6 | 4.0.0 | 2.8.2 | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |

FluentValidation.AspNetCore 11.3.1 is numerically a patch but requires FluentValidation/DI extensions >=11.11.0. Treat it as B6, not an isolated patch. The ASP.NET integration package remains unsupported; replacing automatic validation requires an explicit behavior-change milestone. [Maintainer guidance](https://docs.fluentvalidation.net/en/latest/aspnet.html).

#### Compatible major options, not immediate updates

| Package | Current | Latest .NET 8-compatible stable | Recommendation | Kind | Risk / breaking surface | Batch / required tests |
|---|---|---|---|---|---|---|
| coverlet.collector | 6.0.0 | 10.0.1 | Separate approval; retain conservative train first | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| FluentAssertions | 6.12.0 | 8.10.0 | Separate approval; retain conservative train first | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| FluentValidation | 11.9.0 | 12.1.1 | Separate approval; retain conservative train first | Direct | Medium; high for replacement; Validation pipeline, async rules and dependency injection | B6-validation: Invalid request, authorization order and validation response tests |
| JsonSchema.Net | 6.0.0 | 9.4.0 | Separate approval; retain conservative train first | Direct | Medium; high for majors; Schema IDs, enum/nullability serialization and schema validation APIs | B4-contracts: OpenAPI equality, API serialization, frozen enums and schema tests |
| Microsoft.Data.SqlClient | 5.2.3 | 7.0.2 | Separate approval; retain conservative train first | Direct | High for majors; medium otherwise; SQL mapping, transactions, encryption/authentication and provider behavior | B2-data: SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |
| Microsoft.Extensions.Configuration.Abstractions | 8.0.0 | 10.0.11 | Separate approval; retain conservative train first | Direct | Medium; high for majors; Hosting/DI configuration, platform APIs and JSON behavior | B10-platform: Full suite, host startup/fail-closed configuration, JSON hashing and serialization |
| Microsoft.Extensions.Hosting | 8.0.1 | 10.0.11 | Separate approval; retain conservative train first | Direct | Medium; high for majors; Hosting/DI configuration, platform APIs and JSON behavior | B10-platform: Full suite, host startup/fail-closed configuration, JSON hashing and serialization |
| Microsoft.Extensions.Hosting.WindowsServices | 8.0.1 | 10.0.11 | Separate approval; retain conservative train first | Direct | Medium; high for majors; Hosting/DI configuration, platform APIs and JSON behavior | B10-platform: Full suite, host startup/fail-closed configuration, JSON hashing and serialization |
| Microsoft.Extensions.Http.Polly | 8.0.30 | 10.0.11 | Separate approval; retain conservative train first | Direct | Medium; Retry counts, cancellation, timeout and legacy policy integration | B9-resilience: Fake external-client retries/cancellation, no-write fences and startup validation |
| Microsoft.Extensions.Options.ConfigurationExtensions | 8.0.0 | 10.0.11 | Separate approval; retain conservative train first | Direct | Medium; high for majors; Hosting/DI configuration, platform APIs and JSON behavior | B10-platform: Full suite, host startup/fail-closed configuration, JSON hashing and serialization |
| Microsoft.NET.Test.Sdk | 17.8.0 | 18.9.0 | Separate approval; retain conservative train first | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| MudBlazor | 6.16.0 | 9.9.0 | Separate approval; retain conservative train first | Direct | High; Claude-owned; Component signatures, forms, dialogs, styles and Blazor dependencies | B7-ui: Claude-owned UI build, interaction/accessibility and desktop/mobile visual tests |
| NSubstitute | 5.1.0 | 6.2.0 | Separate approval; retain conservative train first | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| Serilog.AspNetCore | 8.0.0 | 10.0.0 | Separate approval; retain conservative train first | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Serilog.Extensions.Hosting | 8.0.0 | 10.0.0 | Separate approval; retain conservative train first | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Serilog.Settings.Configuration | 8.0.0 | 10.0.1 | Separate approval; retain conservative train first | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Serilog.Sinks.Console | 5.0.1 | 6.1.1 | Separate approval; retain conservative train first | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Serilog.Sinks.File | 5.0.0 | 7.0.0 | Separate approval; retain conservative train first | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Serilog.Sinks.MSSqlServer | 6.5.1 | 10.0.0 | Separate approval; retain conservative train first | Direct | Medium; Sink configuration, batching, retention and log redaction | B8-logging: Redaction, safe audit metadata, configuration binding, shutdown flush; isolated SQL sink validation where applicable |
| Swashbuckle.AspNetCore | 6.5.0 | 10.2.3 | Separate approval; retain conservative train first | Direct | Medium; high for majors; Schema IDs, enum/nullability serialization and schema validation APIs | B4-contracts: OpenAPI equality, API serialization, frozen enums and schema tests |
| System.DirectoryServices | 8.0.0 | 10.0.11 | Separate approval; retain conservative train first | Direct | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| System.DirectoryServices.AccountManagement | 8.0.1 | 10.0.11 | Separate approval; retain conservative train first | Direct | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| System.Text.Json | 8.0.6 | 10.0.11 | Separate approval; retain conservative train first | Direct | Medium; high for majors; Hosting/DI configuration, platform APIs and JSON behavior | B10-platform: Full suite, host startup/fail-closed configuration, JSON hashing and serialization |
| xunit.runner.visualstudio | 2.5.6 | 4.0.0 | Separate approval; retain conservative train first | Direct | Low for patches; medium/high for majors; Test discovery, assertions, mocking, runner/collector compatibility; FluentAssertions 8 license review | B1-tests: Full unit/integration discovery and exact totals; coverage collection and CI runner smoke test |
| Microsoft.EntityFrameworkCore | 8.0.30 | 9.0.19 | Retain 8.0.30; review 9 only if justified | Direct | High for majors; medium otherwise; SQL mapping, transactions, encryption/authentication and provider behavior | B2-data: SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |
| Microsoft.EntityFrameworkCore.Design | 8.0.30 | 9.0.19 | Retain 8.0.30; review 9 only if justified | Direct | High for majors; medium otherwise; SQL mapping, transactions, encryption/authentication and provider behavior | B2-data: SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |
| Microsoft.EntityFrameworkCore.SqlServer | 8.0.30 | 9.0.19 | Retain 8.0.30; review 9 only if justified | Direct | High for majors; medium otherwise; SQL mapping, transactions, encryption/authentication and provider behavior | B2-data: SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |

- EF Core 9.0.19 supports .NET 8, while EF Core 10 requires .NET 10; keep Core/SqlServer/Design and transitive Relational/Abstractions/Analyzers aligned. No automatic EF9 migration is recommended. [EF9 package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/9.0.19), [EF10 package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.11).
- Microsoft.Extensions 10, System.Text.Json 10, DirectoryServices 10, Serilog 10, SqlClient 7, Swashbuckle 10 and MudBlazor 9 expose compatible .NET 8 or .NET Standard assets. They are not inherently .NET 10-only. This does not prove behavioral compatibility. [Hosting metadata](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.11).
- FluentAssertions 8 requires commercial-license review for commercial use; retain 6.12.2 pending that decision. [License guidance](https://fluentassertions.com/introduction).
- Coverlet 10.0.1 supports .NET 8 VSTest, but requires newer test tooling; current guidance specifies SDK >=8.0.414 and Test SDK >=18.4.0. It is not an MTP v2 collector. Verify installed SDK/CI separately before considering the major. [Collector requirements](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/VSTestIntegration.md).
- MudBlazor updates are exclusively a future Claude-owned change; this inventory does not authorize UI work.

#### Security and OpenAPI transitive review

Prefer updating the owning direct package; do not blindly pin every transitive dependency. These are review targets, not independently approved overrides.

| Package | Current resolved version(s) | Latest .NET 8-compatible stable | Kind | Risk / breaking surface | Batch / required tests |
|---|---|---|---|---|---|
| Azure.Core | 1.38.0 | 1.62.0 | Transitive | Medium; high for majors; Hosting/DI configuration, platform APIs and JSON behavior | B10-platform: Full suite, host startup/fail-closed configuration, JSON hashing and serialization |
| Azure.Identity | 1.11.4 | 1.21.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.Identity.Client | 4.61.3 | 4.88.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.Identity.Client.Extensions.Msal | 4.61.3 | 4.88.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.IdentityModel.Abstractions | 6.35.0, 7.7.3 | 8.22.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.IdentityModel.JsonWebTokens | 6.35.0, 7.7.3 | 8.22.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.IdentityModel.Logging | 6.35.0, 7.7.3 | 8.22.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.IdentityModel.Protocols | 6.35.0, 7.7.3 | 8.22.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 6.35.0, 7.7.3 | 8.22.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.IdentityModel.Tokens | 6.35.0, 7.7.3 | 8.22.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.OpenApi | 1.4.3 | 3.10.2 | Transitive | Medium; high for majors; Schema IDs, enum/nullability serialization and schema validation APIs | B4-contracts: OpenAPI equality, API serialization, frozen enums and schema tests |
| System.IdentityModel.Tokens.Jwt | 6.35.0, 7.7.3 | 8.22.0 | Transitive | High; Authentication defaults, token validation, claim mapping and directory APIs | B5-identity: Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |

IdentityModel 6/7 ->8 crosses major token-validation APIs and must be tested with both SqlClient and ASP.NET authentication owners. Updating to the last 6.x version alone does not resolve legacy lifecycle concerns. Microsoft.OpenApi 3.10.2 is metadata-compatible with .NET 8, but cannot be swapped under the existing Swashbuckle 6 code without checking API/namespace changes and dependency bounds; conservative same-major metadata target is 1.6.31.

### D. Defer-to-.NET-10 release lines

| Package | Current | Latest compatible with .NET 8 | Deferred .NET 10 target | Kind | Risk / breaking surface | Batch / required tests |
|---|---|---|---|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.30 | 8.0.30 | 10.0.11 | Direct | High; Authentication defaults, token validation, claim mapping and directory APIs | NET10: framework migration plus Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.AspNetCore.Authentication.Negotiate | 8.0.30 | 8.0.30 | 10.0.11 | Direct | High; Authentication defaults, token validation, claim mapping and directory APIs | NET10: framework migration plus Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | 8.0.30 | 8.0.30 | 10.0.11 | Direct | High; Authentication defaults, token validation, claim mapping and directory APIs | NET10: framework migration plus Fail-closed authorization, exact lookup, redaction and fake authentication tests; separately approved runtime validation |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.30 | 8.0.30 | 10.0.11 | Direct | High; Hosting/DI configuration, platform APIs and JSON behavior | NET10: framework migration plus Full suite, host startup/fail-closed configuration, JSON hashing and serialization |
| Microsoft.AspNetCore.OpenApi | 8.0.30 | 8.0.30 | 10.0.11 | Direct | High; Schema IDs, enum/nullability serialization and schema validation APIs | NET10: framework migration plus OpenAPI equality, API serialization, frozen enums and schema tests |
| Microsoft.EntityFrameworkCore | 8.0.30 | 9.0.19 (retain 8.0.30) | 10.0.11 | Direct | High; SQL mapping, transactions, encryption/authentication and provider behavior | NET10: framework migration plus SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |
| Microsoft.EntityFrameworkCore.Design | 8.0.30 | 9.0.19 (retain 8.0.30) | 10.0.11 | Direct | High; SQL mapping, transactions, encryption/authentication and provider behavior | NET10: framework migration plus SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |
| Microsoft.EntityFrameworkCore.SqlServer | 8.0.30 | 9.0.19 (retain 8.0.30) | 10.0.11 | Direct | High; SQL mapping, transactions, encryption/authentication and provider behavior | NET10: framework migration plus SQL constraint/round-trip and concurrency tests in an authorized disposable SQL instance; full local suite |
| System.Management.Automation | 7.4.18 | 7.4.19 | 7.6.5 | Direct | High; Hosting/DI configuration, platform APIs and JSON behavior | NET10: framework migration plus Full suite, host startup/fail-closed configuration, JSON hashing and serialization |

PowerShell 7.5 uses .NET 9 and 7.6 uses .NET 10 despite sharing package major 7. Do not select 7.6 via highest-minor in this .NET 8 repository. [PowerShell 7.6 release](https://devblogs.microsoft.com/powershell/announcing-powershell-7-6/). Transitive ASP.NET Core 10 and EF Core 10 packages follow the same framework boundary and must move with their owners.

### E. No immediate update / current train

- Microsoft.AspNetCore.Authentication.JwtBearer 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.AspNetCore.Authentication.Negotiate 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.AspNetCore.Authentication.OpenIdConnect 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.AspNetCore.Mvc.Testing 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.AspNetCore.OpenApi 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.Data.SqlClient 5.2.3: current within its installed major train; newer compatible major, if any, requires C review.
- Microsoft.EntityFrameworkCore 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.EntityFrameworkCore.Design 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.EntityFrameworkCore.SqlServer 8.0.30: current within its installed major train; framework-bound target is listed in D.
- Microsoft.Extensions.Configuration.Abstractions 8.0.0: current within its installed major train; newer compatible major, if any, requires C review.
- Microsoft.Extensions.Hosting 8.0.1: current within its installed major train; newer compatible major, if any, requires C review.
- Microsoft.Extensions.Hosting.WindowsServices 8.0.1: current within its installed major train; newer compatible major, if any, requires C review.
- Microsoft.Extensions.Http.Polly 8.0.30: current within its installed major train; newer compatible major, if any, requires C review.
- Microsoft.Extensions.Options.ConfigurationExtensions 8.0.0: current within its installed major train; newer compatible major, if any, requires C review.
- Serilog.Extensions.Hosting 8.0.0: current within its installed major train; newer compatible major, if any, requires C review.
- Serilog.Sinks.Console 5.0.1: current within its installed major train; newer compatible major, if any, requires C review.
- Serilog.Sinks.File 5.0.0: current within its installed major train; newer compatible major, if any, requires C review.
- System.DirectoryServices 8.0.0: current within its installed major train; newer compatible major, if any, requires C review.
- System.DirectoryServices.AccountManagement 8.0.1: current within its installed major train; newer compatible major, if any, requires C review.
- System.Text.Json 8.0.6: current within its installed major train; newer compatible major, if any, requires C review.

Resolved packages with no newer stable version reported: Microsoft.Management.Infrastructure 3.0.0; Microsoft.Management.Infrastructure.Runtime.Unix 3.0.0; Microsoft.Management.Infrastructure.Runtime.Win 3.0.0; Microsoft.PowerShell.Native 700.0.0; Microsoft.Security.Extensions 1.4.0; Microsoft.SqlServer.Server 1.0.0; Newtonsoft.Json 13.0.4; Polly.Extensions.Http 3.0.0; System.Security.AccessControl 6.0.1; System.Text.Encoding 4.3.0; xunit.abstractions 2.0.3. No-update is not equivalent to supported: Polly.Extensions.Http 3.0.0 is deprecated and needs a deliberate resilience-adapter replacement review.

Central-only pins with no direct reference: bunit 1.25.3 and Testcontainers.MsSql 3.7.0 are absent from the resolved graph; Serilog 3.1.1 is resolved transitively. Do not add unused packages merely to update pins. Their central-pin existence is not evidence of SQL-container or UI test execution.

### Deprecation findings

| Package | Installed | Kind | NuGet reason |
|---|---|---|---|
| Azure.Identity | 1.11.4 | Transitive | Other |
| FluentValidation.AspNetCore | 11.3.0 | Direct | Legacy |
| FluentValidation.AspNetCore | 11.3.0 | Transitive | Legacy |
| Microsoft.Identity.Client | 4.61.3 | Transitive | Other |
| Microsoft.Identity.Client.Extensions.Msal | 4.61.3 | Transitive | Other |
| Microsoft.IdentityModel.Abstractions | 6.35.0 | Transitive | Legacy |
| Microsoft.IdentityModel.JsonWebTokens | 6.35.0 | Transitive | Legacy |
| Microsoft.IdentityModel.Logging | 6.35.0 | Transitive | Legacy |
| Microsoft.IdentityModel.Protocols | 6.35.0 | Transitive | Legacy |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 6.35.0 | Transitive | Legacy |
| Microsoft.IdentityModel.Tokens | 6.35.0 | Transitive | Legacy |
| Polly.Extensions.Http | 3.0.0 | Transitive | Legacy |
| System.Collections.Immutable | 6.0.0 | Transitive | Legacy |
| System.IdentityModel.Tokens.Jwt | 6.35.0 | Transitive | Legacy |
| System.Text.Json | 4.7.2 | Transitive | Legacy |
| xunit | 2.6.6 | Direct | Legacy |
| xunit.assert | 2.6.6 | Transitive | Legacy |
| xunit.core | 2.6.6 | Transitive | Legacy |
| xunit.extensibility.core | 2.6.6 | Transitive | Legacy |
| xunit.extensibility.execution | 2.6.6 | Transitive | Legacy |

System.Text.Json 4.7.2 and 8.0.6 both occur in the per-project graph; inspect the owning dependency before consolidation. Likewise, IdentityModel 6.35.0 and 7.7.3 are distinct project resolutions, not proof both are loaded simultaneously in one runtime. Deprecated immutable-collection/Roslyn dependencies should be addressed through EF Design tooling, not broad transitive overrides.
