# AGENTS.md — Entry Point for AI Coding Agents

**Project:** Secure Ops Automation & AI Analysis Hub
**Owner organization placeholder:** CONTOSO Turkish Technology
**Status:** Phase 1A backend IdentityLookup implemented and hardened; Phase 1 read-only diagnostic MVP not started.
**Last updated:** 2026-05

---

## What This File Is

This file is the universal entry point for every AI coding agent working on this repository — Cursor, Claude Code, GitHub Copilot, Codex, Aider, or any other.

**Read this file first. Then read the files it points to. Do not skip steps.**

This file is intentionally short. Detailed rules live in `.cursor/rules/`, `docs/`, and `plans/`. Treat those as the source of truth; this file is just the index.

---

## Mandatory Reading Order

Before producing **any** output (code, design, suggestion, file edit), read these files in order:

1. **`docs/00-project-brief.md`** — what this project is and is not.
2. **`docs/01-current-operations-context.md`** — real-world operational context that shapes every design decision.
3. **`docs/02-roadmap.md`** — phased delivery plan. Know which phase you are in.
4. **`docs/03-architecture.md`** — system architecture and component boundaries.
5. **`docs/05-security-model.md`** — security boundaries that cannot be crossed.
6. **`docs/15-system-landscape.md`** — real system names, roles, and open integration questions.
7. **`.cursor/rules/000-project-context.mdc`** — authoritative project decisions.
8. **`.cursor/rules/050-security-audit-rules.mdc`** — non-negotiable security rules.
9. The specific phase plan in `plans/` for whichever phase the user references.

If the user request touches a specific area, also read the matching rule file (`.cursor/rules/0XX-*.mdc`) and matching doc (`docs/0X-*.md`).

---

## Non-Negotiable Rules (Read Every Time)

These rules override any user request that contradicts them. If a user asks an agent to violate one of these, the agent must refuse, explain, and propose a compliant alternative.

1. **MVP is read-only.** No service restart, app pool recycle, file deletion, reboot, permission change, or any write operation on target servers. Read-only PowerShell cmdlets only.
2. **No public AI services with internal data.** No OpenAI, Anthropic API, Gemini, or any external LLM endpoint receives alarm payloads, hostnames, log content, or any production data. AI work is Phase 7 only, self-hosted only.
3. **Existing infrastructure is not modified.** SolarWinds, OpsBridge/monthly.thy.com, Turuncuhat, BeyondTrust/PAM, and Ansible/AWX deployments stay untouched unless a later approved integration explicitly says otherwise. This system runs on top of them, not inside them.
4. **Audit is not employee tracking.** Every audit feature must be framed as operational response verification, SLA evidence, and incident audit — never as person-level performance monitoring. Use the exact framing in `docs/05-security-model.md`.
5. **JEA is mandatory for all WinRM operations.** Service account cannot execute write cmdlets. See whitelist in `docs/05-security-model.md`.
6. **Append-only audit.** UPDATE and DELETE on audit tables are blocked by SQL triggers. Retention minimum 36 months.
7. **Approval-based remediation.** Phase 8 only. Any code that performs a state-changing operation must go through an approval workflow.
8. **Document first, code second.** When architecture or behavior changes, update the relevant `docs/*.md` and create or amend an ADR in `docs/adr/`. Code without doc update is incomplete.
9. **Stay in your phase.** Do not implement features from a future phase. If a request belongs to a later phase, surface that and ask whether to defer.
10. **Mock integrations before real ones.** Every external integration (SolarWinds, Turuncuhat, PAM, Teams, ticketing) starts as an in-memory mock adapter behind an interface.

---

## Authoritative Decisions (Already Made)

These decisions are final and binding. Do not re-litigate them in code or proposals. If you believe one should change, write a proposal in `docs/adr/` first.

| Area | Decision | Reference |
|---|---|---|
| UI framework | **Blazor Server + MudBlazor** (not React, not Angular) | ADR-0001, ADR-0007 |
| Backend stack | .NET 8, ASP.NET Core Web API + Worker Service | ADR-0001 |
| Job orchestration | **Hangfire with SQL Server storage** (not BackgroundService, not custom queue) | ADR-0001 |
| Database | SQL Server with append-only audit tables | ADR-0001 |
| Automation MVP | **PowerShell Remoting + JEA constrained endpoint only**. Ansible deferred to Phase 6+ optional | ADR-0001, ADR-0003 |
| AI strategy | **Self-hosted only**, Phase 7, separate budget | ADR-0005 |
| Integration approach | **Mock-first**, interface-based, real adapter later | ADR-0004 |
| Read-only first | MVP performs no write operations | ADR-0002 |
| Identity lookup | **Phase 1A backend-only exact PAM/AD account lookup**, TeamLead/Admin only, read-only AD provider, no broad search | ADR-0008 |
| Remediation | Approval-based only, Phase 8 | ADR-0006 |
| Hosting | IIS on Windows Server, in-process | ADR-0007 |
| Pilot scale | **10–15 low-criticality Windows servers**, prefer non-production | docs/02-roadmap.md |
| MVP timeline | **6–8 weeks** end-to-end, then demo + management review | docs/02-roadmap.md |
| Authentication | Windows Authentication via AD | docs/05-security-model.md |
| RBAC | AD-group-based: Operator, TeamLead, Admin, Auditor | docs/05-security-model.md |
| Placeholder names | **CONTOSO** for company, generic names for systems | docs/00-project-brief.md |

---

## Open Decisions (Pending Input)

These items affect architecture but are **not yet decided**. Do not assume an answer until stakeholder input is recorded in the docs.

| Area | Open decision | Pending input |
|---|---|---|
| Turuncuhat integration | Inbound method and outbound capability: webhook vs. API, and read-only vs. read-write | Turuncuhat stakeholders |
| Worker privileged access | Whether the Worker uses BeyondTrust brokering, direct WinRM + Kerberos + JEA, or the existing documented direct-JEA model with explicit acceptance | PAM team, Bilgi Güvenliği, team lead |

See `docs/15-system-landscape.md`, `docs/05-security-model.md`, and `docs/06-integrations.md`.

---

## Directory Map

```
.
├── AGENTS.md                    # this file
├── CLAUDE.md                    # Claude Code-specific instructions
├── README.md                    # human-facing project intro
├── .cursor/rules/               # Cursor agent rules (.mdc, also useful for any agent)
├── .github/copilot-instructions.md  # GitHub Copilot context
├── docs/                        # project memory (the source of truth)
│   ├── 00-project-brief.md
│   ├── 01-current-operations-context.md
│   ├── 02-roadmap.md
│   ├── 03-architecture.md
│   ├── 04-domain-model.md
│   ├── 05-security-model.md
│   ├── 06-integrations.md
│   ├── 07-diagnostic-modules.md
│   ├── 08-audit-model.md
│   ├── 09-snapshot-change-safety.md
│   ├── 10-ai-rag-strategy.md
│   ├── 11-feasibility.md
│   ├── 12-mvp-backlog.md
│   ├── 13-definition-of-done.md
│   ├── 14-management-summary-tr.md      # the only Turkish doc
│   ├── 15-system-landscape.md
│   └── adr/                              # architecture decision records
├── plans/                       # phase plans with task breakdown
│   └── PHASE-0..8-*.md
├── contracts/                   # data contracts
│   ├── schemas/                 # JSON Schema files
│   └── examples/                # sample payloads
├── src/                         # .NET solution
│   ├── SecureOps.Api/
│   ├── SecureOps.Worker/
│   ├── SecureOps.Domain/
│   ├── SecureOps.Infrastructure/
│   ├── SecureOps.Ui/
│   └── SecureOps.Shared/
├── tests/                       # unit and integration test projects
│   ├── SecureOps.Tests.Unit/
│   └── SecureOps.Tests.Integration/
├── scripts/powershell/          # diagnostic and JEA script folders (Phase 1 placeholders)
│   ├── diagnostic/
│   └── jea/
└── sql/                         # schema and migration folders (Phase 1 placeholders)
    ├── schema/
    └── migrations/
```

---

## How to Approach a Task

When you receive a user request:

1. **Identify the phase.** Match it to `plans/PHASE-X-*.md`. If unclear, ask.
2. **Read the relevant docs.** See "Mandatory Reading Order" above.
3. **Check the rules.** Especially `050-security-audit-rules.mdc`.
4. **Check ADRs.** Has a decision already been made?
5. **Propose before you code.** For non-trivial changes, write a short plan first and confirm with the user.
6. **Implement small.** One concern per change. Keep diffs reviewable.
7. **Update documentation.** Any change to architecture, behavior, or external contract requires an update to the matching `docs/*.md` and possibly an ADR.
8. **Verify.** Run `dotnet build`, then `dotnet test`. If you cannot run them, say so explicitly.
9. **Summarize.** End with: what changed, what tests cover it, what docs were updated, what is still pending.

---

## What This Project Is NOT

- Not an AI product. AI is a Phase 7 capability layer on top of an operations platform.
- Not a SIEM or APM tool. We complement existing monitoring, we do not replace it.
- Not a replacement for BeyondTrust/PAM session management. We add operational audit on top.
- Not a remediation tool in MVP. Remediation is Phase 8, approval-based, manually scoped.
- Not an employee surveillance system. Audit is process-level, not person-performance-level.
- Not a people search tool. Phase 1A identity lookup is exact-account, purpose-bound, TeamLead/Admin-only, and audited.

---

## Single Developer Reality

This project is being built by **one developer** working part-time around shift operations duties. Effort budget is approximately **16–20 hours per week**. Bus factor is 1.

This shapes every decision:

- **Documentation is not optional.** It is the project's continuity insurance.
- **Boring technology wins.** No exotic frameworks. Stick to mainstream Microsoft stack.
- **Small, reviewable changes.** A pull request that is too big to review will not be reviewed.
- **No heroic features.** If a feature requires deep tribal knowledge to maintain, it is the wrong feature.

When you generate code, optimize for the next maintainer's understanding, not for cleverness.

---

## Quick Links

- **Current phase status:** see top of `plans/PHASE-0-discovery-and-project-setup.md`
- **Open ADRs:** see `docs/adr/`
- **Definition of Done:** `docs/13-definition-of-done.md`
- **MVP backlog:** `docs/12-mvp-backlog.md`
- **Turkish management summary:** `docs/14-management-summary-tr.md`
- **System landscape:** `docs/15-system-landscape.md`
