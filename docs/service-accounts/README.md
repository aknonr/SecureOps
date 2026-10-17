# Service Accounts Module — Design Note

Current state and open work: [PROGRESS.md](PROGRESS.md) (start there). Business rules: [SPEC.md](SPEC.md). Requirements register:
[integrated-test-activation.md](../integrated-test-activation.md). Dated history,
including this note's former status preamble: [PROGRESS-HISTORY.md](PROGRESS-HISTORY.md) — search it, do not read it end to end.

## Authority

- **Owner decision (2026-10-03):** Claude owns the Service Accounts module's backend, API, UI, SQL migration numbering and
  Windows verification. It supersedes the 2026-09-28 scoped exception (Codex final integration), archived in PROGRESS-HISTORY.md.
  The project owner merges and deploys and approves every execution against an installed system; new SQL is tried on
  a copy of the installed database first. No corporate SQL/AD/Jira/SMTP writes; repository data stays synthetic.
  Writing automation on managed servers (gMSA conversion) still needs its own ADR and approval (AGENTS.md rules 1, 9).
  Repository-wide defaults in `AGENTS.md`/`CLAUDE.md` are unchanged outside this module.
- **Owner-approved exception (2026-10-14):** TARAMA-C (Worker JEA çalıştırıcı, Windows hizmet kurulumu,
  SecureOps.ServiceAccountUsage JEA uç noktası, Jea toplayıcı) sahip kararıyla Claude'da; varsayılan sahiplik değişmez.
  Covers only `src/SecureOps.Worker/Jea/`, `scripts/jea/`, `scripts/worker/`, their tests and docs. Source: owner's answer in the
  coordination chat 2026-10-14 ("a ve b onaylıyorum"; "karar kayıtlarını sen uygula"), given directly in the coordinator chat.

## Reuse decisions (verified paths)

| Need | Reused facility | Path |
|---|---|---|
| Caller identity, approval, capabilities | `IApplicationAccessService.GetCurrentAsync` (persisted access; no claims-based authority) | `src/SecureOps.Infrastructure/Access/` |
| Endpoint capability gate | `CapabilityRequirement` + `CapabilityAuthorizationHandler` | `src/SecureOps.Api/Security/` |
| Capability registration for role bundles | `AccessActionCatalog` (additive spread of module actions) | `src/SecureOps.Infrastructure/Access/AccessActionCatalog.cs` |
| Safe errors | `OperationalProblemDetails` | `src/SecureOps.Api/Middleware/` |
| Atomic audit | same-transaction `INSERT audit.AuditLog`, as `SqlResourceRepository` | `src/SecureOps.Infrastructure/Resources/` |
| Persistence style | Dapper + `Microsoft.Data.SqlClient`, `ConnectionStrings:SecureOpsDb` | existing packages, no EF migrations |
| XLSX writing | text-only managed OpenXML writing pattern (no formulas/macros/links) | pattern of `InUse/InUseWorkbook.cs` |
| Scheduler | existing Hangfire storage/server composition (`TryAddSecureOpsJobServer`) | `Infrastructure/Announcements/Sources/AnnouncementSourceJobHost.cs` |
| UI transport | `AddSecureOpsApiClient` pipeline (API session cookie) | `src/SecureOps.Ui/Program.cs` |
| UI shell | `MainLayout`, `NavMenu`, MudBlazor 6.16, `so-` design tokens | `src/SecureOps.Ui/Shared/` |

Not reused, with reason:
- Original identity/directory reuse decision: exact privileged lookup is not a personnel search; the
  module keeps its own **business-person references** that grant nothing.
  The later approved ADR-0025 adds a separate bounded name-search provider behind module View
  plus Identity.Lookup; it does not change exact lookup or turn person references into authority.
- OCO mail pipeline: reminders never reuse announcement send intents; outbound mail is disabled.
- No PDF library exists and SkiaSharp has no Linux/managed PDF path in this solution; a small
  managed text-only PDF writer (standard Courier font, Turkish glyphs via encoding
  differences) is added inside the module rather than adding a package.

## Module layout

| Layer | Path |
|---|---|
| Domain rules | `src/SecureOps.Domain/ServiceAccounts/` |
| Contracts | `src/SecureOps.Shared/Contracts/ServiceAccounts/` (+ `ServiceAccountCapabilities`) |
| Infrastructure | `src/SecureOps.Infrastructure/ServiceAccounts/` (`Import/`, `Reporting/`, `Reminders/`, SQL repository) |
| API | `src/SecureOps.Api/Controllers/ServiceAccounts/`, module wiring `src/SecureOps.Api/ServiceAccounts/` |
| Worker | `src/SecureOps.Worker/ServiceAccounts/` (recurring reminder schedule only) |
| UI | `src/SecureOps.Ui/Pages/ServiceAccounts/`, `src/SecureOps.Ui/Services/ServiceAccounts/`, `src/SecureOps.Ui/Shared/Components/ServiceAccounts/` |
| SQL payloads | `sql/pending/service-accounts/` retained includes; numbered discovery through `sql/schema/025-*.sql`, `026-*.sql`, `029-*.sql` and matching migrations; separate grants |
| Usage scan tooling | `scripts/powershell/Get-ServiceAccountUsage.ps1` (collector), `scripts/powershell/Invoke-ServiceAccountUsageScan.ps1` (combine), contracts `service-account-usage(-scan).schema.json`, parser `Infrastructure/ServiceAccounts/UsageScans/` |
| Web usage scan | collector seam + fake `UsageScans/Collection/`, job runner/store/views `UsageScans/Jobs/`, rules `Domain/ServiceAccounts/UsageScanJobRules.cs`, API `ServiceAccountUsageScanJobsController`, UI `ServiceAccountUsageScanJob.razor` + `SaWebScanPanel` |
| Tests | `tests/SecureOps.Tests.Unit/ServiceAccounts/`, `tests/SecureOps.Tests.Unit/Ui/ServiceAccountUiTests.cs`, `tests/SecureOps.Tests.Integration/ServiceAccounts/`, `tests/sql/service-accounts/`, `tests/browser/service-accounts.cjs` |

All routes are under `/api/v1/service-accounts/…`.

## Access model

Two layers, both server-side:

1. **Capabilities** (global, persisted role bundles) gate what kind of action is allowed:
   `ServiceAccounts.View`, `.Work`, `.Assign`, `.Verify`, `.Import`, `.Report`, `.Administer`.
   They are registered in `AccessActionCatalog` so administrators can bundle them into roles;
   no existing role is changed and no role is seeded.
2. **Scope grants** (`svcacct.ScopeGrants`) bind an approved application user (`security.Users`)
   to `All`, an `Organization` subtree, or a `Team`. Every list, detail, mutation, export,
   evidence download and job query filters by the caller's current grants. Grants are created
   only by `ServiceAccounts.Administer` holders, audited, versioned and revocable. Imported
   people/teams never grant access; person references are not logins.

Account visibility: `All`; or organization grant covering the account's report organization;
or team grant matching the owner team, an open request's target team, or an incoming handover
target team. Owner-team changes require `Assign` plus organization/All scope; owner-person
changes within a team require `Assign` plus that team or wider scope.

Usage scans (ADR-0027): attaching a person-run scan needs Work plus the responsible basis, or a participant's own open
request; deciding a match needs Work plus the responsible basis. Readers see only the matches for the account's own
searched name and the per-server coverage. See SPEC "Usage scans".

Visibility is not authority (`AccountPermissions.Basis`): organization scope or the owner team is
*responsible*; a team seeing the account only through its targeted open request or an incoming
handover is a *participant* limited to `ParticipantRequestIds` (update without retargeting, linked
action reports, evidence on those, mails). See SPEC "Visibility is not authority".

Entry summary: `GET work-summary` returns open work targeted at the caller's directly granted
teams and, for organization-scope coordinators, scope-wide overdue/follow-up counts, ownership
decisions and performed actions awaiting verification. The account list and "Ekibimin işleri"
show it first, so the caller sees their follow-ups before changing anything.

Scope is re-checked where data is stored (2026-10-16): a mail already recorded under a provider message id is returned or linked only
when the caller's scope covers it by its own anchors (a linked in-scope account, or its contact team for an unlinked mail); a shared mail
lists only the linked accounts the caller may see; a module write re-reads the caller's grants and the account anchor inside its own
transaction; import batches record the stager's organization scope and are listed, read, decided, refreshed and committed only by a
caller whose current scope covers it (batches staged earlier belong to their uploader and to `All`); closing a request re-evaluates the
close rule on the supporting actions as locked inside the close transaction.
Concurrency: every module write transaction takes the application lock `svcacct:import-commit`
in Shared mode first; the import commit holds it exclusively (see PROGRESS, VerifyAction deadlock).

## Data model (SQL candidate `svcacct` schema)

Mutable roots carry `CreatedAt/By`, `UpdatedAt/By` and `rowversion`; stale writes return 409
with a safe field comparison. Every mutation writes `svcacct.History` (append-only, trigger
guarded) and `audit.AuditLog` in the same transaction.

Organizations, Teams (+aliases), People (+aliases; `Provisional`/`Verified`), ScopeGrants,
Accounts (domain/SID nullable, provisional source-bound key, identity state), AccountAliases,
OwnershipAssignments (Proposed/Confirmed/Ended with effective dates), WorkRequests, ActionEvents
(plan/performed/verified on one identity, void), ExternalRecords (OR/OCO/Jira/Other, unique per
type+number) and links, Communications and account links, Findings, Handovers,
IdentityTransitions, ImportBatches (server-held bytes + hash), ImportRows (raw/normalized,
classification, decision), AccountObservations, Evidence (bytes in SQL, scoped download),
ReportSnapshots (immutable payload + exports), ReminderOutbox, History.
Candidate 2 (`SA-002`): AccountUsages (where an account is used; reasoned exception and removal, never deleted) and
TeamRoles (SQL teams and the single gMSA executing team; configuration, never access).
Candidate 3 (`SA-003`, numbered 029, ADR-0026): `ScopeGrants.IsBootstrap`, the self-grant check relaxed only for that one
row (an "All" grant to its own grantor) and a filtered unique index allowing one bootstrap row ever.
Candidate 4 (`SA-004`, numbered 030, ADR-0027): UsageScans (uploaded file ≤ 4 MiB + SHA-256, purpose, searched accounts,
expected gMSA, times, run statement), UsageScanServers (one row per planned server, result and per-source status),
UsageScanItems (matched components, role Former/Expected), UsageScanLinks (scan → account, optional participant request)
and UsageScanDecisions (usage recorded / dismissed with reason). All append-only by trigger; runtime SELECT/INSERT only.
Candidate 8 (`SA-008`, numbered 035, ADR-0030 *Proposed*, OR-2 with a Fake adapter only): ChangePlanRecordSubmissions (one row per "open OR"
attempt for a plan account, written as Sending before any remote call with the exact sent content, idempotency key and fingerprint; only outcome
columns change, along allowed transitions; one live submission per plan account and per OR number) and ChangePlanRecordSubmissionEvents
(append-only). Runtime SELECT/INSERT plus column-level UPDATE on the outcome columns.

Candidate 10 (`SA-010`, numbered 037, web usage scan 2026-10-13): UsageScanJobs (job state, lease, plan hash, collector kind),
UsageScanJobAccounts (filtered unique index: one queued/running scan per account; link outcome) and UsageScanJobServers (live
per-server state, collector status, validated document, matched items). Operational state, never deleted; the evidence stays the
030 record (`tool = Jea`). Contract with the Worker JEA runner: [USAGE-SCAN-JOB-CONTRACT.md](USAGE-SCAN-JOB-CONTRACT.md); DBA:
[DBA-037-TR.md](DBA-037-TR.md).

Dates: plan/business dates are `date`; events carry `datetimeoffset` only when a real instant is
known; `TimePrecision` records DateOnly/Instant/Unknown. Source timestamps without a timezone
stay naive `datetime2` observations. New audit uses UTC. Report weeks use Europe/Istanbul.

## Import design

Journey: choose profile, source report date (never upload time; may stay unknown) with a required
statement of where that date comes from, and declared scope →
upload (XLSX/CSV/JSON; signature, size, zip-entry and ratio, row and cell limits; no formula
evaluation) → column mapping by normalized header (order independent) → server staging with
SHA-256 and mapping version → preview (counts, old/new values, errors, decisions) → decisions
(versioned) → commit with `Idempotency-Key` + preview version (409 on stale) → persisted result.

Profiles: `coordination-list` (evidenced Book1 headers), `dba-handover` (evidenced DBA headers),
`legacy-package` (JSON `wasas.service-account-migration.v1`), `legacy-workbook` (tracking
workbook input sheets, header row 6, helper/formula columns ignored), `generic` (bounded manual
mapping for not-yet-seen files such as the team return file; no invented parser).

The `legacy-package` JSON obeys the same row, column and cell limits as spreadsheets (per section and in total), and caps the person-alias and mail-link collections, before any row is built.

Legacy records keep a stable reference `legacy:{workbookSha256}:{sheet}:{row}`; the JSON package
supplies the same key through `reportWorkbookSha256` plus its `migrationKey`, so importing both
the workbook and the package does not duplicate requests, actions, communications or handovers.
Same bytes with the same declared period is a replay (existing result returned); a different
declared period is recorded as a new observation period without touching business records.

## Reports

`ServiceAccountMetrics` is the single metric implementation used by live reports, snapshots,
XLSX and PDF. Snapshots store the JSON payload, input watermark and metric definition version;
XLSX/PDF are rendered from that stored payload only. Exports are text-only; cells starting with
`= + - @` or control characters are neutralized.

Charts (2026-10-04): `ReportCharts.Build` (Domain) copies chart values from the payload; the reports page (`SaChart`),
the XLSX (native DrawingML charts on "Yönetici özeti", data on the last sheet "Grafik verisi", cached values) and the PDF
(a vector chart page after page 1) draw the same list. All three writers stay deterministic and add no runtime package;
DocumentFormat.OpenXml is a test-only schema validator.

The legacy ownership projection is a labelled figure only: the person named in the legacy inputs
(proposal or confirmation) plus, for otherwise unassigned accounts, the latest request follow-up
person. On the supplied package it reproduces 40 + 13 = 53 accounts / 9 people. Confirmed
ownership metrics never use it.

## Reminders

`ReminderRules` (domain) evaluates open requests (next follow-up / plan end / first send without
reply) with explicit calendar-day rules; `ServiceAccountReminderJob` writes an outbox unique on
(request, rule, due date, channel) and delivers in-app notifications and coordinator drafts under a
SQL claim/lease (`UPDLOCK, READPAST` inside an explicit READ COMMITTED transaction, because pooled
connections can carry SERIALIZABLE) with bounded backoff and a visible dead-letter list. The Worker
registers one recurring job on the existing Hangfire queue (`AddServiceAccountsWorker`) only when the
module and `ServiceAccounts:Reminders:Enabled` are on and Hangfire is configured; otherwise the job
is removed. Administrators can run the same idempotent evaluation from the UI. Business-day rules stay unconfigured
until an approved holiday calendar exists. Mail channel is not implemented.

## UI

Pages `/service-accounts` (scoped list, filters, multi-account mail), `/service-accounts/{id}`
(summary, work/actions, usage and rules, usage scans, ownership, mail, findings, handover/gMSA, evidence, source and history),
`/service-accounts/work` (reminders and drafts), `/service-accounts/imports`, `/service-accounts/reports`
and `/service-accounts/admin`. One nav entry gated on `ServiceAccounts.View`; the API decides every
command. Page loads are serialized so a concurrent access refresh cannot drop a read; commands are
single-flight. MudBlazor 6 does not associate its labels with inputs, so `_Host.cshtml` binds the
existing label by `aria-labelledby` on `.sa-page` fields only. MudBlazor 6.16 expansion-panel headers are a `div` without a tab
stop, so every collapsible form in the module is `SaDisclosure` (native `details`/`summary`: Tab reaches it, Enter/Space
toggle it, ▸/▾ shows the state); a render test fails if a module page uses `MudExpansionPanel` again. MudTabs headers are
divs without a tab stop too, so every account and admin tab header holds a `SaTabTitle` button (Enter/Space switch the tab,
`aria-current` marks the selected one), and every module page gives MudBlazor buttons a 2 px focus ring (they draw none
on their own). Registering an account whose name (without
`DOMAIN\`, a UPN suffix or the trailing `$`) is longer than 15 characters shows a non-blocking hint: a `$` name cannot be a
gMSA as typed, any other name needs a shorter gMSA name if it is converted; the server and Active Directory decide. The
requested gMSA name of a conversion has no field in the API yet (open owner question, review 2026-10-05).

## Actionable unknowns (not invented)

- Corporate mapping of directorates/teams to application users (scope grants) and role bundles.
- Verified directory identities (UPN/object ID/SID/domain) for people and accounts.
- Approved evidence storage/retention (candidate stores bounded bytes in SQL).
- Approved holiday calendar and reminder periods; corporate sender for any future mail.
- Team return (PAAS) file headers: generic mapping until a sample arrives.
- DBA grants and execution of 029 and 030 (with `SA-004-API-permissions.sql`) on the installed system (owner approval
  pending; see PROGRESS.md).
- How the team reaches many servers with the collector is their own approved practice; a product-side fan-out over the
  default WinRM endpoint would be an exception to AGENTS.md rule 3 and needs an owner decision and its own ADR.
- Web scan (2026-10-13): the module side and its contract exist; the Worker JEA runner, the Windows service setup and the
  JEA endpoint package are TARAMA-C (Codex). Until a `Jea` collector is configured the feature is off (fail closed).
