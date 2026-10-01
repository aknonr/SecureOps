# 010 — Architecture Rules

## Applicability

- **Purpose:** Module boundaries, dependency direction, and layering.
- **Applies to:** `src/**/*.cs` and `tests/**/*.cs`, plus architecture or project-boundary changes.
- **Loading:** Routed explicitly from `AGENTS.md` or `docs/agent-guides/README.md`; do not assume automatic discovery.

## Solution Layout

```
src/
├── SecureOps.Domain/          # Pure C#, no external dependencies
├── SecureOps.Shared/          # DTOs, contracts, utilities
├── SecureOps.Infrastructure/  # SQL, PowerShell, integrations
├── SecureOps.Api/             # REST API (ASP.NET Core)
├── SecureOps.Worker/          # Worker Service + Hangfire jobs
└── SecureOps.Ui/              # Blazor Server + MudBlazor
```

## Dependency Direction

```
        ┌──────────────┐
        │   Domain     │ <-- depends on nothing internal
        └──────────────┘
              ▲
              │
        ┌──────────────┐
        │   Shared     │ <-- depends only on Domain
        └──────────────┘
              ▲
              │
        ┌──────────────────┐
        │  Infrastructure  │ <-- depends on Domain, Shared
        └──────────────────┘
              ▲
       ┌──────┴────┬─────────┐
       │           │         │
    ┌─────┐    ┌──────┐  ┌─────┐
    │ Api │    │Worker│  │ Ui  │
    └─────┘    └──────┘  └─────┘
```

No circular dependencies. Ever.

## Each Project Has One Responsibility

| Project | Responsibility | Forbidden |
|---|---|---|
| Domain | Business entities, value objects, domain events | EF Core, ASP.NET, HTTP, SQL |
| Shared | DTOs (records), JSON contract definitions, constants | Database, file I/O |
| Infrastructure | EF Core, Dapper, PowerShell execution, HTTP clients for integrations | ASP.NET pipeline, Blazor components |
| Api | Controllers, middleware, request/response models | Business logic, direct DB access |
| Worker | Hangfire jobs, background services, scheduled tasks | UI concerns |
| Ui | Blazor pages, MudBlazor components, UI state | Direct DB access, PowerShell execution |

## Clean Architecture Principles

- Controllers and Blazor pages are **thin**. They call application services in Infrastructure.
- Application services orchestrate; domain entities encapsulate rules.
- Every external integration is behind an **interface** in Infrastructure, with a mock implementation in tests.
- Every long-running operation goes through Hangfire — not BackgroundService, not Thread, not Task.Run.

## Forbidden Patterns

- Controllers calling DbContext directly.
- Blazor pages calling PowerShell directly.
- Domain classes referencing `Microsoft.EntityFrameworkCore`.
- Static service locator pattern.
- Singleton state mutation.

## File Size Discipline

- Classes: aim under 300 lines. Refactor at 500.
- Methods: aim under 30 lines. Refactor at 60.
- Files: one public type per file unless tightly coupled (e.g., result types alongside their command).

## Configuration Pattern

Use strongly-typed options via `IOptions<T>`:

```csharp
public sealed class SolarWindsOptions
{
    public const string SectionName = "SolarWinds";
    public Uri WebhookEndpoint { get; init; } = null!;
    public string SharedSecret { get; init; } = null!;
}
```

Bind in `Program.cs`:

```csharp
builder.Services.Configure<SolarWindsOptions>(
    builder.Configuration.GetSection(SolarWindsOptions.SectionName));
```

Never read from `IConfiguration` directly in business logic.

## Dependency Injection Lifetimes

- `Singleton` — stateless utilities, options, factories.
- `Scoped` — per-request services, DbContext, application services.
- `Transient` — only when state must be fresh per use.

Default to Scoped. Be very deliberate about Singleton.

## Reference

Read `docs/03-architecture.md` for the full architecture document.
