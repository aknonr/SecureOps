# ADR-0011: Backend-Authoritative Management Reporting

**Status:** Accepted for backend implementation; pilot deployment remains approval-gated
**Date:** 2026-08-23

## Context

SecureOps now persists exact identity lookup outcomes, application access decisions, and Operational Record/Jira workflow transitions. Management reporting must be explainable from those durable backend facts without browser telemetry, directory profile expansion, or person-performance scoring.

The audit and workflow tables were designed for evidence, not unrestricted analytics. Reporting is a privileged read and must not grant ordinary operational roles access to cross-user activity.

## Decision

SecureOps will expose a backend-authoritative management reporting read model with these constraints:

- SQL Server performs bounded aggregation over append-only audit and workflow evidence.
- Report windows are UTC half-open intervals: `fromInclusive <= event < toExclusive`.
- Supported presets are today, rolling 7 days, and rolling 30 days; custom windows are capped at 92 days.
- `Reporting.ManagementView` is a distinct application capability assigned only to the existing Admin and Auditor roles.
- Every report read is audited and fails closed if the audit store cannot accept the privileged-read event.
- Summary reporting returns aggregates. Per-operator activity is a separate paginated endpoint and returns only the persisted corporate principal, counts, and activity bounds; it does not perform AD enrichment.
- SQL views expose only reporting-required columns. The runtime identity receives `SELECT` on those views, not on base audit/history tables.
- Existing audit event codes and workflow transitions define historical metrics. Missing historical evidence is returned as unavailable or documented as a limitation, never inferred.
- Elapsed workflow durations are operational elapsed time, not active labor, productivity, or time saved.

## Consequences

- Migration 005 creates the reporting schema/views and supporting indexes but no mutable reporting fact table.
- The reporting endpoint is unavailable unless Audit, Access, and Operational Record persistence all use SQL Server.
- Rate-limit rejection counts remain unavailable because current rate-limit responses are not audited.
- Invalid items skipped inside historical bulk identity requests do not have individual terminal audit rows.
- Idempotent duplicate-prevention events become measurable only from this release forward.
- Real-user pilot reporting requires Demo authentication and Demo access compatibility to be disabled during real-user bootstrap and thereafter.

## Rejected Alternatives

- Browser analytics: rejected because it is not authoritative and can be blocked or replayed.
- Raw audit export to the browser: rejected because it expands data exposure and does not scale.
- Reporting access through ordinary operational capabilities: rejected because cross-user reporting is a separate privileged purpose.
- Individual performance rankings or time-saved claims: rejected by the anti-surveillance policy and lack of a measured manual baseline.

## References

- `docs/05-security-model.md`
- `docs/08-audit-model.md`
- `docs/25-real-user-pilot-management-reporting-and-dotnet10.md`
- ADR-0010
