using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

namespace SecureOps.Worker.Jea;

/// <summary>
/// The real <see cref="IServiceAccountUsageCollector"/> (<c>Kind = "Jea"</c>, docs/service-accounts/USAGE-SCAN-JOB-CONTRACT.md §3): one
/// <c>Get-SecureOpsAccountUsage -Account … [-ExpectedAccount …] -AsJson</c> per planned server through <see cref="IJeaRunner"/>, at most
/// <c>MaxParallel</c> at once, each server reported exactly once. A per-server failure is a result; cancellation stops starting servers and
/// returns (unreported servers become NotRun in the module). Documents are never logged. It decides nothing about authorization, never writes
/// SQL and never interprets a document beyond the shape the module's parser validates again.
/// </summary>
public sealed class JeaServiceAccountUsageCollector(IJeaRunner runner, ILogger<JeaServiceAccountUsageCollector> logger) : IServiceAccountUsageCollector
{
    /// <inheritdoc />
    public string Kind => UsageCollectorKinds.Jea;

    /// <inheritdoc />
    public async Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observer);
        Dictionary<string, object?> values = new(StringComparer.Ordinal)
        {
            ["Account"] = request.Accounts.ToArray(),
            ["ExpectedAccount"] = request.ExpectedAccount,
            ["AsJson"] = true
        };
        // Built and validated once against the allow-list; an invalid name refuses the whole job before any connection.
        var command = JeaCommand.Create(JeaEndpointCatalog.ServiceAccountUsage.ConfigurationName,
            JeaEndpointCatalog.ServiceAccountUsage.FunctionName, values);
        JeaInvocationLimits limits = new(request.Limits.OpenTimeout, request.Limits.OperationTimeout, request.Limits.MaxDocumentBytes);
        string[] servers = [.. request.Servers.Distinct(StringComparer.OrdinalIgnoreCase)];
        int[] counts = new int[Enum.GetValues<UsageCollectionStatus>().Length];

        await Parallel.ForEachAsync(servers, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(request.Limits.MaxParallel, 1, 64),
            CancellationToken = cancellationToken
        }, async (server, token) =>
        {
            await observer.ServerStartingAsync(server, token);
            UsageCollectionServerResult result = await CollectOneAsync(server, command, limits, token);
            Interlocked.Increment(ref counts[(int)result.Status]);
            await observer.ServerCompletedAsync(result, token);
        });

        logger.LogInformation("JEA usage collection finished. JobId: {JobId} Servers: {Servers} Ok: {Ok} Unreachable: {Unreachable} NoEndpoint: {NoEndpoint} "
            + "Forbidden: {Forbidden} Timeout: {Timeout} Malformed: {Malformed}.", request.JobId, servers.Length, counts[(int)UsageCollectionStatus.Ok],
            counts[(int)UsageCollectionStatus.Unreachable], counts[(int)UsageCollectionStatus.NoEndpoint], counts[(int)UsageCollectionStatus.Forbidden],
            counts[(int)UsageCollectionStatus.Timeout], counts[(int)UsageCollectionStatus.Malformed]);
    }

    private async Task<UsageCollectionServerResult> CollectOneAsync(string server, JeaCommand command, JeaInvocationLimits limits, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        JeaInvocationResult result;
        try
        {
            result = await runner.InvokeAsync(server, command, limits, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A runner must not throw per server; if one does, the server still gets its one result.
            logger.LogWarning("JEA runner failed for one server. FailureType: {FailureType}.", exception.GetType().Name);
            return new(server, UsageCollectionStatus.Malformed, null, timer.Elapsed);
        }

        if (result.Status != JeaInvocationStatus.Ok)
        {
            return new(server, Map(result.Status), null, result.Elapsed);
        }

        byte[]? document = Document(server, result.Output, limits.MaxOutputBytes);
        return document is null
            ? new(server, UsageCollectionStatus.Malformed, null, result.Elapsed)
            : new(server, UsageCollectionStatus.Ok, document, result.Elapsed);
    }

    /// <summary>Runner status → module status (contract §2 table).</summary>
    public static UsageCollectionStatus Map(JeaInvocationStatus status) => status switch
    {
        JeaInvocationStatus.Ok => UsageCollectionStatus.Ok,
        JeaInvocationStatus.Unreachable => UsageCollectionStatus.Unreachable,
        JeaInvocationStatus.NoEndpoint => UsageCollectionStatus.NoEndpoint,
        JeaInvocationStatus.Forbidden => UsageCollectionStatus.Forbidden,
        JeaInvocationStatus.Timeout => UsageCollectionStatus.Timeout,
        _ => UsageCollectionStatus.Malformed
    };

    /// <summary>
    /// The endpoint's JSON text as the module's document: exactly one JSON object with <c>schema = service-account-usage-v1</c>, and
    /// <c>serverName</c> set to the requested name. The endpoint reports <c>$env:COMPUTERNAME</c>; the session was opened to the requested
    /// name with Kerberos mutual authentication, so the requested name is the one the evidence is about (the person-run JEA mode does the
    /// same). Nothing else is changed; the module's parser validates the rest (secret guard first). Null when not one valid object or too large.
    /// </summary>
    public static byte[]? Document(string server, string? output, int maxBytes)
    {
        if (string.IsNullOrEmpty(output) || Encoding.UTF8.GetByteCount(output) > maxBytes)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(output, documentOptions: new JsonDocumentOptions { MaxDepth = 12 }) is not JsonObject document
                || document["schema"] is not JsonValue schema || schema.GetValueKind() != JsonValueKind.String
                || schema.GetValue<string>() != "service-account-usage-v1")
            {
                return null;
            }

            document["serverName"] = server;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document);
            return bytes.Length <= maxBytes ? bytes : null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            // Duplicate keys, invalid JSON or depth: not one valid document.
            return null;
        }
    }
}
