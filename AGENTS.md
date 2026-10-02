# AGENTS.md — WASAS Automation Management / SecureOps

Canonical entry point for every coding agent (Codex, Claude, others). `CLAUDE.md` adds Claude-specific UI guidance; it never overrides this file.

**Sources of truth.** The hard rules below and approved decisions (ADRs, recorded owner decisions) are binding. Code shows what is implemented today; it does not override a hard rule or an approved decision. When code, a guide and a decision disagree, report the mismatch — as a defect if the code breaks a rule or decision, as a stale document otherwise — instead of silently following either.

## The project in one paragraph

An internal Windows operations platform for a shift team at CONTOSO (placeholder name). It ingests monitoring alarms and operational records (SolarWinds → HPE OpsBridge / monthly.thy.com → Turuncuhat ITSM; Jira), runs **read-only** diagnostics on Windows servers through a JEA-constrained PowerShell endpoint, keeps an append-only audit trail in SQL Server, and gives shift engineers a Blazor Server UI. Later phases add rule-based analysis, self-hosted AI, and approval-based remediation. One part-time developer owns it (bus factor 1), so documentation is the continuity plan and mainstream, boring technology wins.

It is **not** a SIEM, APM, PAM replacement, people-search tool, remediation tool (before Phase 8), AI product, or employee-monitoring system.

## Hard rules

These are business and security constraints, not style preferences. They hold even if a request asks otherwise: explain the conflict and offer a compliant alternative.

1. **Read-only on target servers until Phase 8.** No service/app-pool/process control, file or registry writes, reboots, permission or group changes on managed servers. Read-only cmdlets only; JEA enforces this and code must not even construct a write command. *Why:* the platform runs beside production operations and must never be the cause of an outage.
2. **No public AI with internal data.** No alarm payloads, hostnames, logs, identities or internal names go to any public LLM endpoint (OpenAI, Anthropic, Google, Cohere, etc.). Product AI is Phase 7, self-hosted only. *Why:* data-protection and security-team commitments.
3. **JEA for all WinRM.** No unconstrained runspaces; extending the cmdlet allow-list needs an ADR.
4. **Append-only audit, ≥ 36 months.** SQL triggers block UPDATE/DELETE on audit tables; code never tries to modify audit rows.
5. **Audit is operational evidence, not people monitoring.** No leaderboards, per-person performance views or operator comparisons; wording follows `docs/05-security-model.md`.
6. **Fail closed.** Missing or ambiguous identity, authorization, audit, configuration or integration state denies the action.
7. **Existing infrastructure is not modified.** SolarWinds, OpsBridge/monthly.thy.com, Turuncuhat, BeyondTrust/PAM and Ansible/AWX stay untouched unless an approved integration says otherwise; this platform runs on top of them.
8. **Mock first, contracts before real adapters.** New integrations start behind an interface with a fake adapter. Do not enable real source/Jira adapters, add SDM classification rules, configure OIDC, or assume remote idempotency without an approved contract.
9. **Approval-based remediation.** Changing state on managed servers is Phase 8 only and goes through an approval workflow (ADR-0006). Writes to external systems such as Jira follow their own ADRs: preview first, explicit authorized action, durable idempotency (ADR-0009, ADR-0012).
10. **No real secrets or identities in the repo.** No real employee data, PAM accounts, credentials or runtime configuration values in source, fixtures, examples or docs.
11. **Stay in phase.** Work belonging to a later phase (`docs/02-roadmap.md`) is surfaced, not silently built.

## Ownership

- **Codex:** backend, API, domain, infrastructure, Worker, hosting/middleware, security, SQL, integrations, contracts, release engineering and their tests. By explicit owner decision Codex also owns the Planned OCO Announcements UI.
- **Claude:** UI/UX — Razor, CSS, layout, theme, navigation, accessibility, visual behaviour under `src/SecureOps.Ui/` — and its tests.
- These are defaults. The owner may approve a scoped exception for a task or module — for example the 2026-09-28 Service Accounts exception in `docs/service-accounts/README.md`, under which Claude implements that module's backend, SQL candidate, UI and tests while Codex keeps final integration. An exception covers only its stated scope and does not change the defaults.
- Outside your ownership or an approved exception: report the exact need (route, field, permission, behaviour) to the owner instead of changing their layer or inventing data in yours.

## Working agreement

- **Git.** Check branch, HEAD and worktree before changing anything. Never reset, clean, stash, rebase or overwrite work you did not create. Push, open or update PRs, merge or deploy only with the owner's explicit authorization. An authorization stays valid while its scope is unchanged (same branch, PR and kind of operation); a new target, merge, deploy or corporate action needs a new one.
- **Live systems.** Do not touch IIS, app pools, services, bindings, load balancers, databases or live configuration unless the task explicitly authorizes it. Local work cannot validate corporate AD/PAM/LDAP/SQL/IIS/F5 behaviour; use the deterministic fakes and say what remains unverified.
- **Decisions.** A change to an architectural or security decision needs a new or amended ADR in `docs/adr/` first. Behaviour changes update the matching doc in the same change.
- **Verification.** Run the build and the relevant tests (`docs/agent-guides/090-testing-quality.md`). Report exactly what ran and what did not; never claim an unrun pass. Prefer small, reviewable diffs.
- **When to ask.** Ask only for a genuinely new owner decision — new scope, a security trade-off, conflicting sources of truth, or an irreversible or outward-facing action not already authorized. Do not re-ask for something already decided or authorized; otherwise decide, state the assumption, and proceed.

## Fixed decisions (details in `docs/adr/`)

| Area | Decision |
|---|---|
| Stack | .NET 10 LTS (`net10.0`, C# 14, SDK pinned in `global.json`), ASP.NET Core API, Worker Service — ADR-0001 |
| UI | Blazor Server + MudBlazor 6.16 (not React/Angular) — ADR-0001, ADR-0007 |
| Data | SQL Server via Dapper (parameterized SQL) and numbered scripts in `sql/schema/` / `sql/migrations/`; append-only audit — ADR-0001 (amended 2026-10-01) |
| Jobs | Hangfire on SQL Server, hosted by the Worker — ADR-0003 |
| Automation | PowerShell Remoting + JEA only; Ansible optional from Phase 6 — ADR-0003 |
| Access | Authentication source → corporate principal → approval → role → capability; authentication claims never grant access directly — ADR-0010, ADR-0022 |
| Hosting | IIS on Windows Server, in-process — ADR-0007 |
| AI | Self-hosted only, Phase 7 — ADR-0005 |

Other decided areas (identity lookup, operational records/Jira, sessions, OIDC, SDM evaluation, resources, In Use, announcements, reporting) each have their own ADR in `docs/adr/`; read the one for the area you touch.

**Open decisions — do not assume an answer:** Turuncuhat inbound/outbound method and read/write scope; Worker privileged access (BeyondTrust brokering vs. direct WinRM + Kerberos + JEA). See `docs/15-system-landscape.md` and `docs/06-integrations.md`.

## Where to look

Read what the task needs, not everything. Start with the guide for your area; follow its pointers.

| Task | Read |
|---|---|
| Any task, first time in this repo | `docs/agent-guides/000-project-context.md` (short), then the row below |
| Security, auth, audit, secrets | `docs/agent-guides/050-security-audit.md`, `docs/05-security-model.md`, relevant ADR |
| Architecture or project boundaries | `docs/agent-guides/010-architecture.md`, `docs/03-architecture.md` |
| Backend / API / Infrastructure / SQL | `docs/agent-guides/020-backend-dotnet.md`, the project's `README.md`, `sql/README.md` |
| Worker / Hangfire | `docs/agent-guides/030-worker-service.md`, `src/SecureOps.Worker/README.md` |
| PowerShell / JEA | `docs/agent-guides/040-automation-ansible-powershell.md`, `scripts/README.md` |
| UI | `CLAUDE.md`, `docs/agent-guides/060-ui.md`, `docs/contracts/` |
| Tests and quality | `docs/agent-guides/090-testing-quality.md`, `tests/README.md` |
| Contracts | `contracts/README.md`, `docs/contracts/` |
| Operational records / Jira / platform access | `docs/22-operational-record-jira-workflow.md`, `docs/23-platform-access-concurrency-and-release-safety.md`, ADR-0009, ADR-0010 |
| Release | `scripts/release/README.md`, `docs/24-api-test-deployment-readiness.md` |
| Analysis (Phase 6) / AI (Phase 7) | `docs/agent-guides/070-analysis.md` / `080-ai-rag-future-phase.md` |
| Phase scope | `docs/02-roadmap.md` and the matching `plans/PHASE-*.md` |

Layer `README.md` files hold dated handoff and evidence history; search them for the feature you touch rather than reading them end to end.

## Naming

Use **CONTOSO** where a company name is needed. Real system names that are already documented in `docs/15-system-landscape.md` may be used; nothing else that identifies the real organisation or its people.
