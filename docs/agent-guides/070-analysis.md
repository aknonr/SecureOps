# 070 — Rule-Based Analysis (Phase 6)

Not before Phase 6: earlier phases collect data, Phase 6 analyses it. Plan: `docs/02-roadmap.md`, `plans/PHASE-6-rule-based-analysis-and-reporting.md`.

- **Deterministic and explainable.** Same data, same result. No ML or LLM calls (those are Phase 7). A suggestion that no explicit rule can justify does not appear.
- **Prefer SQL**, then C# in Infrastructure for orchestration; Python only when both are clearly worse, with pinned dependencies and output written back to SQL.
- **Rules are configuration** (rule id, parameters, severity, enabled), not hard-coded logic.
- **Findings are not alerts.** They page no one; they appear in the UI and periodic reports and carry the rule, the threshold values crossed, the time window and sample data so "why this finding?" can always be answered.
- Reports describe systems and processes (recurring alarms, problem servers, capacity trends, SLA distributions) — never individual operator performance.
