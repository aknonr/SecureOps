# 00 — Project Brief

## Identity

| Field | Value |
|---|---|
| Project name | Secure Ops Automation & AI Analysis Hub |
| Short name | SecureOps |
| Owner organization | CONTOSO Turkish Technology (placeholder) |
| Owner role | Windows System Administrator / Shift Operations Engineer |
| Status | Pre-implementation. Documentation and scaffolding phase. |
| Version | 0.1 (pre-MVP) |

## One-Sentence Description

A platform that receives Windows monitoring alarms, runs read-only diagnostics through a constrained PowerShell endpoint, captures a complete operational audit trail, and presents structured findings to shift engineers — with later phases adding rule-based analysis, self-hosted AI assistance, and approval-based remediation.

## Problem Statement

In typical Windows-heavy enterprise operations, shift engineers receive a high volume of monitoring alarms (disk full, high CPU, app pool stopped, service crashed, suspicious event log entry). For each, the engineer:

1. Receives the alarm via the monitoring tool.
2. Connects to the target server through a PAM session.
3. Runs an ad-hoc set of manual checks: disk usage, top processes, services, IIS state, event log.
4. Decides whether the alarm is actionable.
5. Documents the result in a ticket and a shift report.

This manual process has consistent problems:

- **Inconsistent execution.** Different engineers run different checks. Output format varies.
- **Weak audit trail.** Existing PAM session logs capture connection-level activity but not the technical investigation done inside the session.
- **Invisible recurrence.** A server that triggers the same alarm five times in two weeks may go unnoticed across shift handovers.
- **Knowledge loss.** New engineers depend on senior engineers for several months before becoming self-sufficient.
- **Manual reporting.** Shift reports and management summaries are assembled by hand.

## What This Project Does

1. **Receives alarms** from the existing monitoring chain: **SolarWinds** is the alarm source, **monthly.thy.com / HPE OpsBridge** provides event-detail context, and **Turuncuhat** is the central ITSM + IVR system where EVT records are created, acknowledged, and closed. The exact SecureOps integration method with Turuncuhat is pending stakeholder input.
2. **Runs read-only diagnostic checks** on the target server using PowerShell Remoting through a JEA constrained endpoint.
3. **Persists structured findings** to SQL Server with an append-only audit trail.
4. **Presents results** to the engineer through a Blazor Server web UI with role-based access.
5. **Supports the existing Turuncuhat workflow** by attaching structured diagnostic context and, after operator review, eventually writing back EVT closure details if stakeholders approve a read-write integration.
6. **Generates standard reports** for tickets, shift handover, and management.
7. **Later** — adds rule-based analysis, self-hosted AI summarization, and approval-based remediation.

## What This Project Is NOT

- **Not a SIEM.** It does not aggregate security events or replace existing security monitoring.
- **Not an APM tool.** It does not replace application performance monitoring.
- **Not a monitoring tool.** It receives alarm context from the existing SolarWinds → OpsBridge → Turuncuhat chain; it does not replace those systems or poll metrics itself.
- **Not a PAM replacement.** It integrates with PAM read-only for session correlation in Phase 4.
- **Not a remediation tool in MVP.** Phase 1–7 perform zero write operations. Phase 8 introduces approval-based remediation only.
- **Not an AI product.** AI is a Phase 7 capability layer; the product is valuable without it.
- **Not an employee surveillance system.** Audit captures what was done (operations), not who performed best (personnel). See `docs/05-security-model.md` for the framing.
- **Not a SaaS or multi-tenant product.** Single-organization, on-premise deployment only.

## Why This Project

### Direct Operational Value

- Standardizes alarm investigation across the shift team.
- Reduces per-alarm investigation time from typical 8–15 minutes to 1–3 minutes (post-MVP target).
- Eliminates manual shift report assembly.
- Surfaces recurring problems that span shifts.
- Accelerates onboarding of new shift engineers.

### Strategic Value

- Builds the structured data foundation that future AI assistance requires.
- Improves audit evidence quality for internal and external review.
- Reduces dependence on individual engineer memory.
- Creates a reproducible model for automating other operational domains later.

### What It Does NOT Solve

- Does not replace good runbooks. It runs the runbooks faster.
- Does not eliminate the need for skilled engineers. It augments their judgment.
- Does not reduce alarm volume. It reduces the cost per alarm.

## Core Principles

1. **Read-only first.** No write operations on target servers in MVP.
2. **Mock integrations first.** Real adapters come after the design is proven against mocks.
3. **Audit everything.** Append-only audit, 36-month minimum retention.
4. **JEA mandatory.** Service account cannot execute write cmdlets.
5. **Document first, code second.** Every behavior change updates docs.
6. **No public AI.** Self-hosted only, Phase 7, separate decision.
7. **Approval-based remediation.** Phase 8, never automatic.
8. **Existing systems untouched.** Monitoring platform, PAM, Ansible/AWX preserved.
9. **Audit is process verification, not personnel monitoring.**
10. **Boring technology wins.** Single-developer reality demands mainstream choices.

## Constraints and Realities

- **Single developer**, part-time, around shift duties. Roughly 16–20 hours/week.
- **Bus factor = 1.** Documentation is continuity insurance.
- **Pilot scale:** 10–15 low-criticality Windows servers, preferably non-production.
- **MVP timeline:** 6–8 weeks ending with a management demo.
- **No new external dependencies** beyond mainstream .NET, SQL Server, PowerShell, and standard libraries.

## Success Criteria

Phase 1 MVP is successful if:

- Alarms from the approved monitoring workflow reliably trigger diagnostic jobs (>= 99% delivery).
- Diagnostic jobs complete in under 30 seconds for the standard alarm types.
- Audit trail is complete: every alarm, job, and operator action is recorded.
- Pilot operators report the system is useful in a structured post-pilot survey.
- Zero unplanned impact on pilot servers.
- Management demo demonstrates the end-to-end flow.

## Out-of-Scope for MVP

See `docs/12-mvp-backlog.md` for the explicit out-of-scope list. Highlights:

- Automatic remediation of any kind.
- AI/RAG features.
- Cross-environment monitoring (Linux, network devices, databases).
- Multi-tenant or external user access.
- Mobile applications.
- Public APIs.
