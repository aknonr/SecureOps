# Enterprise Turuncu Hat and Jira Adapters

## Provider Selection

- `OperationalRecords:SourceProvider=Disabled|Fake|TuruncuHat`
- `Jira:Provider=Disabled|Fake|Corporate`

`Fake` is permitted only in Development, Demo, or Test. Real provider selection is explicit and never falls back to synthetic behavior. Application defaults remain `Disabled`.

## Turuncu Hat Configuration

Server-owned non-secret business configuration:

- `TuruncuHat__BaseUrl`
- `TuruncuHat__TenantId`
- `TuruncuHat__SourceBaseObject`
- `TuruncuHat__RelatedGroupId`
- `TuruncuHat__ExcludedDccIds__N`
- `TuruncuHat__ActivityBaseObject`
- `TuruncuHat__ActivityTaskModelId`
- `TuruncuHat__ActivityGroupId`
- `TuruncuHat__ActivityMainObjectTypeId`
- `TuruncuHat__CompletedStatusId`
- `TuruncuHat__CompletionCommentTemplate` (must contain `{JiraKey}`)
- `TuruncuHat__SessionIdSegmentIndex`
- `TuruncuHat__SessionLifetimeSeconds`
- `TuruncuHat__ConnectTimeoutSeconds`
- `TuruncuHat__RequestTimeoutSeconds`
- `TuruncuHat__MaxResponseBytes`
- `TuruncuHat__MaxDescriptionLength`

Runtime secrets, supplied only through controlled server configuration or an approved secret store:

- `TuruncuHat__Authorization`
- `TuruncuHat__Username`
- `TuruncuHat__Password`

## Jira Configuration

Server-owned non-secret business configuration:

- `Jira__BaseUrl`
- `Jira__AuthenticationMode` (`Basic`)
- `Jira__ProjectKey`
- `Jira__IssueTypeId`
- `Jira__MappingVersion`
- `Jira__TeamCustomField`
- `Jira__TeamValue`
- `Jira__RequesterWatcherCustomField`
- `Jira__AssignmentMode` (`ProjectDefault` or `VerifiedOperatorMapping`)
- `Jira__OperatorAssigneeMappings__N__SecureOpsActor`
- `Jira__OperatorAssigneeMappings__N__JiraUsername`
- `Jira__ReporterMode` (`ProjectDefault` only)
- `Jira__Labels__N`
- `Jira__SummarySeparator`
- `Jira__SummaryMaxLength`
- `Jira__UnresolvedRequesterPolicy`
- `Jira__ConnectTimeoutSeconds`
- `Jira__RequestTimeoutSeconds`
- `Jira__MaxResponseBytes`
- `Jira__UserSearchMaxAttempts`
- `Jira__UserSearchRetryDelayMilliseconds`

Runtime secret:

- `Jira__Authorization`

The Turuncu Hat Authorization value remains a complete runtime header because its scheme is not proven. Controlled Jira evidence proves Basic authentication through `GET /rest/api/2/myself`; `Jira__Authorization` is therefore a complete runtime Basic header and `Jira__AuthenticationMode=Basic` is startup-validated. Header values must never be logged or returned.

## Reviewed Jira Contract

| Jira field | Reviewed create behavior |
|---|---|
| Project | Configured key; controlled evidence is SDM |
| Issue type | ID `3`, Task |
| `summary` | Required |
| `description` | Optional |
| `customfield_12700` (`İlgili Grup`) | Required cascading select; create operation `set`; parent value `WASAS` |
| `customfield_11500` (`Takip Eden Kişiler`) | Optional multi-user picker; create operations `add`, `set`, `remove`; exact Turuncu Hat requester maps to `[{ "name": "<verified-jira-username>" }]` |
| `labels` | Optional array containing `SunucuTalep` |
| `assignee` | Optional; omitted in `ProjectDefault`; emitted only for one exact deployment-verified operator mapping |
| `reporter` | Absent from create metadata for the authenticated integration identity; never emitted |

The authenticated Jira API identity, SecureOps actor, Turuncu Hat requester, assignee, and reporter are separate identities. `VerifiedOperatorMapping` uses exact configured actor keys only; a missing mapping falls back visibly to project default. The effective assignee participates in the existing draft fingerprint, so a different mapped assignee after preview fails as a mapping conflict instead of changing the reviewed create payload. No AD-to-Jira inference, display-name assignment, or fuzzy matching is allowed. The deployment owner must verify every Jira username with exact user search before adding a mapping and increment `Jira__MappingVersion` whenever mappings change.

The controlled `/myself` response exposed `self`, `key`, `name`, `emailAddress`, `avatarUrls`, `displayName`, `active`, `deleted`, `timeZone`, `locale`, `groups`, `applicationRoles`, and `expand`. In one inspected existing issue, assignee and reporter both matched that authenticated API identity, but this is observation only: it does not prove either business-actor mapping or reporter create permission.

Framework HTTP-client request logging is removed for both real providers so base URLs, requester query values, headers, and payloads do not enter application logs. Redirects and unproven cookie state are disabled; connection pooling and concurrency are bounded. Provider telemetry contains only fixed provider/operation/outcome tags, aggregate counts, and duration.

## Safe Runtime Behavior

The source client owns all query grammar. Controlled evidence confirms `QueryResult` exposes `ErrorDescription`, `ErrorDetails`, `ErrorNo`, `TenantId`, `Items`, `MaxPages`, `PageNo`, and `RecordCount`, while cells expose `Key` and `Value`. When keys are present, parsing maps only the exact requested keys and rejects mixed, missing, duplicate, or unexpected keys; reordered keyed cells are safe. Legacy keyless fixtures retain exact-count positional parsing. The client HTML-decodes bounded content, excludes malformed/duplicate records, and computes the existing deterministic source fingerprint because no source ETag is proven. Exact real `Key` values still require validation before activation.

Jira user search uses the evidenced `/rest/api/2/user/search?username=...` endpoint and accepts only one exact `name`, then one exact display-name fallback. Controlled success returned `name`, `key`, and `displayName`. No fuzzy match or first-result selection exists. Jira create is never automatically retried. Any ambiguous submission outcome enters existing reconciliation-required state.

Source completion runs only after the Jira key is persisted. It requires exactly one activity and explicit update success. A failure leaves `JiraExists=true` and retries only source completion.

`GET /api/v1/health/enterprise-integrations` is Admin-only and returns provider selection plus `Configured`, `Disabled`, or `Unavailable`. It returns no URL, credential, session, username, or remote response.

## Sanitized Fixtures Required Before Real TEST

Provide property names, nesting, HTTP status, and relevant non-secret header names for:

1. Turuncu Hat invalid credentials and expired-session responses; exact returned cell `Key` values; empty, application-error, HTTP-error, zero/multiple BPM, and pagination samples.
2. BPM update success and failure responses.
3. Jira user-search no-match, ambiguity, authentication failure, and rate limit.
4. Jira create success, validation failure, authentication failure, rate limit, server failure, and timeout behavior.
5. Source version/ETag or conditional-update semantics.
6. Correlation-header support for both providers.
7. Jira remote idempotency support and reconciliation lookup contract.
8. Confirmation that Jira `name` is an approved durable identity field for assignment and watcher use.

Exact sample templates are tracked in `docs/integrations/turuncu-hat-jira-contract-gaps.md`. Until those fixtures are approved, real external TEST is blocked even though deterministic local adapter tests can run.
