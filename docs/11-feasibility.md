# 11 — Feasibility, Risks, and Schedule Realism

This document gives an honest assessment of the project's feasibility under the single-developer constraint, and the risks that could derail it.

## Capacity Assessment

| Constraint | Value |
|---|---|
| Developers | 1 |
| Effort per week | 16–20 hours (around shift duty) |
| Shift cycle | 6 days (2 evening, 2 night, 2 off) |
| Bus factor | 1 |
| Vacation / sick contingency | None planned |

Working hours per quarter (assuming no extended absence): roughly **210–260 hours**.

## Phase Effort Estimates

| Phase | Estimated hours | Equivalent weeks at 18h/week |
|---|---|---|
| 0 — Discovery | 25–40 | 1.5–2 |
| 1 — Read-only diagnostic MVP | 110–160 | 6–9 |
| 2 — Web UI | 70–110 | 4–6 |
| 3 — Notification + ticket | 50–80 | 3–4 |
| 4 — Audit + verification | 70–110 | 4–6 |
| 5 — PAM / AD / local admin | 90–140 | 5–8 |
| 6 — Rule-based analysis | 70–110 | 4–6 |
| 7 — AI/RAG PoC | 150–250 | 9–14 (separate budget) |
| 8 — Approval-based remediation | 130–200 | 7–11 |

These are **engineering estimates**, not promises. Each phase ends with a checkpoint where scope and timing are re-baselined.

## Schedule Outlook

With a single developer at 18h/week:

| Milestone | Realistic target |
|---|---|
| Phase 0 done | Within 2 weeks of project start |
| Phase 1 MVP demo to management | 6–8 weeks after Phase 0 |
| Phase 2 UI in use by pilot operators | +4–6 weeks (~Q4 2026 with 2026 Q3 start) |
| Phase 3 notifications live | +3–4 weeks |
| Phase 4 audit + verification live | +4–6 weeks |
| End of 2026 | Phase 0–4 complete |
| End of Q1 2027 | Phase 5–6 complete |
| Mid 2027 | Phase 7 reconsidered with management |
| Later 2027+ | Phase 8 considered |

These dates assume:
- Stakeholder responses within 1 week.
- No major scope additions during a phase.
- Test environment provisioned within 2 weeks of Phase 0 start.
- No extended absence of the single developer.

If any assumption breaks, dates slide accordingly. The schedule is **honest**, not optimistic.

## Risk Register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Single developer absence (illness, leave, role change) | Low | Catastrophic | Document-first; mainstream stack; small commits; encourage second developer in Phase 2 |
| R2 | Monitoring platform team delays webhook setup | Medium | High | Mock-first; webhook fallback to SWIS polling in Phase 6+ |
| R3 | Bilgi Güvenliği or Siber Güvenlik review delays | Medium | High | Early engagement in Phase 0; pre-share architecture |
| R4 | PAM service account provisioning delay | Medium | High | Request in Phase 0; mock until provisioned |
| R5 | Scope creep into AI before foundation is ready | High | Medium | Pre-conditions documented; ADR gate |
| R6 | Operator pushback ("this is surveillance") | Medium | High | Framing enforced in UI, reports, communications; HR pre-briefed |
| R7 | JEA endpoint deployment friction across pilot fleet | Medium | Medium | Phase 0 PoC; one-server install script tested |
| R8 | Hangfire SQL Server schema conflicts | Low | Low | Separate Hangfire schema; standard pattern |
| R9 | Pilot servers reject WinRM (firewall, GPO) | Medium | Medium | Phase 0 connectivity test; coordinate with network team |
| R10 | Performance: diagnostic jobs slow on first deploy | Medium | Medium | Timeouts; profile and tune; circuit breakers |
| R11 | Schema migration breaks audit history | Low | High | EF Core migrations tested in test environment; rollback plan |
| R12 | Management priorities shift during multi-phase build | Medium | Medium | Phase-by-phase value; visible incremental deliveries |
| R13 | Test environment unavailable or shared with other projects | Medium | High | Request dedicated in Phase 0; develop on mocks if needed |
| R14 | Phase 7 AI hardware budget rejected | Medium | Low | Project is valuable without AI; surface this in management communication |
| R15 | Phase 8 remediation approval gets misused | Low | High | Bounded catalog; ADR-gated extension; meta-audit |

## Bus Factor Mitigations

Specific actions to reduce single-developer risk:

1. **Documentation completeness** — every behavior described in `docs/` and ADRs.
2. **Mainstream tech only** — .NET, SQL Server, PowerShell. No exotic frameworks.
3. **No tribal knowledge** — anything that "you have to know" is wrong; write it down.
4. **Small commits** — each commit understandable by a new reader.
5. **Architecture decision records** — every key choice is in `docs/adr/`.
6. **Operational runbooks** — `docs/runbooks/` (Phase 1+) for operating the system itself.
7. **Test coverage** — tests serve as executable documentation.
8. **Bring in a second developer in Phase 2** — once UI is starting, the work spreads naturally.
9. **Pair programming weeks** — when a second developer joins, dedicate weeks to walk through.
10. **Avoid hidden state** — configuration is in files, not in someone's head.

## What "Done" Means at Each Phase

See `docs/13-definition-of-done.md` for the canonical DoD. Highlights:

- Phase 0: stakeholder approvals, pilot list, baseline measurements.
- Phase 1: end-to-end alarm → diagnostic → audit flow, tests passing, demo to management.
- Phase 2: UI deployed, operators using it daily, feedback collected.
- Phase 3+: see each phase plan.

## Honest Failure Modes

What "failure" looks like and what we do about it:

| Failure | Response |
|---|---|
| Phase 1 takes 12 weeks instead of 6 | Acceptable. Quality > speed. Surface and re-baseline. |
| Pilot operators reject the system | Acceptable. Adjust UI; re-pilot. Better than forcing an unwanted tool. |
| One stakeholder team refuses to approve | Acceptable. Document concerns; adapt design; do not bypass. |
| The single developer leaves the project | Hardest case. Documentation must enable a handover. This is the primary continuity insurance. |
| AI hardware request denied | Acceptable. Phase 7 can be deferred or canceled without losing prior phases' value. |

## What We Will NOT Do to Hit a Date

- Skip ADRs to ship faster.
- Skip tests to ship faster.
- Skip audit instrumentation to ship faster.
- Ship without security team review.
- Mock the audit log in production.
- Hardcode secrets to avoid PAM dependency.

If a date is at risk, **scope is cut**, not quality.

## Re-baselining Triggers

The schedule is re-baselined when:

- A phase exceeds its estimate by 25%.
- A major risk materializes (R1, R3, R6, R13).
- Scope is added or removed by management.
- Quarterly review identifies new priorities.

Re-baselining is normal, not failure.
