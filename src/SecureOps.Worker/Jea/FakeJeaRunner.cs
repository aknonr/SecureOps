using System.Text.Json;

namespace SecureOps.Worker.Jea;

/// <summary>
/// Deterministic, network-free <see cref="IJeaRunner"/> for tests and the local demo (AGENTS.md rule 8). The answer depends only on the
/// server name (same scheme as the module's fake collector): a name containing <c>unreach</c> → Unreachable, <c>noep</c> → NoEndpoint,
/// <c>deny</c> → Forbidden, <c>slow</c> → Timeout, <c>bad</c> → Malformed; otherwise Ok with the output of <c>okOutput</c>, by default a
/// synthetic <c>service-account-usage-v1</c> document for the usage endpoint (one Windows service <c>SynSvc</c> per searched account when
/// the name contains <c>hit</c>). Its <c>serverName</c> is the short upper-case name, as <c>$env:COMPUTERNAME</c> would be.
/// </summary>
public sealed class FakeJeaRunner(Func<string, JeaCommand, string>? okOutput = null, TimeSpan? delay = null) : IJeaRunner
{
    /// <inheritdoc />
    public async Task<JeaInvocationResult> InvokeAsync(string computerName, JeaCommand command, JeaInvocationLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (delay is { } wait && wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string key = computerName.ToLowerInvariant();
        JeaInvocationStatus status = key.Contains("unreach", StringComparison.Ordinal) ? JeaInvocationStatus.Unreachable
            : key.Contains("noep", StringComparison.Ordinal) ? JeaInvocationStatus.NoEndpoint
            : key.Contains("deny", StringComparison.Ordinal) ? JeaInvocationStatus.Forbidden
            : key.Contains("slow", StringComparison.Ordinal) ? JeaInvocationStatus.Timeout
            : key.Contains("bad", StringComparison.Ordinal) ? JeaInvocationStatus.Malformed
            : JeaInvocationStatus.Ok;
        TimeSpan elapsed = delay ?? TimeSpan.FromMilliseconds(5);
        return status == JeaInvocationStatus.Ok
            ? new(status, (okOutput ?? UsageDocument)(computerName, command), elapsed, null)
            : new(status, null, elapsed, "fake");
    }

    /// <summary>A synthetic usage document for the given call (schema-valid; no real names).</summary>
    public static string UsageDocument(string computerName, JeaCommand command)
    {
        string[] accounts = command.Parameters.FirstOrDefault(p => p.Key == "Account").Value as string[] ?? [];
        string? expected = command.Parameters.FirstOrDefault(p => p.Key == "ExpectedAccount").Value is string value ? value : null;
        bool hit = computerName.Contains("hit", StringComparison.OrdinalIgnoreCase);
        object[] components = hit
            ? [.. accounts.Select((a, i) => new { ComponentType = "WindowsService", ComponentName = "SynSvc" + (i + 1), Identity = a, MatchedAccount = a, State = "Running", Detail = (string?)null })]
            : [];
        object? verification = expected is null ? null : new
        {
            ExpectedAccount = expected,
            Status = hit ? "NotConverted" : "NoComponents",
            RunningAsGmsa = Array.Empty<object>(),
            StillFormerAccount = components
        };
        return JsonSerializer.Serialize(new
        {
            schema = "service-account-usage-v1",
            serverName = computerName.Split('.')[0].ToUpperInvariant(),
            generatedAt = "2026-01-05T08:00:00.0000000+00:00",
            durationMs = 5,
            accounts,
            scanResult = "Success",
            sources = new { WindowsServices = "Success", ScheduledTasks = "Success", Iis = "NotInstalled" },
            components,
            verification,
            warnings = Array.Empty<string>()
        });
    }
}
