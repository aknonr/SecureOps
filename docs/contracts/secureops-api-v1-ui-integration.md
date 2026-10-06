# SecureOps API v1 UI Integration Contract

## In Use Manual Verification, 2026-09-22

New execution intents use Manual verification mode. Acknowledged BPM stops at
Unconfirmed, not Completed; timeout/lost response stays Unknown with no replay.
Success=false is Rejected. The authorized execution DTO includes frozen SourceCode.
Only Completed plus Closure/Verified evidence permits a system-verified closure
label. ManualVerification/ManuallyConfirmed is a separate attributed human event.

POST `in-use/{id}/execution/manual-verification` accepts OperationId,
ExpectedRevision and exact SourceCode. View, Review and Complete are required;
current SQL authorization, stopped-state eligibility and revision are checked in
the audit transaction. It accepts no actor/time/authoritative status from the
browser. ConfirmedBy, ConfirmedByLabel and event At come from trusted server
context. Repeated/stale/wrong-OR requests conflict; original initiator/evidence
and the active duplicate fence survive. No remote call or retry occurs.

This is not rc6.26 or sealed E-06 behavior. Real transport remains gated on IU-05
attachment/conditional-update/eligible-task contracts, not automatic final OR
readback alone. No source record navigation URL is established. See ADR-0020 and
the single integrated-test-activation register for E-07 payload and acceptance.

## Post-rc6.26 Catalogue And Reporter Suggestion

This source is not in installed rc6.26. `GET in-use/reports` requires current
InUse.View and InUse.Review, before SQL counts/pages. Search is literal metadata
substring over archived OR/host/original preparer label/account; From inclusive,
To exclusive preparation instants, Version, Status Current/Superseded/Discarded,
Page 1..10000 and PageSize 1..100. Count/page share one AsOf transaction, not a
retained cross-page management snapshot. Coverage explicitly excludes unindexed
history. Missing metadata stays null; no current-person or current-OR substitution.
AttachmentStatus is separately Verified/Unknown/NotVerified, never inferred from
archive existence or local lifecycle. Download the selected version using the
existing report endpoint and returned CurrentRecordVersion; a conflict requires
refresh, never regenerating historical bytes. `POST in-use/{id}/reports/index`
takes expectedVersion and 1..25 unique positive archived versions. Completed
indexes survive an interrupted batch; repeating exact metadata is idempotent,
conflicting immutable metadata fails. SQL 024 is required. Missing persistence is
503, not an empty successful result.

`GET in-use/{id}/reporter-suggestion` requires InUse.View and InUse.Assign and is
audited; it never saves assignment. States: Matched, ReporterUnverified,
MappingNotConfigured, MappingUnverified, NoMatch, Ambiguous, Ineligible. Only
Matched returns an eligible candidate and source/version/mapping-bound
fingerprint. Explicit assignment may add reporterDecision Accept/Reject/Override
and reporterFingerprint. The API resolves current eligibility again; stale
fingerprints fail with 409. Existing manual/unassigned saves remain supported.
An API-owned reviewed identity crosswalk is empty by default; display-name
similarity cannot create a mapping. The saved decision freezes relation/scope,
source versions, mapping revision/review/expiry and trusted acting identity;
refresh preserves it. This does not establish a corporate mapping or source closer.

## OCO Sender And Role Assignment, 2026-09-15

PUT access/users/{id}/roles takes roles and expectedVersion, no Reason. Unknown
legacy reason JSON is ignored. Approval/rejection/disable reasons are unchanged.
GET announcements adds nullable currentSender from valid persisted actor Mail.
Draft saves no longer require Announcements.Sender. MIME/new preparations reject
missing/invalid actor Mail (400 AnnouncementSenderUnavailable) or changed Mail
(409 AnnouncementSenderChanged); explicit resave is required. HTML/draft work and
immutable preparation history remain available. No send endpoint is introduced.

## Current Reporter Contract, 2026-09-12

The preparation gate below was satisfied by operator A/B evidence and connected
in the 2026-09-11 increment. Existing explicit refresh persists each server's
RFC/Bildiren; page reads remain database-only. This task changes presentation
and new workbook evidence, not API/schema/identity contracts. Exact resolution
is labelled `RFC eşleşti`; references/provenance use collapsed source evidence.
See the current canonical handoff in `docs/24-api-test-deployment-readiness.md`.

## Related Request Reporter Preparation, 2026-09-11

Each stored server may now carry nullable `relatedRequestReporter`, omitted for
legacy snapshots. It binds parentId/serviceItemId/rfcReference/referenceKind,
requestId/requestCode, raw display/userReference, state/displayState/referenceState
and lastVerifiedAt. The existing JSON aggregate persists this additive member;
no migration is needed. Normal production refresh does not populate it until
operator A/B evidence validates the candidate mapping. No new HTTP lookup exists.

Use the exact visible label **İlgili talebi bildiren**, per server, when the
verified mapping is wired. p_rel_requester is observed as Bildiren, not an
independently verified Istem Sahibi. Existing Requester names remain compatibility
members. Do not populate the separate Istem Sahibi or local reviewer assignment.
TuruncuHatQueryParser reads KEY.p_rel_requester into OperationalRecordSourceItem.Requester;
DiscoverAsync copies that parent display into InUseSource.Requester. The workbook's
legacy Requester row and UI parent labels consume it. None of those usages verifies
the separate Istem Sahibi field or the RFC target; Jira's authenticated reporter
policy is also independent. Historical snapshots/export archives remain unchanged.
KEY.p_rel_requester is raw display; SET.p_rel_requester is a separate user reference.
The shared DisplayText helper decodes once and returns state instead of a current
person when stale/failed; render as escaped plain text. Legacy rows remain unknown.

State values: ExactMatch, MissingRfc, NotFoundOrNotVisible, AmbiguousMatch,
IdentityMismatch, Forbidden, Failed, Stale, NotQueried. Display/reference states
are independently Returned/Empty/Null/Omitted. EffectiveState returns Stale for
missing/future/older-than-24-hour verification. Failed same-reference refresh
retains historical evidence with the failure state; unqueried/absent relationships
retain it as Stale. Verification-time-only changes preserve source fingerprints;
content/state changes invalidate drafts, without changing manual assignments.
UI list/detail changes remain with the UI owner after operator mapping evidence.

## Bounded SDM Pilot, rc6.15

Classification adds ServerRetirement=7 without renumbering v1 values; review-only
tracking, never decommissioning. Record responses add `sourceFingerprint`, the
current stored source digest for exact-version policy review, not an approval token.
Existing preview/create capabilities, confirmation fingerprints and command routes
remain. Empty `OperationalRecords:Pilot` fails closed. ADR-0018 defines the proposed
server-owned one-record policy; only ServerRequest has an evaluable positive path.
Schema 013 is required for its persisted evidence. Existing v1 remains negative.
Requester and authenticated Jira reporter are separately resolved; source Reporter
is not inferred. UI reason suggestions do not authorize publication. No new source
query keys or remote reconciliation/search contract is introduced.

## In Use Relationship Evidence, 2026-09-10

Workflow follow-up: assignment is optional for approved View+Review actors;
versions and actual actor provenance remain mandatory. A fixed-scope SQL
application lock prevents overlapping discoveries from distinct command IDs/API
instances, without blocking stored reads. No API route/DTO shape changed.
The optional RFC-hop contract exists only in the standalone diagnostic overload,
not the HTTP diagnostic or normal refresh. See its README and current canonical
UI handoff; no persisted RFC owner or source completion is implemented yet.

Additive source fields: serviceItemsState/affectedAssetsState (Complete,
NotQueried, Forbidden, Failed, Ambiguous), nullable affectedAssetCount and creator
evidence. Missing old JSON fields default to NotQueried/null. Servers are service
items, not affected assets. Only Complete plus an empty set establishes zero;
failed enrichment retains previous evidence, with explicit state and stale
review version. Service items also support Observed (exact semantic mapping,
global completeness unverified) and Partial (previous rows omitted by the latest
bounded result are retained; not current report-ready). An observed empty set
does not establish authoritative zero. Explicit refresh reads at most 100 roots
and 10 service items per root within 45 seconds; lists/detail never query remotely.
The exact supported KEY display/SET reference mappings are in the legacy parity
record. Fields include missing-key provenance; no positional fallback, name
resolution or auto-assignment. Dictionary labels Virtual PC User/RFC Kaydı are presentation
evidence only, not invented corporate property names or assignment policies.
Operational Records list reuses Requester; no new search/sort or remote lookup.

POST `/api/v1/in-use/{id}/relationship-evidence` accepts expectedVersion/sourceId.
It requires View, Refresh and OperationalRecords.ViewDiagnostics, Test environment
and unchanged external-write fences. Exact source ID must match the persisted
record/version. Returns JSON Root/ServiceItems keyed cell type/alias arrays,
Completeness=Unverified, AffectedAssets=NotQueried; never raw source values.
Failure returns normal 400/403/404/409/503 boundaries; no partial diagnostic output.
No discovery enrichment or local review mutation occurs, only append-only audit.
See the canonical TEST runbook for invocation, bounds, masking and stop conditions.

Claude-owned UI work must consume `docs/contracts/secureops-api-v1.openapi.json` and this companion contract. Do not invent routes, request fields, status codes, role checks, or workflow states. Every endpoint requires authentication unless explicitly noted; capability failures return 403 ProblemDetails.

ProblemDetails includes safe `code`, `stage`, `retryable`, `correlationId`, and `traceId` extensions. Access mutations distinguish `AccessValidationFailed` (400/validation), `AccessRequestAlreadyDecided` and `AccessUserInvalidState` (409/lifecycle), `AccessConcurrencyConflict` (409/concurrency/retryable), and `AccessSelfApprovalDenied` (403/authorization). Policy denials remain 403.

## Persisted Operational Browsing

### Independent In Use Workspace

All `/api/v1/in-use` routes require authentication and InUse.View. They do not
reuse OR-to-Jira eligibility, claims or source-close commands. Additional gates:

| Method/path suffix | Additional capability | Contract |
|---|---|---|
| GET root | none | persisted search (max 100), view=all/mine/unassigned/review/tracking/pending/verification, status=Unreviewed/Draft/Stale/Discarded, sort=code/oldest/newest (default code; date modes retained, deferred in UI), page=1..100000, pageSize=1..100 default 25; matching total plus refresh state |
| GET /overview | Reporting.ManagementView | audited stored OR counts, refresh health, up to five oldest confirmed-open parent records with verified creation evidence; no source call |
| POST /{id}/completion-intent | InUse.Review | expectedVersion, commandId, reportSha256; current ready draft and exact private archive required. Saves actor-bound Blocked intent only; no external execution |
| GET /{id} | none | distinct source ownership evidence, per-server evidence, local assignee, source and aggregate versions, nullable draft |
| GET /assignees | InUse.Assign | at most 200 approved InUse.View+Review application IDs/principals; no directory search or source-name matching |
| POST /refresh | InUse.Refresh | nonempty commandId GUID; existing command tracking and refresh rate limit; at most 100 accepted records, 45s orchestration deadline and existing response-byte bounds |
| PUT /{id}/assignment | InUse.Assign | expectedVersion, nullable assigneeId, nonblank reason max 500; approved exact identity or explicit unassignment |
| PUT /{id}/draft | InUse.Review; assignment optional | expectedVersion, sourceVersion, answers (serverId/check/value/evidence), notes max 2000; max 900 answers, unique server/check; evidence optional max 500. Blank evidence/notes and omitted historical answers preserve stored history |
| POST /{id}/report | InUse.Review | expectedVersion; default previews a current saved draft. archive=true requires Complete or Observed nonempty service items and explicit Yes/No for all three operator questions. Observed reports retain the unverified-completeness warning; Partial/failed relationships are blocked. archivedVersion downloads immutable history, checked against expected current record version |

Creation/lifecycle evidence and FirstSeenAt/Completion are additive nullable JSON.
Existing source JSON omits absent creation/lifecycle fields to preserve source hashes.
No corporate creation/lifecycle wire mapping is asserted. AwaitingAnswers and
ReportReady partition all stored ORs by local readiness; Open counts only explicit
Open evidence. Null status is Unknown, never confirmed open. The result journal
has no HTTP endpoint; only a future verified executor can supply remote outcomes.
Unknown outcomes require reconciliation; no retry path leaves Blocked/failed/unknown.

Normal editable codes are InternetOut/InternetIn/Microsegmented. Unknown may be
saved, but readiness returns 400 InUseIncomplete with server/field-specific text.
Reports include PreparedBy (authenticated application user ID), PreparedAt,
SourceId, Size and Archived. GET detail includes ArchivedVersions. Archive download
requires View+Review even for old versions; it never represents current evidence.
There is no public/static file route. Storage is an atomic private JSON envelope
containing XLSX bytes and metadata, configured by InUseReports:Directory. No SQL
migration/grant change. Failures return no successful artifact; repeated versions
reuse the original bytes, actor and timestamp. Preview alone is not an archive.

Values are Unknown/Yes/No/NotApplicable. Check codes are InternetOut, InternetIn,
Microsegmented, NmsRequested, MemoryAlarm, CpuAlarm, UpDownAlarm, DiskAlarm,
Verified. Omitted answers remain unknown. Stale source versions reject save/export;
assignment and refresh also advance aggregate versions. A conflict returns 409
InUseConflict and is never automatically retried. Invalid input/assignee returns
400; missing record 404; unauthorized or nonassigned review 403; persistence/audit
failure 503. Download must revalidate the preview's version. Report preparation
does not mark technical checks successful, approve business policy or upload.

Failed refresh records actionable safe state while preserving all stored records
and the last successful timestamp. Partial/absent records are retained, never
interpreted as source closure. Real root-query success remains explicitly
SourceCompletenessUnverified; unqueried Virtual PC User/RFC and ownership contracts remain missing.
Local Fake/Simulation fixtures are labelled synthetic and only selected through
the existing validated synthetic profile. Corporate profile/fences are unchanged.

Admin receives the four local capabilities; new InUseReviewer and InUseCoordinator
roles enable narrow explicit approval. No existing SDM/Resources role is broadened.
SQL deployment requires 012 and the exact grants in `sql/README.md`; no Worker.

### Existing Operational Records

`GET /api/v1/operational-records/stored` requires `OperationalRecords.View`.
It reads persisted data only, with `search` (literal code/title substring, max
100 characters), optional workflow `state`, `sort=updated|oldest|code`, one-based
`page` (max 100000), and `pageSize` (1-100, default 25). Invalid queries return
400 ProblemDetails; denied access returns 403. Response: `items`, `total`,
`page`, `pageSize`; total is matching persisted rows, never a source total.
Sort ties use source identifier. Corporate mode excludes synthetic records
before counting/paging. SQL count and rows share a bounded serializable read.
Existing GET root remains the explicit bounded source refresh; no polling,
typing, sorting, page navigation or passive reload may call it.
No migration or runtime grant is added; baseline remains 001-011.

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

Role codes are `Admin`, `Lead`, `Operator`, `JiraPublisher`, `Auditor`, `ReadOnly`, and `ResourceCurator`. Render behavior from returned capabilities, but treat server authorization as authoritative. ResourceCurator grants only Resources.View and Resources.Manage; its UI label is a Claude-owned follow-up.

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
| `POST /api/v1/identity/lookup` | `Identity.Lookup` | `{ "account": "sample.user", "purpose": null }`; purpose is optional; legacy `alertId`/`turuncuhatEvtId` are deprecated and optional | `IdentityLookupResponse`; `user.accountTypeEvidence`: `User`, `ManagedServiceAccount`, `GroupManagedServiceAccount` | `InvalidIdentityInput`, `IdentityNotFound`, `IdentityProviderTimeout`, `IdentityProviderUnavailable`, `AuditStoreUnavailable` |
| `POST /api/v1/identity/bulk-lookup` | `Identity.Lookup` | `accounts` array plus optional purpose, maximum configured count | ordered `BulkIdentityLookupResponse` | `InvalidIdentityInput`, 429 |
| `GET /api/v1/identity/lookup/cache-diagnostics` | `SystemDiagnostics` | none | aggregate counters without account labels | 403 |
| `GET /api/v1/health/identity-provider` | Authenticated outside Development | none | provider name and real-provider flag | 401 |
| `GET /api/v1/health/persistence` | Authenticated outside Development | none | `NotConfigured`, `Healthy`, or `Unhealthy` SQL readiness without connection details | 401, 503 |

## Directory Explorer

Identity and Directory Explorer principal inputs accept one trailing `$` on a nonempty
sAMAccountName (for example `syn.gmsa$`). Dollars elsewhere, managed-account UPN forms,
wildcards and LDAP grammar are rejected. Managed accounts are resolved only by exact
sAMAccountName within the two managed object classes; ordinary computer accounts do not
match. `accountTypeEvidence` reports class evidence, never a name-based classification.
No password or secret value is read or returned. Runtime overrides using the old
`IdentityLookup:AllowedAccountPattern` must be updated separately; see G-26.

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

Additive SDM response fields (ADR-0018): `recommendedClassification` is a nullable
frozen numeric classification; `ruleSetVersion` and `evaluatedAt` are nullable
string/UTC timestamp. Null means unevaluated. `reasonCodes` and
`blockingConditions` are non-null ordinal-sorted unique string arrays (empty
when unevaluated). `sdmCandidateRecommended` and `externalWriteEligible` are
non-null booleans, always false in v1. `evaluationStale` defaults true when
unevaluated; `sourceChanged` defaults false and latches true on observed change.
`CategorySupported` denotes a recognized enum, not an approved SDM policy;
`CategoryPolicyPending` remains a blocker. No approval fields or endpoint exist.
Render recommendation separately from durable workflow state, preserve reason
order, and localize stable codes without parsing source text. Action Center may
display this evidence but cannot infer approval or enable corporate publication.

SDM additive close contract: preview, transfer and record expose
`sourceCloseRequested`; records also expose effective `sourceCloseEnabled`.
Confirmed `JiraCreated` with false intent has presentation category `SourceOpen`:
"Jira oluşturuldu. Turuncu Hat kaydı açık bırakıldı." Never present Completed,
failed close, or pending close. `retryEligible` includes persisted intent and the
current close gate. These fields grant no capability. Old preview fingerprints
require review after the close-intent fingerprint update.

The review-only request accepts existing enum values `ServerRequest` (0) and
`SoftwareInstallation` (2), with `expectedVersion`. The additive preview fields
are `requestType`, `reviewOnly`, `blockingConditions` and `recordVersion`.
Operator declaration is not source classification: review does not persist a
classification, set eligibility, or issue a create-authorizing preview token.
Selection changes discard the draft/confirmation; recoverable errors retain the
selection. Application-installation label mapping is unresolved and never falls
back to SunucuTalep. Existing blockers and unresolved identity remain explicit.

`GET /api/v1/operational-records` is a source refresh, not a passive database-only read. It imports/classifies the bounded configured source response and is rate-limited. `Simulation` and legacy `Fake` are Development/Demo/Test-only, `Disabled` fails closed, and `TuruncuHat` is a typed real adapter whose external TEST activation remains contract-gated. `createdAt` is nullable because the reviewed legacy projection does not supply a source timestamp.

`GET /api/v1/health/enterprise-integrations` is Admin-only and exposes only provider selection and safe status; it never exposes URLs, credentials, sessions, identities, or remote payloads.

| Method and route | Capability | Request | Success | Important errors |
|---|---|---|---|---|
| `GET /api/v1/operational-records` | `OperationalRecords.View` | none | `OperationalRecordResponse[]` | `OperationalSourceUnavailable`, `OperationalRecordQueryFailed`, `AuditStoreUnavailable`, 429 |
| `GET /api/v1/operational-records/{id}` | `OperationalRecords.View` | route GUID | `OperationalRecordResponse` including claim, freshness, retry, and version state | `OperationalRecordNotFound` |
| `POST /api/v1/operational-records/{id}/jira-preview` | `OperationalRecords.CreateJiraPreview` | no body | `JiraPreviewResponse`; optional `assigneeUsername` is the effective exact mapped assignee; optional `reporterUsername` is the exact Jira user resolved from the server-authenticated operator; never creates Jira or closes source | record state/requester/operator-reporter/Jira validation codes, 429 |
| `POST /api/v1/operational-records/{id}/jira-review` | `OperationalRecords.CreateJiraPreview` | `{ "requestType": 0, "expectedVersion": 1 }` | review-only `JiraPreviewResponse`, audited; no external dispatch or identity lookup | 409 stale/already-previewed/claimed/known-key/reconciliation state, 422 unsupported type, 403 capability, 429 |
| `POST /api/v1/operational-records/{id}/jira` | `OperationalRecords.CreateJira` | optional `Idempotency-Key` header, maximum configured length; no reporter input | `JiraTransferResponse` | `ExternalWritesDisabled`, `InvalidIdempotencyKey`, `OperatorReporterResolutionFailed`, `JiraReporterRejected`, `OperationalRecordAlreadyClaimed`, `OperationalRecordChanged`, `OperationalRecordNoLongerOpen`, `WorkflowAlreadyInProgress`, `JiraCreateFailed` |
| `POST /api/v1/operational-records/{id}/retry` | `OperationalRecords.Retry` | optional `Idempotency-Key` header | resumed `JiraTransferResponse` | `ExternalWritesDisabled`, `WorkflowAlreadyCompleted`, conflict/freshness/Jira/source-close codes |

The UI must refresh authoritative record state after 409, 422, or command completion. It must not infer claim ownership, retry safety, Jira success, or source-close success from local state. Preserve and show the returned correlation ID for support diagnostics without exposing raw external responses.

`stage=jira-reconciliation` takes precedence over every error code and retry hint.
An unacknowledged create/retry (transport loss, timeout, unreadable success or
unclassified server failure) is also presented as uncertain until a fresh state
read resolves it. Never resubmit automatically. Preview fingerprints bind exact
source content; a changed source invalidates the locally held preview. Persisted
older fingerprints fail closed and are never reset by the UI. The SDM detail
renders `reasonCodes`/`blockingConditions` as Turkish guidance, separately from
candidate recommendation, Jira eligibility and external-write eligibility.
No approval action or new API route is introduced.

`OperatorReporterResolutionFailed` is a non-create outcome: show "İşlemi yapan kullanıcı Jira üzerinde doğrulanamadığı için kayıt oluşturulmadı." `JiraReporterRejected` means Jira rejected the verified reporter field or the integration account lacks reporter-change permission; do not suggest falling back to the integration identity.

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
## Resource Catalogue and Shift Start Sets: Claude Handoff

Backend v1 is implemented under ADR-0019. The UI and current verification handoff
are in `src/SecureOps.Ui/README.md`.
This is local catalogue/preference storage, not target-system integration.

### Permissions and Storage

Every operation requires authentication, Approved access, and `Resources.View`.
All reviewed existing application roles receive Resources.View. Only Admin and
the separately assigned ResourceCurator role receive `Resources.Manage`; Lead
does not. An existing Admin uses `PUT /api/v1/access/users/{id}/roles` with the
latest access version and the complete replacement role list to add/remove
ResourceCurator. No role is assigned to any real user by the migration or task.

Ownership uses the internal UserId returned by the existing access service,
never submitted owner IDs, display names, department, employee number or claims
alone. Admin cannot inspect or edit another user's preferences through these
routes. Management permission does not grant access to vCenter or other targets.

Resource persistence follows `Access:RepositoryProvider`: SqlServer uses the
same `ConnectionStrings:SecureOpsDb`; InMemory is a local/test substitute with
no restart durability. Apply migrations 001-010 and reviewed object grants before
deploying SQL-backed binaries. No catalogue data is seeded or deployment-bound.

### Endpoints

All success responses below use 200. IDs are GUIDs generated by the server.

| Method and route | Extra permission | Body/query | Response |
|---|---|---|---|
| GET /api/v1/resources/categories | Manage for includeArchived=true | includeArchived=false | ResourceCategory[] |
| POST /api/v1/resources/categories | Manage | SaveResourceCategoryRequest | ResourceCategory |
| PUT /api/v1/resources/categories/{id} | Manage | complete category replacement | ResourceCategory |
| GET /api/v1/resources/links | Manage for includeArchived=true | ResourceQuery | ResourcePage |
| GET /api/v1/resources/environments | Manage for includeArchived=true | search (0-40), categoryId (optional), includeArchived=false | ResourceEnvironmentOptions |
| GET /api/v1/resources/links/{id} | Manage for includeArchived=true | includeArchived=false | ResourceLink |
| POST /api/v1/resources/links | Manage | SaveResourceLinkRequest | ResourceLink |
| PUT /api/v1/resources/links/{id} | Manage | complete link replacement | ResourceLink |
| GET /api/v1/resources/me | none | none | ResourcePreferencesResponse |
| PUT /api/v1/resources/me/guide | none | { expectedVersion } | ResourcePreferencesResponse |
| PUT /api/v1/resources/me/favourites/{id} | none | { favourite, expectedVersion } | ResourcePreferencesResponse |
| POST /api/v1/resources/me/sets | none | SaveShiftSetRequest | ResourcePreferencesResponse |
| PUT /api/v1/resources/me/sets/{id} | none | ordered additions and explicit removals | ResourcePreferencesResponse |
| DELETE /api/v1/resources/me/sets/{id} | none | expectedVersion query | ResourcePreferencesResponse |
| GET /api/v1/resources/me/sets/{id}/resolve | none | none | ShiftSetResponse |

### Fields and Limits

- Category: id, name (required, 1-80), displayOrder (0-100000, default 0),
  managersOnly (false), archived (false), version, updatedAt. Maximum 200 stored
  categories, including archived ones; no hard category deletion.
- Link: id, categoryId (required existing non-archived category), name (1-120),
  url (1-2048), purpose (1-300), notes (nullable, maximum 1000), environment and
  location (nullable, maximum 40 each), tags (maximum 10 nonempty unique tags,
  30 characters each), displayOrder (0-100000), active (true), archived (false),
  version and updatedAt. Text is trimmed plain text; controls, bidi formatting
  characters and markup delimiters are rejected. Null tags become [] and tags
  are trimmed, invariant lower-case, and ordinally sorted. Empty optional strings
  are allowed; they have no implied identity or authorization meaning.
- Category/link create requires expectedVersion=0. PUT requires the exact positive
  current version; writes increment it. Version and updatedAt are server-owned.
  Times are UTC. Archive by PUT with archived=true and current version. Reordering
  is the same optimistic update, not a separate unrestricted operation.
- ResourcePage: items (always an array), page, pageSize and total. Page defaults to
  1 (1-10000); pageSize defaults to 50 (1-100). Search maximum 100; exact categoryId,
  environment, location and tag filters combine with AND. Search is literal
  substring over name, purpose and individual tags, never a regex or SQL pattern.
  SQL comparison is case-insensitive/accent-sensitive under Latin1_General_100_CI_AS_SC.
  Empty filters are ignored. Sort is category displayOrder, link displayOrder,
  then ordinal GUID text. Only permitted matching rows contribute to total.
- ResourcePreferencesResponse: version (0 before first edit), favourites ([]),
  sets ([]), defaultSetId (nullable), guideDismissed (false). Favourites are ordered by ordinal GUID.
  Maximum 200 favourite references and 20 sets. No owner/profile fields.
- SaveShiftSetRequest: name (required 1-80; unique per owner ignoring ordinal case),
  linkIds (required array, 0-100 unique nonempty GUIDs), isDefault (false),
  expectedVersion (personal aggregate version, including on POST), removeLinkIds
  (optional, 0-100 unique nonempty GUIDs). The two ID arrays must be disjoint.
  POST accepts no removals. PUT merges rather than replaces: every omitted saved
  reference survives, including for legacy clients without removeLinkIds.
  New callers must explicitly name currently visible, owned members to remove.
  After removals, requested IDs fill the existing requested members' slots in
  request order; omitted members retain their positions, and surplus requested
  members append. Example: saved [A, hidden, B], request [B, A, C] yields
  [B, hidden, A, C] internally. The merged state is limited to 100 references.
  The response still contains no hidden IDs or excluded-reference counts.
- ResourceEnvironmentOptions: values (at most 100 distinct nonempty environments,
  case-insensitive alphabetical order), hasMore. Values come only from permitted
  current links across all pages, optionally within categoryId. Search is bounded
  literal substring; refine it when hasMore=true. Archive inclusion is manager-only.
  This endpoint returns no link details and is independent of active link search.
- PUT /me/guide marks only the caller's guide invitation dismissed; it does not
  record lessons, steps, completion dates or browsing. Replay stays available.
  The optional boolean is stored in existing preference JSON, defaulting false
  for old rows; no migration or browser storage is required.
- ShiftSetResponse: id, name, links (current ResourceLink[], always array),
  isDefault. No raw hidden/missing IDs or excluded-reference counts are returned.
  Creating/updating with isDefault=true replaces the previous default atomically.
  Setting false on the current default or deleting it clears defaultSetId.
  Names and saved references live only in the owner's aggregate.
- All personal mutations compare the single personal aggregate version, increment
  it and return refreshed state; a successful repeated intent can increment it
  again. A rejected stale request changes nothing. Refresh GET /resources/me before
  resubmitting after a conflict; never automatically overwrite another tab's edit.

### Visibility and Archive Semantics

Category managersOnly is inherited by every child link. Ordinary readers cannot
discover restricted entries through ID, search, favourites or set resolution.
Active=false, archived links, and archived categories are excluded from normal
reads and all personal projections, even for managers. The manager-only
includeArchived read exposes administrative edit metadata, not an opening list.

Saved references are retained internally when a link becomes unavailable; no
copied URL/name is exposed through personal responses. Resolving uses the latest
catalogue state. Restoring availability can make the saved reference visible
again. Adding an unavailable reference is rejected without saying whether it
was missing, archived or restricted. Editing links inside an archived category
requires restoring that category first. An empty saved set remains a valid named
set and returns links=[].

Omission is never explicit deletion, even if an omitted reference becomes visible
between projection and save. Rename/default/add/reorder therefore preserve hidden
membership. Explicit removal of an unavailable reference is rejected with the same
404 as any unavailable ID. Delete the whole owned group to discard all its saved
references. There is no legacy full-replacement API that silently discards them.

### Errors and UI States

401 requires authentication. Pending, Disabled and insufficient capability produce
403 through the existing access conventions. Missing/inaccessible entries and
cross-user sets both use 404 ResourceNotFound. Service validation uses 400
ResourceValidationFailed; capacity uses 400 ResourceLimitExceeded. Model-binding
or DataAnnotations rejection can use the existing framework ValidationProblemDetails
shape with errors. Stale versions use 409 ResourceConcurrencyConflict,
stage=concurrency, retryable=true. Persistence/audit outage is 503 and must never
be displayed as a successful save. Other safe access/audit codes remain possible.

Keep existing data while loading, prevent duplicate submission, show an explicit
empty state for arrays with no items, and refresh state after success. Do not
fabricate completed saves after network failure; reconcile with GET first.
Unknown-outcome POST creation is not guaranteed idempotent; inspect existing
entries before retrying. No request body, URL, query values, or personal set content
belongs in client/server logs or analytics.

### Opening Links Is UI-Owned

Use GET /resources/me/sets/{id}/resolve immediately before a user-initiated opening.
Only open returned links in returned order. Browser tab policies can block multiple
opens; offer individual-link fallback. Use noopener/noreferrer and do not forward
WASAS cookies, OIDC tokens, Authorization headers, passwords or referrer content.
Do not claim the destination loaded or authenticated merely because opening was
attempted. Permissions can change after resolution; browser opening is not an
authorization grant on the destination. No server open, fetch, scan, favicon,
screenshot, script execution, health-check or SSO endpoint exists.

### HTTPS Policy and Synthetic Examples

Only absolute well-formed HTTPS URLs are accepted. Scheme/host are canonicalized
using System.Uri before storage. Embedded credentials, fragments, backslashes,
control characters, malformed/double-encoded paths, semicolon/equal-sign/@ path
credentials and explicit secret path segments are rejected. Arbitrary query keys
are forbidden. V1 permits only orgId, dashboard, view, tab, from, to, refresh,
theme, environment, location and page (case-insensitive, no duplicate keys or
encoded key names). At most 10 key/value pairs; values are 1-80 ASCII letters,
digits, spaces or -_.:, after one percent decode. Nested URLs, secondary encoded
values and credential parameter names are rejected. Authors must never place
credentials in paths or innocuous filter values; syntax checks cannot determine
the business meaning of arbitrary opaque text. No insecure legacy exception is
enabled; unsupported destination URL shapes need a separately reviewed policy.

Synthetic link creation, after creating a synthetic category:

```json
{"categoryId":"10000000-0000-0000-0000-000000000001","name":"Synthetic dashboard","url":"https://example.invalid/dashboard?orgId=1&view=summary","purpose":"Synthetic fixture only","notes":null,"environment":"Test","location":"Lab","tags":["sample"],"displayOrder":0,"active":true,"archived":false,"expectedVersion":0}
```

Synthetic personal set creation (use actual returned synthetic link IDs in tests):

```json
{"name":"Synthetic start","linkIds":["20000000-0000-0000-0000-000000000001"],"isDefault":true,"expectedVersion":0}
```

Empty response before the first personal mutation:

```json
{"version":0,"favourites":[],"sets":[],"defaultSetId":null}
```

No real URLs, accounts, employee data or production-looking demo catalogue entries
are included. Next Claude task: catalogue/list/detail and manager forms, personal
favourites and set editor/default selection, version-conflict handling, and
user-initiated opening with fallback. SDM Action Center integration and positive
business policy/approval remain separate pending milestones.
# Workspace Additions, 2026-09-07

`PUT /api/v1/resources/me/layout` accepts `{ layout, expectedVersion }` and returns
the current owner's ResourcePreferencesResponse. `workspaceLayout` is additive:
view `cards|list`, density `comfortable|compact`, pageSize `10|25|50|100`, shortcuts
an ordered unique subset of `links|groups|requests|catalogue` permitted by current
capabilities. Reset sends defaults with the current version. Existing preferences
survive; 409 never implies retry permission. No arbitrary owner ID is accepted.
Legacy clients can omit/ignore the field; reads default without persisting.

`POST /api/v1/resources/links/resolve` accepts `{ linkIds }`, 1-100 distinct nonempty
GUIDs, and returns only currently permitted ResourceLink entries in supplied order.
No missing details or destination requests. UI opening remains a separate explicit
native browser action. Personal-group additions reuse the versioned merge route.
## E-08 In Use compatibility note

Existing routes and manual-verification authorization remain unchanged. The
historically named `ConfirmInUseClosureRequest` route now records the meaning frozen
in the server intent, not a browser-selected interpretation: new
`WasasActivityManual` means WASAS activity attestation, old modes mean the original
OR attestation. `Completed` alone is not OR closure; inspect verified Closure
evidence. New verified BPM activity retains its duplicate fence and is projected
separately to tracking and `InUse.ActivityVerified` reporting. Query views add
`review` and `tracking` with SQL-side count/paging. Manual confirmation is excluded.
Optional source WASAS/stage observations remain absent with the current corporate
adapter; their schema is not a newly invented corporate API contract.
