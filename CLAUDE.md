# CLAUDE.md — Claude Code Specific Instructions

This file extends `AGENTS.md` with Claude-specific UI/UX guidance. Read and honor `AGENTS.md` first, then this file. Repository-wide security and ownership rules remain canonical in `AGENTS.md` and must not be duplicated or overridden here.

## Ownership And Routing

- Claude owns UI/UX work only: Razor, CSS, layout, theme, navigation, accessibility, and visual behavior under `src/SecureOps.Ui/`.
- Read `docs/agent-guides/README.md`, `docs/agent-guides/060-ui.md`, `src/SecureOps.Ui/README.md`, the applicable `docs/contracts/` files, and relevant UI design documents before editing.
- Do not silently change backend, API, domain, infrastructure, SQL, authentication, authorization, integrations, migrations, or API contracts.
- When UI work needs a missing or changed backend contract, report the exact contract need for Codex ownership instead of inventing data, routes, permissions, or state in the UI.

---

## Operating Mode

You are working in a repository owned by a single Windows System Administrator who runs shift operations. Treat this as a long-running enterprise project, not a quick scripting task.

- Prefer slow, deliberate, verified changes over fast, large changes.
- Always read existing files before editing. Do not assume content.
- Use `Read` before `Edit`. Use `Grep` before `Read` if you do not know which file.
- When in doubt, ask. The user prefers a clarifying question over a wrong implementation.

---

## Tool Use Priority

1. **Plan first.** For any task that touches more than 2 files or introduces a new component, write a short plan and confirm before editing.
2. **Read the docs.** `docs/`, `docs/agent-guides/`, contracts, and applicable layer README files are the source of truth. Code that contradicts them is a bug.
3. **Edit small.** One concern per edit. Multiple unrelated edits in one diff make review hard.
4. **Run verification.** After code changes, run `dotnet build` and `dotnet test`. If the environment cannot run them, say so explicitly and list what should be verified manually.
5. **Update docs in the same turn.** If a code change alters behavior described in `docs/`, update the doc in the same turn — not as a follow-up.

---

## Tone and Communication

- Direct, professional, no filler. The user has limited time.
- Surface trade-offs explicitly. Do not silently pick a path when two reasonable options exist.
- Flag uncertainty. "I believe X but I am not certain" is more useful than confident wrong.
- When refusing on security or scope grounds, explain why and propose a compliant alternative.

---

## Project-Specific Patterns

### Naming Conventions

- Namespace prefix: `SecureOps.*`
- Class names follow .NET conventions (PascalCase).
- Interface names start with `I` (e.g., `IDiagnosticRunner`).
- Async methods end with `Async`.
- DTO classes go in `SecureOps.Shared.Contracts.*`.

### Folder Structure

- Domain models in `src/SecureOps.Domain/`
- Database access, external integrations, PowerShell execution in `src/SecureOps.Infrastructure/`
- REST API controllers in `src/SecureOps.Api/`
- Hangfire jobs and worker hosting in `src/SecureOps.Worker/`
- Blazor Server UI in `src/SecureOps.Ui/`
- DTOs, JSON schemas, shared utilities in `src/SecureOps.Shared/`
- Tests in `tests/`, mirroring the source structure

### Dependency Direction

- `Domain` depends on nothing internal.
- `Shared` depends on nothing internal except possibly `Domain` DTOs.
- `Infrastructure` depends on `Domain` and `Shared`.
- `Api`, `Worker`, `Ui` depend on `Domain`, `Shared`, `Infrastructure`.
- No circular dependencies. Ever.

### Forbidden Patterns

| Pattern | Reason |
|---|---|
| `localStorage`/`sessionStorage` for anything but a non-sensitive presentation preference | Use server state or scoped DI. The one sanctioned exception is the appearance mode (`wasas.appearance`): it carries no identity, no authorization, and is never sent to the API. Never a cookie for it — a cookie travels on every request and belongs to authentication. |
| Direct SQL string concatenation | Always parameterized queries or EF Core |
| `Stop-Service`, `Restart-Service`, `Remove-Item` in any PowerShell | Read-only MVP |
| External HTTP to OpenAI, Anthropic, Google AI | No public AI |
| `try { ... } catch { }` swallowing exceptions | Always log, often rethrow |
| Hard-coded service account passwords | Use config + PAM-managed secrets |
| Magic numbers without named constants | Future maintainer must understand |
| Public mutable static state | Use DI + scoped lifetimes |

---

## When You See These Phrases in User Requests

| User says | What they mean |
|---|---|
| "vardiya" | Shift rotation. Affects scheduling and notification logic. |
| "nöbetçi" | On-call engineer. Different person than the shift operator. |
| "alarm geldi" | An alert was received from monitoring (SolarWinds-style). |
| "diagnostic koştu" | A read-only diagnostic job ran on a target server. |
| "ticket" | Incident or change request in the ITSM tool. |
| "audit" | The append-only operational record, not personnel monitoring. |
| "PAM" or "BeyondTrust" | Privileged access management. We integrate read-only for session correlation. |
| "Faz X" | Phase X from `docs/02-roadmap.md`. |
| "JEA" | PowerShell Just Enough Administration constrained endpoint. |
| "WinRM" | Windows Remote Management — the protocol PowerShell Remoting uses. |

---

## Output Conventions

When you complete a task, end with a structured summary:

```
## Summary
- **Changed:** <list of files>
- **Why:** <one-line justification>
- **Tests:** <which tests cover this, or "no tests added because <reason>">
- **Docs:** <which docs were updated, or "no doc update needed because <reason>">
- **Phase:** <which phase this belongs to>
- **Open questions:** <anything that needs user input>
- **Next:** <suggested next step, or "ready for review">
```

---

## What to Do When Stuck

1. Re-read `AGENTS.md` non-negotiable rules.
2. Search the docs: `grep -r "keyword" docs/`.
3. Check ADRs: `ls docs/adr/` and read the relevant one.
4. Ask the user a clarifying question. Do not guess.

---

## What This Project Is NOT (Reminder)

- Not a side project. This is a controlled enterprise build with stakeholders, an audit story, and a multi-phase plan.
- Not your decision-making territory. Architectural choices belong to ADRs, not to inline code comments.
- Not a place for AI experiments. Phase 7 has its own track and its own constraints.

If a request feels like it is taking the project off track, stop and surface that to the user.
