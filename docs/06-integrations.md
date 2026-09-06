# 06 — Integrations

All external system integrations. The universal rule: **mock-first**. Every integration starts with an in-memory mock behind an interface; the real adapter is built and tested in isolation.

## Adapter Pattern

Every integration follows the same pattern:

```csharp
// 1. Interface in SecureOps.Domain.Integrations or .Infrastructure.Integrations
public interface IMonitoringPlatformClient
{
    Task<AlertDetail?> GetAlertByExternalIdAsync(string externalId, CancellationToken ct);
    Task<bool> VerifyConnectionAsync(CancellationToken ct);
}

// 2. Mock implementation in SecureOps.Infrastructure.Integrations.Mocks
public sealed class MockMonitoringPlatformClient : IMonitoringPlatformClient { ... }

// 3. Real implementation in SecureOps.Infrastructure.Integrations.SolarWinds
public sealed class SolarWindsClient : IMonitoringPlatformClient { ... }

// 4. DI registration switches by config
services.AddSingleton<IMonitoringPlatformClient>(sp =>
    options.UseMock
        ? new MockMonitoringPlatformClient()
        : new SolarWindsClient(...));
```

The mock is the default in development and integration tests. The real adapter is wired only in environments with the real system available.

## Monitoring Chain (SolarWinds → OpsBridge → Turuncuhat)

The real alarm chain at CONTOSO is:

1. **SolarWinds** detects the alarm condition.
2. **monthly.thy.com / HPE OpsBridge** appears to provide event-detail viewing between systems.
3. **Turuncuhat** receives the alarm, creates the `EVT-XXXXX` record, opens PR/OR/OCO records as needed, sends mail, places IVR calls, and owns acknowledgment/closure workflow.

For reusable architecture language, this is still the **monitoring platform chain**. In our environment, Turuncuhat is the durable operational workflow system, not merely a generic ticketing tool.

Three SolarWinds-specific paths exist; we use them in order of phase.

### Path 1 — Webhook (MVP, Phase 1)

The original MVP assumption was that the monitoring platform calls our webhook when an alarm fires. With the real landscape now known, the preferred inbound integration point may instead be **Turuncuhat** rather than SolarWinds directly. Exact method is pending stakeholder input.

- Endpoint: `POST /api/v1/alerts/webhook`
- Authentication: HMAC-SHA256, header `X-SecureOps-Signature`
- Replay protection: timestamp in payload, 5-minute window
- Source IP allowlist

Sample payload (full schema in `contracts/schemas/alarm-payload.schema.json`):

```json
{
  "externalId": "SW-ALERT-12345",
  "serverName": "APPSRV-12.contoso.com",
  "alertType": "Disk",
  "severity": "High",
  "message": "Disk D: at 92% on APPSRV-12",
  "occurredAt": "2026-06-15T14:23:17+03:00",
  "metadata": {
    "drive": "D:",
    "usedPercent": 92,
    "freeGb": 8
  }
}
```

### Path 2 — Polling SWIS REST API (Phase 6+)

Phase 6 may add active polling for back-fill and reconciliation:

- Base URL: `https://solarwinds.contoso.local:17774/SolarWinds/InformationService/v3/Json/Query`
- Authentication: service account credentials
- Query SWIS using SWQL
- Reconciliation: nightly comparison of received alarms vs. SWIS records, surface gaps

This is supplementary, not a replacement for the webhook.

### Path 3 — Trap Bridge (Not Planned)

Some sites use SNMP traps. We do not plan a direct SNMP listener; if needed, the existing monitoring chain already converts traps into managed workflow events.

## Turuncuhat Integration (Pending Stakeholder Input)

Turuncuhat is the central ITSM + IVR system in the real workflow. It creates EVT records, sends alarm mail/IVR, and is the place where alarms are acknowledged and closed.

Expected SecureOps relationship:

- **Inbound:** receive or fetch EVT data so diagnostics are tied to the Turuncuhat workflow record.
- **Outbound:** after operator review, potentially write EVT closure data back to Turuncuhat, including status, `Kapanış açıklaması`, and `Aksiyon alındı mı?`.

Open questions — **pending stakeholder input; see open questions in `AGENTS.md`:**

1. Does Turuncuhat support **webhook delivery**, or must SecureOps use an **API pull** model?
2. Will the approved integration be **read-only** (fetch EVT data only) or **read-write** (also close/update EVT after operator review)?

Until those questions are answered:

- Model Turuncuhat behind an interface and start with a mock adapter.
- Do not assume direct SolarWinds → SecureOps delivery is the final production path.
- Keep Turuncuhat as the organization-facing ticket/EVT system of record.

## PAM Integration (BeyondTrust-style, Phase 4+)

Read-only correlation only.

- Capabilities used: query active sessions, query historical sessions by user and time.
- Authentication: service account in the PAM tool, scoped to read.
- Use: in Phase 4, correlate alert response timestamps with PAM session activity to verify the operator was active on the server during the response.

We do NOT:
- Modify PAM configuration.
- Initiate or terminate PAM sessions.
- Bypass PAM for our own access (the Worker uses JEA via WinRM, not via PAM).

### PAM Account Resolver Hook (Phase 1A)

Phase 1A adds a mocked `IPamAccountResolver` hook for identity lookup. The hook exists so later BeyondTrust account metadata or Phase 4 session correlation can enrich a PAM account with an AD account identity.

First release behavior:

- The resolver is mock/direct-pass-through by default.
- No BeyondTrust API call is required.
- No PAM write or session operation is allowed.
- Real BeyondTrust metadata lookup requires stakeholder approval and an ADR/update before it is enabled.

## Active Directory

- Used at authentication time (Windows Auth).
- Used at startup to resolve AD group membership for RBAC.
- Used by Phase 1A IdentityLookup for exact, read-only account resolution.
- Caching: 5-minute TTL for group membership; configurable.
- LDAP queries through `System.DirectoryServices.AccountManagement`.

IdentityLookup constraints:

- One exact account per request.
- Account values are accepted only in the POST body of `/api/v1/identity/lookup`; no URL/query-string account lookup is allowed.
- Default provider is mock in development; production can enable the read-only AD provider by configuration.
- `GET /api/v1/identity/lookup/capabilities` reports effective behavior of the active provider. `supportsUpnLookup` is true only when exact UPN lookup is supported and enabled.
- The mock provider performs deterministic exact sAMAccountName lookup and optional exact UPN lookup over the same seeded identities. Unknown exact values remain NotFound.
- Audit write availability is required before provider access; lookup fails closed if audit is unavailable.
- Provider implementations enforce max length, exact-input validation, and exact-match result verification.
- Real AD provider calls use a short timeout (`IdentityLookup:ProviderTimeoutSeconds`, default 3 seconds). Provider timeout returns the safe user-facing `DirectoryProviderTimeout` error code and writes `IdentityLookupProviderTimeout`.
- The lookup POST endpoint is rate-limited.
- The approved lookup attributes are display name, `sAMAccountName`, UPN, mail, department, title, manager display name, enabled state, and locked state.
- No group membership, SID, distinguished name, phone, address, password metadata, or raw LDAP attributes are returned.

## Audit Store Integration

Phase 1A supports three audit providers:

| Provider | Use | Notes |
|---|---|---|
| `InMemory` | Local development only | Not persistent; forbidden in Production |
| `File` | Development/Test/UAT | Writes JSONL files to `Audit:File:Directory`, outside the publish folder |
| `SqlServer` | Production target | Uses `ConnectionStrings:SecureOpsDb`; schema remains the target production architecture |

Persistent providers use a bounded background queue. Request threads do not write audit files directly. If the queue cannot accept an event and fail-closed behavior is active, IdentityLookup returns `AuditUnavailable` and does not query AD. If a queued persistent write is accepted but the background sink later fails, audit health becomes `Unhealthy` with `AuditSinkUnavailable`; subsequent fail-closed lookups are blocked before provider access until audit writes recover.

## Teams Notification (Phase 3+)

Outgoing webhook to a Teams channel.

- One channel per environment (Prod, Test).
- Optional: separate critical-vs-warning channels.
- Authentication: the webhook URL is the secret.
- Rate limit: respect Teams limits; queue and batch if needed.
- Adaptive Cards format for rich layout.

Sample card:

```json
{
  "type": "message",
  "attachments": [{
    "contentType": "application/vnd.microsoft.card.adaptive",
    "content": {
      "type": "AdaptiveCard",
      "version": "1.4",
      "body": [
        { "type": "TextBlock", "text": "Disk Alert: APPSRV-12", "weight": "Bolder", "size": "Medium" },
        { "type": "FactSet", "facts": [
          { "title": "Drive", "value": "D:" },
          { "title": "Used", "value": "92%" },
          { "title": "Free", "value": "8 GB" }
        ]}
      ],
      "actions": [
        { "type": "Action.OpenUrl", "title": "Open in SecureOps", "url": "..." }
      ]
    }
  }]
}
```

## Mail (Phase 3+)

- SMTP relay (existing internal relay).
- Authentication: anonymous within the relay's allowed senders, or service account.
- Templates in Razor (`*.cshtml`) for HTML mail; plain-text fallback.
- Bulk-safe: throttled to avoid hitting relay limits.

## Ticketing System (Turuncuhat in our environment; Phase 3+, Optional)

Turuncuhat is the real ticketing system in this environment. Initially, ticket integration may still remain **paste-ready text** only if stakeholders approve read-only integration:

- The system generates a structured block for the operator to paste into the ticketing tool.
- Format: subject, summary, technical detail, diagnostic results, recommended next steps.
- No direct API call to the ticketing tool.

If direct integration is later required or approved:

- Add a `ITicketingClient` interface.
- Mock implementation for dev.
- Real adapter as a separate library.

## Snapshot / Virtualization (Phase 5+, Optional)

For Phase 8 (write operations), the system queries snapshot state before any write:

- "Does this VM have a recent snapshot?"
- "Within what time window?"

Read-only API call to the virtualization platform. No snapshot creation by this system.

## Ansible / AWX (Phase 6+ Optional, Not in MVP)

The existing Ansible/AWX is **not modified** in MVP. If later we decide to use it:

- AWX API for launching playbooks.
- Service account in AWX scoped to specific job templates.
- All launches audited in SecureOps.

But explicitly: **not before Phase 6, and only by ADR**.

## SQL Server

The SecureOps database lives on the existing enterprise SQL Server.

- Separate database, separate login.
- Integrated Windows auth from the service account.
- Backup managed by the SQL Server team's existing process.
- Schema migrations via EF Core or Flyway-style SQL scripts in `sql/migrations/`.

## Configuration Surface

Every integration has a config section:

```json
{
  "SolarWinds": {
    "WebhookSecretFromPam": "secureops/solarwinds-webhook-hmac",
    "AllowedSourceIps": ["10.0.x.x/16"],
    "SwisBaseUrl": "https://solarwinds.contoso.local:17774",
    "UseMock": false
  },
  "Pam": {
    "BaseUrl": "https://beyondtrust.contoso.local",
    "ServiceAccountFromPam": "secureops/pam-readonly",
    "UseMock": true
  },
  "IdentityLookup": {
    "Provider": "Mock",
    "StripDomainPrefix": true,
    "NormalizeToLowerInvariant": true,
    "EnableUpnLookup": true,
    "MaxAccountLength": 128,
    "AllowedAccountPattern": "^[a-zA-Z0-9._@-]+$"
  },
  "Teams": {
    "WebhookUrls": {
      "Default": "from-pam",
      "Critical": "from-pam"
    },
    "UseMock": true
  },
  "Mail": {
    "SmtpHost": "smtp.contoso.local",
    "SmtpPort": 25,
    "FromAddress": "secureops@contoso.local",
    "UseMock": true
  }
}
```

`UseMock: true` makes the mock adapter active. The Worker logs a clear "MOCK IN USE" warning at startup for any mocked integration.

## Integration Testing Strategy

- **Unit tests** with mocks: validate contract handling.
- **Integration tests** against the mock adapters: validate end-to-end orchestration.
- **Smoke tests** against real integrations: separate test project, runs only in environments with real systems available.
- **Contract tests**: validate JSON payloads against `contracts/schemas/*.schema.json`.

## Operational Record and Jira Backend Foundation

The backend replacement for the legacy operator-driven workflow is documented in `docs/22-operational-record-jira-workflow.md`. It uses `IOperationalRecordClient`, `IRequesterResolver`, and `IJiraClient` boundaries. Defaults remain disabled, Fake is synthetic-only, and typed Turuncu Hat/Jira adapters require explicit validated runtime selection. No PowerShell is launched and no browser credential is collected. Real external TEST remains blocked by the sanitized fixture gate in `docs/integrations/turuncu-hat-jira-contract-gaps.md`; Jira create keeps durable SQL idempotency and unknown-outcome reconciliation.

## Reference

- `contracts/schemas/` — JSON schemas for all payloads
- `contracts/examples/` — sample payloads
- `docs/agent-guides/030-worker-service.md` — Worker-side patterns
