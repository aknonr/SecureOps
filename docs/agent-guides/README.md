# Agent Guides

Short, area-specific guidance for coding agents, routed from `AGENTS.md`. Each guide records only what is specific to this project or easy to get wrong here; general good engineering practice is assumed.

When sources disagree: `AGENTS.md` hard rules and approved decisions (ADRs, recorded owner decisions) bind; code shows what is implemented and does not override them; layer `README.md` files and these guides describe and may be stale. Report the disagreement rather than silently following either side.

| Guide | Read when |
|---|---|
| `000-project-context.md` | First task in the repo: systems, pilot scope, vocabulary |
| `010-architecture.md` | Touching project boundaries or dependencies |
| `020-backend-dotnet.md` | API, Domain, Shared, Infrastructure, SQL |
| `030-worker-service.md` | Worker, Hangfire, PowerShell invocation |
| `040-automation-ansible-powershell.md` | Scripts, JEA, cmdlets |
| `050-security-audit.md` | Auth, authorization, audit, secrets, data handling |
| `060-ui.md` | Blazor UI (with `CLAUDE.md`) |
| `070-analysis.md` | Phase 6 rule-based analysis |
| `080-ai-rag-future-phase.md` | Any AI proposal or Phase 7 work |
| `090-testing-quality.md` | Building, testing, definition of done |
