# Secure Ops Automation & AI Analysis Hub

An enterprise Windows operations platform that receives monitoring alarms, runs read-only diagnostics, captures structured audit data, and surfaces actionable findings to shift engineers.

> **Status:** Pre-implementation. Documentation and scaffolding phase.
> **Owner:** CONTOSO Turkish Technology (placeholder)
> **Stack:** .NET 8, Blazor Server, SQL Server, PowerShell Remoting + JEA
> **MVP timeline:** 6–8 weeks

---

## What This Project Does

When a Windows monitoring alarm fires (disk full, high CPU, app pool stopped, service down, suspicious event log entry), this system:

1. **Receives the alarm** via webhook from the monitoring platform (SolarWinds-style).
2. **Runs read-only diagnostic checks** on the target server using PowerShell Remoting through a JEA constrained endpoint.
3. **Stores structured results** in SQL Server with an append-only audit trail.
4. **Surfaces findings** to the on-shift engineer through a Blazor Server web UI.
5. **Generates standard reports** for tickets, shift handover, and management.

Later phases add rule-based analysis, private AI-assisted summarization, and approval-based remediation.

## What This Project Is NOT

- Not a replacement for the existing monitoring platform.
- Not a SIEM or APM tool.
- Not a remediation tool in MVP — every action is read-only.
- Not an AI product. AI is a later phase capability, fully self-hosted, optional.
- Not an employee surveillance system. Audit is process verification, not person performance monitoring.

---

## Getting Started

This repo currently contains documentation, agent rules, and an empty .NET solution scaffold. There is no runnable application code yet. The first runnable artifact will be the Phase 1 read-only diagnostic MVP.

### For AI coding agents

**Start here:** [`AGENTS.md`](./AGENTS.md). Then follow the mandatory reading order.

### For human developers

**Start here:** [`docs/00-project-brief.md`](./docs/00-project-brief.md), then [`docs/02-roadmap.md`](./docs/02-roadmap.md).

### For management

**Turkish summary:** [`docs/14-management-summary-tr.md`](./docs/14-management-summary-tr.md).

---

## Repository Layout

| Folder | Purpose |
|---|---|
| `docs/` | Project memory. Architecture, security, integrations, diagnostics, AI strategy, ADRs. |
| `plans/` | Phase-by-phase implementation plans with task breakdown. |
| `contracts/` | JSON schemas and example payloads for cross-component contracts. |
| `.cursor/rules/` | Cursor agent rules. Also useful as reference for any agent. |
| `src/` | .NET solution. Currently scaffolded only. |
| `tests/` | Unit and integration test projects. |
| `scripts/powershell/` | Diagnostic and JEA scripts (Phase 1+). |
| `sql/` | Schema and migration scripts (Phase 1+). |

---

## Roadmap (High Level)

| Phase | Focus | Duration |
|---|---|---|
| 0 | Discovery, baseline, stakeholder alignment | 1–2 weeks |
| 1 | Read-only diagnostic MVP | 4–6 weeks |
| 2 | Web UI + dashboard | 3–5 weeks |
| 3 | Notification + ticket enrichment | 2–4 weeks |
| 4 | Audit + alarm response verification | 3–5 weeks |
| 5 | PAM / AD / local admin compliance | 4–6 weeks |
| 6 | Rule-based analysis + reporting | 3–5 weeks |
| 7 | Private AI / RAG PoC (separate budget) | 6–10 weeks |
| 8 | Approval-based remediation (future) | 6–8 weeks |

See [`docs/02-roadmap.md`](./docs/02-roadmap.md) for full detail.

---

## Core Principles

1. **Read-only first.** No write operations on target servers in MVP.
2. **Mock integrations first.** Real adapters come after the design is proven.
3. **Audit everything.** Append-only, 36-month minimum retention.
4. **JEA mandatory.** Service account is constrained to read cmdlets only.
5. **Document first, code second.** Every behavior change updates a doc.
6. **No public AI.** Self-hosted only, Phase 7, separate decision.
7. **Approval-based remediation.** Phase 8, never automatic.
8. **Existing systems untouched.** SolarWinds, BeyondTrust/PAM, Ansible/AWX preserved.

---

## License

Internal project. Not for distribution.
