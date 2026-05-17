# 05 — Security Model

Security is the defining constraint of this project. This document is the canonical reference for all security boundaries.

## Threat Model (Summary)

| Threat | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Compromised service account → write operations on target servers | High | Low | JEA constrained endpoint blocks all writes at the PowerShell layer |
| SQL injection in API | High | Low | Parameterized queries, EF Core, FluentValidation |
| Webhook spoofing | Medium | Medium | HMAC-signed payloads, source IP allowlist |
| Audit tampering | High | Low | Append-only triggers, separate DB role for audit writes |
| Privilege escalation through UI | High | Low | Server-side authorization on every endpoint |
| Leakage of internal data via AI (Phase 7) | High | Medium | Self-hosted only + mandatory masking |
| Misuse as employee surveillance | Medium | Medium | UI framing, role separation, audit of audit queries |

## Authentication

### Web UI and API

- **Windows Authentication** via Active Directory.
- Service principal: the IIS app pool runs under a domain-joined service account.
- User identity flows through `HttpContext.User`.
- No custom token issuance, no JWT in MVP.

### Webhook Endpoint

If the approved Turuncuhat integration is webhook-based, the approved caller invokes `/api/v1/alerts/webhook`. Authentication:

- **HMAC-SHA256** signature in `X-SecureOps-Signature` header.
- Shared secret stored in PAM, retrieved at API startup.
- Timestamp in payload, request rejected if older than 5 minutes (replay protection).
- Source IP allowlist for the approved webhook caller.

### Service-to-Service

- Worker → SQL: integrated Windows authentication with the Worker service account.
- Worker → Target Servers: WinRM HTTPS + Kerberos, with the JEA endpoint name in the connection.

## Authorization (RBAC)

### Roles

| Role | AD Group (placeholder) | Permissions |
|---|---|---|
| Operator | `CONTOSO\SecureOps-Operators` | View alerts, view diagnostic results, view personal audit, copy ticket text |
| TeamLead | `CONTOSO\SecureOps-Leads` | All Operator + trigger manual diagnostic, view team audit, manage server tags |
| Admin | `CONTOSO\SecureOps-Admins` | All TeamLead + configuration changes, rule management, system administration |
| Auditor | `CONTOSO\SecureOps-Auditors` | Read-only access to all audit data, including AI audit |

Group names are configured in `appsettings.json`; the table `dbo.RbacRoles` maps codes to group names.

### Policy Names

Use the constants in `SecureOps.Shared.Auth.Policies`:

```csharp
public static class Policies
{
    public const string OperatorOrAbove = "OperatorOrAbove";
    public const string TeamLeadOrAbove = "TeamLeadOrAbove";
    public const string AdminOnly = "AdminOnly";
    public const string AuditorOnly = "AuditorOnly";
    public const string CanViewAudit = "CanViewAudit";          // Auditor OR Admin
    public const string CanTriggerDiagnostic = "CanTriggerDiagnostic"; // TeamLead OR Admin
}
```

### Enforcement

- Every controller method has `[Authorize(Policy = ...)]`.
- Every Blazor page has `[Authorize(Policy = ...)]`.
- Server side enforces; UI hiding is courtesy, never security.

## JEA (Just Enough Administration)

JEA is the layer that physically prevents the service account from running write cmdlets.

### Configuration Files

Stored in `scripts/jea/`:

- `SecureOpsDiagnosticEndpoint.pssc` — Session configuration.
- `SecureOpsDiagnosticRole.psrc` — Role capability with the visible cmdlet whitelist.
- `Install-SecureOpsJeaEndpoint.ps1` — One-time install script for each target server.

### Whitelist (Canonical)

Allowed cmdlets:

```
Get-Disk
Get-PSDrive
Get-Volume
Get-Partition
Get-PhysicalDisk
Get-CimInstance
Get-WmiObject
Get-Process
Get-Service
Get-WinEvent
Get-EventLog
Get-ChildItem
Get-Item
Get-Content
Test-Path
Get-Counter
Get-NetTCPConnection
Get-NetAdapter
Get-ScheduledTask
Get-Website
Get-WebApplication
Get-WebAppPoolState
Get-WebBinding
Get-WebConfigurationProperty
Measure-Object
Select-Object
Sort-Object
Where-Object
ForEach-Object
Format-List
Format-Table
Out-String
ConvertTo-Json
```

### Forbidden Cmdlets

Everything not on the whitelist. Notably:

- `Stop-*`, `Start-*`, `Restart-*` for services, processes, computers
- `Remove-*`, `Clear-*`, `Set-*` (writes)
- `New-*` (creates)
- `Invoke-Command` with script blocks
- `Add-LocalGroupMember`, `Set-LocalUser`
- Anything that modifies AD, registry, files, or services

### Adding to the Whitelist

To add a cmdlet:

1. Write an ADR explaining the operational need.
2. Get Bilgi Güvenliği approval (recorded in the ADR).
3. Update `SecureOpsDiagnosticRole.psrc`.
4. Update this whitelist section.
5. Update `.cursor/rules/050-security-audit-rules.mdc`.
6. Redeploy the JEA endpoint to all target servers.

## Service Account

- Single service account: `CONTOSO\svc-secureops` (placeholder name).
- Password managed by PAM (BeyondTrust-style), rotated regularly.
- Member of: `CONTOSO\SecureOps-ServiceAccounts` (operational AD group).
- Granted: Log on as a service, JEA endpoint access on pilot servers, DB access on the SecureOps database.
- NOT granted: Local admin on target servers. JEA enforces what it can do.

### Worker Privileged-Access Path — Pending Stakeholder Input

The final Worker-to-target-server access model is **not decided yet**. The decision is pending input from the PAM / BeyondTrust team, Bilgi Güvenliği, and the team lead.

| Scenario | Description | Current status |
|---|---|---|
| X | Worker uses a BeyondTrust API or brokered session flow before opening WinRM access to the target server. | Candidate; depends on PAM capability and approval |
| Y | Worker uses direct WinRM over Kerberos to the JEA endpoint, while BeyondTrust remains the PAM system for human operators. | Candidate; requires explicit stakeholder approval |
| Z | The currently documented direct-JEA model continues as-is if the PAM team gives explicit acceptance for service-account automation. | Candidate; requires explicit acceptance |

Until that decision is recorded:

- JEA remains mandatory in every scenario.
- The Worker must not be treated as already approved to bypass BeyondTrust.
- Architecture and implementation notes referring to direct WinRM + JEA describe the current documented model, not a closed decision.

## Audit (See `docs/08-audit-model.md` for Detail)

- Append-only `audit.AuditLog` table.
- UPDATE/DELETE blocked by trigger.
- Every state-changing operation creates an audit entry.
- Every privileged read (audit query, AI prompt) creates an audit entry.
- Minimum 36-month retention.
- Auditor and Admin roles can query the audit log; their queries are themselves audited (meta-audit).

## Audit Is Not Surveillance — Canonical Language

The framing used in all user-facing text:

> "Operational response verification, SLA evidence, and audit. This is process auditing, not personnel monitoring."

The Turkish version (used in management communications, mirrored in `docs/14-management-summary-tr.md`):

> "Kişi takibi amacıyla değil; kritik alarmlarda operasyonel müdahale doğrulama, SLA ve audit amacıyla."

### Practical Enforcement

UI:
- No leaderboards.
- Default filters by alarm, time, or server — not by operator.
- Operator-level filters available only to Auditor and Admin, and produce a meta-audit entry.

Reports:
- Aggregate to team level by default.
- Per-operator breakdown available to compliance roles only, with watermarking.

Naming:
- Page titles: "Operational Audit", "Response Verification".
- Filter labels: "Action type", "Time window".
- Never: "Operator performance", "Response time leaderboard", "Operator comparison".

## Secrets Management

| Secret | Storage | Retrieval |
|---|---|---|
| Service account password | PAM | At service startup (IIS app pool identity from AD anyway; for direct DB use, integrated auth) |
| Webhook HMAC secret | PAM or `appsettings.Local.json` (dev only) | At API startup |
| SQL connection string | Integrated auth preferred; otherwise PAM | At service startup |
| SMTP credentials (Phase 3) | PAM | At startup |
| Teams webhook URL (Phase 3) | `appsettings` (URL is the secret) | At startup |
| AI model API key (Phase 7) | N/A — self-hosted | N/A |

### Logging Hygiene

- Serilog destructuring filters strip known-secret property names.
- Connection strings logged with password redacted.
- Stack traces never contain secrets.

## Network Boundaries

| Path | Protocol | Port | Auth |
|---|---|---|---|
| Internal users → UI | HTTPS | 443 | Windows Auth |
| Internal users → API | HTTPS | 443 | Windows Auth |
| Monitoring → API webhook | HTTPS | 443 | HMAC + IP allowlist |
| Worker → SQL Server | TLS | 1433 | Integrated Auth |
| Worker → Target Servers | WinRM HTTPS | 5986 | Kerberos + JEA |
| Worker → Monitoring SWIS (Phase 6+) | HTTPS | 17774 | Service account |
| Worker → Teams webhook (Phase 3+) | HTTPS | 443 | URL secret |
| Worker → SMTP relay (Phase 3+) | TCP | 25 / 587 | Internal |

No public ingress. No outbound to public AI services ever.

## Data Masking (Phase 7 Requirement)

Before any data reaches the AI layer:

| Class | Original | Masked |
|---|---|---|
| Hostname | `APPSRV-12.contoso.com` | `SRV-A1B2C3` (consistent hash) |
| Username | `CONTOSO\jane.doe` | `<user-role>` (e.g., `<admin-user>`) |
| IP address | `10.4.51.22` | `10.4.x.x` |
| User path | `C:\Users\jane.doe\...` | `C:\Users\<masked>\...` |
| Connection string | `Server=...;Pwd=...` | `<connection-string>` |
| Customer ID | (any pattern) | `<customer-id>` |

Masking is implemented in `SecureOps.Infrastructure.Ai.DataMasker` and tested in `SecureOps.Tests.Unit.Ai`.

## Configuration Hardening

- HTTPS everywhere (no plain HTTP listener).
- TLS 1.2 minimum.
- HSTS enabled.
- Anti-forgery tokens on POST endpoints called from the UI.
- Rate limiting on webhook (100 req/min per source IP).
- CSP headers on UI.
- No server version disclosure (remove `Server` header).

## Incident Response

If a security issue is detected:

1. Immediately stop the Worker service (kill switch).
2. Disable the API webhook endpoint via config flag.
3. Preserve audit logs and ship to forensic storage.
4. Notify Bilgi Güvenliği and Siber Güvenlik.
5. Review the audit log for the suspected window.
6. Apply fix; reactivate after review.

## References

- `.cursor/rules/050-security-audit-rules.mdc` — agent enforcement rules
- `docs/08-audit-model.md` — audit specification
- `docs/10-ai-rag-strategy.md` — AI-specific security (Phase 7)
