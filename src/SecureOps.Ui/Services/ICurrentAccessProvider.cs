namespace SecureOps.Ui.Services;

/// <summary>
/// Circuit-scoped access to the caller's application access projection.
/// </summary>
/// <remarks>
/// The layout, navigation, and several pages all need the same projection on first render. This
/// provider resolves it once per circuit and shares the result so a single page load does not produce
/// one <c>GET /access/me</c> call per component.
/// </remarks>
public interface ICurrentAccessProvider
{
    /// <summary>
    /// Raised when the cached snapshot is replaced, so components can re-render.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets the current snapshot, loading it if it is absent or stale.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current access snapshot.</returns>
    public Task<AccessSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the cached snapshot and reloads it from the API.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Freshly loaded access snapshot.</returns>
    public Task<AccessSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}
