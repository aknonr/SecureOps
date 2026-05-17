# Phase 4 — Audit and Alarm Response Verification

**Duration:** 3–5 weeks
**Goal:** Complete audit story; correlate alarm response with PAM activity; produce verification reports.

## Critical Framing

This phase contains features that could be misunderstood as employee surveillance. The framing is canonical and must be preserved:

> "Operational response verification, SLA evidence, and audit. Process auditing, not personnel monitoring."

> Turkish: "Kişi takibi amacıyla değil; kritik alarmlarda operasyonel müdahale doğrulama, SLA ve audit amacıyla."

Every UI element, report header, filter label, and export filename in this phase uses this language.

## Pre-Conditions

- [ ] Phase 3 complete.
- [ ] PAM read-only API access tested.
- [ ] Internal audit team has reviewed audit format from Phase 1.
- [ ] HR briefed on the framing (no surprises).

## Deliverables

1. `IPamSessionClient` interface with mock + real implementations.
2. PAM session correlation logic.
3. Alarm response verification reports (SLA-focused).
4. Audit export with watermarking.
5. Retention policy enforcement (36-month minimum verified).
6. Optional: hash chain for tamper detection (ADR-gated).

## Task Breakdown

### Sprint 1 — PAM Integration (~1 week)

| # | Task | Estimate |
|---|---|---|
| P4-T01 | `IPamSessionClient` interface + `PamSession` DTO | 3h |
| P4-T02 | `MockPamSessionClient` | 3h |
| P4-T03 | `BeyondTrustPamClient` (real adapter) | 12h |
| P4-T04 | PAM session correlation service | 8h |
| P4-T05 | Configuration + secrets via PAM-managed credentials | 4h |

### Sprint 2 — Verification Reports (~1.5 weeks)

| # | Task | Estimate |
|---|---|---|
| P4-T06 | Alarm response verification report (per alert) | 8h |
| P4-T07 | SLA evidence report (per time window) | 8h |
| P4-T08 | Aggregate team-level metrics | 4h |
| P4-T09 | Audit export (CSV, PDF) with watermarking | 8h |
| P4-T10 | Compliance role permissions verified | 3h |

### Sprint 3 — Retention and Optional Hash Chain (~1 week)

| # | Task | Estimate |
|---|---|---|
| P4-T11 | Retention policy enforcement job (monthly partition rotate) | 6h |
| P4-T12 | Archive job for old audit entries | 4h |
| P4-T13 | Optional: hash chain (`EntryHash`, `PrevEntryHash`) (ADR required) | 12h |
| P4-T14 | Optional: tamper detection job | 4h |
| P4-T15 | Test: tamper detection works | 3h |

### Sprint 4 — UI and Documentation (~0.5–1 week)

| # | Task | Estimate |
|---|---|---|
| P4-T16 | UI: PAM correlation viewer | 4h |
| P4-T17 | UI: SLA dashboard | 6h |
| P4-T18 | Documentation: audit query examples | 3h |
| P4-T19 | Documentation: internal audit handbook | 4h |

**Total Phase 4 estimate:** ~100 hours, ~5–6 weeks at 18h/week.

## Anti-Surveillance Enforcement

| Element | Constraint |
|---|---|
| Page titles | "Operational Audit", "Response Verification", "SLA Evidence" |
| Default sort | By time, by alert, or by server — not by operator |
| Filters | Time, alert type, server are primary; operator is secondary and audited |
| Reports | Aggregate to team by default; per-operator marked "INTERNAL AUDIT" and watermarked |
| Exports | Filename: `operational-audit-<date>.csv`, not `operator-activity-<date>.csv` |
| Notification | No notifications mention specific operator performance |

## Exit Criteria

- [ ] PAM correlation working on pilot data.
- [ ] Auditor role can produce a complete picture of "what was done about alarm X" in under 30 seconds.
- [ ] Internal audit team has approved the report format.
- [ ] Retention policy enforced; old entries archived correctly.
- [ ] Watermarking on exports verified.
- [ ] No UI element violates the anti-surveillance framing.
- [ ] DoD satisfied.

## Risks

| Risk | Mitigation |
|---|---|
| PAM API access limited | Use mock; reduce scope of correlation |
| Operator concern about surveillance | Re-emphasize framing; HR sign-off |
| Audit data growth | Partition strategy; archive policy |
| Hash chain complexity | Optional; defer to Phase 5 if blocked |

## Hand-off to Phase 5

Phase 5 plan: `plans/PHASE-5-pam-ad-local-admin-compliance.md`.
