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
| `POST /api/v1/access/logout` | Authenticated | none | ends the SecureOps application session and clears its handle; corporate-provider logout remains host/browser managed | `SessionRevoked`, `SessionStoreUnavailable`, `AuditStoreUnavailable` |

Role codes are `Admin`, `Lead`, `Operator`, `JiraPublisher`, `Auditor`, and `ReadOnly`. Render behavior from returned capabilities, but treat server authorization as authoritative.

After rejection, `accessStatus` remains `Pending`, `pendingRequestId` is null, and `latestRequest.status` is `Rejected`. Ordinary access never creates a replacement. There is no reapplication route. Profile fields are nullable provider results; the UI must not derive display data. Role PUT is replace semantics: read the current user and submit its latest `version`, then refresh after any conflict.

## Application Sessions

The `__Host-SecureOps.ApplicationSession` cookie is a Secure, HttpOnly, SameSite=Lax, browser-session-only opaque handle. It is not corporate authentication or an authorization source. Do not persist, display, log, or replay it in UI state.

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/sessions/current` | Authenticated | none | safe current session timestamps, internal IDs, provider-neutral principal/provider metadata, `isCurrent=true`, authentication method, and access version | `SessionExpired`, `SessionRevoked`, `SessionStoreUnavailable` |
| `GET /api/v1/sessions/active?page=1&pageSize=50` | `Access.ManageUsers` | bounded page; maximum configured 100 | safe active-session metadata with nullable `principal`, `normalizedPrincipal`, `displayName`, `authenticationProvider`, and `isCurrent`; no IP, device, cookie, or directory data | validation, authorization, store/audit unavailable |
| `POST /api/v1/sessions/revoke` | `Access.ManageUsers` | `{ "sessionId": "...", "reason": "..." }` | exact terminal session ID/reason/time | `SessionValidationFailed`, `SessionNotFound`, store/audit unavailable |

Idle expiry, absolute expiry, explicit logout, administrative revocation, access disable, and access-version change are server authoritative. A later Negotiate request may authenticate again and create a new SecureOps session; the application cookie does not implement Remember Me or provider logout.

One browser authentication session must present one stable application-session handle to every API client. The server-rendered UI must forward a protected browser-session correlation value through one shared API session handler; separate typed-client cookie containers are not browser identity. Do not merge missing-cookie sessions by user, because that would collapse separate/private browsers and weaken revocation semantics. The current UI does not yet implement this forwarding, so end-to-end browser deduplication and API logout remain a UI-owned integration blocker.

## Identity

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/identity/me` | Authenticated | none | current caller metadata | 401 |
| `GET /api/v1/identity/lookup/capabilities` | `Identity.Lookup` | none | validation limits, returned fields, and effective active-provider `supportsUpnLookup` | `AccessPending`, `AccessDisabled`, `AccessDenied` |
| `POST /api/v1/identity/lookup` | `Identity.Lookup` | `{ "account": "sample.user", "purpose": null }`; purpose is optional; legacy `alertId`/`turuncuhatEvtId` are deprecated and optional | `IdentityLookupResponse` | `InvalidIdentityInput`, `IdentityNotFound`, `IdentityProviderTimeout`, `IdentityProviderUnavailable`, `AuditStoreUnavailable` |
| `POST /api/v1/identity/bulk-lookup` | `Identity.Lookup` | `accounts` array plus optional purpose, maximum configured count | ordered `BulkIdentityLookupResponse` | `InvalidIdentityInput`, 429 |
| `GET /api/v1/identity/lookup/cache-diagnostics` | `SystemDiagnostics` | none | aggregate counters without account labels | 403 |
| `GET /api/v1/health/identity-provider` | Authenticated outside Development | none | provider name and real-provider flag | 401 |

## Directory Explorer

Phase 1 routes remain unchanged. Directory requests are exact-only POST bodies with `account` or `group`, optional `purpose`, and optional `refresh`; membership-path requests also require `targetGroup`. Negative membership-path results are conclusive only when traversal metadata reports no limit or truncation.

| Method and route | Capability | Success |
|---|---|---|
| `POST /api/v1/directory/principals/groups` | `Identity.Groups.View` | paged direct groups only |
| `POST /api/v1/directory/groups/lookup` | `Identity.Groups.View` | exact group metadata |
| `POST /api/v1/directory/groups/members` | `Identity.Groups.Members.View` | paged direct members only |
| `POST /api/v1/directory/principals/memberships` | `Identity.Groups.View` | separate direct/transitive groups and traversal metadata |
| `POST /api/v1/directory/principals/membership-paths` | `Identity.Groups.View` | bounded proven paths to one exact group |
| `POST /api/v1/directory/principals/account-health` | `Identity.Groups.View` | nullable health evidence; `lastLogonTimestampUtc` is approximate |
| `POST /api/v1/directory/principals/service-evidence` | `Identity.Groups.View` | bounded SPNs, account-type evidence, and membership counts |
| `POST /api/v1/directory/principals/privileged-memberships` | `Identity.PrivilegedGroups.View` | Admin-only evidence for exact server-configured groups |
| `POST /api/v1/directory/groups/analysis` | `Identity.Groups.Members.View` | overview, explicit direct members, nested groups, bounded effective members/topology, direct/transitive parents, completeness metadata |
| `POST /api/v1/directory/groups/export` | `Identity.Groups.Export` | Admin-only bounded formula-safe CSV for explicit direct or effective members |

The API does not classify service/PAM accounts from names, infer administrator status from group text, expose LDAP filters/cookies, or use returned directory data as application authorization. Zero SPNs is successful empty evidence. Primary membership is separate from explicit direct membership, and group member lists state that primary-group-only relationships are not included. Common failures are `DirectoryInvalidInput`, `DirectoryPrincipalNotFound`, `DirectoryGroupNotFound`, `DirectoryQueryLimitExceeded`, `DirectoryTraversalPartial`, `DirectoryProviderTimeout`, `DirectoryProviderUnavailable`, and `AuditStoreUnavailable`.

Group and member DTOs now return nullable `lookupKey`. When present it is the server-returned exact `sAMAccountName` to use for group lookup, direct members, analysis, membership checks, export, and nested navigation. Display `name`, but send `lookupKey`; never send `distinguishedName`. Direct-member pages accept bounded sizes 25, 50, and 100 and use the existing opaque continuation token.

## Operational Records

`GET /api/v1/operational-records` is a source refresh, not a passive database-only read. It imports/classifies the bounded configured source response and is rate-limited. `Simulation` and legacy `Fake` are Development/Demo/Test-only, `Disabled` fails closed, and `TuruncuHat` is a typed real adapter whose external TEST activation remains contract-gated. `createdAt` is nullable because the reviewed legacy projection does not supply a source timestamp.

`GET /api/v1/health/enterprise-integrations` is Admin-only and exposes only provider selection and safe status; it never exposes URLs, credentials, sessions, identities, or remote payloads.

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/operational-records` | `OperationalRecords.View` | none | `OperationalRecordResponse[]` | `OperationalSourceUnavailable`, `OperationalRecordQueryFailed`, `AuditStoreUnavailable`, 429 |
| `GET /api/v1/operational-records/{id}` | `OperationalRecords.View` | route GUID | `OperationalRecordResponse` including claim, freshness, retry, and version state | `OperationalRecordNotFound` |
| `POST /api/v1/operational-records/{id}/jira-preview` | `OperationalRecords.CreateJiraPreview` | no body | `JiraPreviewResponse`; optional `assigneeUsername` is the effective exact mapped assignee, otherwise null/project default; never creates Jira or closes source | record state/requester/Jira validation codes, 429 |
| `POST /api/v1/operational-records/{id}/jira` | `OperationalRecords.CreateJira` | optional `Idempotency-Key` header, maximum configured length | `JiraTransferResponse` | `ExternalWritesDisabled`, `InvalidIdempotencyKey`, `OperationalRecordAlreadyClaimed`, `OperationalRecordChanged`, `OperationalRecordNoLongerOpen`, `WorkflowAlreadyInProgress`, `JiraCreateFailed` |
| `POST /api/v1/operational-records/{id}/retry` | `OperationalRecords.Retry` | optional `Idempotency-Key` header | resumed `JiraTransferResponse` | `ExternalWritesDisabled`, `WorkflowAlreadyCompleted`, conflict/freshness/Jira/source-close codes |

The UI must refresh authoritative record state after 409, 422, or command completion. It must not infer claim ownership, retry safety, Jira success, or source-close success from local state. Preserve and show the returned correlation ID for support diagnostics without exposing raw external responses.

`OperationalRecordResponse.claimed` means the stored claim expiry is later than server time. `claimedBy`, `claimedAt`, and `claimExpiresAt` are the authoritative lease metadata; an expired lease can retain historical owner/timestamps while `claimed=false`. `version` changes with persisted workflow mutations. `reconciliationRequired=true` means an unknown Jira-create outcome blocks automatic retry. `jiraExists=true` means a trusted Jira key is persisted. `retryEligible=true` means the current durable state is one the retry endpoint can safely resume; it is always false while reconciliation is required.

`presentationState` is a stable UI category independent of English descriptions: `NeedsAttention` -> "İnceleme Gerekiyor", `Actionable` -> "Jira'ya Aktarılabilir", `InProgress` -> "İşlemde", and `Completed` -> "Tamamlandı". The operator sequence is `Kaydı İncele` -> `Jira Taslağını Önizle` -> `Doğrula` -> `Jira Kaydı Oluştur` -> `Kaynak Kaydı Tamamla`.

When both providers are explicitly `Simulation`, record, preview, transfer, and enterprise-health responses return `simulationMode=true` plus the notice that no real Jira issue will be created. The fixed synthetic scenarios cover happy path/idempotent replay, stale source, safe Jira failure, unknown Jira outcome requiring reconciliation, source-completion failure, and close-only retry. `Simulation` is rejected at startup in Pilot and Production and cannot be paired with a real provider.

When the TEST real-data gate is active, Operational Record, preview, and enterprise-health responses return `readOnlyIntegrationMode=true` and `readOnlyNotice="GERÇEK VERİ — YAZMA KAPALI"`. Claude must render that notice prominently and hide/disable Jira create, source completion, and retry commands. The API remains authoritative: an accidental command call returns 409 `ExternalWritesDisabled`, stage `external-write-fence`, `retryable=false`.

The v1 wire contract intentionally serializes Operational Record enums as integers. `OperationalRecordClassification` is `ServerRequest=0`, `EnvironmentRequest=1`, `SoftwareInstallation=2`, `ConfigurationRequest=3`, `OperationalSupport=4`, `NotJiraEligible=5`, `NeedsManualReview=6`. `OperationalRecordWorkflowState` is `Imported=0`, `Classified=1`, `NeedsManualReview=2`, `Eligible=3`, `Previewed=4`, `CreateRequested=5`, `CreatingJira=6`, `JiraCreated=7`, `ClosingOperationalRecord=8`, `Completed=9`, `JiraCreateFailed=10`, `OperationalRecordCloseFailed=11`. These values must not be renumbered or reordered. A future string-enum representation requires an explicitly versioned API contract; it cannot be introduced silently in v1.

## Management Reporting

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/reporting/management/summary` | `Reporting.ManagementView` | `window=today|7d|30d|custom`; custom also requires UTC `from` and `to` | bounded team-level identity, workflow, adoption, session-governance, security, and elapsed-duration aggregates | `ReportingValidationFailed`, non-retryable `ReportingPersistenceNotConfigured`, retryable `ReportingUnavailable`, `AuditStoreUnavailable` |
| `GET /api/v1/reporting/management/operators` | `Reporting.ManagementView` | same window plus `page` and `pageSize` (maximum 100) | server-paginated persisted actor counts and activity bounds | same reporting errors |

Only Admin and Auditor receive this capability. Windows are UTC half-open intervals and custom ranges are capped at 92 days. Duration fields are elapsed system workflow time, not active labor or time saved. Operator data must not be rendered as rankings or performance comparisons.

Duration identity is the stable `key`: `importToPreview`, `claimToJiraCreation`, or `claimToCompletion`. UI logic and localization must not match `definition` text or rely on duration array order. The legacy definition remains fallback display text.

Use `limitations[*].code` for localization and behavior; `message` is optional fallback text. The legacy `dataLimitations` string array remains temporarily for compatibility and must not be parsed. Both routes return `coverage` with `requestedFromUtc`, `requestedToUtc`, nullable `coverageFromUtc`, and `coverageComplete`. A zero is a measured zero only when coverage is complete and no matching metric-specific limitation applies. G-16 adoption and Operational Record/Jira trend series remain unavailable and must not be inferred client-side.
