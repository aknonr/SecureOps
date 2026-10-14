using System.Text;
using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

/// <summary>
/// Deterministic, network-free collector for tests and the local demo (rule 8: fake before the real adapter). The answer depends
/// only on the server and account names, so the same request always gives the same results:
/// <list type="bullet">
/// <item>server name containing <c>unreach</c> → Unreachable, <c>noep</c> → NoEndpoint, <c>deny</c> → Forbidden, <c>slow</c> →
/// Timeout, <c>bad</c> → Malformed (garbage, no document); <c>part</c> → Ok with IIS failed (Partial).</item>
/// <item>otherwise Ok; per account a stable hash decides whether a Windows service, a scheduled task and/or an IIS application
/// pool runs as it (roughly one server in three has a match).</item>
/// <item>gMSA check: the verification block follows the components; with no former match, every other server shows the gMSA.</item>
/// </list>
/// Every name in its documents is synthetic (<c>SynSvc…</c>, <c>SynTask…</c>, <c>SynPool…</c>). It never opens a connection.
/// </summary>
public sealed class FakeServiceAccountUsageCollector(TimeProvider clock, TimeSpan? delayPerServer = null) : IServiceAccountUsageCollector
{
    /// <inheritdoc />
    public string Kind => UsageCollectorKinds.Fake;

    /// <inheritdoc />
    public async Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken)
    {
        using SemaphoreSlim gate = new(Math.Clamp(request.Limits.MaxParallel, 1, 64));
        Task[] running = [.. request.Servers.Select(async server =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await observer.ServerStartingAsync(server, cancellationToken);
                TimeSpan delay = Delay(server);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, clock, cancellationToken);
                }

                await observer.ServerCompletedAsync(Result(request, server, delay), cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        })];
        await Task.WhenAll(running);
    }

    /// <summary>The answer for one server, without delay (used by tests to state the expected result).</summary>
    public UsageCollectionServerResult Result(UsageCollectionRequest request, string server, TimeSpan elapsed)
    {
        string key = server.ToLowerInvariant();
        UsageCollectionStatus status = key.Contains("unreach", StringComparison.Ordinal) ? UsageCollectionStatus.Unreachable
            : key.Contains("noep", StringComparison.Ordinal) ? UsageCollectionStatus.NoEndpoint
            : key.Contains("deny", StringComparison.Ordinal) ? UsageCollectionStatus.Forbidden
            : key.Contains("slow", StringComparison.Ordinal) ? UsageCollectionStatus.Timeout
            : key.Contains("bad", StringComparison.Ordinal) ? UsageCollectionStatus.Malformed
            : UsageCollectionStatus.Ok;
        return status == UsageCollectionStatus.Ok
            ? new UsageCollectionServerResult(server, status, Document(request, server, key.Contains("part", StringComparison.Ordinal), elapsed), elapsed)
            : new UsageCollectionServerResult(server, status, null, elapsed);
    }

    private TimeSpan Delay(string server) =>
        delayPerServer is { } delay && delay > TimeSpan.Zero ? delay * (1 + Hash(server, "delay") % 4) / 2 : TimeSpan.Zero;

    private byte[] Document(UsageCollectionRequest request, string server, bool partial, TimeSpan elapsed)
    {
        List<(string Type, string Name, string Identity, string Account, string? State, string? Detail)> found = [];
        foreach (string account in request.Accounts)
        {
            uint hash = Hash(server, account);
            string bare = account[(account.IndexOf('\\', StringComparison.Ordinal) + 1)..];
            if (hash % 3 == 0)
            {
                found.Add(("WindowsService", $"SynSvc{hash % 97:00}", account, account, "Running", null));
            }

            if (hash % 5 == 1)
            {
                found.Add(("ScheduledTask", $@"\Syn\SynTask{hash % 89:00}", account, account, "Ready", null));
            }

            if (!partial && hash % 7 == 2)
            {
                found.Add(("IisAppPool", $"SynPool{hash % 83:00}", bare, account, "Started", null));
            }
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("schema", "service-account-usage-v1");
            json.WriteString("serverName", server);
            json.WriteString("generatedAt", clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            json.WriteNumber("durationMs", (long)elapsed.TotalMilliseconds);
            json.WriteStartArray("accounts");
            foreach (string account in request.Accounts)
            {
                json.WriteStringValue(account);
            }

            json.WriteEndArray();
            json.WriteString("scanResult", partial ? "Partial" : "Success");
            json.WriteStartObject("sources");
            json.WriteString("WindowsServices", "Success");
            json.WriteString("ScheduledTasks", "Success");
            json.WriteString("Iis", partial ? "Failed" : "Success");
            json.WriteEndObject();
            json.WriteStartArray("components");
            foreach ((string Type, string Name, string Identity, string Account, string? State, string? Detail) component in found)
            {
                Component(json, component.Type, component.Name, component.Identity, component.Account, component.State);
            }

            json.WriteEndArray();
            if (request.ExpectedAccount is { } expected)
            {
                bool gmsa = found.Count == 0 && Hash(server, expected) % 2 == 0;
                json.WriteStartObject("verification");
                json.WriteString("ExpectedAccount", expected);
                json.WriteString("Status", found.Count > 0 ? "NotConverted" : gmsa ? "Converted" : "NoComponents");
                json.WriteStartArray("RunningAsGmsa");
                if (gmsa)
                {
                    Component(json, "WindowsService", $"SynSvc{Hash(server, expected) % 97:00}", expected, expected, "Running");
                }

                json.WriteEndArray();
                json.WriteStartArray("StillFormerAccount");
                foreach ((string Type, string Name, string Identity, string Account, string? State, string? Detail) component in found)
                {
                    Component(json, component.Type, component.Name, component.Identity, component.Account, component.State);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteStartArray("warnings");
            if (partial)
            {
                json.WriteStringValue("IIS yapılandırması okunamadı (sahte).");
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void Component(Utf8JsonWriter json, string type, string name, string identity, string account, string? state)
    {
        json.WriteStartObject();
        json.WriteString("ComponentType", type);
        json.WriteString("ComponentName", name);
        json.WriteString("Identity", identity);
        json.WriteString("MatchedAccount", account);
        json.WriteString("State", state);
        json.WriteEndObject();
    }

    /// <summary>FNV-1a over the lower-cased pair: stable across processes and platforms (string.GetHashCode is not).</summary>
    private static uint Hash(string server, string account)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(server.ToLowerInvariant() + "|" + account.ToLowerInvariant()))
        {
            hash = (hash ^ b) * 16777619;
        }

        return hash;
    }
}
