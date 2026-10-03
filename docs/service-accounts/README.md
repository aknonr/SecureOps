# Service Accounts Module — Design Note

Owner decision (2026-10-03): Claude owns this module end to end — see "Authority and baseline". SA-003 is numbered
029 (ADR-0026); source inventory is 001-029. Not applied to the installed TEST system; see PROGRESS.md.

Owner-approved local successor (2026-10-03): all seven current Service Accounts
capabilities belong to the genuine protected Admin bundle, with explicit module
scope and self-grant protections unchanged. New 028 is an additive role-bundle
amendment only; see [ADMIN-OPERATIONS-20261003.md](ADMIN-OPERATIONS-20261003.md).
Source changes are isolated on .NET 8; the sealed 0e85c9d candidate is unchanged.
This is not target SQL execution, deployment or .NET 10 integration approval.

Owner-approved local correction (2026-10-02): protected Admin navigation and independent
general AD name search, with explicit scope and no target changes:
[ADMIN-ACCESS-AND-GENERAL-LOOKUP-20261002.md](ADMIN-ACCESS-AND-GENERAL-LOOKUP-20261002.md).
Source inventory is now 001-027; the sealed 5264635 packages and prior evidence are unchanged.

Previous combined review (2026-10-02): PR #4 and final PR #6 are integrated with current master/PR #5.
SQL discovery is 001-026: numbered 025/026 include the retained SA-001/SA-002 payloads;
grants remain separate and unassigned. See [COMBINED-INTEGRATION-20261002.md](COMBINED-INTEGRATION-20261002.md).
This is local source verification, not installation or corporate acceptance.

Previous integration status (2026-10-01): pinned `b4fdf8d` plus `7e227ed` delta is integrated locally
on `feature/service-accounts-pinned-integration-20260929`, based on `e997c5b`.
Not deployed or corporately accepted. Combined Windows evidence and limitations:
[INTEGRATION-FOLLOWUP-20261001.md](INTEGRATION-FOLLOWUP-20261001.md).
The canonical requirements register remains [integrated-test-activation.md](../integrated-test-activation.md).
Business rules: [SPEC.md](SPEC.md). Historical stage evidence: [PROGRESS.md](PROGRESS.md).
The authority/baseline history below describes the original isolated development,
not the current integration state.

## Authority and baseline

- **Owner decision (2026-10-03), supersedes the Codex split below for this module only:** Claude owns the Service
  Accounts module's backend, API, UI, SQL migration numbering and Windows verification (previously with Codex).
  The project owner merges and deploys and approves every execution against an installed system; new SQL is tried on
  a copy of the installed database first. No corporate SQL/AD/Jira/SMTP writes; repository data stays synthetic.
  Writing automation on managed servers (gMSA conversion) still needs its own ADR and approval (AGENTS.md rules 1, 9).
  Repository-wide defaults in `AGENTS.md`/`CLAUDE.md` are unchanged outside this module.
- Scoped owner exception (2026-09-28): Claude implements domain, contracts, infrastructure,
  API, SQL candidate, UI and tests **for this module only**. Codex keeps existing workflows,
  platform architecture, release engineering and final integration. Repository-wide ownership
  rules in `AGENTS.md`/`CLAUDE.md` are unchanged.
- Codex's integrated source (`deda848…`, branch `feature/sdm-integrated-test-20260928`) was not
  published when this work started (remote had only `master`, `feature/sql-runtime-hardening-20260902`,
  `feature/ui-or-sdm-jira-only-confirmation-20260928`, `fix/phase1a-closure-hardening`). The
  temporary baseline is the verified UI handoff commit
  `a3037175bb0bb9ecc7ab5c36c7c28607726469ce`. **Codex must reconcile this branch with the
  integrated source before acceptance.** Stale `master` was not used. The integrated branch has
  since been published at `e997c5b68cebcd23716860a9b06fdc25ebbb4493` (tested product `deda848…`);
  see PROGRESS.md for the read-only reconciliation preview. Nothing was merged or rebased here.

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

Visibility is not authority (`AccountPermissions.Basis`): organization scope or the owner team is
*responsible*; a team seeing the account only through its targeted open request or an incoming
handover is a *participant* limited to `ParticipantRequestIds` (update without retargeting, linked
action reports, evidence on those, mails). See SPEC "Visibility is not authority".

Entry summary: `GET work-summary` returns open work targeted at the caller's directly granted
teams and, for organization-scope coordinators, scope-wide overdue/follow-up counts, ownership
decisions and performed actions awaiting verification. The account list and "Ekibimin işleri"
show it first, so the caller sees their follow-ups before changing anything.

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
(summary, work/actions, ownership, mail, findings, handover/gMSA, evidence, source and history),
`/service-accounts/work` (reminders and drafts), `/service-accounts/imports`, `/service-accounts/reports`
and `/service-accounts/admin`. One nav entry gated on `ServiceAccounts.View`; the API decides every
command. Page loads are serialized so a concurrent access refresh cannot drop a read; commands are
single-flight. MudBlazor 6 does not associate its labels with inputs, so `_Host.cshtml` binds the
existing label by `aria-labelledby` on `.sa-page` fields only.

## Actionable unknowns (not invented)

- Corporate mapping of directorates/teams to application users (scope grants) and role bundles.
- Verified directory identities (UPN/object ID/SID/domain) for people and accounts.
- Approved evidence storage/retention (candidate stores bounded bytes in SQL).
- Approved holiday calendar and reminder periods; corporate sender for any future mail.
- Team return (PAAS) file headers: generic mapping until a sample arrives.
- DBA grants and execution of 029 on the installed system (owner approval pending; see PROGRESS.md).
