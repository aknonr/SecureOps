# 02 — Roadmap

The project unfolds in **9 phases** (Phase 0 through Phase 8). Each phase produces a deliverable that is useful on its own; later phases depend on earlier ones but are not required for MVP value.

## Phase Map

| Phase | Name | Category | Duration | Status |
|---|---|---|---|---|
| 0 | Discovery and Project Setup | MVP | 2–3 weeks | Pending |
| 1A | Identity Lookup / PAM AD User Lookup | MVP helper | 1–2 weeks | Pending |
| 1 | Read-Only Diagnostic MVP | MVP | 4–6 weeks | Pending |
| 2 | Web UI and Dashboard | MVP | 3–5 weeks | Pending |
| 3 | Notification and Ticket Enrichment | ROI | 2–4 weeks | Pending |
| 4 | Audit and Alarm Response Verification | ROI | 3–5 weeks | Pending |
| 5 | PAM / AD / Local Admin Compliance | ROI | 4–6 weeks | Pending |
| 6 | Rule-Based Analysis and Reporting | ROI | 3–5 weeks | Pending |
| 7 | Private AI / RAG PoC | Vision | 6–10 weeks | Pending — separate budget |
| 8 | Approval-Based Remediation | Future | 6–8 weeks | Pending — future |

MVP = Phase 0–2, including Phase 1A (end-to-end value, 10–16 weeks)
ROI = Phase 3–6 (operational return, 12–20 additional weeks)
Vision = Phase 7 (AI capability, separate decision)
Future = Phase 8 (write operations, future)

## Realistic Schedule Outlook

With a single developer at ~16–20 hours per week:

- **End of 2026:** Phases 0 through 4 likely complete. Operational value visible.
- **Q1 2027:** Phases 5 and 6 complete.
- **Mid 2027:** Phase 7 (AI) reconsidered with management, separate budget request.
- **Later 2027 or beyond:** Phase 8 only if Phases 1–6 are stable and stakeholders agree.

**These dates assume:**
- Stakeholder responses within 1 week.
- No major scope additions during phases.
- Test environment available within 2 weeks of Phase 0.
- No extended absence of the single developer.

## Phase 0 — Discovery and Project Setup (2–3 weeks)

**Goal:** Establish baseline, gather approvals, finalize pilot scope. No code.

### Deliverables

1. Pilot server list (10–15 servers) with owner sign-off.
2. Baseline measurement of current alarm volume and analysis time.
3. Bilgi Güvenliği architecture pre-review.
4. Siber Güvenlik architecture pre-review.
5. PAM service account request submitted.
6. Monitoring platform integration questions answered.
7. Test environment requested.
8. Risk matrix finalized.
9. Phase 1 go/no-go decision.

### Exit Criteria

- Pilot list approved.
- Service account approved (may still be provisioning).
- Test environment access confirmed.
- Both security teams have given written feedback.
- Phase 1 backlog reviewed and committed.

See `plans/PHASE-0-discovery-and-project-setup.md` for tasks.

## Phase 1A — Identity Lookup / PAM AD User Lookup (1–2 weeks)

**Goal:** Let authorized backend users resolve an exact PAM account or AD username to read-only Active Directory user information for incident response verification. No UI dependency.

### Deliverables

1. Backend endpoint `POST /api/v1/identity/lookup`.
2. Swagger/Postman-testable request and response contracts.
3. Config-based username normalization.
4. Read-only Active Directory provider behind an interface.
5. Mock PAM account resolver hook for later BeyondTrust metadata or Phase 4 correlation.
6. Audit logging for every lookup request, success, not-found result, and failure.
7. Unit tests for normalization, provider behavior, authorization metadata, and audit behavior.

### Non-Goals

- No AD write operations.
- No password reset, account unlock, or group modification.
- No broad wildcard or bulk search.
- No UI dependency.
- No AI/RAG integration.
- No Teams integration.

### Exit Criteria

- TeamLead/Admin users can test the endpoint from Swagger/Postman.
- Operator/Auditor-only users are not authorized.
- Exact account lookup returns only approved operational fields.
- Every lookup creates an audit entry without storing returned personal details in audit.
- Mock PAM resolver remains the default until BeyondTrust access is approved.

See `plans/PHASE-1A-identity-lookup-mvp.md`.

## Phase 1 — Read-Only Diagnostic MVP (4–6 weeks)

**Goal:** A working read-only diagnostic system processing alarms from a webhook end-to-end. Phase 1A identity lookup is already available as a backend helper but is not a diagnostic module.

### Deliverables

1. .NET solution skeleton with all projects building.
2. SQL schema deployed (Alerts, AlertEvents, DiagnosticJobs, DiagnosticResults, Audit).
3. Webhook endpoint receiving alarms.
4. Hangfire-based job orchestration.
5. JEA endpoint deployed on pilot servers.
6. Six diagnostic modules: Disk, CPU, Memory, IIS, Service, EventLog.
7. PowerShell scripts in `scripts/diagnostic/`.
8. Integration tests using mock SolarWinds adapter.
9. Basic console output / log view (real UI in Phase 2).
10. Operational runbook for system administrator.

### Exit Criteria

- 99%+ webhook delivery success.
- Diagnostic jobs complete in < 30 seconds for standard alarms.
- All operations audited.
- Zero impact on pilot servers (verified by SolarWinds, by application owners).
- All non-AI tests pass.

See `plans/PHASE-1-readonly-diagnostic-mvp.md`.

## Phase 2 — Web UI and Dashboard (3–5 weeks)

**Goal:** Replace console/log with a usable Blazor Server UI.

### Deliverables

1. Blazor Server project hosted on IIS.
2. Windows Authentication wired to AD.
3. AD-group-based authorization policies.
4. Alert list page with filtering.
5. Alert detail page with diagnostic results.
6. Server list page.
7. Audit query page (Auditor/Admin only).
8. Dashboard with current alert counts and recent activity.
9. Real-time alert updates via Blazor SignalR.
10. Operator onboarding guide.

### Exit Criteria

- All authenticated CONTOSO users can access according to their AD group.
- Pages load in < 2 seconds for typical data volumes.
- Mobile-friendly (responsive) — operators may use tablets.
- All UI flows have audit entries.
- bUnit tests cover key components.

See `plans/PHASE-2-web-ui-and-dashboard.md`.

## Phase 3 — Notification and Ticket Enrichment (2–4 weeks)

**Goal:** The system reaches engineers via Teams/mail and prepares ticket text.

### Deliverables

1. Notification adapter interfaces.
2. Mock notifier (default in dev).
3. Teams adapter (webhook).
4. Mail adapter (SMTP relay).
5. Notification templates (configurable).
6. Ticket draft generator: structured paste-ready text.
7. Shift report draft generator.
8. Configurable notification rules (by alarm type, severity, time).

### Notification Strategy

| Event type | Channel | Audience | Frequency |
|---|---|---|---|
| Critical alarm + diagnostic complete | Teams (instant) | On-call group | At event |
| High severity alarm | Teams (instant) | Shift channel | At event |
| Shift-end summary | Mail | Shift supervisor + team | Per shift end |
| Weekly management summary | Mail | IT management | Monday morning |
| Recurring alarm warning | Mail report | System administration | Weekly or threshold |

Configurable; see `docs/06-integrations.md` for adapter details.

### Exit Criteria

- Notifications delivered within 60 seconds of diagnostic completion.
- Ticket draft format approved by Turuncuhat stakeholders.
- Shift report draft accepted by pilot operators.

See `plans/PHASE-3-notification-and-ticket-enrichment.md`.

## Phase 4 — Audit and Alarm Response Verification (3–5 weeks)

**Goal:** Audit completeness; alarm response verification with PAM correlation.

### Deliverables

1. Audit query UI for compliance roles.
2. PAM session correlation (read-only).
3. Alarm response verification reports.
4. SLA evidence reports.
5. Retention policy enforced (36 months minimum).
6. Audit export for internal audit.

### Critical Framing

This phase contains the alarm response verification feature. As committed to management, this is framed and labeled as:

> "operational response verification, SLA evidence, and audit — not person tracking"

Every UI element, report header, filter label, and export filename uses this language.

### Exit Criteria

- Auditor role can produce a complete picture of "what was done about alarm X" in under 30 seconds.
- PAM session correlation tested against pilot data.
- Internal audit team has reviewed and approved the audit format.

See `plans/PHASE-4-audit-and-alarm-response-verification.md`.

## Phase 5 — PAM / AD / Local Admin Compliance (4–6 weeks)

**Goal:** Surface local admin compliance and PAM coverage gaps.

### Deliverables

1. Local administrators inventory across pilot fleet (read-only).
2. Comparison against expected list (per-server policy).
3. PAM coverage report (which servers are managed by PAM, which are not).
4. Drift detection and reporting.
5. Compliance dashboards.

### Exit Criteria

- Local admin scan completes for full fleet in under 1 hour.
- Drift detected within 24 hours of change.
- Reports delivered to security and compliance roles.

See `plans/PHASE-5-pam-ad-local-admin-compliance.md`.

## Phase 6 — Rule-Based Analysis and Reporting (3–5 weeks)

**Goal:** Use accumulated data to produce useful periodic reports without AI.

### Deliverables

1. Rule definitions (configurable, in DB).
2. Rule evaluation engine.
3. AnalysisFindings storage and UI.
4. SQL views for common reports.
5. Optional Python scripts for statistical analysis.
6. Weekly and monthly management report generator.

### Exit Criteria

- At least 5 useful rules in production (recurring alarms, response time outliers, capacity trends, etc.).
- Reports generated on schedule.
- Outputs explainable: every finding traceable to its rule and source data.

See `plans/PHASE-6-rule-based-analysis-and-reporting.md`.

## Phase 7 — Private AI / RAG PoC (6–10 weeks, separate budget)

**Goal:** Pilot a self-hosted AI assistant against masked operational data.

### Pre-conditions (must all be true)

1. Phase 1–6 complete and stable.
2. 6+ months of structured audit data accumulated.
3. Bilgi Güvenliği + Siber Güvenlik approval.
4. Budget approved for GPU server and storage.
5. Data masking strategy implemented and tested.

### Deliverables

1. Self-hosted LLM (Ollama-based).
2. Self-hosted vector database (Qdrant or pgvector).
3. Data masking pipeline.
4. RAG retrieval and generation pipeline.
5. AI audit log.
6. Limited Phase 7 UI in Blazor.

### Exit Criteria

- AI never receives unmasked production data.
- All AI interactions audited.
- AI produces useful summaries on at least 3 representative scenarios.
- Operators report the AI is helpful in a structured survey.

See `plans/PHASE-7-private-ai-rag-poc.md`.

Longer-term assistant and inventory ideas that are intentionally outside active Phase 1-6 scope are captured separately in `docs/16-future-vision.md`.

## Phase 8 — Approval-Based Remediation (6–8 weeks, future)

**Goal:** Enable approved, audited write operations on target servers.

### Pre-conditions (must all be true)

1. Phase 1–6 stable in production.
2. Stakeholder approval for write operations.
3. Approval workflow design reviewed.
4. Snapshot/rollback capability verified.

### Deliverables

1. Approval workflow (operator request → lead approval → execute).
2. Snapshot/rollback safety check.
3. Bounded set of approved remediation actions (initially small: service start, app pool start — no deletions).
4. Audit of every approval and execution.
5. Emergency abort mechanism.

### Exit Criteria

- Every write operation has snapshot/rollback ready.
- No remediation executes without approval.
- All operations audited with full chain.

See `plans/PHASE-8-approval-based-remediation.md`.

## Dependencies Between Phases

```
Phase 0 → Phase 1A → Phase 1 → Phase 2 ─┬→ Phase 3
                              ├→ Phase 4 → Phase 5
                              └→ Phase 6 → Phase 7
                                          → Phase 8
```

Phases 3, 4, and 6 can proceed in parallel once Phase 2 is done if resources allow. With a single developer, they will be sequential.

Phase 7 requires Phase 6 data. Phase 8 requires Phase 6 stability.

## Risk Register Summary

See `docs/11-feasibility.md` for the full risk matrix. Top risks:

| Risk | Mitigation |
|---|---|
| Single developer (bus factor 1) | Document-first, mainstream tech, small commits |
| Turuncuhat / monitoring-chain integration delays | Mock-first; decide inbound contract in Phase 0; retain later SWIS fallback where useful |
| Stakeholder review delays | Early engagement in Phase 0 |
| Scope creep into AI | AI gated by explicit pre-conditions |
| Operator pushback ("surveillance") | "Not surveillance" framing enforced everywhere |
| JEA endpoint deployment friction | Phase 0 includes JEA proof-of-concept |
