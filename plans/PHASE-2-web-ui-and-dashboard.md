# Phase 2 — Web UI and Dashboard

**Duration:** 3–5 weeks
**Goal:** Replace console/log access with a usable Blazor Server UI deployed to IIS.

## Pre-Conditions

- [ ] Phase 1 complete.
- [ ] Pilot operators have given Phase 1 feedback.
- [ ] IIS hosting environment available.
- [ ] AD groups for RBAC created (or planned).

## Deliverables

1. `SecureOps.Ui` Blazor Server project hosted on IIS.
2. Windows Authentication wired to AD.
3. Authorization policies for the 4 roles (Operator, TeamLead, Admin, Auditor).
4. Pages: Dashboard, Alert List, Alert Detail, Server List, Audit Query (Auditor/Admin).
5. Real-time alert updates via Blazor SignalR.
6. MudBlazor theme with the SecureOps color palette.
7. Operator onboarding guide.
8. bUnit tests for key components.
   *Implementation note (2026-10-01): planned, not implemented as written — component render tests use `HtmlRenderer` in `tests/SecureOps.Tests.Unit/Ui/`; bUnit is pinned centrally but not referenced.*

## Task Breakdown

### Sprint 1 — Project Setup and Auth (~1 week)

| # | Task | Estimate |
|---|---|---|
| P2-T01 | Initialize Blazor Server project | 4h |
| P2-T02 | Add MudBlazor + theme | 4h |
| P2-T03 | Windows Authentication setup | 4h |
| P2-T04 | AD group membership resolution | 6h |
| P2-T05 | Authorization policies in `SecureOps.Shared.Auth.Policies` | 3h |
| P2-T06 | Audit on session start/end | 2h |
| P2-T07 | Layout: MainLayout, NavMenu | 4h |
| P2-T08 | Audit hooks: `AlertViewed`, `DiagnosticViewed` | 2h |

### Sprint 2 — Core Pages (~1.5 weeks)

| # | Task | Estimate |
|---|---|---|
| P2-T09 | Dashboard page with KPI cards | 8h |
| P2-T10 | Alert List page with filtering | 8h |
| P2-T11 | Alert Detail page | 8h |
| P2-T12 | Diagnostic Result viewer (renders by schema) | 8h |
| P2-T13 | Server List page | 4h |
| P2-T14 | API client services (`SecureOps.Ui.Services`) | 6h |
| P2-T15 | Error handling + snackbar feedback | 3h |

### Sprint 3 — Audit and Real-Time (~1 week)

| # | Task | Estimate |
|---|---|---|
| P2-T16 | Audit Query page (Auditor/Admin only) | 8h |
| P2-T17 | Audit CSV export | 4h |
| P2-T18 | Real-time alert push via Blazor SignalR | 8h |
| P2-T19 | Alert acknowledge / resolve actions | 4h |
| P2-T20 | Meta-audit on audit queries | 2h |

### Sprint 4 — Polish and Deployment (~0.5–1 week)

| # | Task | Estimate |
|---|---|---|
| P2-T21 | Responsive design check (tablets) | 3h |
| P2-T22 | Accessibility audit (keyboard, ARIA) | 4h |
| P2-T23 | bUnit tests on key components | 6h |
| P2-T24 | IIS deployment guide | 3h |
| P2-T25 | Operator onboarding guide | 4h |
| P2-T26 | Deploy to pilot environment | 4h |
| P2-T27 | Pilot operator training session | 2h |

**Total Phase 2 estimate:** ~120 hours, ~5–6 weeks at 18h/week.

## UI Constraints (Reaffirm)

From `docs/agent-guides/060-ui.md` and `docs/05-security-model.md`:

- No leaderboards.
- No "fastest responder" widgets.
- Default sorting/filtering by time, alert, or server — not by operator.
- Page titles use "operational" language, not "personnel" language.
- Per-operator views available only to Auditor/Admin, with meta-audit.

## Exit Criteria

- [ ] All pages load in < 2 seconds for typical data volumes.
- [ ] Windows Authentication working; roles correctly resolved from AD.
- [ ] Audit Query page works and meta-audit captured.
- [ ] Real-time updates working (new alerts appear without refresh).
- [ ] Pilot operators have used the system for at least 2 weeks.
- [ ] Survey shows operators find the UI useful.
- [ ] bUnit tests pass.
- [ ] All UI flows audited.
- [ ] DoD satisfied per item.

## Risks

| Risk | Mitigation |
|---|---|
| Windows Auth issues across browsers | Test Chrome, Edge, Firefox; document workarounds |
| Blazor SignalR scaling on the IIS host | Limit concurrent connections; monitor memory |
| Operator UX confusion | Iterate based on pilot feedback; offer training |
| AD group lookup latency | Cache with 5-minute TTL |

## Hand-off to Phase 3

Phase 3 plan: `plans/PHASE-3-notification-and-ticket-enrichment.md`.
