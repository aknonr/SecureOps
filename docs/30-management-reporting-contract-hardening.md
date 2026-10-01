# Management Reporting Contract Hardening

## Scope and Compatibility

The existing routes and query parameters remain unchanged:

- `GET /api/v1/reporting/management/summary`
- `GET /api/v1/reporting/management/operators`

This hardening is additive. Existing counts, `definition` text, duration statistics, and legacy summary `dataLimitations` strings retain their meanings. New consumers must use duration `key`, limitation `code`, and `coverage`; English text and array order are presentation details, never contract identities.

## Stable Duration Keys

| Key | Preserved meaning |
|---|---|
| `importToPreview` | First persisted import to first persisted preview |
| `claimToJiraCreation` | Workflow claim to durable Jira issue-key persistence |
| `claimToCompletion` | Workflow claim to durable workflow completion |

Every emitted duration contains `key`, legacy `definition`, `sampleCount`, and nullable minimum/average/maximum seconds. Array order does not identify a metric.

## Stable Limitation Codes

The additive `limitations` array contains a stable `code` and optional fallback `message`:

- `RateLimitRejectionsUnavailable`
- `AccessVersionConflictHistoryUnavailable`
- `OperationalSourceOutageHistoryUnavailable`
- `BulkIdentityInvalidItemHistoryUnavailable`
- `DuplicateCreatePreventionHistoryIncomplete`
- `ElapsedDurationsNotActiveEffort`
- `HistoryBeforePersistenceUnavailable` when requested history predates persisted evidence

Localization uses `code`. `message` is fallback presentation text and must not drive behavior. The legacy `dataLimitations` string array remains temporarily for backward compatibility.

## Evidence Coverage Algorithm

Both reporting responses contain:

- `requestedFromUtc`: resolved inclusive report boundary;
- `requestedToUtc`: resolved exclusive report boundary;
- `coverageFromUtc`: earliest retained `OccurredAt` for the known reporting-action catalog in the append-only `reporting.ManagementAuditEvents` view, or null when no persisted reportable evidence exists;
- `coverageComplete`: true only when `coverageFromUtc` exists and is at or before `requestedFromUtc`.

The coverage query is independent of the selected report window so it cannot mistake the first event inside a window for the persistence boundary. It groups by the indexed action column before taking the earliest boundary and excludes management-report read events themselves. A null boundary, a boundary inside the requested window, or a boundary after it produces `coverageComplete=false`. Therefore a zero count is a measured zero only when coverage is complete and no metric-specific limitation code applies.

Coverage is a persisted-history boundary, not proof that every metric existed historically. Known action-specific gaps remain explicit limitation codes. The algorithm does not infer browser activity, rate-limit history, source outages, old bulk-item outcomes, manual effort, or pre-persistence history.

## Gap Status

- **G-14 resolved:** every duration has a stable key.
- **G-15 resolved:** every known limitation has a stable code and fallback message.
- **G-17 resolved:** requested and persisted-evidence boundaries distinguish complete, partial, and unavailable history.
- **G-16 unresolved/out of scope:** no adoption or Operational Record/Jira trend series were added.

No SQL view, table, grant, or migration changes are required. The migration chain remains `001` through `007`.
