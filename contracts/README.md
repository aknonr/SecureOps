# contracts/

Cross-component data contracts. Authoritative source for the shape of payloads that cross system boundaries.

## Structure

- `schemas/` — JSON Schema (draft 2020-12) files. One file per contract.
- `examples/` — Sample payloads. Used in tests, documentation, and demos.

## Contracts

| Schema | Purpose | Phase |
|---|---|---|
| `alarm-payload.schema.json` | Inbound alarm payload from the monitoring workflow; carries the upstream external ID and optional Turuncuhat EVT ID | 1 |
| `diagnostic-result.schema.json` | Envelope for any diagnostic module output | 1 |
| `audit-event.schema.json` | Serialized audit entry shape | 1 |
| `identity-lookup.schema.json` | Phase 1A identity lookup request/response, safe metadata, health, and error responses | 1A |
| `notification-message.schema.json` | Generic notification payload before channel-specific rendering | 3 |
| `ai-analysis-request.schema.json` | Request to the internal AI service | 7 |
| `ai-analysis-response.schema.json` | Response from the internal AI service | 7 |

## Examples

| File | Matches schema |
|---|---|
| `alarm-payload-example.json` | `alarm-payload.schema.json` |
| `diagnostic-result-disk-example.json` | `diagnostic-result.schema.json` (payload: disk-diagnostic-v1) |
| `diagnostic-result-service-example.json` | `diagnostic-result.schema.json` (payload: service-diagnostic-v1) |
| `audit-event-examples.json` | `audit-event.schema.json` (array) |
| `identity-lookup-example.json` | `identity-lookup.schema.json` |
| `notification-message-example.json` | `notification-message.schema.json` |
| `ai-analysis-example.json` | `ai-analysis-request.schema.json` + `ai-analysis-response.schema.json` |

## Versioning

Contracts are versioned with a suffix when they evolve in a breaking way (e.g., `disk-diagnostic-v2`). Backward-compatible additions stay at the same version. Document changes in this README and in the change log of the affected schema file.

## Validation in Code

`SecureOps.Shared` mirrors these schemas as C# `record` types. Contract tests validate that every example file passes schema validation, and that the C# types serialize to schema-conformant JSON.

Validation library: `JsonSchema.Net` (pinned in `Directory.Packages.props`).

## Phase 1A IdentityLookup API Contract

Endpoints:
- `POST /api/v1/identity/lookup` accepts account input in the JSON body only.
- `GET /api/v1/identity/me` returns current caller metadata.
- `GET /api/v1/identity/lookup/capabilities` returns lookup limits and returned-field metadata.
- `GET /api/v1/health/audit-store` returns safe audit-store status.
- `GET /api/v1/health/identity-provider` returns safe identity-provider status.
- `GET /api/v1/health/enterprise-integrations` returns Admin-only provider selection and safe status without endpoint or credential detail.

No endpoint accepts an account value in a URL path or query string.

Error responses use:

```json
{ "errorCode": "AuditUnavailable", "message": "Identity lookup audit is unavailable.", "correlationId": "trace-id" }
```

Known lookup `errorCode` values are `InvalidRequestBody`, `PurposeRequired`, `InvalidIdentityLookupRequest`, `EmptyAccount`, `AccountTooLong`, `BulkLookupRejected`, `SearchPatternRejected`, `AccountPatternRejected`, `AuditUnavailable`, `DirectoryProviderTimeout`, `ProviderUnavailable`, `RateLimitExceeded`, and `IdentityLookupUnavailable`.

`GET /api/v1/health/audit-store` returns safe status only. If a queued persistent audit write fails in the background, the response may show `status: "Unhealthy"` and `lastErrorCode: "AuditSinkUnavailable"`; it must not expose file paths, connection strings, account names, or personal data.

Swagger and examples must use fake values only; do not include real PAM/AD account names, real people, real email addresses, or production EVT IDs.

## Alarm Identity Convention

Alarm contracts now preserve both workflow identifiers when available:

- `externalId` keeps the upstream monitoring-source identifier, such as the SolarWinds alarm ID.
- `turuncuhatEvtId` carries the optional Turuncuhat EVT identifier used by the operational workflow.

The EVT ID is expected to become the more stable organizational reference if the final Turuncuhat integration contract confirms it is always available to SecureOps.
