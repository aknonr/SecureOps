# 01 — Current Operations Context

This document captures the operational environment that shapes every design decision. The system is not built in a vacuum; it is built into the specific workflow described here.

## SDM Foundation: Source Implementation

The Resource Links and Shift Start Sets backend v1 is implemented in source under
ADR-0019: shared catalogue/categories, explicit curator management, private
favourites and ordered/default sets. SQL-backed deployment requires migrations
001-010 and resource object grants. Catalogue changes then require no deployment.
Claude's next resource UI task consumes the committed API/OpenAPI handoff in
`docs/contracts/secureops-api-v1-ui-integration.md`. This is not a deployment or
corporate TEST result; SDM positive rules and approval remain pending. Local
isolated SQL execution evidence is recorded in the deployment-readiness document.

The deterministic evaluation foundation `WASAS-SDM-2026.09-v1` is implemented
in source (ADR-0018), with additive response evidence and offline migration 009.
The verified TEST baseline remains four real records, zero malformed/ambiguous
rows, all `NeedsManualReview`, `JiraEligible=false`, and no synthetic records.
The new evaluator also returns `SdmCandidateRecommended=false`; positive SDM
category policy and structured per-record group/DCC/category/server/IP evidence
remain pending. No approval endpoint is implemented. External writes remain
disabled (`ReadOnlyIntegrationMode=true`, `ControlledTestWritesEnabled=false`).
The next UI milestone is the Action Center consuming the additive evaluation
contract; backend approval/publication policy is a separate milestone. This
source change is not a deployment or a new corporate smoke-test result.

## Organization Snapshot

| Attribute | Value |
|---|---|
| Organization | CONTOSO Turkish Technology (placeholder) |
| Team size | 13 engineers |
| Shift coverage model | Mixed: 1 dedicated shift engineer + on-call rotation among 13 |
| Planned growth | Shift team expanding from 1 to 3 dedicated engineers |

## Shift Pattern

The dedicated shift engineer (project owner) operates on a 6-day rotating pattern:

| Day | Hours | Notes |
|---|---|---|
| 1 | 15:00–23:00 | Evening shift |
| 2 | 15:00–23:00 | Evening shift |
| 3 | 23:00–07:00 | Night shift |
| 4 | 23:00–07:00 | Night shift |
| 5 | Off | Day off |
| 6 | Off | Day off |

Cycle repeats.

When the shift engineer is off duty, an **on-call engineer** (rotating among the team) handles alarms. The on-call engineer also covers gap hours when the shift transitions — for example, if a shift ends at 23:00 and the next coverage begins at 00:00, the on-call covers that gap, with overtime compensation possible.

The on-call engineer also supports **OCO transitions** (operations control office shift changes), where coordination overhead is highest.

## Why This Matters for Design

The shift pattern creates several design pressures:

- **Asynchronous knowledge transfer.** Engineers handing off shifts need structured handover artifacts, not verbal summaries.
- **On-call overhead is a real budget.** Overtime hours for off-hours coverage are measurable; reducing this is a tangible savings target.
- **Onboarding the new shift hires.** With 2 new shift engineers joining, the current dependency on senior knowledge becomes a bottleneck. Documentation and standardized tooling reduce ramp time.
- **Coverage gaps are operational risk.** OCO transitions and shift boundaries are the moments when alarms are most likely to be missed or mishandled.

## Existing Tools and Systems

| System | Role | Integration approach |
|---|---|---|
| SolarWinds | Source of CPU, disk, and service alarms | Source system in the existing alarm chain; possible SWIS API later |
| monthly.thy.com / HPE OpsBridge | Event-detail viewer layer between SolarWinds and Turuncuhat | No SecureOps integration yet identified |
| Turuncuhat | Central ITSM + IVR system; creates EVT records, opens PR/OR/OCO records, sends mail/IVR, and owns acknowledge/close workflow | Bidirectional SecureOps integration under review |
| BeyondTrust | Privileged access management for human operators via LDAP credentials | Read-only session correlation (Phase 4); Worker connection model pending stakeholder input |
| Dynatrace | Existing APM platform | Usage in this project unclear; no integration yet |
| Ansible / AWX | Existing automation | **Preserved untouched in MVP**, reconsidered Phase 6+ |
| Active Directory | Authentication and authorization | Windows Authentication, AD group-based RBAC |
| Confluence WASAS | Team documentation repository | No integration yet |
| Turuncuhat (ticketing role) | Incident management | It is the real ticketing system in this environment |
| Microsoft Teams | Notification channel | Webhook integration (Phase 3) |
| SMTP | Mail notifications | Standard SMTP relay (Phase 3) |
| vSphere | VM management | Read-only snapshot query (Phase 5+) |

**None of these existing systems are modified by this project.** SecureOps runs on top of them.

## Current Alarm Workflow

The real operator workflow is centered on **Turuncuhat**, not only on the original alarm source:

1. **SolarWinds** detects CPU, disk, service, or similar conditions.
2. **monthly.thy.com / HPE OpsBridge** exposes event-detail context between the monitoring source and the ITSM workflow.
3. **Turuncuhat** receives the alarm, creates an `EVT-XXXXX` record, may open `PR` / `OR` / `OCO` records, sends mail, and places IVR calls to on-call engineers.
4. The on-call engineer receives mail or IVR from Turuncuhat, opens the EVT, follows the event-detail link, and manually investigates through SolarWinds and/or BeyondTrust.
5. The engineer returns to Turuncuhat to close the EVT with `Çözüldü` status, `Kapanış açıklaması`, and the answer to `Aksiyon alındı mı?`.

For SecureOps, this means the durable operational reference is the **Turuncuhat EVT**, while SolarWinds remains the upstream alarm source.

## Alarm Volume Estimates (Pre-Pilot Baseline)

Estimates from the project owner. To be replaced with measured data during Phase 0 baseline collection.

| Metric | Estimate |
|---|---|
| Alarms per shift (8 hours) | 4–6 |
| Recurring alarms per shift | 3–5 (subset of above) |
| Average manual analysis time per alarm | 8–15 minutes |
| Shift report preparation time | 30–45 minutes |
| Critical incidents per week | 1–3 |
| Audit query frequency (from leadership) | 1–2 per month |

## Pain Points the Project Targets

### 1. Inconsistent Investigation

Two engineers facing the same disk-full alarm may:
- Run different commands.
- Look at different folders.
- Reach different conclusions.
- Document the result in completely different ticket formats.

The system standardizes the investigation.

### 2. Weak Cross-Shift Visibility

A server triggering the same alarm five times across two weeks may not be noticed if each shift handles it as a one-off. The system surfaces recurrence.

### 3. Audit Reconstruction

Today, answering "what was done about alarm X last Tuesday?" requires:
- PAM session log search.
- Ticket archaeology.
- Engineer memory.

This takes hours and rarely produces a complete picture. The system makes it a 30-second query.

### 4. New-Engineer Bottleneck

A new shift engineer typically depends on senior engineers for 6–8 months. With 2 new hires planned, this dependency becomes a productivity drag. Structured runbooks driven by the system reduce dependency time to 2–3 months (target).

### 5. Repetitive Manual Reporting

The shift report is assembled manually. The system generates a draft automatically from the audit trail.

## OCO Transitions

The Operations Control Office (OCO) transition is a known weak point. During transitions:

- Ownership of in-flight alarms is unclear.
- New responders may lack context.
- Documentation lag is highest.

The system supports OCO by:
- Tagging alarms with the responsible operator at receipt time.
- Producing a transition summary on demand.
- Persisting all in-flight diagnostic results so the incoming responder sees the same context.

## Audit Reframing (Critical)

Audit is **not** employee performance monitoring. The framing used throughout this project, in user-facing labels, in reports, and in stakeholder conversations:

> *"Audit captures what was done in response to an alarm — operationally — for the purposes of incident response verification, SLA evidence, and compliance review. It does not measure individual operator performance."*

This framing was committed to management in the project's foundational email and must be preserved consistently.

### Practical Implications

- No leaderboards.
- No per-operator response time charts in the default UI.
- Filter UI defaults to "by alarm" or "by time", not "by operator".
- Reports aggregate to team level, not individual.
- Per-operator queries are possible for **audit and compliance roles only**, and produce an audit entry of their own.

## Stakeholders

| Stakeholder group | Role in this project |
|---|---|
| IT / System Administration management | Project sponsor and budget owner |
| Bilgi Güvenliği (Information Security) | Architecture review, JEA endpoint approval, audit retention policy |
| Siber Güvenlik (Cyber Security) | Attack surface review, data flow approval, AI phase review |
| PAM / BeyondTrust team | Service account, Worker connection model, JEA endpoint, session correlation |
| Monitoring platform team | SolarWinds alert source details and SWIS/API questions |
| Turuncuhat team | EVT integration path, acknowledgment/closure workflow, API or webhook feasibility |
| Network team | Firewall rules for WinRM, SQL, monitoring webhook |
| Application owners | Approval for pilot server inclusion |
| Internal audit | Audit log format and retention review |
| HR (informational) | Proactive briefing on the "not surveillance" framing |
| Shift team (13 engineers) | End users, feedback source, pilot participants |
| Shift supervisor | Pilot training, change communication |

Note: **Bilgi Güvenliği** and **Siber Güvenlik** are intentionally listed as two separate stakeholders. They have distinct responsibilities at CONTOSO and both must approve.

## Regulatory and Compliance Context

| Domain | Relevance |
|---|---|
| KVKK (Turkish data protection) | Operational logs may contain personal-identifying data; masking and retention policies must comply |
| ISO 27001 | Audit log integrity, access control, change management |
| Internal audit policy | Append-only retention, format, queryability |
| Sector-specific (if applicable) | BDDK (banking), BTK (telco) — not specified at this layer |

Detail on retention and masking is in `docs/05-security-model.md` and `docs/08-audit-model.md`.

## Single-Developer Reality

The project is built by **one developer** working part-time around the shift schedule. Effort budget approximately **16–20 hours per week**.

This shapes every choice:

- **Boring technology** preferred over exotic.
- **Mainstream Microsoft stack** to leverage existing skills.
- **Mock-first integrations** to avoid being blocked on other teams.
- **Document-first** because the next maintainer may not be the developer.
- **Small, incremental changes** because review depth equals review reliability.
- **No heroic features** — if it can't be maintained by one person, it doesn't ship.

## What Changes if a Second Developer Joins

If the team grows to 2+ developers:

- Code review becomes mandatory (currently self-review against the DoD checklist).
- Pair programming sessions for high-risk areas (security, audit).
- Pull requests instead of direct commits.
- Branching strategy formalizes.

These are documented in `docs/13-definition-of-done.md` but are conditional on team size.
