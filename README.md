# Secure Ops Automation & AI Analysis Hub

Current backend TEST deployment readiness, migrations, bootstrap access, exact runtime configuration, release validation, and rollback are documented in `docs/24-api-test-deployment-readiness.md`. Frontend integrations must consume `docs/contracts/secureops-api-v1.openapi.json` and `docs/contracts/secureops-api-v1-ui-integration.md`.

Current post-rc6.26 work and separate source/payload/target acceptance are tracked
in `docs/integrated-test-activation.md`; executable Turkish handoff:
`docs/post-rc626-continuation-tr.md`. The archive catalogue adds SQL 024; it is not
installed by this source change. No successor release or full activation is claimed.

An enterprise Windows operations platform that receives monitoring alarms, runs read-only diagnostics, captures structured audit data, and surfaces actionable findings to shift engineers.

> **Status:** The real Turuncu Hat read-only import is deployed and verified in TEST at source `0ec0376`; external writes remain disabled, and deterministic SDM classification plus the broader controlled pilot remain pending.
> **Owner:** CONTOSO Turkish Technology (placeholder)
> **Stack:** .NET 10 LTS (C# 14), Blazor Server + MudBlazor, SQL Server, PowerShell Remoting + JEA
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

This repo contains documentation, agent rules, a .NET solution, working Phase 1A IdentityLookup backend code, provider-neutral access approval, audit persistence hardening, the durable Operational Record to Jira backend foundation, and backend-authoritative management reporting. The corporate Turuncu Hat read-only import is verified in TEST; Jira creation, Turuncu Hat completion, and approved deterministic classification rules remain deferred. Local automated tests continue to use deterministic substitutes rather than corporate endpoints.

### For AI coding agents

**Start here:** [`AGENTS.md`](./AGENTS.md). It states the hard rules and routes each task to the guides it needs.

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
| `docs/agent-guides/` | Portable detailed agent guidance, routed from `AGENTS.md` and `CLAUDE.md`. |
| `src/` | .NET solution: Api, Worker, Ui, Domain, Infrastructure, Shared. Phase 1A code currently lives mainly in Api, Infrastructure, and Shared. |
| `tests/` | Unit and integration test projects. Current tests cover Phase 1A identity lookup, audit hardening, authorization, validation, correlation, and rate-limit metadata. |
| `scripts/powershell/` | Empty diagnostic and JEA script folders reserved for Phase 1. |
| `sql/` | Reviewed offline audit/access-control SQL assets; never executed by local tests. |

## Local Identity Lookup Boundary

Basic PAM-style account identifiers are ordinary exact directory-account inputs, handled through the same read-only `sAMAccountName` path as other accounts. Account-owner resolution and vendor PAM integration remain planned. Local development uses fakes only and cannot validate corporate AD, PAM, SQL, IIS, or load-balancer behavior.

---

## Roadmap (High Level)

For current status and detailed durations, `docs/02-roadmap.md` is authoritative.

| Phase | Focus | Duration |
|---|---|---|
| 1A | Identity Lookup / PAM AD User Lookup | 1-2 weeks |
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
