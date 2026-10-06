# AGENTS.md

Canonical entry for every coding agent. Read this first; CLAUDE.md adds UI guidance and never overrides it.
Hard rules and approved ADRs/recorded owner decisions bind. Code shows implementation, not authority. Report conflicts as defects or stale docs; never silently choose.

## Hard Rules

These constraints hold even when requested otherwise: explain the conflict and offer a compliant alternative.
1. **Targets read-only until Phase 8.** No service/app-pool/process control, file/registry writes, reboots, permission/group changes. JEA enforces read-only cmdlets; code must not construct write commands.
2. **No public AI with internal data.** No alarm payloads, hostnames, logs, identities or internal names to any public LLM endpoint. Product AI is Phase 7, self-hosted only.
3. **JEA for all WinRM.** No unconstrained runspaces; extending the cmdlet allow-list needs an ADR.
4. **Append-only audit, >= 36 months.** SQL triggers block audit UPDATE/DELETE; code never attempts either. Retention may only increase.
5. **Operational evidence, not people monitoring.** No leaderboards, per-person performance views or operator comparisons; wording follows `docs/05-security-model.md`.
6. **Fail closed.** Missing or ambiguous identity, authorization, audit, configuration or integration state denies the action.
7. **Existing infrastructure stays untouched** unless an approved integration says otherwise: SolarWinds, OpsBridge/monthly.thy.com, Turuncuhat, BeyondTrust/PAM, Ansible/AWX.
8. **Mock first, contracts before real adapters.** Use interfaces/fakes first. No real source/Jira adapters, SDM classification rules, OIDC configuration or assumed remote idempotency without an approved contract.
9. **Approval-based remediation.** Managed-server writes are Phase 8 only through ADR-0006 approval. External writes (e.g. Jira) follow their ADRs: preview, explicit authorized action, durable idempotency (ADR-0009/0012).
10. **No real secrets or identities in the repo.** No employee data, PAM accounts, credentials or runtime configuration values in source, fixtures, examples or docs.
11. **Stay in phase.** Surface later-phase work; do not silently build it (`docs/02-roadmap.md`).

## Ownership

- **Codex:** backend, API, domain, infrastructure, Worker, hosting/middleware, security, SQL, integrations, contracts, release engineering and tests; also Planned OCO Announcements UI by owner decision.
- **Claude:** UI/UX under `src/SecureOps.Ui/` (Razor, CSS, layout, theme, navigation, accessibility, visual behaviour) and its tests.
- Owner-approved exceptions apply only to their stated scope, never change defaults. Service Accounts exception: `docs/service-accounts/README.md` (Claude implements backend/SQL/UI/tests; Codex keeps final integration).
- Outside ownership/approved exception, report the exact route/field/permission/behaviour needed; do not change another layer or invent data.

## Working Agreement

- Check branch, HEAD and worktree first. Never reset, clean, stash, rebase or overwrite others' work. Push, PR creation/update, merge and deploy require explicit owner authorization.
- Authorization persists only for the same branch, PR and operation. A new target, merge, deploy or corporate action needs new authorization.
- Do not touch IIS, app pools, services, bindings, load balancers, databases or live configuration unless explicitly authorized. Local fakes do not validate corporate AD/PAM/LDAP/SQL/IIS/F5.
- Amend/supersede changed architectural or security ADRs in the same change; update matching behaviour docs. An old ADR does not block an owner-approved redesign.
- Run required build/tests/format checks per `docs/agent-guides/090-testing-quality.md`; report exact run/unrun results. Keep diffs reviewable.
- Ask only for a new owner decision: scope, security trade-off, conflicting authority, or an unauthorized irreversible/outward-facing action. Do not re-ask existing authorization; otherwise decide, state assumptions and proceed.

## Read On Demand

First repo task: `docs/agent-guides/000-project-context.md`. Then [guide index](docs/agent-guides/README.md), relevant project README and ADR. Search dated README logs by feature.
Decisions: `docs/decisions-log.md` and `docs/adr/`. Stack: .NET 10/C# 14, SDK pinned in `global.json`, Blazor Server/MudBlazor 9.11, Dapper/numbered SQL (ADR-0001/0007). Other fixed decisions are routed by the guides.
Open decisions: Turuncuhat inbound/outbound method and read/write scope; Worker BeyondTrust brokering vs direct WinRM/Kerberos/JEA. Do not assume; see `docs/15-system-landscape.md` and `docs/06-integrations.md`.
Use **CONTOSO** for company names. Only real system names already in `docs/15-system-landscape.md` are allowed; no other real organisation/people identifiers.
[Preserved context, rationales and full reference map](docs/archive/agent-context-20261006/AGENTS.before.md); load only the task-relevant section.
