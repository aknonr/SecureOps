using System.Data;
using Dapper;
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
        const string sql = """
            INSERT INTO audit.AuditLog
                (OccurredAt, Actor, Action, AlertId, ServerName, CorrelationId, DetailsJson, SourceIp)
            VALUES
                (@OccurredAt, @Actor, @Action, @AlertId, @ServerName, @CorrelationId, @DetailsJson, @SourceIp);
            """;

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (AuditEvent auditEvent in events)
        {
            CommandDefinition command = new(
                sql,
                new
                {
                    auditEvent.OccurredAt,
                    auditEvent.Actor,
                    auditEvent.Action,
                    auditEvent.AlertId,
                    auditEvent.ServerName,
                    auditEvent.CorrelationId,
                    DetailsJson = auditEvent.Details is null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(auditEvent.Details, AuditJson.SerializerOptions),
                    auditEvent.SourceIp
                },
                commandType: CommandType.Text,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);
        }
    }
}
