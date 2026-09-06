# 050 — Security and Audit Rules (Always Apply)

## Applicability

- **Purpose:** Non-negotiable security and audit rules. Read every time.
- **Applies to:** Every task.
- **Loading:** Routed explicitly from `AGENTS.md`; do not assume automatic discovery.

## Hard Boundaries

These rules are non-negotiable. If a user request appears to violate them, refuse, explain, and propose a compliant alternative.

### 1. Read-Only on Target Servers

In MVP (Phase 1–6), no code on this platform may:

- Stop, start, or restart any Windows service.
- Recycle, stop, or start any IIS application pool.
- Delete, modify, or create any file on target servers.
- Reboot, shutdown, or suspend any server.
- Modify local administrators, AD groups, file permissions, or registry.
- Push configuration changes, patches, or installers.
- Execute arbitrary `Invoke-Command` script blocks not in the JEA whitelist.

The JEA constrained endpoint enforces this at the PowerShell layer. The code layer must also refuse to construct any command that would attempt these operations.

### 2. No Public AI

Forbidden destinations for any production data:

- `api.openai.com`
- `api.anthropic.com`
- `generativelanguage.googleapis.com`
- `api.cohere.ai`
- Any other public LLM inference endpoint

This applies to:
- Alarm payloads
- Hostnames
- Log content
- User identifiers
- Any text containing names of internal servers, applications, or people

AI work is Phase 7, self-hosted only. See `docs/10-ai-rag-strategy.md`.

### 3. JEA Mandatory

All PowerShell Remoting to target servers must go through the JEA constrained endpoint defined in `scripts/jea/`. Code that bypasses JEA — for example, opening an unconstrained runspace — is a security violation.

The cmdlet whitelist is in `docs/05-security-model.md`. To extend the whitelist, write an ADR.

### 4. Append-Only Audit

The `audit.AuditLog` table is append-only. UPDATE and DELETE are blocked by SQL triggers. Any code that attempts to modify audit rows must fail.

```sql
CREATE TRIGGER audit.tr_AuditLog_BlockUpdateDelete
ON audit.AuditLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51000, 'Audit log is append-only.', 1;
END;
```

Retention: minimum 36 months. Configurable upward only, never downward.

### 5. Authentication

- Web UI: Windows Authentication via Active Directory.
- API (called from internal): Windows Authentication.
- Webhook from monitoring: HMAC-signed shared secret in header.
- Service-to-service: Windows Authentication or mTLS — never plain API keys.

No custom auth schemes. No bearer tokens for production endpoints.

### 6. Authorization (RBAC)

Roles map to AD groups. Group names are configurable; placeholders below.

| Role | AD Group (placeholder) | Permissions |
|---|---|---|
| Operator | `CONTOSO\SecureOps-Operators` | View alerts, view diagnostics, view own audit |
| TeamLead | `CONTOSO\SecureOps-Leads` | Operator + trigger manual diagnostic, view team audit |
| Admin | `CONTOSO\SecureOps-Admins` | All operational features + config |
| Auditor | `CONTOSO\SecureOps-Auditors` | Read-only access to all audit data |

Use ASP.NET Core authorization policies named in `SecureOps.Shared.Auth.Policies`. Never check group names inline.

### 7. Secrets Management

- Service account credentials stored in PAM (BeyondTrust-style), retrieved at runtime.
- Webhook shared secrets in `secrets.json` outside source control, or in a secret store.
- Connection strings: integrated Windows authentication preferred; if password required, stored in PAM.
- Never log secrets. Never include secrets in stack traces. Use Serilog destructuring filters.

### 8. Network Boundaries

- API and UI exposed only on the internal network. No public ingress.
- Worker → Target Server: WinRM HTTPS (port 5986), Kerberos auth.
- Worker → SolarWinds: HTTPS, mutual auth if available.
- Worker → SQL Server: TCP 1433 with TLS.
- Outbound to public internet: blocked at firewall except for explicit allow-list (Windows Update, etc., not application traffic).

### 9. Data Masking

Before any data reaches the AI layer (Phase 7) or any external log aggregator, the following must be masked:

- Hostnames (replace with consistent hash)
- Usernames (replace with role)
- IP addresses (replace with subnet)
- File paths containing user names (`C:\Users\<name>` → `C:\Users\<masked>`)
- Connection strings, tokens, password fragments

Masking happens at the data layer, before serialization.

### 10. Audit Is Not Surveillance

The framing of all audit features is **operational response verification**, not person-level performance monitoring. Use language from `docs/05-security-model.md`:

> Audit records what was done, when, and by what process — not who performed best.

UI labels, dashboards, and reports must use this framing. Phrases like "operator performance", "individual response time leaderboard", or "comparison of operators" are forbidden.

## What an Agent Must Do

When implementing any feature, an agent must:

1. Confirm the feature does not violate any rule above.
2. If unclear, ask the user before coding.
3. Add audit calls at every state-changing point and every privileged read.
4. Verify the JEA whitelist covers any cmdlets the feature requires; if not, surface this for ADR review before extending.
5. Add tests that verify forbidden operations fail.

## Reference

Read `docs/05-security-model.md` and `docs/08-audit-model.md` for the full specification.
