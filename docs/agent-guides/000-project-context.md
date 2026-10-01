# 000 — Project Context (Always Apply)

## Applicability

- **Purpose:** Authoritative project context, decisions, and non-negotiable rules. Read first.
- **Applies to:** Every task.
- **Loading:** Routed explicitly from `AGENTS.md`; do not assume automatic discovery.

You are working on **Secure Ops Automation & AI Analysis Hub**, an enterprise Windows operations platform owned by CONTOSO Turkish Technology (placeholder).

## What This Project Is

A platform that:
- Receives Windows monitoring alarms (disk, CPU, memory, IIS, app pool, service, event log) from the monitoring chain used at CONTOSO: **SolarWinds** as alarm source, **monthly.thy.com / HPE OpsBridge** as event-detail layer, and **Turuncuhat** as the central ITSM + IVR workflow system.
- Runs **read-only** diagnostic checks on target Windows servers via PowerShell Remoting through a JEA constrained endpoint.
- Stores structured findings and a complete audit trail in SQL Server.
- Surfaces results to shift engineers via a Blazor Server web UI.
- Later phases add rule-based analysis, private AI assistance, and approval-based remediation.

Read `docs/15-system-landscape.md` for the current real-world system map and unresolved integration questions.

## What This Project Is NOT

- Not a SIEM. Not an APM tool. Not a PAM replacement.
- Not an AI product. AI is a Phase 7 capability.
- Not a remediation tool in MVP.
- Not employee surveillance. Audit is process verification.

## Authoritative Decisions

These are final. To change one, write an ADR in `docs/adr/`.

- **UI:** Blazor Server + MudBlazor. Not React. Not Angular.
- **Backend:** .NET 8, ASP.NET Core Web API + Worker Service.
- **Database:** SQL Server with append-only audit (UPDATE/DELETE blocked by trigger).
- **Job orchestration:** Hangfire with SQL Server storage. Not BackgroundService for long-running jobs.
- **Automation:** PowerShell Remoting + JEA only in MVP. Existing Ansible/AWX is not touched. Ansible considered Phase 6+ only, optional.
- **Auth:** Windows Authentication via Active Directory.
- **RBAC:** AD-group-based. Roles: Operator, TeamLead, Admin, Auditor.
- **Logging:** Serilog with structured JSON output.
- **AI:** Self-hosted only (e.g., Ollama, Qdrant). Public AI APIs forbidden.

## Open Decisions

- **Turuncuhat integration:** exact inbound/outbound method is pending stakeholder input.
- **Worker privileged access:** BeyondTrust brokering vs. direct WinRM + Kerberos + JEA is pending input from the PAM team, Bilgi Güvenliği, and team lead.

## Pilot Constraints

- 10–15 low-criticality Windows servers, preferably non-production.
- 1–2 alarm types in initial pilot (recommended: Disk + Service).
- Parallel operation with human operators for 4–6 weeks.
- 6–8 week MVP timeline ending with a management demo and review.

## Single-Developer Reality

One developer working part-time around shift duty. Bus factor = 1.

- Document-first approach is mandatory.
- Boring, mainstream technology over exotic.
- Small, reviewable changes.
- No tribal knowledge dependencies.

## Placeholder Naming

Use **CONTOSO** when a company name is required. Keep generic role labels where useful for reusable architecture language, but use real system names when they are known and documented in `docs/15-system-landscape.md`:

- **SolarWinds** — alarm source
- **monthly.thy.com / HPE OpsBridge** — event-detail viewer layer
- **Turuncuhat** — central ITSM + IVR system
- **BeyondTrust** — PAM system
