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
- `Jira__ProjectKey`
- `Jira__IssueTypeId`
- `Jira__MappingVersion`
- `Jira__TeamCustomField`
- `Jira__TeamValue`
- `Jira__RequesterWatcherCustomField`
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

The Authorization values are complete runtime header values because the sanitized evidence does not prove a Basic, Bearer, or other authentication scheme. They must never be logged or returned.

Framework HTTP-client request logging is removed for both real providers so base URLs, requester query values, headers, and payloads do not enter application logs. Redirects and unproven cookie state are disabled; connection pooling and concurrency are bounded. Provider telemetry contains only fixed provider/operation/outcome tags, aggregate counts, and duration.

## Safe Runtime Behavior

The source client owns all query grammar. It parses only the evidenced fields, HTML-decodes bounded content, excludes malformed/duplicate records, and computes the existing deterministic source fingerprint because no source ETag is proven. The remaining check-to-write race is not hidden.

Jira user search prefers one exact stable `name`, then one exact display-name fallback. No fuzzy match or first-result selection exists. Jira create is never automatically retried. Any ambiguous submission outcome enters existing reconciliation-required state.

Source completion runs only after the Jira key is persisted. It requires exactly one activity and explicit update success. A failure leaves `JiraExists=true` and retries only source completion.

`GET /api/v1/health/enterprise-integrations` is Admin-only and returns provider selection plus `Configured`, `Disabled`, or `Unavailable`. It returns no URL, credential, session, username, or remote response.

## Sanitized Fixtures Required Before Real TEST

Provide property names, nesting, HTTP status, and relevant non-secret header names for:

1. Turuncu Hat login success, invalid credentials, and expired session.
2. OR query success, empty result, malformed/error result, and pagination behavior.
3. BPM activity query with exactly one, zero, and multiple rows.
4. BPM update success and failure.
5. Jira user-search success, no match, authentication failure, and rate limit.
6. Jira create success, validation failure, authentication failure, rate limit, server failure, and timeout behavior.
7. Source version/ETag or conditional-update semantics.
8. Correlation-header support for both providers.
9. Jira remote idempotency support and reconciliation lookup contract.
10. Confirmation that Jira `name` is an approved durable identity field for this deployment.

Exact sample templates are tracked in `docs/integrations/turuncu-hat-jira-contract-gaps.md`. Until those fixtures are approved, real external TEST is blocked even though deterministic local adapter tests can run.
