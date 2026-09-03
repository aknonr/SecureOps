# Turuncu Hat and Jira Sanitized Contract Gaps

Controlled evidence received through 2026-09-04 resolves Jira Basic authentication, `/myself`, one exact username search success, field/create metadata, one existing issue projection, Turuncu Hat login success shape, the exact active-record query request, successful nested-array `QueryResult.Items`, independently optional empty/null error fields, and one matching BPM activity. Real write TEST remains blocked on the samples below. Every example must preserve HTTP status, method, path, relevant non-secret header names, property names, nesting, value types, and empty/null behavior. Replace URLs, credentials, sessions, people, corporate content, and issue keys with placeholders.

## Resolved Evidence

- Jira Basic `/rest/api/2/myself`, exact `username` search, Task ID `3`, required/optional create fields, multi-user watcher, cascading group, optional assignee, and absent reporter metadata.
- Existing Jira issue presence for assignee, reporter, `customfield_11500`, `customfield_12700`, and `SunucuTalep`; observed assignee/reporter identity equality is not treated as create policy.
- String `LoginResult` with two observed pipe segments; exact `SMSS_oRFF` active/group/DCC request; successful nested-array `QueryResult.Items`; independently optional empty/null query error fields; `Key`/`Value` cell properties; and exactly one controlled BPM query match. Observed segment lengths are not invariants.

## Open Evidence

Required examples:

1. Turuncu Hat invalid-credential and expired-session responses.
2. One sanitized Operational Record row showing the exact `Key` string for every requested select; empty result; application error; HTTP error; and pagination/truncation behavior. The successful four-row keyless projection and exact no-pagination request are resolved.
3. BPM activity query with zero and multiple rows, including exact returned `Key` strings.
4. BPM update success with the complete `UpdateResult`, plus business and HTTP failures.
5. Source version/ETag/conditional-update semantics and provider correlation headers, if supported.
6. Jira user-search no match, ambiguity, authentication failure, and rate limit.
7. Jira create success with the complete status/body/headers; validation, authentication, rate-limit, server-error, and timeout outcomes.
8. Jira remote idempotency and reconciliation lookup evidence.
9. Confirmation that Jira `name` is an approved durable identity for watcher and assignee mappings.
10. The exact business rule that determines when the configured `SunucuTalep` label applies; the current mapping treats configured labels as fixed for every eligible record.

Do not infer retryability or write outcome from an undocumented body. Provider-specific error fixtures will be added only after these reviewed examples exist.
