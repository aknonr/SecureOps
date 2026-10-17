using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SecureOps.Infrastructure.Audit;

internal static class SqlAuditRow
{
    private const int _commandTimeoutSeconds = 15;

    internal static async Task InsertAsync(SqlConnection connection, SqlTransaction? transaction, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit.AuditLog
                (OccurredAt, Actor, Action, AlertId, ServerName, CorrelationId, DetailsJson, SourceIp)
            VALUES
                (@OccurredAt, @Actor, @Action, @AlertId, @ServerName, @CorrelationId, @DetailsJson, @SourceIp);
            """;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    auditEvent.OccurredAt,
                    auditEvent.Actor,
                    auditEvent.Action,
                    auditEvent.AlertId,
                    auditEvent.ServerName,
                    auditEvent.CorrelationId,
                    DetailsJson = auditEvent.Details is null ? null : JsonSerializer.Serialize(auditEvent.Details, AuditJson.SerializerOptions),
                    auditEvent.SourceIp
                },
                transaction,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AuditWriteUnavailableException("Audit row could not be persisted.", exception);
        }
    }
}
