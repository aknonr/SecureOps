# 070 — Analysis Rules (Phase 6 — Before AI)

## Applicability

- **Purpose:** Rule-based analysis layer for Phase 6, before AI. SQL/Python first.
- **Applies to:** `src/SecureOps.Infrastructure/Analysis/**/*.cs`, `sql/analysis/**/*.sql`, and `scripts/python/**/*.py`.
- **Loading:** Routed explicitly from `AGENTS.md` or `docs/agent-guides/README.md`; do not assume automatic discovery.

## Position in the Roadmap

This layer sits between raw diagnostic data (Phase 1–4) and the AI/RAG layer (Phase 7). It uses **deterministic, explainable, rule-based analysis** to produce findings — no machine learning, no LLMs.

Do not implement this layer before Phase 6. Phase 1–5 collect the data; Phase 6 analyzes it.

## What This Layer Does

Takes audit + diagnostic data accumulated in SQL and produces:

- **Recurring alarm reports.** "Server X had Y disk alerts in the last 30 days."
- **Problematic server reports.** Top N servers by alert count, by mean time to resolution, by repeat rate.
- **Pattern detection.** "App Pool W3WP recycle correlated with high memory alerts on these servers."
- **Capacity foresight.** "Disk D: on Server X is trending toward full in approximately 14 days at current growth rate."
- **SLA reports.** Alarm response time distribution, time-to-acknowledge, time-to-resolve.
- **Audit summaries for management.** Weekly/monthly aggregated views.

All outputs are **deterministic**: same input data, same output. Run it twice, get the same answer.

## Implementation Hierarchy

Prefer in this order:

1. **SQL views and stored procedures** for set-based queries.
2. **C# in `SecureOps.Infrastructure.Analysis.*`** for orchestration and complex aggregation.
3. **Python scripts in `scripts/python/`** for one-off statistical analysis (only if SQL + C# is awkward).

If a question can be answered in SQL, do not write Python.

## SQL Patterns

Analytical SQL lives in `sql/analysis/`:

```
sql/analysis/
├── views/
│   ├── v_RecurringAlertsByServer.sql
│   ├── v_AlertResponseTimes.sql
│   └── v_DiskGrowthTrend.sql
├── procedures/
│   ├── sp_GenerateWeeklyReport.sql
│   └── sp_DetectAnomalousActivity.sql
└── functions/
    └── fn_BusinessHoursBetween.sql
```

- Use indexed views where read-heavy.
- Use `OPTION (RECOMPILE)` only for ad-hoc analyst queries, not production paths.
- Avoid cursors. Set-based always.

## Python Layer (Optional)

Only when SQL + C# is clearly worse:

```python
# scripts/python/disk_growth_forecast.py
"""
Forecast disk growth for monitored drives using linear regression.
Reads from SQL, writes findings back to SQL.
"""

import pyodbc
import pandas as pd
from sklearn.linear_model import LinearRegression
# ...
```

- Use `pyodbc` for SQL Server access with integrated Windows auth.
- Pin dependencies in `scripts/python/requirements.txt`.
- Use a virtual environment.
- Output to SQL or a structured file in `analysis-outputs/`, not stdout.
- Document what each script does in a top-level docstring.

## Rule Definitions

Rules are configuration, not hardcoded logic. Stored in `appsettings.json` or a dedicated `RuleDefinitions` table.

Example rule schema:

```json
{
  "ruleId": "RECURRING_DISK_ALARM",
  "displayName": "Recurring disk alarm on the same server",
  "description": "Same server has triggered disk alarms more than N times in M days.",
  "parameters": {
    "thresholdCount": 5,
    "windowDays": 30
  },
  "severity": "Warning",
  "enabled": true
}
```

The analysis engine evaluates each enabled rule against current data and writes findings to a `AnalysisFindings` table for the UI and reports.

## Output Format

Every analysis output is a structured "Finding":

```csharp
public sealed record AnalysisFinding(
    Guid Id,
    string RuleId,
    string Subject,           // e.g., "Server APPSRV-12"
    string Summary,
    Dictionary<string, string> Details,
    Severity Severity,
    DateTimeOffset DetectedAt,
    DateTimeOffset DataAsOf);
```

Findings are not alerts. They do not page anyone. They appear in the UI dashboard and in periodic reports.

## Explainability

Every finding must include enough detail in `Details` to explain why it was raised. A user clicking a finding should see:

- The rule that produced it.
- The numeric values that crossed the threshold.
- The time window analyzed.
- Sample data points (anonymized if needed).

No black-box outputs. If a user asks "why this finding?", the system must answer with referenced data.

## Forbidden in Phase 6

- Machine learning models. ML waits for Phase 7.
- Prompts to LLMs. Same — Phase 7.
- "Smart" suggestions not backed by an explicit rule. If a rule cannot articulate the logic, the suggestion does not appear.

## Reference

Read `docs/02-roadmap.md` (Phase 6 section), `docs/08-audit-model.md`, and `docs/10-ai-rag-strategy.md` for boundary with AI.
