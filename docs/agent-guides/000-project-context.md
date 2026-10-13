# 000 — Project Context

Background for a first task in the repo; binding rules are in `AGENTS.md`.

## The project in one paragraph

An internal Windows operations platform for a shift team at CONTOSO (placeholder name). It ingests monitoring alarms and operational records (SolarWinds → HPE OpsBridge / monthly.thy.com → Turuncuhat ITSM; Jira), runs **read-only** diagnostics on Windows servers through a JEA-constrained PowerShell endpoint, keeps an append-only audit trail in SQL Server, and gives shift engineers a Blazor Server UI. Later phases add rule-based analysis (6), self-hosted AI (7) and approval-based remediation (8). It is **not** a SIEM, APM, PAM replacement, people-search tool, remediation tool (before Phase 8), AI product, or employee-monitoring system.

## Fixed decisions (details in `docs/adr/`)

| Area | Decision |
|---|---|
| Stack | .NET 10 LTS (`net10.0`, C# 14, SDK pinned in `global.json`), ASP.NET Core API, Worker Service — ADR-0001 (Amendment 2) |
| UI | Blazor Server + MudBlazor 9.11 (not React/Angular) — ADR-0001, ADR-0007 |
| Data | SQL Server via Dapper (parameterized SQL) and numbered scripts in `sql/schema/` / `sql/migrations/`; append-only audit — ADR-0001 (amended 2026-10-01) |
| Jobs | Hangfire on SQL Server, hosted by the Worker — ADR-0003 |
| Automation | PowerShell Remoting + JEA only; Ansible optional from Phase 6 — ADR-0003 |
| Access | Authentication source → corporate principal → approval → role → capability; authentication claims never grant access directly — ADR-0010, ADR-0022 |
| Hosting | IIS on Windows Server, in-process — ADR-0007 |
| AI | Self-hosted only, Phase 7 — ADR-0005 |

Other decided areas (identity lookup, operational records/Jira, sessions, OIDC, SDM evaluation, resources, In Use, announcements, reporting, service accounts) each have their own ADR; read the one for the area you touch.

## Systems around the platform

- **SolarWinds** — alarm source. **HPE OpsBridge / monthly.thy.com** — event-detail layer. **Turuncuhat** — central ITSM + IVR workflow. **Jira** — operational-record target (ADR-0009, ADR-0012). **BeyondTrust** — PAM (read-only correlation). Existing Ansible/AWX is not touched.
- The platform runs on top of these systems, never inside them; it does not modify their configuration. Current map and open questions: `docs/15-system-landscape.md`.

## Pilot scope

10–15 low-criticality Windows servers (prefer non-production), one or two alarm types first (Disk, Service), 4–6 weeks parallel run with human operators, then a management review. Phase status: `docs/02-roadmap.md` and `plans/`.

## Access model

Authentication only establishes who someone is. Access comes from the persisted path *principal → approval status → application role (bundle) → capability* (ADR-0010, ADR-0022). The API enforces capability policies (`SecureOps.Shared.Auth.Policies`, `Capabilities`); the UI only reflects them. Do not reintroduce AD-group or role-name checks in code.

## Why the constraints exist

Bus factor is 1 and the developer works part-time around shift duty. Documentation is the continuity plan, mainstream technology keeps maintenance possible, and small changes keep review possible. Every audit or reporting feature is framed as operational response evidence (SLA, incident verification), never as individual performance measurement.
