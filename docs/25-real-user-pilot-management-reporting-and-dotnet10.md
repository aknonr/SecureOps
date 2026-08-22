# Real-User Pilot, Management Reporting, and .NET 10 Readiness

## Pilot Authentication and Persistence

The interim corporate authentication path is IIS Windows Authentication -> ASP.NET Core Negotiate -> `ClaimsPrincipal.Identity.Name` -> `ICorporatePrincipalResolver` -> persisted SecureOps approval -> role -> capability. Do not add an LDAP password form. OIDC remains a future authentication provider behind the same application access model.

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
| `Access__BootstrapAdministrators__0` | one approved exact `DOMAIN\account` for bootstrap only |
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

The runtime identity remains `DOMAIN\WASAST_YONETIM`. Do not configure SQL credentials or KRON/AAPM for this connection. Migrations 001-007 and grants must be completed before SQL providers are selected. The App Pool identity needs read/write/create permission only on the configured Data Protection key-ring directory; no key material is deployed from Git. After at least two reviewed persisted Admin assignments exist, remove the bootstrap array and restart in a controlled window. Persisted access remains; disabled users are never bootstrapped again.

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

Durations report sample count, minimum, average, and maximum elapsed seconds for import -> preview, claim -> Jira creation, and claim -> completion. They include waits and retries. They are not manual effort, active handling time, time saved, or operator performance.

## Database Impact

Migration 005 creates limited `reporting` views over audit/workflow data and supporting indexes. Migration 007 creates authoritative `security.ApplicationSessions` plus a limited reporting view. Runtime needs `SELECT, INSERT, UPDATE` on the session table and `SELECT` on each reporting view. It still needs no `SELECT` on base `audit.AuditLog` or history tables, no `DELETE`, no DDL, and no schema ownership.

## Known Historical Gaps

- Browser/UI actions are intentionally excluded.
- Existing rows cannot prove manual-process duration or time saved.
- Historical rate-limit rejections, access concurrency failures, source-query outages, and bulk-invalid item outcomes are incomplete or absent.
- A future manual baseline must use an approved, bounded sample: record start/end timestamps and workflow type for the old process, collect no content beyond operational references, aggregate at team level, and compare equivalent work classes and time windows.

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
