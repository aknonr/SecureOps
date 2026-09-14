namespace SecureOps.Ui.Services;

/// <summary>Circuit-owned cancellation and generation guard; no content or authorization cache.</summary>
public sealed class AnnouncementPreviewRequests : IDisposable
{
    private CancellationTokenSource? _pending;
    private long _generation;
    /// <summary>Also invalidates not-yet-loaded sandbox frames after an edit.</summary>
    public long Generation => _generation;
    /// <summary>Invalidates earlier responses even when their transport ignores cancellation.</summary>
    public (long Generation, CancellationToken Token) Begin(CancellationToken lifetime)
    {
        Cancel();
        _pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        return (_generation, _pending.Token);
    }
    /// <summary>Only a still-visible, current request may publish HTML or an error.</summary>
    public bool IsCurrent(long generation) => generation == _generation && _pending is { IsCancellationRequested: false };
    /// <summary>Used on edit, hide, save, conflict, navigation and disposal.</summary>
    public void Cancel()
    { _generation++; _pending?.Cancel(); _pending?.Dispose(); _pending = null; }
    /// <inheritdoc />
    public void Dispose() => Cancel();
}
