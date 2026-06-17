using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// SQL Server audit writer for production append-only audit logging.
/// </summary>
public sealed class SqlAuditWriter : IAuditWriter
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new SQL audit writer.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <exception cref="InvalidOperationException">Thrown when the SecureOps connection string is missing.</exception>
    public SqlAuditWriter(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SecureOps")
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOps is required for SQL audit logging.");
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit.AuditLog
                (OccurredAt, Actor, Action, AlertId, ServerName, CorrelationId, DetailsJson, SourceIp)
            VALUES
                (@OccurredAt, @Actor, @Action, @AlertId, @ServerName, @CorrelationId, @DetailsJson, @SourceIp);
            """;

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

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
                DetailsJson = auditEvent.Details is null ? null : JsonSerializer.Serialize(auditEvent.Details),
                auditEvent.SourceIp
            },
            commandType: CommandType.Text,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
