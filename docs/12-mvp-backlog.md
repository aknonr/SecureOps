# 12 — MVP Backlog

Phase 0 + Phase 1 backlog at the work-item level. Use this as the working task list. Each item is sized "S/M/L" approximately (S < 4h, M < 12h, L < 24h).

## Phase 0 — Discovery and Project Setup

| # | Task | Size | Owner | Deliverable |
|---|---|---|---|---|
| P0-01 | Send stakeholder kick-off mail (Bilgi Güv, Siber Güv, PAM, monitoring, network) | S | Dev | Mail thread |
| P0-02 | Draft pilot server list (15 candidates) | M | Dev | `docs/pilot-servers.md` |
| P0-03 | Get pilot owner sign-off | M | Dev + owners | Signed list |
| P0-04 | Baseline measurement: alarms/shift, analysis time | M | Dev | `docs/baseline-2026Q2.md` |
| P0-05 | Bilgi Güvenliği pre-review meeting | M | Dev + InfoSec | Meeting notes, action items |
| P0-06 | Siber Güvenlik pre-review meeting | M | Dev + CyberSec | Meeting notes, action items |
| P0-07 | PAM service account request | S | Dev + PAM team | Ticket reference |
| P0-08 | Confirm monitoring-chain / Turuncuhat intake path | S | Dev + monitoring team + Turuncuhat team | Confirmation of feasible inbound model |
| P0-09 | Test environment request | S | Dev + IT ops | Server provisioned |
| P0-10 | Network: firewall rules for WinRM, SQL, monitoring webhook | M | Dev + network team | Ticket and confirmation |
| P0-11 | JEA endpoint PoC on a single test server | L | Dev | Working endpoint + script |
| P0-12 | Risk matrix finalized | S | Dev | `docs/11-feasibility.md` updated |
| P0-13 | Phase 1 backlog reviewed and committed | S | Dev | This file |
| P0-14 | Phase 0 retrospective + Phase 1 go/no-go | S | Dev + management | Decision recorded |
| P0-15 | Send Turuncuhat integration inquiry mail | S | Dev | Mail thread |
| P0-16 | Send BeyondTrust + PAM + service account clarification mail | S | Dev | Mail thread |
| P0-17 | Track replies and update integration / security docs | S | Dev | `docs/06-integrations.md`, `docs/05-security-model.md` |

**Exit criteria:** all P0-01 through P0-17 complete.

Phase 1A current status: P1A-01 through P1A-11 are implemented in the backend and covered by unit/integration tests. P1A-12 remains pending because it requires an approved read-only real AD test account/environment before production readiness can be claimed.

## Phase 1A — Identity Lookup / PAM AD User Lookup

| # | Task | Size | Owner | Deliverable |
|---|---|---|---|---|
| P1A-01 | Add ADR and update roadmap/security/integration/audit docs | S | Dev | ADR-0008 + docs updated |
| P1A-02 | Add identity lookup request/response contracts | S | Dev | DTOs + JSON schema |
| P1A-03 | Implement config-based username normalization | S | Dev | Wildcard/bulk rejected |
| P1A-04 | Implement identity lookup service with mock PAM resolver hook | M | Dev | Correlation-ready service |
| P1A-05 | Implement read-only AD provider | M | Dev | Exact `sAMAccountName` / UPN lookup |
| P1A-06 | Add `POST /api/v1/identity/lookup` | S | Dev | Swagger/Postman-testable endpoint |
| P1A-07 | Add audit hooks for every lookup outcome | S | Dev | `IdentityLookup*` audit entries |
| P1A-08 | Add safe metadata endpoints | S | Dev | `/identity/me`, `/identity/lookup/capabilities`, `/health/audit-store`, `/health/identity-provider` |
| P1A-09 | Add production hardening controls | M | Dev | Swagger auth, fail-closed audit, provider guard, rate limit |
| P1A-10 | Add file audit persistence for dev/test | M | Dev | Bounded queue + JSONL file sink outside publish folder |
| P1A-11 | Add unit/API tests | M | Dev | Tests passing |
| P1A-12 | Complete real AD smoke test in Test/UAT | S | Dev + AD/Security owner | Approved read-only account verified; no personal data leaked in audit |

**Exit criteria:** all P1A-01 through P1A-12 complete, TeamLead/Admin access enforced, Operator/Auditor-only denied, audit fail-closed verified, persistent dev/test audit available, and no AD/PAM writes introduced.

## Phase 1 — Read-Only Diagnostic MVP

### Foundation (Sprint 1, ~1.5 weeks)

| # | Task | Size | Deliverable |
|---|---|---|---|
| P1-01 | Initialize .NET solution with all projects (Api, Worker, Ui, Domain, Infrastructure, Shared) | M | Solution builds |
| P1-02 | Add `.editorconfig`, `.gitignore`, `Directory.Build.props` | S | Consistent build settings |
| P1-03 | Add Serilog + structured logging in API and Worker | S | Logs to file + console |
| P1-04 | Add health endpoint `/api/v1/health` | S | Returns 200 with build info |
| P1-05 | ~~Add EF Core DbContext skeleton~~ — **superseded** 2026-10-01 (ADR-0001 amendment: Dapper retained) | — | — |
| P1-06 | First SQL migration: Servers, Alerts, AlertEvents, audit.AuditLog + trigger | M | DB schema deployed |

*Implementation status (2026-10-01): P1-06 is partly implemented — `audit.AuditLog` and its trigger exist (`sql/schema/001-…`); Servers, Alerts and AlertEvents tables do not exist yet.*
| P1-07 | Seed pilot servers from `docs/pilot-servers.md` | S | Servers table populated |
| P1-08 | Add Hangfire to Worker with SQL storage | M | Hangfire dashboard accessible |

### Webhook + Persistence (Sprint 2, ~1.5 weeks)

| # | Task | Size | Deliverable |
|---|---|---|---|
| P1-09 | Define `AlertPayload` DTO + JSON schema | S | `contracts/schemas/alarm-payload.schema.json` |
| P1-10 | Implement HMAC verification middleware | M | Webhook rejects invalid signatures |
| P1-11 | Implement `AlertsWebhookController` | M | POST /api/v1/alerts/webhook works |
| P1-12 | Implement alert normalization service | M | Payload → `Alert` entity |
| P1-13 | Implement `IAuditWriter` (SQL) | M | Audit entries persist |
| P1-14 | Wire `AuditAction.AlertReceived`, `AlertPersisted`, `DiagnosticEnqueued` | S | Audit trail begins |
| P1-15 | Hangfire enqueue from controller | S | Alert triggers job |
| P1-16 | Integration test: POST → persist → enqueue | M | Test passes |

### Diagnostic Engine (Sprint 3, ~2 weeks)

| # | Task | Size | Deliverable |
|---|---|---|---|
| P1-17 | Implement `IPowerShellRunner` with JEA runspace | L | Can invoke `Get-PSDrive` on test server |
| P1-18 | Implement `DiagnosticRunner` + module registry | M | Selects module by alert type |
| P1-19 | Implement `DiskDiagnosticModule` | L | Returns disk-diagnostic-v1 JSON |
| P1-20 | Implement `ServiceDiagnosticModule` | M | Returns service-diagnostic-v1 |
| P1-21 | Implement `CpuDiagnosticModule` | M | Returns cpu-diagnostic-v1 |
| P1-22 | Implement `MemoryDiagnosticModule` | M | Returns memory-diagnostic-v1 |
| P1-23 | Implement `IisDiagnosticModule` | L | Returns iis-diagnostic-v1 |
| P1-24 | Implement `EventLogDiagnosticModule` | M | Returns eventlog-diagnostic-v1 |
| P1-25 | Persist `DiagnosticResult` to SQL | S | Results table populated |
| P1-26 | Hangfire job → diagnostic runner → result persist | M | End-to-end Hangfire test |
| P1-27 | Audit: DiagnosticStarted/Completed/Failed | S | Audit entries on each |

### JEA Deployment (Sprint 4, ~1 week)

| # | Task | Size | Deliverable |
|---|---|---|---|
| P1-28 | Write `SecureOpsDiagnosticEndpoint.pssc` | M | Session config file |
| P1-29 | Write `SecureOpsDiagnosticRole.psrc` with whitelist | M | Role capability file |
| P1-30 | Write `Install-SecureOpsJeaEndpoint.ps1` | M | Installation script |
| P1-31 | Test deployment to one pilot server | M | Endpoint working |
| P1-32 | Test deployment to all pilot servers | L | Coordinated deployment |
| P1-33 | Verify forbidden cmdlets blocked | S | Security test passes |

### Mocks and Tests (Sprint 5, ~1 week)

| # | Task | Size | Deliverable |
|---|---|---|---|
| P1-34 | Mock monitoring-chain clients (`MockTuruncuhatClient`, `MockMonitoringPlatformClient`) | S | Mocks available in test |
| P1-35 | Mock PowerShell runner with canned outputs | M | Unit tests don't need a server |
| P1-36 | Unit tests: each diagnostic module | L | Coverage > 80% |
| P1-37 | Integration test: full webhook → result flow | M | Passes end to end |
| P1-38 | Security test: append-only audit trigger | S | Verified |
| P1-39 | Performance test: 10 concurrent webhooks | S | < 1s response per webhook |

### Operational Readiness (Sprint 6, ~0.5–1 week)

| # | Task | Size | Deliverable |
|---|---|---|---|
| P1-40 | Operational runbook: starting/stopping the system | M | `docs/runbooks/01-start-stop.md` |
| P1-41 | Operational runbook: troubleshooting a stuck job | S | `docs/runbooks/02-job-stuck.md` |
| P1-42 | Operational runbook: audit log query examples | S | `docs/runbooks/03-audit-query.md` |
| P1-43 | Deployment guide for production | M | `docs/runbooks/04-deployment.md` |
| P1-44 | Demo script for management presentation | S | `docs/demo-script.md` |
| P1-45 | Management demo + Q&A | S | Demo delivered, feedback collected |

**Exit criteria:** all P1-01 through P1-45 complete; success criteria in `docs/00-project-brief.md` met.

## Explicit Out-of-Scope for MVP

Reaffirm what is **not** in MVP:

- No web UI beyond a minimal status page (Phase 2 builds the real UI).
- No broad identity search; Phase 1A allows exact account lookup only for TeamLead/Admin.
- No notification channels (Phase 3).
- No PAM correlation (Phase 4).
- No analysis or reports (Phase 6).
- No AI (Phase 7).
- No write operations (Phase 8).
- No mobile apps.
- No multi-tenant.
- No public API.
- No Linux server support.

If any of these are requested mid-MVP, surface, defer, and update `docs/02-roadmap.md`.

## Definition of Done (Per Item)

Each item is "done" when:

1. Code compiles cleanly.
2. Tests pass.
3. Relevant `docs/*.md` updated.
4. ADR added if a key decision was made.
5. Audit instrumented where appropriate.
6. Self-reviewed against `docs/13-definition-of-done.md`.

## Reference

- `docs/02-roadmap.md` — phase plan
- `docs/13-definition-of-done.md` — DoD checklist
- `plans/PHASE-0-discovery-and-project-setup.md` — Phase 0 detail
- `plans/PHASE-1-readonly-diagnostic-mvp.md` — Phase 1 detail
