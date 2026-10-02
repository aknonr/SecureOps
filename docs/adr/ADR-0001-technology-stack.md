# ADR-0001 — Technology Stack

**Status:** Accepted; amended 2026-10-01 (data access, Amendment 1) and 2026-10-02 (.NET 10, Amendment 2)
**Date:** 2026-05
**Decision makers:** Project owner

## Context

A single developer working part-time around shift duties needs to build an enterprise Windows operations platform. The team is Windows-heavy. The existing infrastructure runs on .NET, IIS, SQL Server, Active Directory, and PowerShell.

A multi-year roadmap (Phases 1–8) requires a stack that:

- A single developer can maintain.
- Builds on existing organizational skills.
- Avoids exotic dependencies.
- Integrates natively with Windows Authentication and AD.
- Supports both a web UI and long-running background jobs.
- Has a clear path to extending with a self-hosted AI/RAG layer later.

## Decision

The stack is:

| Layer | Choice |
|---|---|
| Language | C# |
| Framework | .NET 10 (LTS) — *amended 2026-10-02; originally .NET 8 (LTS) (Amendment 2)* |
| API | ASP.NET Core Web API |
| Worker | .NET Worker Service (Windows Service) |
| UI | **Blazor Server + MudBlazor** |
| Background jobs | **Hangfire** with SQL Server storage |
| Database | SQL Server (existing enterprise) |
| Data access | Dapper with parameterized SQL over numbered SQL Server scripts — *amended 2026-10-01; originally "EF Core for CRUD, Dapper for high-volume audit writes" (Amendment 1)* |
| Logging | Serilog with structured JSON |
| PowerShell | `System.Management.Automation` + JEA |
| Authentication | Windows Authentication via Active Directory |
| Authorization | ASP.NET Core authorization policies; direct AD-group grants superseded by ADR-0010 application capabilities |

Hosting model:

- API and UI on IIS in-process.
- Worker as a Windows Service.
- SQL Server on the existing enterprise database server.

## Alternatives Considered

### UI: React (or Angular, Vue) SPA + Web API

Rejected because:
- Two ecosystems (.NET + Node/npm) to maintain by one developer.
- Custom Windows Authentication integration required.
- SPA build pipeline is friction.
- MudBlazor covers our component needs adequately.

### UI: Razor Pages or MVC

Rejected because:
- Less interactive than Blazor Server.
- Real-time updates require separate SignalR plumbing.
- Component reusability is weaker than Blazor Server + MudBlazor.

### Background jobs: BackgroundService / IHostedService

Rejected because:
- No durable queue.
- No retry semantics out of the box.
- No dashboard for monitoring queued/running/failed jobs.
- Crash mid-job → data loss.

### Background jobs: Quartz.NET

Rejected because:
- Configuration heavier than Hangfire for this scale.
- Hangfire's SQL Server storage matches our DB choice.
- Hangfire's dashboard is a built-in operational benefit.

### Background jobs: Service Bus / RabbitMQ

Rejected because:
- New infrastructure to provision and maintain.
- Pilot scale doesn't justify the overhead.
- May be reconsidered if cross-process messaging becomes a need beyond Phase 6.

### Database: PostgreSQL

Rejected because:
- SQL Server is the existing enterprise standard.
- Operations team has SQL Server skills and tooling.
- No advantage for the workload.

### Database: NoSQL (Mongo, etc.)

Rejected because:
- Audit and reporting workloads are relational by nature.
- ACID requirements for audit are non-negotiable.

### PowerShell: Spawn `powershell.exe` process

Rejected because:
- `System.Management.Automation` is the supported in-process API.
- Better error handling and stream separation.
- No process spawn overhead per call.

### Auth: Custom OAuth / OIDC

Rejected for MVP because:
- AD-integrated Windows Auth covers the use case.
- No external user access in scope.
- Can be added later if requirements change.

## Consequences

### Positive

- Single ecosystem (.NET) reduces context switching.
- Native Windows Auth and AD integration.
- All choices are mainstream Microsoft technologies with extensive documentation.
- Hangfire dashboard provides operational visibility.
- MudBlazor accelerates UI development.

### Negative

- Blazor Server holds UI state on the server (memory cost per connected user).
- Hangfire SQL polling adds some load to the database.
- The Worker process must be deployed and managed separately from the IIS app.
- Migration to a different stack later would be expensive.

### Neutral

- Build and CI tooling will be .NET-centric.
- Hiring future developers requires .NET familiarity.

## Implementation Notes

- All projects target `net10.0` (Amendment 2).
- Solution projects are `SecureOps.Api`, `SecureOps.Worker`, `SecureOps.Ui`, `SecureOps.Domain`, `SecureOps.Infrastructure`, and `SecureOps.Shared`.
- `SecureOps.Shared` is an accepted shared/common layer for cross-process contracts, authorization policy constants, and strongly typed configuration options. It may depend on `SecureOps.Domain` only.
- `SecureOps.Shared` must not contain ASP.NET pipeline code, data-access mappings, SQL access, PowerShell execution, file audit IO, external integration clients, or Blazor components.
- Nullable reference types enabled.
- TreatWarningsAsErrors enabled.
- Latest analyzer level.
- `.editorconfig` at repo root enforces style.

## Amendment 1 — Data access (2026-10-01)

**Original decision (2026-05):** EF Core for CRUD on `dbo.*` tables, Dapper for high-volume audit writes.

**What was built:** every persistence path — access, sessions, operational records, resources, In Use, announcements,
reporting, Service Accounts and audit — uses Dapper with parameterized SQL in `SecureOps.Infrastructure`. The
schema is a sequence of numbered, reviewed scripts in `sql/schema/` and `sql/migrations/` (see `sql/README.md`).
No EF Core model or `DbContext` was created.

**Decision (owner):** Dapper with parameterized SQL and numbered SQL scripts is the approved data-access approach.
EF Core adoption is no longer a planned requirement.

**Why the current approach is retained:**

- Schema scripts are reviewed contracts executed through the approved DBA process and never by application
  startup; explicit, versioned SQL fits that process better than generated ORM migrations.
- Append-only audit triggers, constraints and the transactional pairing of a change with its audit event are
  expressed directly in SQL and covered by isolated SQL tests.
- Optimistic concurrency uses explicit version columns checked in the statements themselves.
- One maintainer: a single, working data-access style is cheaper to keep correct than two.

**Consequences:** guidance, plans and READMEs no longer require an EF Core model; tasks that asked for one are
marked superseded, not implemented. The EF Core package references still present in `SecureOps.Infrastructure`
are unused; removing them is a separate code change and not part of this amendment. Introducing an ORM later
requires a new ADR.

## Amendment 2 — .NET 10 (2026-10-02)

**Why:** .NET 8 (and 9) support ends 2026-11-10. .NET 10 is the current LTS, supported to 2028-11. .NET 11 (STS,
November 2026) would not extend support and is not yet released.

**Decision (owner):** all projects target `net10.0`. The exact-SDK policy from G-30 stays: `global.json` pins SDK
10.0.112 with roll-forward disabled; C# 14 and analyzer level 10.0. Microsoft packages follow one 10.0.x servicing
patch; PowerShell hosting moves to the 7.6 line.

**Consequences:** API/UI servers need the .NET 10 IIS Hosting Bundle and the Worker the .NET 10 runtime; servers take
runtime security patches through that bundle independently of the SDK pin. The OIDC handler's automatic Pushed
Authorization Requests (default since .NET 9) is kept off until tested against the corporate IdP
(`Oidc:PushedAuthorization`). The unused EF Core references noted in Amendment 1 were removed. Rollback is the previous .NET 8 release package on hosts that still have the .NET 8
bundle. MudBlazor 6 still runs but is unsupported; its migration remains separate UI work.

## References

- `docs/03-architecture.md`
- `docs/agent-guides/010-architecture.md`
- `docs/agent-guides/020-backend-dotnet.md`
- `ADR-0007-iis-hosting-model.md`
