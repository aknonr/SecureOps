using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Ui.Services;

/// <summary>Keeps only the latest resource search, even when a transport ignores cancellation.</summary>
public sealed class ResourceQueryState : IDisposable
{
    private CancellationTokenSource? _cancellation;
    private long _sequence;
    private bool _disposed;

    /// <summary>Last confirmed page, retained while another search is running.</summary>
    public ResourcePage? Page { get; private set; }
    /// <summary>Failure from the latest request only.</summary>
    public UiProblem? Problem { get; private set; }
    /// <summary>Whether the latest request is pending.</summary>
    public bool Loading { get; private set; }

    /// <summary>Loads a query without allowing older successes or failures to replace newer results.</summary>
    public async Task LoadAsync(ResourceQuery query, Func<ResourceQuery, CancellationToken, Task<ResourcePage>> load)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long sequence = ++_sequence;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        Loading = true;
        Problem = null;
        try
        {
            ResourcePage page = await load(query, token);
            if (sequence == _sequence)
            {
                Page = page;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Deliberately superseded requests are not user-facing failures.
        }
        catch (SecureOpsApiException exception)
        {
            if (sequence == _sequence)
            {
                Problem = exception.Problem;
            }
        }
        finally
        {
            if (sequence == _sequence)
            {
                Loading = false;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        ++_sequence;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }
}
