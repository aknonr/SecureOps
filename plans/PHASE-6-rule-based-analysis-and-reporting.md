# Phase 6 — Rule-Based Analysis and Reporting

**Duration:** 3–5 weeks
**Goal:** Use accumulated audit and diagnostic data to produce useful periodic reports — without AI.

## Pre-Conditions

- [ ] Phase 5 complete.
- [ ] At least 3 months of structured data accumulated (preferably more).
- [ ] Stakeholders have agreed which reports they want.

## Deliverables

1. Rule definition storage and management UI.
2. Rule evaluation engine.
3. `AnalysisFindings` table and UI.
4. Initial set of 5+ rules in production.
5. SQL views for common analyses.
6. Optional Python script(s) for statistical analysis.
7. Weekly and monthly management report generators.

## Task Breakdown

### Sprint 1 — Engine Foundation (~1 week)

| # | Task | Estimate |
|---|---|---|
| P6-T01 | `AnalysisRule` entity + storage | 4h |
| P6-T02 | `AnalysisFinding` entity + storage | 3h |
| P6-T03 | `IRuleEvaluator` interface + base engine | 6h |
| P6-T04 | Hangfire recurring scheduler for rule evaluation | 4h |
| P6-T05 | Audit hooks for findings | 2h |

### Sprint 2 — Initial Rules (~1.5 weeks)

| # | Task | Estimate |
|---|---|---|
| P6-T06 | Rule: Recurring alarm on the same server (configurable thresholds) | 5h |
| P6-T07 | Rule: Top N servers by alarm count | 4h |
| P6-T08 | Rule: Response time outliers (with anti-surveillance framing) | 6h |
| P6-T09 | Rule: Disk growth trend (capacity foresight) | 6h |
| P6-T10 | Rule: App pool recycle correlation | 5h |
| P6-T11 | Rule: Service flap detection (start/stop pattern) | 5h |

### Sprint 3 — Reports and UI (~1 week)

| # | Task | Estimate |
|---|---|---|
| P6-T12 | SQL views for common queries | 6h |
| P6-T13 | UI: Findings list with filtering | 6h |
| P6-T14 | UI: Finding detail with explainability | 5h |
| P6-T15 | Weekly report generator + mail dispatch | 6h |
| P6-T16 | Monthly management report | 5h |

### Sprint 4 — Polish (~0.5–1 week)

| # | Task | Estimate |
|---|---|---|
| P6-T17 | Optional Python script (disk growth forecast) | 6h |
| P6-T18 | Documentation: how to add a rule | 3h |
| P6-T19 | Tests | 6h |

**Total Phase 6 estimate:** ~85 hours, ~5 weeks at 18h/week.

## Constraints (Reaffirm from `.cursor/rules/070-analysis-rules.mdc`)

- All outputs deterministic.
- No machine learning.
- No LLM use.
- Every finding explainable: rule + values + context.
- Anti-surveillance framing maintained.

## Exit Criteria

- [ ] At least 5 useful rules in production.
- [ ] Findings displayed in UI with explainability.
- [ ] Weekly and monthly reports delivered on schedule.
- [ ] Reports approved by management.
- [ ] DoD satisfied.

## Risks

| Risk | Mitigation |
|---|---|
| Insufficient data for trends | Document; defer specific rules until enough data |
| Rule thresholds need tuning | UI for adjustment; iterate |
| Report content needs management input | Iterate based on feedback |
| Performance: rule evaluation slow | SQL views + indexes; profile |

## Hand-off to Phase 7

Phase 7 plan: `plans/PHASE-7-private-ai-rag-poc.md`. Note: Phase 7 has separate budget and pre-conditions.
