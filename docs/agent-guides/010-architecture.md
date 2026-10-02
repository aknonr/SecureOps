# 010 — Architecture

Full description: `docs/03-architecture.md`.

## Projects and dependency direction

| Project | Holds | Must not contain |
|---|---|---|
| `SecureOps.Domain` | Entities, value objects, rules | Any internal or framework dependency |
| `SecureOps.Shared` | Contract DTOs (records), auth constants (`Policies`, `Capabilities`), configuration options | I/O |
| `SecureOps.Infrastructure` | Dapper/SQL, PowerShell, integration adapters, application services | ASP.NET pipeline, Blazor |
| `SecureOps.Api` | Controllers, middleware, ProblemDetails | Business logic, direct SQL |
| `SecureOps.Worker` | Hangfire server and jobs | HTTP endpoints, UI concerns |
| `SecureOps.Ui` | Blazor pages/components calling the API over HTTP | Direct database or PowerShell access |

Domain ← Shared ← Infrastructure ← {Api, Worker, Ui}. No cycles.

## Project-specific principles

- Every external integration sits behind an interface with a fake/in-memory implementation used locally and in tests (ADR-0004).
- Long-running or scheduled work runs as Hangfire jobs in the Worker, not `BackgroundService`, threads or `Task.Run` (ADR-0003).
- API and Worker coordinate only through SQL and Hangfire; the UI only through the API.
- Configuration is bound to typed options (`IOptions<T>`); validate it at startup and fail closed on missing security-relevant values.
- Keep controllers and pages thin; put behaviour where it can be unit-tested.
