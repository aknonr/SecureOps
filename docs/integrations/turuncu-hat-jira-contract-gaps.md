# Turuncu Hat and Jira Sanitized Contract Gaps

Real external TEST remains blocked until the owner supplies sanitized HTTP examples. Every example must preserve HTTP status, method, path, relevant non-secret header names, property names, nesting, value types, and empty/null behavior. Replace URLs, credentials, sessions, people, corporate content, and issue keys with placeholders.

Required examples:

1. Turuncu Hat login success, including the complete `LoginResult` shape.
2. Turuncu Hat invalid-credential and expired-session responses.
3. Operational Record query success with one item and every selected field.
4. Operational Record query success with zero items.
5. Operational Record query application error and HTTP error.
6. Pagination indicators and a response proving whether truncation can occur.
7. BPM activity query with exactly one matching row.
8. BPM activity query with zero and multiple matching rows.
9. BPM update success with the complete `UpdateResult`.
10. BPM update business failure and HTTP failure.
11. Jira user search success, no match, ambiguity, authentication failure, and rate limit.
12. Jira create success with the complete response.
13. Jira create validation, authentication, rate-limit, server-error, and timeout outcomes.
14. Source version/ETag/conditional-update and correlation-header evidence, if supported.
15. Jira remote idempotency and reconciliation lookup evidence, plus confirmation that `name` is an approved durable user identifier.

Do not infer retryability or write outcome from an undocumented body. Provider-specific error fixtures will be added only after these reviewed examples exist.
