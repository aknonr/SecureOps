# Phase 5 — PAM / AD / Local Admin Compliance

**Duration:** 4–6 weeks
**Goal:** Surface local admin compliance and PAM coverage gaps across the pilot fleet.

## Pre-Conditions

- [ ] Phase 4 complete.
- [ ] Expected local admin list defined per pilot server (or per server class).
- [ ] PAM team has confirmed which servers are PAM-managed.

## Deliverables

1. Local administrators inventory job (read-only, fleet-wide).
2. `ExpectedLocalAdmins` table + management UI.
3. Drift detection between actual and expected.
4. PAM coverage report (which servers managed by PAM, which not).
5. Compliance dashboard for Admin/Auditor.
6. Scheduled jobs for recurring inventory.

## Task Breakdown

### Sprint 1 — Inventory Job (~1 week)

| # | Task | Estimate |
|---|---|---|
| P5-T01 | PowerShell: `Get-LocalGroupMember -Group Administrators` via JEA | 4h |
| P5-T02 | `LocalAdminInventoryJob` Hangfire job | 6h |
| P5-T03 | Parallel inventory across fleet with throttling | 6h |
| P5-T04 | Result persistence | 3h |
| P5-T05 | Audit hook | 1h |

### Sprint 2 — Expected List and Drift (~1.5 weeks)

| # | Task | Estimate |
|---|---|---|
| P5-T06 | `ExpectedLocalAdmins` table + EF entity | 3h |
| P5-T07 | Admin UI: manage expected list per server | 8h |
| P5-T08 | Drift detection: actual vs expected | 6h |
| P5-T09 | Notification for new drift | 3h |
| P5-T10 | Drift report (per-server, per-fleet) | 6h |

### Sprint 3 — PAM Coverage (~1 week)

| # | Task | Estimate |
|---|---|---|
| P5-T11 | PAM API: list managed accounts/servers | 4h |
| P5-T12 | Reconciliation: fleet vs PAM-managed | 6h |
| P5-T13 | Coverage gap report | 4h |
| P5-T14 | Admin UI: PAM coverage view | 6h |

### Sprint 4 — Dashboard and Schedule (~0.5–1 week)

| # | Task | Estimate |
|---|---|---|
| P5-T15 | Compliance dashboard | 8h |
| P5-T16 | Hangfire recurring schedule (daily inventory) | 3h |
| P5-T17 | Documentation: compliance runbook | 4h |
| P5-T18 | Tests | 6h |

**Total Phase 5 estimate:** ~110 hours, ~6–7 weeks at 18h/week.

## Exit Criteria

- [ ] Local admin scan completes for full pilot fleet in under 1 hour.
- [ ] Drift detected within 24 hours of change.
- [ ] PAM coverage report accurate per pilot data.
- [ ] Reports delivered to security and compliance roles.
- [ ] DoD satisfied.

## Risks

| Risk | Mitigation |
|---|---|
| Some servers reject `Get-LocalGroupMember` (very old OS) | Skip and report; alternative WMI query |
| Inventory job slow on large fleets | Parallel + throttled; metrics |
| Expected list maintenance burden | UI for self-service; CSV bulk import |
| PAM API limitations | Reduce scope; document gaps |

## Hand-off to Phase 6

Phase 6 plan: `plans/PHASE-6-rule-based-analysis-and-reporting.md`.
