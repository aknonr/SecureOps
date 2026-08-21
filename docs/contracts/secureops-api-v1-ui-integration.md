# SecureOps API v1 UI Integration Contract

Claude-owned UI work must consume `docs/contracts/secureops-api-v1.openapi.json` and this companion contract. Do not invent routes, request fields, status codes, role checks, or workflow states. Every endpoint requires authentication unless explicitly noted; capability failures return 403 ProblemDetails.

ProblemDetails includes safe `code`, `stage`, `retryable`, `correlationId`, and `traceId` extensions. Access mutations distinguish `AccessValidationFailed` (400/validation), `AccessRequestAlreadyDecided` and `AccessUserInvalidState` (409/lifecycle), `AccessConcurrencyConflict` (409/concurrency/retryable), and `AccessSelfApprovalDenied` (403/authorization). Policy denials remain 403.

## Access

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/access/me` | Authenticated | none | status, roles, capabilities, nullable exact profile, latest request, pending request ID, user version, auth source, session policy | 401, `AuditStoreUnavailable` |
| `GET /api/v1/access/requests?status=Pending` | `Access.ApproveRequests` | optional `Pending`, `Approved`, or `Rejected` query | enriched `AccessRequestResponse[]` with decision actor/version | `AccessValidationFailed` |
| `GET /api/v1/access/users` | `Access.ManageUsers` | none | authoritative `AccessUserResponse[]` | 403, `AuditStoreUnavailable` |
| `GET /api/v1/access/users/{id}` | `Access.ManageUsers` | route GUID | `AccessUserResponse` with roles, capabilities, latest request, history, profile, version | `AccessRecordNotFound` |
| `POST /api/v1/access/requests/{id}/approve` | `Access.ApproveRequests` | `{ "reason": "...", "roles": ["Operator"], "expectedVersion": 1 }` | decided `AccessRequestResponse` | validation, not found, already decided, concurrency, self-approval codes |
| `POST /api/v1/access/requests/{id}/reject` | `Access.ApproveRequests` | `{ "reason": "...", "roles": null, "expectedVersion": 1 }` | decided `AccessRequestResponse` | same decision codes |
| `PUT /api/v1/access/users/{id}/roles` | `Access.AssignRoles` | `{ "roles": ["Lead"], "reason": "...", "expectedVersion": 2 }` | `CurrentAccessResponse` | validation, not found, user state, concurrency codes |
| `POST /api/v1/access/users/{id}/disable` | `Access.ManageUsers` | `{ "reason": "...", "expectedVersion": 2 }` | disabled `CurrentAccessResponse` | validation, not found, user state, concurrency codes |
| `POST /api/v1/access/logout` | Authenticated | none | `LogoutResponse`; provider-managed logout intent only | `AuditStoreUnavailable` |

Role codes are `Admin`, `Lead`, `Operator`, `JiraPublisher`, `Auditor`, and `ReadOnly`. Render behavior from returned capabilities, but treat server authorization as authoritative.

After rejection, `accessStatus` remains `Pending`, `pendingRequestId` is null, and `latestRequest.status` is `Rejected`. Ordinary access never creates a replacement. There is no reapplication route. Profile fields are nullable provider results; the UI must not derive display data. Role PUT is replace semantics: read the current user and submit its latest `version`, then refresh after any conflict.

## Identity

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/identity/me` | Authenticated | none | current caller metadata | 401 |
| `GET /api/v1/identity/lookup/capabilities` | `Identity.Lookup` | none | validation limits, returned fields, and effective active-provider `supportsUpnLookup` | `AccessPending`, `AccessDisabled`, `AccessDenied` |
| `POST /api/v1/identity/lookup` | `Identity.Lookup` | `{ "account": "sample.user", "purpose": "Approved operational purpose", "alertId": null, "turuncuhatEvtId": null }` | `IdentityLookupResponse` | `InvalidIdentityInput`, `IdentityNotFound`, `IdentityProviderTimeout`, `IdentityProviderUnavailable`, `AuditStoreUnavailable` |
| `POST /api/v1/identity/bulk-lookup` | `Identity.Lookup` | same purpose plus `accounts` array, maximum configured count | ordered `BulkIdentityLookupResponse` | `InvalidIdentityInput`, 429 |
| `GET /api/v1/identity/lookup/cache-diagnostics` | `SystemDiagnostics` | none | aggregate counters without account labels | 403 |
| `GET /api/v1/health/identity-provider` | Authenticated outside Development | none | provider name and real-provider flag | 401 |

## Operational Records

`GET /api/v1/operational-records` is a source refresh, not a passive database-only read. It imports/classifies the bounded fake/approved source response and is rate-limited.

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/operational-records` | `OperationalRecords.View` | none | `OperationalRecordResponse[]` | `OperationalSourceUnavailable`, `OperationalRecordQueryFailed`, `AuditStoreUnavailable`, 429 |
| `GET /api/v1/operational-records/{id}` | `OperationalRecords.View` | route GUID | `OperationalRecordResponse` including claim, freshness, retry, and version state | `OperationalRecordNotFound` |
| `POST /api/v1/operational-records/{id}/jira-preview` | `OperationalRecords.CreateJiraPreview` | no body | `JiraPreviewResponse`; never creates Jira or closes source | record state/requester/Jira validation codes, 429 |
| `POST /api/v1/operational-records/{id}/jira` | `OperationalRecords.CreateJira` | optional `Idempotency-Key` header, maximum configured length | `JiraTransferResponse` | `InvalidIdempotencyKey`, `OperationalRecordAlreadyClaimed`, `OperationalRecordChanged`, `OperationalRecordNoLongerOpen`, `WorkflowAlreadyInProgress`, `JiraCreateFailed` |
| `POST /api/v1/operational-records/{id}/retry` | `OperationalRecords.Retry` | optional `Idempotency-Key` header | resumed `JiraTransferResponse` | `WorkflowAlreadyCompleted`, conflict/freshness/Jira/source-close codes |

The UI must refresh authoritative record state after 409, 422, or command completion. It must not infer claim ownership, retry safety, Jira success, or source-close success from local state. Preserve and show the returned correlation ID for support diagnostics without exposing raw external responses.
