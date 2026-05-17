# Phase 1 — Read-Only Diagnostic MVP

**Duration:** 4–6 weeks (single developer at 18h/week)
**Goal:** End-to-end webhook → diagnostic → audit flow, with no writes on target servers, demonstrating value on the pilot fleet.

## Pre-Conditions

- [ ] Phase 0 complete (see `plans/PHASE-0-discovery-and-project-setup.md`).
- [ ] PAM service account at least requested (preferably provisioned).
- [ ] Test environment available.
- [ ] JEA PoC successful on one server.

## Deliverables

1. .NET 8 solution skeleton with all 6 source projects and 2 test projects.
2. SQL schema deployed: Servers, Alerts, AlertEvents, DiagnosticJobs, DiagnosticResults, audit.AuditLog with trigger.
3. Hangfire infrastructure with SQL Server storage.
4. Webhook endpoint `/api/v1/alerts/webhook` with HMAC verification.
5. Six diagnostic modules: Disk, CPU, Memory, IIS, Service, EventLog.
6. JEA endpoint deployed on all pilot servers.
7. PowerShell scripts in `scripts/diagnostic/`.
8. Mock SolarWinds, PAM adapters for tests.
9. Unit + integration test suite passing.
10. Operational runbook (`docs/runbooks/01-04-*.md`).
11. Management demo + Q&A.

## Sprint Structure (Approximate)

### Sprint 1 — Foundation (~1.5 weeks)

| # | Task | Estimate | Files |
|---|---|---|---|
| P1-T01 | Initialize solution with 6 src + 2 test projects | 4h | `src/`, `tests/` |
| P1-T02 | `.editorconfig`, `.gitignore`, `Directory.Build.props` | 2h | root |
| P1-T03 | Add Serilog with file + console sinks | 3h | `Api/Program.cs`, `Worker/Program.cs` |
| P1-T04 | Health endpoint `/api/v1/health` | 1h | `Api/Controllers/HealthController.cs` |
| P1-T05 | EF Core DbContext skeleton | 3h | `Infrastructure/Data/SecureOpsDbContext.cs` |
| P1-T06 | First SQL migration: core tables + audit trigger | 5h | `sql/migrations/V001_*.sql` |
| P1-T07 | Seed pilot servers from doc | 2h | `sql/seed/Servers.sql` |
| P1-T08 | Add Hangfire with SQL storage to Worker | 4h | `Worker/Program.cs` |

### Sprint 2 — Webhook + Persistence (~1.5 weeks)

| # | Task | Estimate | Files |
|---|---|---|---|
| P1-T09 | `AlertPayload` DTO + JSON schema | 2h | `Shared/Contracts/`, `contracts/schemas/` |
| P1-T10 | HMAC verification middleware | 4h | `Api/Middleware/HmacAuthMiddleware.cs` |
| P1-T11 | `AlertsWebhookController` POST endpoint | 3h | `Api/Controllers/AlertsWebhookController.cs` |
| P1-T12 | Alert normalization service | 4h | `Infrastructure/Alerts/AlertNormalizer.cs` |
| P1-T13 | `SqlAuditWriter` (Dapper) | 4h | `Infrastructure/Audit/SqlAuditWriter.cs` |
| P1-T14 | Audit calls: AlertReceived, AlertPersisted, DiagnosticEnqueued | 2h | controllers, services |
| P1-T15 | Hangfire enqueue from controller | 2h | `AlertsWebhookController` |
| P1-T16 | Integration test: POST → persist → enqueue | 4h | `Tests.Integration/Api/` |

### Sprint 3 — Diagnostic Engine (~2 weeks)

| # | Task | Estimate | Files |
|---|---|---|---|
| P1-T17 | `IPowerShellRunner` with JEA runspace | 8h | `Infrastructure/PowerShell/JeaPowerShellRunner.cs` |
| P1-T18 | `IDiagnosticModuleRegistry` + `DiagnosticRunner` | 4h | `Infrastructure/Diagnostic/` |
| P1-T19 | `DiskDiagnosticModule` | 8h | `Infrastructure/Diagnostic/DiskDiagnosticModule.cs`, `scripts/diagnostic/Get-DiskDiagnostic.ps1` |
| P1-T20 | `ServiceDiagnosticModule` | 5h | `ServiceDiagnosticModule.cs`, `Get-ServiceDiagnostic.ps1` |
| P1-T21 | `CpuDiagnosticModule` | 5h | `CpuDiagnosticModule.cs`, `Get-CpuDiagnostic.ps1` |
| P1-T22 | `MemoryDiagnosticModule` | 5h | `MemoryDiagnosticModule.cs`, `Get-MemoryDiagnostic.ps1` |
| P1-T23 | `IisDiagnosticModule` | 8h | `IisDiagnosticModule.cs`, `Get-IisDiagnostic.ps1` |
| P1-T24 | `EventLogDiagnosticModule` | 6h | `EventLogDiagnosticModule.cs`, `Get-EventLogDiagnostic.ps1` |
| P1-T25 | `DiagnosticResult` persistence | 2h | `Infrastructure/Diagnostic/SqlResultStore.cs` |
| P1-T26 | Hangfire `RunDiagnosticJob` wiring | 3h | `Worker/Jobs/RunDiagnosticJob.cs` |
| P1-T27 | Audit: DiagnosticStarted/Completed/Failed | 2h | `RunDiagnosticJob` |

### Sprint 4 — JEA Deployment (~1 week)

| # | Task | Estimate | Files |
|---|---|---|---|
| P1-T28 | `SecureOpsDiagnosticEndpoint.pssc` | 4h | `scripts/jea/` |
| P1-T29 | `SecureOpsDiagnosticRole.psrc` with whitelist | 6h | `scripts/jea/` |
| P1-T30 | `Install-SecureOpsJeaEndpoint.ps1` | 4h | `scripts/jea/` |
| P1-T31 | Test deployment to one pilot server | 4h | (operational) |
| P1-T32 | Deploy to all pilot servers (coordinated) | 8h | (operational) |
| P1-T33 | Verify forbidden cmdlets blocked | 2h | `Tests.Integration/Security/` |

### Sprint 5 — Tests and Mocks (~1 week)

| # | Task | Estimate | Files |
|---|---|---|---|
| P1-T34 | `MockMonitoringPlatformClient` | 2h | `Tests/Mocks/` |
| P1-T35 | `MockPowerShellRunner` with canned outputs | 4h | `Tests/Mocks/` |
| P1-T36 | Unit tests for each diagnostic module | 8h | `Tests.Unit/Diagnostic/` |
| P1-T37 | Integration test: webhook → result | 4h | `Tests.Integration/` |
| P1-T38 | Security test: append-only audit trigger | 2h | `Tests.Integration/Sql/` |
| P1-T39 | Performance test: 10 concurrent webhooks | 3h | `Tests.Integration/Performance/` |

### Sprint 6 — Operational Readiness (~0.5–1 week)

| # | Task | Estimate | Files |
|---|---|---|---|
| P1-T40 | Runbook: start/stop the system | 2h | `docs/runbooks/01-start-stop.md` |
| P1-T41 | Runbook: troubleshoot stuck job | 2h | `docs/runbooks/02-job-stuck.md` |
| P1-T42 | Runbook: audit log query examples | 2h | `docs/runbooks/03-audit-query.md` |
| P1-T43 | Deployment guide | 4h | `docs/runbooks/04-deployment.md` |
| P1-T44 | Demo script | 2h | `docs/demo-script.md` |
| P1-T45 | Management demo + Q&A | 2h | (event) |

**Total Phase 1 estimate:** ~155 hours, roughly 6–9 weeks at 18h/week.

## Exit Criteria

- [ ] Webhook delivery 99%+ in pilot testing.
- [ ] Diagnostic jobs complete in < 30s for standard alarms.
- [ ] All operations audited (verified by manual inspection of `audit.AuditLog`).
- [ ] Zero unplanned impact on pilot servers (verified with server owners and monitoring platform).
- [ ] Unit + integration tests pass.
- [ ] Append-only enforcement verified.
- [ ] JEA endpoint blocks forbidden cmdlets (security test passes).
- [ ] Operational runbooks complete.
- [ ] Management demo delivered.
- [ ] Pilot operators report the system is useful (structured survey).
- [ ] Definition of Done in `docs/13-definition-of-done.md` satisfied per item.

## Risks Specific to Phase 1

| Risk | Mitigation |
|---|---|
| JEA endpoint deployment friction across pilot | Phased rollout; one server first; coordinate with server owners |
| Hangfire schema conflict with existing DB | Separate schema; standard pattern; tested in test env |
| Webhook auth issues with monitoring platform | Mock first; coordinate with monitoring team early |
| Diagnostic timeouts on slow servers | Per-module timeout config; partial result on timeout |
| Service account permission issues | Identified in Phase 0; PAM workflow |

## Hand-off to Phase 2

Phase 2 starts when:
- All Phase 1 exit criteria met.
- Management demo feedback incorporated.
- Phase 2 backlog reviewed.

Phase 2 plan: `plans/PHASE-2-web-ui-and-dashboard.md`.
