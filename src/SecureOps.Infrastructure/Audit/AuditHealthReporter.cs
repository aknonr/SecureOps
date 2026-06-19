using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Audit;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Reports safe audit-store health metadata.
/// </summary>
public sealed class AuditHealthReporter
{
    private readonly AuditOptions _options;
    private readonly IAuditQueueMetrics _queueMetrics;
    private readonly IAuditStoreHealthState _healthState;

    /// <summary>
    /// Initializes an audit health reporter.
    /// </summary>
    /// <param name="options">Audit options.</param>
    /// <param name="queueMetrics">Audit queue metrics.</param>
    /// <param name="healthState">Audit store health state.</param>
    public AuditHealthReporter(
        IOptions<AuditOptions> options,
        IAuditQueueMetrics queueMetrics,
        IAuditStoreHealthState healthState)
    {
        _options = options.Value;
        _queueMetrics = queueMetrics;
        _healthState = healthState;
    }

    /// <summary>
    /// Gets safe health metadata.
    /// </summary>
    /// <returns>Audit-store health response.</returns>
    public AuditStoreHealthResponse GetHealth()
    {
        return new AuditStoreHealthResponse(
            _healthState.IsHealthy ? "Healthy" : "Unhealthy",
            _options.Provider,
            !string.Equals(_options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase),
            _options.FailClosed,
            _queueMetrics.Enabled,
            _queueMetrics.Capacity,
            _queueMetrics.Count,
            _healthState.LastErrorCode);
    }
}
