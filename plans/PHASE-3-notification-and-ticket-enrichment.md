# Phase 3 — Notification and Ticket Enrichment

**Duration:** 2–4 weeks
**Goal:** The system reaches engineers through Teams and mail; ticket text generated automatically.

## Pre-Conditions

- [ ] Phase 2 complete; UI in use by pilot.
- [ ] Teams channel(s) created and webhook URLs provisioned.
- [ ] SMTP relay confirmed.
- [ ] Turuncuhat ticket / EVT format agreed with stakeholders.

## Deliverables

1. `INotifier` interface family + mock implementations.
2. Teams adapter (webhook + Adaptive Card).
3. SMTP mail adapter with HTML + text templates.
4. Ticket draft generator.
5. Shift report draft generator.
6. Notification rule engine (configurable by alarm type, severity, time).
7. Notification log (`NotificationLog` table) and UI viewer.

## Task Breakdown

### Sprint 1 — Adapters (~1 week)

| # | Task | Estimate |
|---|---|---|
| P3-T01 | `INotifier` interface and `NotificationMessage` DTO | 3h |
| P3-T02 | `MockNotifier` (dev default) | 2h |
| P3-T03 | `TeamsNotifier` with Adaptive Card | 8h |
| P3-T04 | `MailNotifier` with Razor templates | 8h |
| P3-T05 | Configuration: webhook URLs, SMTP, templates | 4h |
| P3-T06 | `NotificationLog` SQL migration | 2h |
| P3-T07 | Audit hooks: NotificationSent, NotificationFailed | 2h |

### Sprint 2 — Rule Engine and Templates (~1 week)

| # | Task | Estimate |
|---|---|---|
| P3-T08 | Notification rule entity + storage | 4h |
| P3-T09 | Rule engine: alert + diagnostic → set of notifications | 6h |
| P3-T10 | Notification templates per alarm type (6 templates) | 8h |
| P3-T11 | Dispatch from Worker after diagnostic completes | 4h |
| P3-T12 | Retry with backoff (Polly) | 3h |

### Sprint 3 — Ticket and Reports (~1 week)

| # | Task | Estimate |
|---|---|---|
| P3-T13 | Ticket draft format (subject, body sections) | 6h |
| P3-T14 | Ticket draft API + UI button "Copy ticket text" | 4h |
| P3-T15 | Shift report aggregator (8-hour window) | 8h |
| P3-T16 | Shift report scheduled job (Hangfire recurring) | 4h |
| P3-T17 | UI: Notification log viewer | 4h |
| P3-T18 | Tests + documentation | 6h |

**Total Phase 3 estimate:** ~80 hours, ~4–5 weeks at 18h/week.

## Notification Strategy

| Event | Channel | Audience | Frequency |
|---|---|---|---|
| Critical alarm + diagnostic | Teams (instant) | On-call group | At event |
| High severity alarm + diagnostic | Teams (instant) | Shift channel | At event |
| Shift-end summary | Mail | Shift team + lead | At shift end |
| Weekly management summary | Mail | IT management | Monday morning |
| Recurring alarm warning | Mail | System administration | Weekly or threshold |

## Exit Criteria

- [ ] Notifications delivered within 60 seconds of diagnostic completion.
- [ ] Teams Adaptive Card displays correctly.
- [ ] Mail templates approved by stakeholders.
- [ ] Ticket draft format approved.
- [ ] Pilot operators report notification volume is reasonable.
- [ ] Notification failures retried automatically.
- [ ] Notification log queryable.
- [ ] DoD satisfied.

## Risks

| Risk | Mitigation |
|---|---|
| Notification fatigue | Configurable rules; severity gating; off-hours mode |
| Teams webhook rate limits | Queueing + batching |
| SMTP relay limits | Throttling; batch shift reports |
| Template content needs iteration | Pilot feedback loop |

## Hand-off to Phase 4

Phase 4 plan: `plans/PHASE-4-audit-and-alarm-response-verification.md`.
