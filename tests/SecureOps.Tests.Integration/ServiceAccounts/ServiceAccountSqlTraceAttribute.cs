using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SecureOps.Tests.Integration.ServiceAccounts;
using Xunit.Sdk;

[assembly: ServiceAccountSqlTrace]

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Opt-in test/SPID correlation without changing connection pools, SQL or test scheduling.</summary>
public sealed class ServiceAccountSqlTraceAttribute : BeforeAfterTestAttribute
{
    private static readonly string? _file = Environment.GetEnvironmentVariable("SECUREOPS_SA_SQL_TRACE");
    private static readonly AsyncLocal<string?> _test = new();
    private static readonly object _sync = new();
    private static readonly ConcurrentBag<IDisposable> _subscriptions = [];
    private static readonly Lazy<IDisposable> _listener = new(() => DiagnosticListener.AllListeners.Subscribe(new Listeners()));

    /// <inheritdoc/>
    public override void Before(MethodInfo methodUnderTest)
    {
        if (string.IsNullOrWhiteSpace(_file))
        {
            return;
        }

        _ = _listener.Value;
        _test.Value = $"{methodUnderTest.DeclaringType?.FullName}.{methodUnderTest.Name}";
        Write(new { kind = "test-start", utc = DateTimeOffset.UtcNow, test = _test.Value });
    }

    /// <inheritdoc/>
    public override void After(MethodInfo methodUnderTest)
    {
        if (string.IsNullOrWhiteSpace(_file))
        {
            return;
        }

        Write(new { kind = "test-end", utc = DateTimeOffset.UtcNow, test = _test.Value });
        _test.Value = null;
    }

    private static void Write(object record)
    {
        lock (_sync)
        {
            File.AppendAllText(_file!, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
    }

    private sealed class Listeners : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "SqlClientDiagnosticListener")
            {
                _subscriptions.Add(value.Subscribe(new Commands(), name =>
                    name.StartsWith("Microsoft.Data.SqlClient.WriteCommand", StringComparison.Ordinal)));
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    private sealed class Commands : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is null
                || value.Value.GetType().GetProperty("Command")?.GetValue(value.Value) is not SqlCommand command
                || command.Connection is not { State: ConnectionState.Open } connection
                || !connection.DataSource.Equals("(localdb)\\SecureOpsResourcesV1", StringComparison.OrdinalIgnoreCase)
                || !connection.Database.StartsWith("SecureOps_Sa", StringComparison.Ordinal))
            {
                return;
            }

            // Only session metadata and a SQL hash: no command text, parameters, identities or credentials.
            Write(new
            {
                kind = value.Key,
                utc = DateTimeOffset.UtcNow,
                test = _test.Value,
                processId = Environment.ProcessId,
                threadPoolThreads = ThreadPool.ThreadCount,
                pendingWorkItems = ThreadPool.PendingWorkItemCount,
                sessionId = connection.ServerProcessId,
                clientConnectionId = connection.ClientConnectionId,
                operationId = value.Value.GetType().GetProperty("OperationId")?.GetValue(value.Value),
                sqlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.CommandText)))
            });
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
