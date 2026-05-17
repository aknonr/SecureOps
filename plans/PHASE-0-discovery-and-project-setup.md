# Phase 0 — Discovery and Project Setup

**Duration:** 1–2 weeks
**Goal:** Establish baseline, gather approvals, finalize pilot scope. **No application code in this phase.**

## Pre-Conditions

- [ ] Management has approved beginning Phase 0.
- [ ] Project owner has 16+ hours/week dedicated.
- [ ] Stakeholder list confirmed (see `docs/01-current-operations-context.md`).

## Deliverables

1. Pilot server list (10–15 servers) with owner sign-off.
2. Baseline measurement of current alarm volume and analysis time.
3. Bilgi Güvenliği architecture pre-review.
4. Siber Güvenlik architecture pre-review.
5. PAM service account request submitted.
6. Monitoring platform integration feasibility confirmed.
7. Test environment provisioned.
8. Network firewall rules identified.
9. JEA endpoint proof-of-concept on one test server.
10. Risk matrix finalized.
11. Phase 1 backlog reviewed and committed.
12. Go/no-go decision for Phase 1.

## Task Breakdown

### Week 1 — Stakeholder Engagement

| # | Task | Hours | Status |
|---|---|---|---|
| P0-T01 | Send stakeholder kick-off mail | 1 | [ ] |
| P0-T02 | Identify and contact pilot server owners | 4 | [ ] |
| P0-T03 | Draft `docs/pilot-servers.md` with 15 candidates | 3 | [ ] |
| P0-T04 | Schedule Bilgi Güv pre-review meeting | 1 | [ ] |
| P0-T05 | Schedule Siber Güv pre-review meeting | 1 | [ ] |
| P0-T06 | Submit PAM service account request | 1 | [ ] |
| P0-T07 | Discuss webhook with monitoring platform team | 2 | [ ] |
| P0-T08 | Request test environment from IT ops | 1 | [ ] |
| P0-T09 | Network team meeting: WinRM, SQL, webhook firewall rules | 2 | [ ] |

### Week 2 — Technical Validation

| # | Task | Hours | Status |
|---|---|---|---|
| P0-T10 | Collect baseline data: alarm count per shift over 2 weeks | 4 | [ ] |
| P0-T11 | Collect baseline: average analysis time per alarm | 3 | [ ] |
| P0-T12 | Draft `docs/baseline-2026Q2.md` with baseline numbers | 2 | [ ] |
| P0-T13 | Conduct Bilgi Güv pre-review meeting | 2 | [ ] |
| P0-T14 | Conduct Siber Güv pre-review meeting | 2 | [ ] |
| P0-T15 | Address Bilgi Güv feedback in `docs/05-security-model.md` | 3 | [ ] |
| P0-T16 | Address Siber Güv feedback (data flow, masking) | 3 | [ ] |
| P0-T17 | Build JEA PoC: install endpoint on one test server | 4 | [ ] |
| P0-T18 | Verify JEA PoC: forbidden cmdlets blocked, allowed cmdlets work | 3 | [ ] |
| P0-T19 | Finalize risk matrix in `docs/11-feasibility.md` | 2 | [ ] |
| P0-T20 | Review Phase 1 backlog `docs/12-mvp-backlog.md` | 2 | [ ] |
| P0-T21 | Phase 0 retro + Phase 1 go/no-go meeting | 2 | [ ] |

**Total Phase 0 effort:** approximately 42 hours over 1.5–2 weeks.

## Exit Criteria

All of these must be true to declare Phase 0 complete and start Phase 1:

- [ ] Pilot list approved by server owners.
- [ ] PAM service account in progress (may not be provisioned yet, but request acknowledged).
- [ ] Test environment access confirmed.
- [ ] Bilgi Güvenliği has given written feedback.
- [ ] Siber Güvenlik has given written feedback.
- [ ] JEA PoC working on test server.
- [ ] Risk matrix finalized.
- [ ] Phase 1 backlog committed.
- [ ] Go/no-go: Go.

## Stakeholder Outputs

| Stakeholder | Expected output | Owner |
|---|---|---|
| Bilgi Güvenliği | Architecture pre-review notes; concerns / requirements | InfoSec lead |
| Siber Güvenlik | Data flow review; attack surface assessment | CyberSec lead |
| PAM team | Service account creation acknowledgment | PAM admin |
| Monitoring platform team | Webhook capability confirmation | Monitoring admin |
| Network team | Firewall ticket reference + ETA | Network admin |
| IT ops | Test environment server name and access | IT ops |
| Pilot server owners | Written approval for inclusion | Each owner |

## Risks Specific to Phase 0

| Risk | Mitigation |
|---|---|
| Stakeholder unavailability | Schedule early; offer flexible time slots; written async fallback |
| Test environment delay | Use developer workstation for JEA PoC if needed |
| Pilot owner reluctance | Emphasize non-production focus and read-only nature |
| Baseline data hard to collect | Use rough estimates if precise data unavailable; flag as assumption |

## Documents Produced

- `docs/pilot-servers.md` (new)
- `docs/baseline-2026Q2.md` (new)
- `docs/stakeholder-meeting-notes/` (new, one file per meeting)
- `docs/11-feasibility.md` (updated with Phase 0 findings)
- `docs/05-security-model.md` (updated with InfoSec/CyberSec input)

## After Phase 0

Begin Phase 1 immediately if go decision is reached. Phase 1 plan: `plans/PHASE-1-readonly-diagnostic-mvp.md`.
