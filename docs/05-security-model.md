# 05 — Security Model

Security is the defining constraint of this project. This document is the canonical reference for all security boundaries.

## Threat Model (Summary)

| Threat | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Compromised service account → write operations on target servers | High | Low | JEA constrained endpoint blocks all writes at the PowerShell layer |
| SQL injection in API | High | Low | Parameterized Dapper queries only, request validation |
| Webhook spoofing | Medium | Medium | HMAC-signed payloads, source IP allowlist |
| Audit tampering | High | Low | Append-only triggers, separate DB role for audit writes |
| Privilege escalation through UI | High | Low | Server-side authorization on every endpoint |
| Identity lookup misuse as people search | Medium | Medium | TeamLead/Admin only; exact lookup only; bounded rate; all requests/outcomes audited with target hash |
| Leakage of internal data via AI (Phase 7) | High | Medium | Self-hosted only + mandatory masking |
| Misuse as employee surveillance | Medium | Medium | UI framing, role separation, audit of audit queries |

## Authentication

### Web UI and API

- Corporate OIDC readiness is implemented but disabled by default. Current Demo/Test authentication remains available until activation is separately approved.
- The browser UI uses Authorization Code flow with PKCE. The UI stores the API access token only in its server-side browser-session store and relays it to the API as a bearer token.
- The API validates issuer, signature, audience, and lifetime, then reduces the token to a bounded reviewed identity. SecureOps does not issue its own corporate identity token.
- `issuer + sub` produces the opaque stable access identity. Validated `loginname`, `displayname`, `mail`, and `uid` claims are stored as bounded nullable profile metadata; changing them does not change access identity or authorization. `loginname` remains the exact Jira and optional Active Directory lookup identity.
- `uygulama-role` is retained only as non-authoritative evidence. It never grants a SecureOps role or capability.
- Authentication establishes only a corporate principal. Persisted SecureOps Access -> Role -> Capability remains the authorization authority, and unknown authenticated users remain pending.
- SecureOps never requests or handles the user's LDAP/Jira password. The Jira integration credential remains only the REST technical identity.

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

### Application Roles

Resources.View is available to approved users through all reviewed roles.
Resources.Manage is granted only to Admin and the limited ResourceCurator role,
not Lead. Existing Access.AssignRoles administrators can assign ResourceCurator
with the versioned role-replacement endpoint; it grants no other capability and
no destination-system permission. No real user is granted this role by migration.
Private favourites and shift sets are owned by internal UserId and cannot be
read or changed by another user, including Admin. Current category visibility and
link active/archive state are rechecked on every saved-reference response.

| Role | Permissions |
|---|---|
| Operator | View Operational Records and generate read-only Jira previews |
| Lead | Identity lookup, Operational Record create/retry, diagnostics and team view |
| Admin | All implemented application capabilities, including access approval, role assignment, and management reporting |
| JiraPublisher | Operational Record view/preview/create/retry |
| Auditor | Read-only audit, workflow diagnostics, and management reporting |
| ReadOnly | Operational Record view only |

Phase 1A identity lookup requires the `Identity.Lookup` capability. Operators do not receive this privileged read in the first release.

Management summary and paginated operator-activity reporting require the separate `Reporting.ManagementView` capability, assigned only to Admin and Auditor. These privileged reads are themselves audited. Reports expose bounded process evidence and must not rank, compare, or score individuals.

First-seen authenticated users are `Pending` and receive no operational capability. Administrators approve requests and assign persisted application roles. Disabled status is checked on each capability-protected request. The optional first-Admin bootstrap accepts only one exact server-configured login name from a validated OIDC issuer, requires SQL persistence, and permanently closes after any Admin assignment has ever existed. It is not an authentication-claim or AD-group authorization path.

### Authentication and Role Strategy

OIDC activation remains server-owned and requires the approved corporate metadata/client contract. Direct LDAP/AD password login is prohibited because it would make SecureOps handle user passwords directly.

The persisted SecureOps access record remains the authorization boundary regardless of authentication source. PAM/BeyondTrust may verify privileged sessions or supply metadata later, but it is not the normal application login mechanism. OIDC resolves issuer/subject to the same corporate-principal boundary and does not rewrite application authorization.

Future role vocabulary, subject to ADR before implementation:

| Future role | Intended scope |
|---|---|
| SuperAdmin | Break-glass platform administration |
| PlatformAdmin | SecureOps platform configuration and environment operations |
| AutomationAdmin | Automation catalog and worker execution ownership |
| Manager | Management reporting and approval visibility |
| TeamLead | Operational lead functions, including Phase 1A lookup |
| Operator | Daily alert/diagnostic operation |
| Auditor | Audit review and evidence export |
| SecurityReviewer | Security review, audit verification, and data-flow approval |

Initial authorization intent:
- Identity lookup: `TeamLeadOrAbove` or a future explicitly approved privileged-support role.
- Audit viewing/export: Auditor, SecurityReviewer, Manager, PlatformAdmin, or Admin-equivalent roles.
- Configuration changes: PlatformAdmin or SuperAdmin.
- Future remediation approval: Manager, TeamLead, or other ADR-approved approvers.
- Future remediation execution: AutomationAdmin/service workflow only.
- Future AI/RAG access: restricted by role and masking policy; never broadly available by default.

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

## Identity Lookup (Phase 1A)

IdentityLookup / PamAdUserLookup is a backend-only privileged read for incident response verification.

Allowed:
- Exact lookup of one PAM account or AD username per request.
- Config-based normalization such as trimming, optional `DOMAIN\` stripping, and case normalization.
- Read-only AD lookup by exact `sAMAccountName` and, when UPN-shaped, exact `userPrincipalName`.
- Returning only display name, account name, UPN, mail, department, title, manager display name, enabled/locked state, and source.

Forbidden:
- AD writes of any kind: password reset, unlock, enable/disable, group modification, attribute update.
- PAM writes or session changes.
- Wildcard, bulk, fuzzy, or directory-browsing search.
- Returning group membership, SID, distinguished name, phone, address, password metadata, or raw LDAP attributes.

Every lookup writes audit entries for request and outcome. Purpose/context is optional for read-only identity and directory lookup; supplied text is represented only by hash and length, and no default reason is fabricated. Audit details must not store returned personal-detail fields or raw directory targets.

Production hardening:
- `POST /api/v1/identity/lookup` is the only endpoint that accepts an account value. The API must not add `GET` lookup routes by account because account values would leak into URLs, browser history, proxy logs, and IIS access logs.
- Safe metadata endpoints may exist: `GET /api/v1/identity/me`, `GET /api/v1/identity/lookup/capabilities`, `GET /api/v1/health/audit-store`, and `GET /api/v1/health/identity-provider`. These endpoints must not accept account input, file paths, connection strings, or personal AD data.
- `POST /api/v1/identity/lookup` uses `TeamLeadOrAbove`; Operator-only and Auditor-only users are denied.
- The lookup POST endpoint is rate-limited by authenticated user + endpoint, not by IP only.
- If audit writing is unavailable, lookup fails closed and must not query AD or PAM.
- Provider implementations enforce exact input safety again at provider level: max length, no wildcard/filter/bulk input, and exact-match verification.
- Returned failures are generic. Detailed provider and audit exceptions are logged internally with correlation ID.
- `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Correlation-ID` are supported. Load balancer proxy trust must be controlled outside the app or by host configuration.

### Swagger Security Model

- Swagger/OpenAPI declares Windows Integrated Authentication using Negotiate.
- Development may expose Swagger locally for implementation and Postman testing.
- Non-development Swagger routes require authentication.
- Non-development API endpoints require authenticated users by fallback policy unless a route is explicitly exempted.
- Swagger examples and screenshots must use placeholders only. Do not include real account names, real personal names, real email addresses, or production incident IDs.

### Audit Storage and Folders

Audit logs are not technical application logs.

| Store | Purpose | Example folder |
|---|---|---|
| Application publish folder | Application binaries and configuration | IIS site publish path |
| Technical app logs | Troubleshooting and stack traces | `D:\SecureOps\Logs` |
| Audit logs | Operational evidence and privileged-read trail | `D:\SecureOps\Audit` |

Development and Test may use `Audit.Provider=File`, but the folder must be configurable and outside the publish directory. Production must not use `InMemory`; production must run `Audit.FailClosed=true`. SQL Server is the target production audit store when the database is available, using `ConnectionStrings:SecureOpsDb`.

Persistent audit providers use a bounded background queue. Request threads enqueue audit events and do not perform file IO. If a queued persistent write later fails, the audit-store health state becomes `Unhealthy` with a safe code such as `AuditSinkUnavailable`; with fail-closed enabled, later identity lookups are blocked before AD/PAM provider access until audit writes recover. The technical log records a Critical event without account names or returned personal detail fields.

IIS app pool identity permissions:
- Application publish folder: Read and Read & Execute only. Do not grant write permissions to the deployed application directory for audit output.
- Audit folder, for example `D:\SecureOps\Audit`: create files, write, append, and read as required for audit health verification.
- Technical log folder, for example `D:\SecureOps\Logs`: create files, write, append, and read as required for troubleshooting.
- Delete permission is not required for Phase 1A. If a later retention job deletes old files, that permission must be granted only to the retention identity and documented separately.
- Use a dedicated app pool identity or service account. Do not grant Domain Admin, broad local admin, or unrelated file-system privileges for IdentityLookup.

### Load Balancer and Windows Authentication

- Forward `X-Forwarded-For` to preserve source IP in audit.
- Forward `X-Forwarded-Proto` when TLS terminates before IIS.
- Kerberos behind a load balancer requires SPN planning for the service name. NTLM may require connection affinity.
- IdentityLookup is stateless and does not require sticky sessions by itself; authentication mode or later Blazor Server UI may require it.

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
5. Update `docs/agent-guides/050-security-audit.md`.
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

Directory Explorer privileged reads require `Identity.Groups.View` or `Identity.Groups.Members.View`; privileged-group analysis separately requires `Identity.PrivilegedGroups.View`. All operations use exact server-controlled queries under the API process identity and reject raw LDAP/filter input and credentials. Recursive Phase 2 reads are bounded by depth, nodes, edges, paths, timeout, cache, and rate limits, return explicit truncation metadata, and audit only safe counts/outcomes rather than membership, SPN, health, path, or raw-DN payloads.

## Application Session Governance

SecureOps tracks an opaque server-side application session after corporate authentication. The cookie contains no credential, role, access decision, directory identity, network address, or device data and cannot grant access by itself. Each protected request remains subject to authentication plus current application access and `AccessVersion` validation. Idle timeout, absolute timeout, logout, administrative revocation, access disable, and access-version change end effective sessions. Last-seen persistence is throttled and heartbeats are not audited.

Pilot and Production require a persistent ASP.NET Core Data Protection key ring protected at rest. Runtime key-ring paths, certificates, and ACLs are server-owned configuration; key material is never stored in source control. Negotiate remains the interim authentication provider and no LDAP username/password login is introduced.

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
| API → Active Directory | LDAP/LDAPS or domain APIs | 389/636 or domain default | App pool/service identity, read-only |
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

- `docs/agent-guides/050-security-audit.md` — agent enforcement rules
- `docs/08-audit-model.md` — audit specification
- `docs/10-ai-rag-strategy.md` — AI-specific security (Phase 7)
