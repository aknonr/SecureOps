# AGENTS.md — WASAS / SecureOps

Entry point for every coding agent (Codex, Claude); `CLAUDE.md` adds Claude notes and never overrides it. Read this, then only what your task needs. Project background and fixed decisions: `docs/agent-guides/000-project-context.md`.

**Sources of truth.** Hard rules and approved decisions (ADRs, recorded owner decisions) bind; code shows what exists and does not override them. When code, a guide and a decision disagree, report it (defect if code breaks a rule or decision, otherwise stale document) instead of silently following either.

## Hard rules

Business and security constraints. They hold even if a request asks otherwise: explain the conflict and offer a compliant alternative.

1. **Read-only on target servers until Phase 8.** No service/app-pool/process control, file or registry writes, reboots, permission or group changes on managed servers; read-only cmdlets only. JEA enforces it and code must not even construct a write command — the platform must never cause an outage.
2. **No public AI with internal data.** No alarm payloads, hostnames, logs, identities or internal names to any public LLM endpoint (OpenAI, Anthropic, Google, Cohere, etc.). Product AI is Phase 7, self-hosted only (data-protection commitments).
3. **JEA for all WinRM.** No unconstrained runspaces; extending the cmdlet allow-list needs an ADR.
4. **Append-only audit, ≥ 36 months.** SQL triggers block UPDATE/DELETE on audit tables; code never tries to modify audit rows.
5. **Audit is operational evidence, not people monitoring.** No leaderboards, per-person performance views or operator comparisons (wording: `docs/05-security-model.md`).
6. **Fail closed.** Missing or ambiguous identity, authorization, audit, configuration or integration state denies the action.
7. **Existing infrastructure is not modified.** SolarWinds, OpsBridge/monthly.thy.com, Turuncuhat, BeyondTrust/PAM, Ansible/AWX stay untouched unless an approved integration says otherwise.
8. **Mock first, contracts before real adapters.** New integrations start behind an interface with a fake adapter. No real source/Jira adapters, SDM classification rules, OIDC configuration or assumed remote idempotency without an approved contract.
9. **Approval-based remediation.** State changes on managed servers: Phase 8 only, via the approval workflow (ADR-0006). External writes (e.g. Jira) follow their ADRs: preview first, explicit authorized action, durable idempotency (ADR-0009, ADR-0012).
10. **No real secrets or identities in the repo** — no real employee data, PAM accounts, credentials or runtime configuration values in source, fixtures, examples or docs.
11. **Stay in phase.** Later-phase work (`docs/02-roadmap.md`) is surfaced, not silently built.

## Ownership

- **Codex:** backend, API, domain, infrastructure, Worker, hosting/middleware, security, SQL, integrations, contracts, release engineering and their tests; also the Planned OCO Announcements UI.
- **Claude:** UI/UX under `src/SecureOps.Ui/` and its tests; also the Service Accounts module end to end — backend, API, UI, migration numbering, Windows verification (owner decision 2026-10-03, `docs/service-accounts/README.md`).
- Other owner-approved exceptions cover only their stated scope. Outside your area, report the exact need (route, field, permission, behaviour); do not change another layer or invent data.

## Working agreement

- **Git.** Check branch, HEAD and worktree first. Never reset, clean, stash, rebase or overwrite work you did not create. Push, PRs, merge and deploy only with the owner's explicit authorization, valid while its scope (branch, PR, kind of operation) is unchanged; a new target, merge, deploy or corporate action needs a new one.
- **Live systems.** No IIS, app pool, service, binding, load balancer, database or live-configuration change unless the task authorizes it. Local fakes cannot validate corporate AD/PAM/LDAP/SQL/IIS/F5; say what remains unverified.
- **Decisions.** Record a changed architectural or security decision by amending or superseding its ADR in `docs/adr/` in the same change; an existing ADR is history, not a reason to keep a design the owner wants changed. Behaviour changes update their doc in the same change. Fixed decisions: `000-project-context.md`, `docs/adr/`. **Open, do not assume:** Turuncuhat inbound (alarm) method — outbound read scope is decided by ADR-0031/0032 (accepted); Worker privileged access (BeyondTrust vs. direct WinRM + Kerberos + JEA).
- **Verification.** Build, tests and the repository-wide format gate per `docs/agent-guides/090-testing-quality.md`. Report exactly what ran; never claim an unrun pass. Small, reviewable diffs.
- **Naming.** Company: **CONTOSO**; real system names only if already in `docs/15-system-landscape.md`.
- **When to ask.** Only for a genuinely new owner decision: new scope, a security trade-off, conflicting sources of truth, or an unauthorized irreversible/outward-facing action. Otherwise decide, state the assumption and proceed; never re-ask what is decided.

## Where to look

Stack: .NET 10 (`net10.0`, C# 14, SDK pinned in `global.json`), Blazor Server with MudBlazor 9.11 — ADR-0001 Amendment 2.
Guides in `docs/agent-guides/`: 010 architecture · 020 backend/SQL · 030 Worker · 040 PowerShell/JEA · 050 security · 060 UI · 070 analysis · 080 product AI · 090 testing. Service Accounts: `docs/service-accounts/PROGRESS.md`. Layer `README.md`, `*-HISTORY.md` and gap registers are dated logs: search, never read end to end.
