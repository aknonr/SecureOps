# Portable Agent Guides

These guides contain detailed, vendor-neutral instructions for agents working on WASAS Automation Management / SecureOps. They are loaded through the routing instructions in `AGENTS.md` and `CLAUDE.md`; no editor or agent should assume automatic discovery.

`AGENTS.md` is the canonical repository entry point. Layer-specific README files remain authoritative for their layer. Select applicable ADRs, contracts, integration documents, phase plans, current-operations documents, and release-candidate evidence according to the task. Report instruction conflicts before editing.

## Migrated Metadata Semantics

The former Cursor frontmatter used `description` for the guide purpose, `globs` for file-scope matching, and `alwaysApply` to request automatic inclusion. Each guide now records those semantics in its ordinary Markdown `Applicability` section. Repository-wide guides are routed for every task; scoped guides are routed by `AGENTS.md`, `CLAUDE.md`, and the table below. The guides are not assumed to be discovered automatically.

## Routing Table

| Work | Required guides and documentation |
|---|---|
| Every task | `000-project-context.md`, `050-security-audit.md`, `090-testing-quality.md` |
| Architecture, source, or test changes | `010-architecture.md` and the affected layer README files |
| Codex backend, API, domain, infrastructure, integration, or hosting | `020-backend-dotnet.md` and the affected `src/SecureOps.*/README.md` files |
| Worker and Hangfire | `030-worker-service.md`, `src/SecureOps.Worker/README.md` |
| PowerShell, JEA, or future Ansible work | `040-automation-ansible-powershell.md`, `scripts/README.md`, `docs/05-security-model.md` |
| Security, authentication, authorization, audit, or secrets | `050-security-audit.md` and the applicable security/ADR documents |
| UI/UX (Planned OCO: Codex; otherwise Claude) | `CLAUDE.md`, `060-ui.md`, `src/SecureOps.Ui/README.md`, and `docs/contracts/` |
| Deterministic analysis | `070-analysis.md` and the applicable Phase 6 and audit documents |
| Future AI/RAG | `080-ai-rag-future-phase.md` and `docs/10-ai-rag-strategy.md` |
| Testing and quality | `090-testing-quality.md`, `tests/README.md`, `docs/13-definition-of-done.md` |
| SQL | `sql/README.md`, `010-architecture.md`, `020-backend-dotnet.md`, and applicable deployment documents |
| Contracts | `contracts/README.md`, `docs/contracts/`, and affected integration documentation |
| Release operations | `scripts/release/README.md`, `docs/24-api-test-deployment-readiness.md`, and applicable `docs/release-candidates/` evidence |

## Guide Index

- `000-project-context.md`: project identity, fixed decisions, scope, and operating context.
- `010-architecture.md`: solution boundaries and dependency direction.
- `020-backend-dotnet.md`: .NET backend implementation conventions.
- `030-worker-service.md`: Worker, Hangfire, PowerShell invocation, and resilience.
- `040-automation-ansible-powershell.md`: PowerShell, JEA, and deferred Ansible constraints.
- `050-security-audit.md`: non-negotiable security and audit boundaries.
- `060-ui.md`: Blazor Server and MudBlazor UI constraints.
- `070-analysis.md`: deterministic Phase 6 analysis rules.
- `080-ai-rag-future-phase.md`: self-hosted Phase 7 AI/RAG boundaries.
- `090-testing-quality.md`: build, test, quality, and commit requirements.
