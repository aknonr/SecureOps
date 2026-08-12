using Microsoft.Extensions.Logging;

namespace SecureOps.Ui.Services;

/// <summary>
/// Circuit-scoped cache over <c>GET /api/v1/access/me</c>.
/// </summary>
public sealed class CurrentAccessProvider : ICurrentAccessProvider, IDisposable
{
    /// <summary>
    /// How long a snapshot is reused before it is reloaded.
    /// </summary>
    /// <remarks>
    /// Short enough that an approval or role change appears without the operator reloading the page,
    /// long enough that navigating between pages does not call the API on every render. Commands that
    /// can change access refresh explicitly rather than waiting for this to lapse.
    /// </remarks>
    private static readonly TimeSpan _snapshotLifetime = TimeSpan.FromSeconds(60);

    private readonly IAccessApiClient _accessApi;
    private readonly ILogger<CurrentAccessProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AccessSnapshot? _snapshot;

    /// <summary>
    /// Initializes a new access provider.
    /// </summary>
    /// <param name="accessApi">Access API client.</param>
    /// <param name="logger">Logger.</param>
    public CurrentAccessProvider(IAccessApiClient accessApi, ILogger<CurrentAccessProvider> logger)
    {
        _accessApi = accessApi;
        _logger = logger;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public Task<AccessSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(force: false, cancellationToken);

    /// <inheritdoc />
    public Task<AccessSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(force: true, cancellationToken);

    private async Task<AccessSnapshot> LoadAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force && TryGetFresh(out AccessSnapshot? cached))
        {
            return cached!;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Re-check inside the gate: concurrent first renders queue here, and only the first should
            // issue the request. The rest reuse whatever it produced.
            if (!force && TryGetFresh(out AccessSnapshot? current))
            {
                return current!;
            }

            AccessSnapshot snapshot;

            try
            {
                snapshot = new AccessSnapshot(
                    await _accessApi.GetCurrentAsync(cancellationToken),
                    Problem: null,
                    DateTimeOffset.UtcNow);
            }
            catch (SecureOpsApiException ex)
            {
                _logger.LogWarning(
                    "Application access could not be resolved. Code: {Code}. CorrelationId: {CorrelationId}.",
                    ex.Problem.Code,
                    ex.Problem.CorrelationId);

                snapshot = new AccessSnapshot(Access: null, ex.Problem, DateTimeOffset.UtcNow);
            }

            _snapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke();
        }
    }

    private bool TryGetFresh(out AccessSnapshot? snapshot)
    {
        snapshot = _snapshot;
        return snapshot is not null
            && DateTimeOffset.UtcNow - snapshot.LoadedAt < _snapshotLifetime;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
