using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// SQL Server audit writer for production append-only audit logging.
/// </summary>
public sealed class SqlAuditWriter : IAuditEventSink
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new SQL audit writer.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <exception cref="InvalidOperationException">Thrown when the SecureOpsDb connection string is missing.</exception>
    public SqlAuditWriter(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for SQL audit logging.");
    }

    /// <inheritdoc />
    public async Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (AuditEvent auditEvent in events)
        {
            await SqlAuditRow.InsertAsync(connection, null, auditEvent, cancellationToken);
        }
    }
}
