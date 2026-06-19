namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Tracks runtime health of the configured audit store.
/// </summary>
public interface IAuditStoreHealthState
{
    /// <summary>
    /// Whether the audit store is currently considered healthy.
    /// </summary>
    public bool IsHealthy { get; }

    /// <summary>
    /// Last safe machine-readable failure code.
    /// </summary>
    public string? LastErrorCode { get; }

    /// <summary>
    /// Last failure timestamp, if any.
    /// </summary>
    public DateTimeOffset? LastFailureAt { get; }

    /// <summary>
    /// Marks the audit store as healthy.
    /// </summary>
    public void MarkHealthy();

    /// <summary>
    /// Marks the audit store as unhealthy.
    /// </summary>
    /// <param name="errorCode">Safe machine-readable error code.</param>
    public void MarkUnhealthy(string errorCode);
}

/// <summary>
/// Thread-safe runtime audit-store health state.
/// </summary>
public sealed class AuditStoreHealthState : IAuditStoreHealthState
{
    private readonly object _sync = new();
    private bool _isHealthy = true;
    private string? _lastErrorCode;
    private DateTimeOffset? _lastFailureAt;

    /// <inheritdoc />
    public bool IsHealthy
    {
        get
        {
            lock (_sync)
            {
                return _isHealthy;
            }
        }
    }

    /// <inheritdoc />
    public string? LastErrorCode
    {
        get
        {
            lock (_sync)
            {
                return _lastErrorCode;
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? LastFailureAt
    {
        get
        {
            lock (_sync)
            {
                return _lastFailureAt;
            }
        }
    }

    /// <inheritdoc />
    public void MarkHealthy()
    {
        lock (_sync)
        {
            _isHealthy = true;
            _lastErrorCode = null;
            _lastFailureAt = null;
        }
    }

    /// <inheritdoc />
    public void MarkUnhealthy(string errorCode)
    {
        lock (_sync)
        {
            _isHealthy = false;
            _lastErrorCode = errorCode;
            _lastFailureAt = DateTimeOffset.UtcNow;
        }
    }
}
