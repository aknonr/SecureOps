# 020 — Backend .NET Rules

## Applicability

- **Purpose:** Backend rules for .NET 8, ASP.NET Core API, Hangfire, and EF Core.
- **Applies to:** `src/SecureOps.Api/**/*.cs`, `src/SecureOps.Infrastructure/**/*.cs`, `src/SecureOps.Domain/**/*.cs`, and `src/SecureOps.Shared/**/*.cs`.
- **Loading:** Routed explicitly from `AGENTS.md` or `docs/agent-guides/README.md`; do not assume automatic discovery.

## Language and Framework

- Target framework: `net8.0`.
- `<Nullable>enable</Nullable>` in every csproj.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` for `src/`.
- `<ImplicitUsings>enable</ImplicitUsings>` for brevity.
- `LangVersion`: latest stable.

## Naming

| Construct | Convention | Example |
|---|---|---|
| Namespace | `SecureOps.<Project>.<Folder>` | `SecureOps.Api.Controllers` |
| Class | PascalCase | `DiagnosticJobRunner` |
| Interface | `I` + PascalCase | `IDiagnosticRunner` |
| Method | PascalCase, `Async` suffix for async | `RunDiagnosticAsync` |
| Parameter / local | camelCase | `serverId`, `alertPayload` |
| Constant | PascalCase | `MaxRetryAttempts` |
| Private field | `_camelCase` | `_logger` |

## Controllers (ASP.NET Core)

- One controller per resource. Keep them thin.
- Route: `api/v1/<resource>`.
- Return `IActionResult` or `ActionResult<T>`.
- Use `[Authorize]` with policy names from `SecureOps.Shared.Auth.Policies`.
- Validate inputs with FluentValidation or DataAnnotations.

```csharp
[ApiController]
[Route("api/v1/alerts")]
[Authorize(Policy = Policies.OperatorOrAbove)]
public sealed class AlertsController : ControllerBase
{
    private readonly IAlertService _alertService;

    public AlertsController(IAlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlertDetailDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _alertService.GetByIdAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
```

## Hangfire Jobs

- All long-running diagnostic runs go through Hangfire.
- Use `BackgroundJobClient` to enqueue, `IRecurringJobManager` for scheduled.
- Job methods are public, take simple serializable parameters.
- Jobs must be idempotent: re-running with the same input produces the same result.
- Use `[AutomaticRetry(Attempts = 3)]` for transient failures.
- Configure SQL Server storage in `Program.cs`.

```csharp
public sealed class DiagnosticJob
{
    private readonly IDiagnosticRunner _runner;

    public DiagnosticJob(IDiagnosticRunner runner)
    {
        _runner = runner;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 120, 300 })]
    public async Task RunAsync(Guid alertId, CancellationToken cancellationToken)
    {
        await _runner.RunForAlertAsync(alertId, cancellationToken);
    }
}
```

## Validation

Use FluentValidation:

```csharp
public sealed class CreateAlertCommandValidator : AbstractValidator<CreateAlertCommand>
{
    public CreateAlertCommandValidator()
    {
        RuleFor(x => x.ServerName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.AlertType).IsInEnum();
        RuleFor(x => x.Severity).IsInEnum();
    }
}
```

## Error Handling

- Use middleware for uncaught exceptions. Convert to `ProblemDetails` response.
- Never expose stack traces in production responses.
- Log full exception with `_logger.LogError(ex, "Context: {ServerName}", serverName)`.
- Use `Result<T>` pattern for expected business failures, not exceptions.

## EF Core (when used)

- One `DbContext` per bounded context. Keep it lean.
- Migrations live in `src/SecureOps.Infrastructure/Migrations/`.
- Never call `SaveChanges()` synchronously. Always `SaveChangesAsync(cancellationToken)`.
- Use `AsNoTracking()` for read-only queries.
- No lazy loading. Explicit `Include()` only.

## Logging (Serilog)

- Structured logs only. No string interpolation in log messages.
- Use semantic property names.

```csharp
// GOOD
_logger.LogInformation("Diagnostic completed for {ServerName} in {DurationMs}ms",
    serverName, durationMs);

// BAD
_logger.LogInformation($"Diagnostic completed for {serverName} in {durationMs}ms");
```

- Correlate logs with `TraceId` from `Activity.Current?.Id`.

## Cancellation Tokens

- Every async method that does I/O takes a `CancellationToken` and propagates it.
- Cancellation tokens are the last parameter.
- Never pass `CancellationToken.None` unless you have a specific reason — comment why.

## Async / Await

- No `async void` except event handlers.
- No `.Result` or `.Wait()` — these deadlock under sync context.
- No `Task.Run` to "make sync code async" — fix the sync code.

## Reference

Read `docs/03-architecture.md` and `docs/04-domain-model.md`.
