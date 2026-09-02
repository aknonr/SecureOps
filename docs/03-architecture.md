# 03 — Architecture

System architecture for SecureOps. This document is the canonical source for component boundaries, data flow, and hosting model.

## High-Level Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│                    Monitoring Platform                                │
│                    (SolarWinds-style)                                 │
│                                                                       │
│   Alarm fires → Alert Action (HTTPS POST) → SecureOps API webhook    │
└────────────────────────────────┬─────────────────────────────────────┘
                                 │ webhook
                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│ SecureOps API (ASP.NET Core, IIS-hosted)                              │
│  - /api/v1/alerts/webhook (HMAC-signed)                               │
│  - /api/v1/alerts (browse)                                            │
│  - /api/v1/diagnostic (browse, manual trigger)                        │
│  - /api/v1/identity/lookup (TeamLead/Admin privileged read)           │
│  - /api/v1/audit (compliance roles)                                   │
│                                                                       │
│  Validates → Normalizes → Persists alert → Enqueues Hangfire job     │
└────┬─────────────────────────────────────────────────────┬───────────┘
     │                                                      │
     │ SQL Server                                          │ Hangfire
     ▼                                                      ▼
┌────────────────────────────────┐    ┌────────────────────────────────┐
│ SQL Server                      │    │ SecureOps Worker (Windows Svc) │
│  - Servers                      │    │  - Hangfire job server         │
│  - Alerts, AlertEvents          │◀───┤  - Diagnostic runners          │
│  - DiagnosticJobs, Results      │    │  - JEA PowerShell invoker      │
│  - audit.AuditLog (append-only) │    │  - Notification dispatcher     │
│  - Hangfire schema              │    │  - Audit writer                │
│  - RBAC tables                  │    │                                 │
└─────────────────────────────────┘    └──────────────────┬─────────────┘
                                                          │ WinRM/Kerberos
                                                          │ via JEA endpoint
                                                          ▼
                                       ┌────────────────────────────────┐
                                       │  Pilot Windows Servers (10–15)  │
                                       │  JEA constrained endpoint       │
                                       │  Read-only cmdlets only         │
                                       └────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│ SecureOps UI (Blazor Server + MudBlazor, IIS-hosted)                  │
│  - Windows Authentication                                             │
│  - AD-group RBAC policies                                             │
│  - Real-time alert list (SignalR)                                     │
│  - Diagnostic viewer                                                  │
│  - Audit query (compliance roles)                                     │
└──────────────────────────────────────────────────────────────────────┘
```

The diagram above captures the original generic inbound-monitoring shape. In the real CONTOSO environment, the operational alarm chain is **SolarWinds → monthly.thy.com / HPE OpsBridge → Turuncuhat**, and the intended SecureOps boundary with Turuncuhat is **bidirectional**. Exact webhook/API details are pending stakeholder input.

## Components

### SecureOps.Api

ASP.NET Core 8 Web API. Hosted on IIS in-process.

Current implementation status: Phase 1A IdentityLookup, provider-neutral access approval/capability authorization, server-side application-session governance, persistent Data Protection configuration, safe health metadata, the fake-adapter Operational Record/Jira workflow, and a SQL-aggregated management reporting read model are implemented. Phase 1 alert intake and diagnostic job orchestration remain planned. SQL contracts exist but are never applied by the application.

Responsibilities:
- Accept monitoring webhooks (HMAC-signed).
- Provide REST API for the UI and any future internal consumers.
- Enforce authorization.
- Resolve authenticated principals through persisted Pending/Approved/Disabled application access before capabilities are granted.
- Keep exact identity lookup and the read-only Directory Explorer as separate services/controllers. Directory Explorer preserves exact direct-group, exact group-metadata, and bounded direct-member queries, and adds bounded provider-neutral enrichment for transitive membership, proven paths, account health, SPNs, and separately authorized privileged-group evidence. It accepts no raw LDAP filters or credentials.
- Keep authentication provider-neutral and separate from the server-side SecureOps session record. The protected session handle is not a corporate credential or authorization source; current access status and version remain authoritative.
- Normalize alert payloads.
- Enqueue diagnostic jobs via Hangfire client API.
- Resolve exact PAM/AD account lookups through the IdentityLookup service (Phase 1A).
- Enforce durable command idempotency, bounded workflow claims, and external source freshness for Operational Record/Jira commands.
- Return bounded management aggregates from limited SQL views under a distinct audited reporting capability.

Does NOT:
- Execute PowerShell.
- Run long operations inline.
- Render HTML.
- Perform AD or PAM writes.

### SecureOps.Worker

.NET Worker Service. Registered as a Windows Service.

Responsibilities:
- Host the Hangfire server.
- Execute diagnostic jobs.
- Invoke PowerShell through JEA.
- Write diagnostic results to SQL.
- Write audit entries.
- Dispatch notifications (Phase 3+).
- Run scheduled jobs (rule evaluation in Phase 6+).

Does NOT:
- Serve HTTP traffic.
- Render UI.
- Call directly into the API or UI processes (communication is through SQL + Hangfire).

### SecureOps.Ui

Blazor Server + MudBlazor. Hosted on IIS in-process, separate site or virtual app from API.

Responsibilities:
- Render pages for shift engineers, leads, admins, auditors.
- Real-time updates via Blazor's SignalR hub.
- Call the API for data; never DB directly.
- Enforce authorization policies (server enforces, UI hides).

### SecureOps.Domain

Pure C# domain model. No external dependencies.

Contents:
- Entities: Server, Alert, AlertEvent, DiagnosticJob, DiagnosticResult, AuditEntry.
- Value objects: AlertSeverity, AlertType, DiagnosticJobStatus.
- Domain events.
- Domain services (if needed).

### SecureOps.Infrastructure

External integrations and data access.

Contents:
- EF Core DbContext + entity configurations.
- Repositories.
- JEA PowerShell runner.
- Adapters: MonitoringPlatformAdapter, PamAdapter, TeamsNotifier, MailNotifier, TicketingAdapter, SnapshotAdapter — each with a mock implementation.
- Hangfire job classes.

### SecureOps.Shared

Cross-cutting types.

Contents:
- DTOs for API contracts (records).
- JSON schemas (mirrored from `contracts/schemas/`).
- Authorization policy name constants.
- Strongly typed configuration options.
- Common utilities with no infrastructure dependencies.

Does NOT contain:
- ASP.NET middleware/controllers.
- EF Core mappings, SQL access, or audit file IO.
- PowerShell execution or external system clients.
- Blazor components.

## Data Flow: Receive EVT → Diagnose → Update Turuncuhat

```
1. SolarWinds fires an alarm.
2. monthly.thy.com / HPE OpsBridge exposes event-detail context.
3. Turuncuhat receives the alarm, creates the EVT record, and remains the operational system of record for acknowledgment and closure.
4. SecureOps receives or fetches EVT context from Turuncuhat. Exact inbound method is pending stakeholder input (webhook vs. API).
5. API verifies the inbound request if webhook-based, or authenticates the outbound request if API-pull-based.
6. API validates the payload against alarm-payload.schema.json.
7. API normalizes (resolves server by name, parses severity, etc.).
8. API persists Alert + AlertEvent("Received") + AuditEntry.
9. API enqueues Hangfire job: RunDiagnosticJob(alertId).
10. API records successful intake according to the final Turuncuhat integration contract.

(asynchronously)

11. Worker Hangfire picks up the job.
12. Worker writes AuditEntry("DiagnosticStarted").
13. Worker selects diagnostic module based on alert type (Disk, CPU, etc.).
14. Worker opens the approved target-server connection path. Current direct JEA usage is documented, but the final BeyondTrust/WinRM model is pending stakeholder input.
15. Worker invokes the read-only PowerShell command(s).
16. Worker parses structured output into DiagnosticResult.
17. Worker persists DiagnosticResult + AuditEntry("DiagnosticCompleted").
18. Worker (Phase 3+) dispatches notification.
19. UI observers (Blazor SignalR) refresh the alert list.

(operator interaction)

20. Operator opens the alert in UI.
21. UI calls API for AlertDetail.
22. API records AuditEntry("AlertViewed").
23. Operator reviews the diagnostic result and decides whether the EVT can be closed.
24. If Turuncuhat read-write integration is approved, SecureOps writes back EVT closure fields such as `Çözüldü`, `Kapanış açıklaması`, and `Aksiyon alındı mı?`; otherwise the operator performs the close action manually in Turuncuhat.
25. Each SecureOps-side state change and Turuncuhat exchange creates an AuditEntry.
```

## Hosting Model

Single Windows Server (production) or two (pilot scale, no HA in MVP):

- **IIS** hosting `SecureOps.Api` and `SecureOps.Ui` (separate sites, both HTTPS only).
- **Windows Service** hosting `SecureOps.Worker`.
- **SQL Server** on the existing enterprise SQL Server (separate database, separate login).

For pilot, all three on the same server is acceptable. For production, separate the Worker from the IIS host.

See `docs/adr/ADR-0007-iis-hosting-model.md`.

## Network Topology

| From | To | Protocol | Port | Notes |
|---|---|---|---|---|
| Turuncuhat ↔ API | EVT intake and EVT update | HTTPS | 443 | Exact webhook/API contract pending |
| Internal users | UI | HTTPS | 443 | Windows Auth |
| Internal users | API | HTTPS | 443 | Windows Auth |
| Worker | SQL Server | TLS | 1433 | Integrated auth |
| Worker | Target Windows Servers | WinRM HTTPS | 5986 | Kerberos + JEA |
| Worker | SolarWinds SWIS (Phase 6+) | HTTPS | 17774 | Service account |
| Worker → Teams webhook (Phase 3+) | HTTPS | 443 | Teams URL |
| Worker → SMTP (Phase 3+) | TCP | 25 or 587 | Internal relay |

All endpoints internal. No public ingress.

## Configuration

Hierarchical configuration via `IConfiguration`:

1. `appsettings.json` (committed, non-secret defaults).
2. `appsettings.{Environment}.json` (committed, environment-specific non-secrets).
3. `appsettings.Local.json` (NOT committed, developer overrides).
4. Environment variables (containerized or service-runner overrides).
5. PAM-resolved secrets at startup (service account credentials).

Configuration sections:
- `ConnectionStrings` (`SecureOpsDb` for the SQL Server audit/data store when enabled)
- `SolarWinds`
- `Pam`
- `IdentityLookup`
- `Jea`
- `Hangfire`
- `Notifications`
- `Audit`
- `Rbac`

Each section has a strongly-typed options class in `SecureOps.Shared.Configuration`.

## Logging and Observability

- **Serilog** for structured logging in API and Worker.
- **Sinks:** File (rolling daily), SQL Server (Serilog.Sinks.MSSqlServer), and optionally Application Insights or Seq if added in Phase 6+.
- **Correlation:** `Activity.Current` traces, propagated to Hangfire jobs and SQL.
- **Health endpoints:** `/api/v1/health` for process liveness, `/api/v1/health/persistence` for a bounded read-only SQL readiness probe, `/api/v1/health/audit-store` for safe audit-store status, `/api/v1/health/identity-provider` for safe identity-provider configuration status, and Admin-only `/api/v1/health/enterprise-integrations` for safe Turuncu Hat/Jira selection and availability. Process liveness and SQL readiness remain authentication-protected outside Development but bypass SQL-backed application-session creation so an outage remains diagnosable. URLs, identities, credentials, sessions, exception details, and remote payloads are excluded.

## Theming

MudBlazor theme defined centrally. Color tokens for reuse in custom components:

| Token | Value | Use |
|---|---|---|
| `--so-primary` | navy (#0F1B3D) | Headers, primary buttons |
| `--so-accent` | amber (#D4A04C) | Highlights, callouts |
| `--so-success` | green (#0D7C66) | Success states |
| `--so-warning` | amber-orange (#F0A04C) | Warnings |
| `--so-danger` | red (#C73E3A) | Errors, critical alerts |
| `--so-bg` | light ice (#F2F4F8) | Page background |
| `--so-text` | dark slate (#1F2937) | Body text |

## Future Architecture Extensions

| Phase | Extension |
|---|---|
| 3 | Notification adapters added to Worker |
| 4 | PAM adapter added to Worker (read-only) |
| 6 | Analysis engine + SQL views; optional Python container |
| 7 | AI service (Ollama + Qdrant + masking + audit) on isolated subnet |
| 8 | Approval workflow service + snapshot adapter |

Each extension keeps the core flow unchanged.

## Architecture Decision Records

See `docs/adr/`:
- ADR-0001 Technology stack
- ADR-0002 Read-only first
- ADR-0003 Worker service vs script
- ADR-0004 Mock integrations first
- ADR-0005 AI/RAG later phase
- ADR-0006 Approval-based remediation
- ADR-0007 IIS hosting model
- ADR-0008 Read-only identity lookup
- ADR-0009 Durable Operational Record to Jira workflow
