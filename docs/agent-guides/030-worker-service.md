# 030 — Worker Service Rules

## Applicability

- **Purpose:** Worker Service rules for Hangfire jobs, PowerShell invocation, and resilience.
- **Applies to:** `src/SecureOps.Worker/**/*.cs`.
- **Loading:** Routed explicitly from `AGENTS.md` or `docs/agent-guides/README.md`; do not assume automatic discovery.

## Hosting Model

- The Worker is a separate process: `SecureOps.Worker`, registered as a Windows Service.
- The API and Worker share the same SQL Server database but run as independent processes.
- Communication between API and Worker is **only** through Hangfire job enqueues and the shared database — no direct in-memory calls, no message broker in MVP.

## Hangfire Configuration

Configure Hangfire in `Program.cs` of the Worker:

```csharp
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
    options.Queues = new[] { "diagnostic", "notification", "default" };
});
```

## Job Patterns

### Diagnostic Job

```csharp
public sealed class RunDiagnosticJob
{
    private readonly IDiagnosticRunner _runner;
    private readonly IAuditWriter _audit;
    private readonly ILogger<RunDiagnosticJob> _logger;

    public RunDiagnosticJob(
        IDiagnosticRunner runner,
        IAuditWriter audit,
        ILogger<RunDiagnosticJob> logger)
    {
        _runner = runner;
        _audit = audit;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 120, 300 })]
    [Queue("diagnostic")]
    public async Task ExecuteAsync(Guid alertId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting diagnostic for alert {AlertId}", alertId);

        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditAction.DiagnosticStarted,
            AlertId = alertId,
            Actor = "system:worker"
        }, cancellationToken);

        try
        {
            await _runner.RunForAlertAsync(alertId, cancellationToken);
            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditAction.DiagnosticCompleted,
                AlertId = alertId,
                Actor = "system:worker"
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostic failed for alert {AlertId}", alertId);
            await _audit.WriteAsync(new AuditEvent
            {
                Action = AuditAction.DiagnosticFailed,
                AlertId = alertId,
                Actor = "system:worker",
                ErrorMessage = ex.Message
            }, cancellationToken);
            throw; // let Hangfire retry
        }
    }
}
```

## PowerShell Invocation

Use `System.Management.Automation` directly, NOT `Process.Start("powershell.exe")`.

```csharp
public sealed class JeaPowerShellRunner : IPowerShellRunner
{
    private readonly JeaOptions _options;

    public JeaPowerShellRunner(IOptions<JeaOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PowerShellResult> InvokeAsync(
        string serverName,
        string command,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        var connectionInfo = new WSManConnectionInfo
        {
            ComputerName = serverName,
            ShellUri = $"http://schemas.microsoft.com/powershell/{_options.ConfigurationName}",
            AuthenticationMechanism = AuthenticationMechanism.Kerberos,
            Credential = _options.GetCredential()
        };

        using var runspace = RunspaceFactory.CreateRunspace(connectionInfo);
        await Task.Run(() => runspace.Open(), cancellationToken);

        using var ps = PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddCommand(command);
        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
            {
                ps.AddParameter(key, value);
            }
        }

        var results = await Task.Run(() => ps.Invoke(), cancellationToken);
        return new PowerShellResult(
            success: !ps.HadErrors,
            output: results.Select(r => r.BaseObject).ToList(),
            errors: ps.Streams.Error.Select(e => e.ToString()).ToList());
    }
}
```

## Resilience

- Every external call (WinRM, SQL, HTTP) is wrapped in a Polly policy: retry with exponential backoff + circuit breaker.
- Configure timeouts explicitly. No infinite waits.
- Use `CancellationToken` everywhere; honor it in long-running loops.

```csharp
public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
```

## Forbidden in Worker

- Direct UI updates (no SignalR push from Worker — UI polls or subscribes via API).
- Synchronous PowerShell invocation in tight loops.
- Long-running operations outside Hangfire.
- File I/O without explicit configuration of allowed paths.

## Reference

Read `docs/07-diagnostic-modules.md` and `docs/05-security-model.md`.
