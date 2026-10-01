using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.Persistence;

/// <summary>Executes the bounded SQL readiness probe.</summary>
public interface ISqlPersistenceProbe
{
    /// <summary>Opens the configured database and executes a non-mutating scalar query.</summary>
    public Task ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>SQL Server implementation of the persistence readiness probe.</summary>
public sealed class SqlPersistenceProbe : ISqlPersistenceProbe
{
    private const int _probeTimeoutSeconds = 5;
    private readonly string _connectionString;

    /// <summary>Initializes the probe from server-owned runtime configuration.</summary>
    public SqlPersistenceProbe(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_probeTimeoutSeconds));

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(timeout.Token);
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        command.CommandTimeout = _probeTimeoutSeconds;
        object? result = await command.ExecuteScalarAsync(timeout.Token);
        if (!Equals(result, 1))
        {
            throw new InvalidOperationException("SQL persistence readiness probe returned an unexpected result.");
        }
    }
}

/// <summary>Reports SQL readiness without exposing connection or exception details.</summary>
public sealed class SqlPersistenceHealthReporter
{
    private readonly bool _sqlServerConfigured;
    private readonly ISqlPersistenceProbe _probe;
    private readonly ILogger<SqlPersistenceHealthReporter> _logger;

    /// <summary>Initializes the SQL persistence readiness reporter.</summary>
    public SqlPersistenceHealthReporter(
        IConfiguration configuration,
        ISqlPersistenceProbe probe,
        ILogger<SqlPersistenceHealthReporter> logger)
    {
        _sqlServerConfigured = SqlPersistenceConfigurationValidator.IsSqlServerConfigured(configuration);
        _probe = probe;
        _logger = logger;
    }

    /// <summary>Returns NotConfigured, Healthy, or Unhealthy readiness state.</summary>
    public async Task<SqlPersistenceHealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        if (!_sqlServerConfigured)
        {
            return new SqlPersistenceHealthResponse("NotConfigured", false, null);
        }

        try
        {
            await _probe.ProbeAsync(cancellationToken);
            return new SqlPersistenceHealthResponse("Healthy", true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogWarning("Configured SQL persistence readiness probe failed. ErrorCode: {ErrorCode}", OperationalErrorCodes.PersistenceUnavailable);
            return new SqlPersistenceHealthResponse("Unhealthy", true, OperationalErrorCodes.PersistenceUnavailable);
        }
    }
}
