using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SecureOps.Worker.Jea;

/// <summary>Outcome of one call to one server. Only <see cref="Ok"/> carries output; every other value is "no information".</summary>
public enum JeaInvocationStatus
{
    /// <summary>Tamam: the endpoint function returned exactly one text value within the limits.</summary>
    Ok,
    /// <summary>Erişilemedi: no WinRM answer within the open timeout (name not resolved, port closed, host down).</summary>
    Unreachable,
    /// <summary>UçNoktaYok: WinRM answered, the session configuration is not registered.</summary>
    NoEndpoint,
    /// <summary>YetkiYok: the endpoint exists, the Worker identity was refused (Kerberos or role mapping).</summary>
    Forbidden,
    /// <summary>ZamanAşımı: connected, no complete answer within the operation timeout.</summary>
    Timeout,
    /// <summary>Bozuk: connected, the answer was not exactly one text value within the size limit (or the function failed).</summary>
    Malformed
}

/// <summary>Bounds of one call.</summary>
/// <param name="OpenTimeout">Connection open timeout.</param>
/// <param name="OperationTimeout">Time allowed for the function after the connection opened.</param>
/// <param name="MaxOutputBytes">Largest UTF-8 output accepted; more is <see cref="JeaInvocationStatus.Malformed"/>.</param>
public sealed record JeaInvocationLimits(TimeSpan OpenTimeout, TimeSpan OperationTimeout, int MaxOutputBytes);

/// <summary>Result of one call.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Output">The one text value when <see cref="JeaInvocationStatus.Ok"/>; otherwise null. Callers never log it.</param>
/// <param name="Elapsed">Wall time spent on this server.</param>
/// <param name="FailureType">Stable failure detail for logs (exception type or code), never a remote message.</param>
public sealed record JeaInvocationResult(JeaInvocationStatus Status, string? Output, TimeSpan Elapsed, string? FailureType);

/// <summary>
/// Calls one allow-listed JEA function on one server, read-only (AGENTS.md rules 1, 3). Shared by the service account usage collector
/// and, later, the Phase 1 diagnostics. A per-server failure is a result, never an exception; only cancellation of
/// <c>cancellationToken</c> throws (<see cref="OperationCanceledException"/>).
/// </summary>
public interface IJeaRunner
{
    /// <summary>Opens a constrained session to <paramref name="computerName"/>, runs <paramref name="command"/> and closes the session.</summary>
    public Task<JeaInvocationResult> InvokeAsync(string computerName, JeaCommand command, JeaInvocationLimits limits, CancellationToken cancellationToken);
}

/// <summary>A failure the transport classified itself (for example from a WinRM error code).</summary>
public sealed class JeaTransportException(JeaInvocationStatus status, string failureType) : Exception("JeaTransportFailure:" + failureType)
{
    /// <summary>The classified status (never <see cref="JeaInvocationStatus.Ok"/>).</summary>
    public JeaInvocationStatus Status { get; } = status == JeaInvocationStatus.Ok ? JeaInvocationStatus.Malformed : status;

    /// <summary>Stable detail for logs.</summary>
    public string FailureType { get; } = failureType;
}

/// <summary>Opens constrained sessions. The real implementation is <see cref="WsManJeaTransport"/>; tests use a fake.</summary>
public interface IJeaTransport
{
    /// <summary>Opens a session to the endpoint's configuration on <paramref name="computerName"/> or throws (<see cref="JeaTransportException"/> when classified).</summary>
    public Task<IJeaSession> OpenAsync(string computerName, JeaEndpoint endpoint, TimeSpan openTimeout, CancellationToken cancellationToken);
}

/// <summary>One open constrained session.</summary>
public interface IJeaSession : IAsyncDisposable
{
    /// <summary>Runs the command and returns every output object's base value, or throws.</summary>
    public Task<IReadOnlyList<object?>> InvokeAsync(JeaCommand command, TimeSpan operationTimeout, CancellationToken cancellationToken);
}

/// <summary>
/// The transport-independent runner: enforces both timeouts itself (the transport's own WinRM timeouts are a second fence), classifies
/// failures per stage, requires exactly one text output within the size limit, and always closes the session. A session left behind by a
/// timeout is disposed in the background so the caller is not held by a remote host that stopped answering.
/// </summary>
public sealed class JeaRunner(IJeaTransport transport, ILogger<JeaRunner> logger, int maxPendingCloses = JeaRunner.DefaultMaxPendingCloses,
    TimeSpan? closeTimeout = null) : IJeaRunner
{
    /// <summary>Most session closes that may be unfinished at once; a hung close keeps its slot until it really ends.</summary>
    public const int DefaultMaxPendingCloses = 16;

    private readonly TimeSpan _closeTimeout = closeTimeout ?? TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _closeSlots = new(Math.Max(1, maxPendingCloses));

    /// <inheritdoc />
    public async Task<JeaInvocationResult> InvokeAsync(string computerName, JeaCommand command, JeaInvocationLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        if (!JeaCommand.IsComputerName(computerName))
        {
            // Never a connection attempt to a name that is not a plain host name or FQDN.
            return new(JeaInvocationStatus.Unreachable, null, timer.Elapsed, "invalidComputerName");
        }

        IJeaSession? session = null;
        bool abandoned = false;
        try
        {
            try
            {
                // An open that completes after we gave up is closed as soon as it arrives.
                session = await WithTimeoutAsync(t => transport.OpenAsync(computerName, command.Endpoint, limits.OpenTimeout, t), limits.OpenTimeout,
                    late => _ = late.ContinueWith(t => t.IsCompletedSuccessfully ? DisposeQuietlyAsync(t.Result) : Task.CompletedTask,
                        CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default), cancellationToken);
            }
            catch (TimeoutException)
            {
                return new(JeaInvocationStatus.Unreachable, null, timer.Elapsed, "openTimeout");
            }
            catch (JeaTransportException classified)
            {
                return new(classified.Status, null, timer.Elapsed, classified.FailureType);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Unclassified open failure: no usable WinRM answer.
                return new(JeaInvocationStatus.Unreachable, null, timer.Elapsed, exception.GetType().Name);
            }

            IReadOnlyList<object?> output;
            try
            {
                output = await WithTimeoutAsync(t => session.InvokeAsync(command, limits.OperationTimeout, t), limits.OperationTimeout,
                    _ => abandoned = true, cancellationToken);
            }
            catch (TimeoutException)
            {
                return new(JeaInvocationStatus.Timeout, null, timer.Elapsed, "operationTimeout");
            }
            catch (JeaTransportException classified)
            {
                return new(classified.Status, null, timer.Elapsed, classified.FailureType);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The function failed or the stream broke after the session opened: no valid answer.
                return new(JeaInvocationStatus.Malformed, null, timer.Elapsed, exception.GetType().Name);
            }

            if (output.Count != 1 || output[0] is not string text || text.Length == 0)
            {
                return new(JeaInvocationStatus.Malformed, null, timer.Elapsed, "outputShape");
            }

            if (Encoding.UTF8.GetByteCount(text) > limits.MaxOutputBytes)
            {
                return new(JeaInvocationStatus.Malformed, null, timer.Elapsed, "outputTooLarge");
            }

            return new(JeaInvocationStatus.Ok, text, timer.Elapsed, null);
        }
        finally
        {
            if (session is not null)
            {
                if (abandoned || cancellationToken.IsCancellationRequested)
                {
                    _ = DisposeQuietlyAsync(session);
                }
                else
                {
                    await DisposeQuietlyAsync(session);
                }
            }
        }
    }

    private static async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> start, TimeSpan timeout, Action<Task<T>> onAbandon,
        CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<T> work = start(bounded.Token);
        try
        {
            return await work.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            await bounded.CancelAsync();
            Abandon(work, onAbandon);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Abandon(work, onAbandon);
            throw;
        }
    }

    private static void Abandon<T>(Task<T> work, Action<Task<T>> onAbandon)
    {
        onAbandon(work);
        // Observe a late fault so it never surfaces as an unobserved task exception.
        _ = work.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private async Task DisposeQuietlyAsync(IJeaSession session)
    {
        try
        {
            // Closes that hang are bounded: a slot is held until the close really ends, and a close that finds none within the close
            // timeout is skipped (logged) instead of piling another blocked pool thread on top of the hung ones.
            if (!await _closeSlots.WaitAsync(_closeTimeout))
            {
                logger.LogWarning("JEA session close skipped: too many closes are still unfinished.");
                return;
            }

            // Closing a real session can block its calling thread (synchronous runspace disposal), so it always runs on a pool thread and
            // a waiting caller is released after the close timeout; the close itself is left to finish or fail on its own.
            var close = Task.Run(async () => await session.DisposeAsync());
            _ = close.ContinueWith(t =>
            {
                _ = t.Exception;
                _closeSlots.Release();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await close.WaitAsync(_closeTimeout);
        }
        catch (TimeoutException)
        {
            logger.LogDebug("JEA session close did not finish within the close timeout.");
        }
        catch (Exception exception)
        {
            logger.LogDebug("JEA session close failed. FailureType: {FailureType}.", exception.GetType().Name);
        }
    }
}
