using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Commands;

namespace SecureOps.Infrastructure.Commands;

/// <summary>Writes typed evidence and the required audit atomically with the caller's state change.</summary>
public static class SqlOperationEvidence
{
    /// <summary>No remote effect may precede a successfully committed intent event.</summary>
    public static async Task AppendAsync(SqlConnection connection, SqlTransaction transaction, OperationEvidence evidence, CancellationToken token, bool writeAudit = true)
    {
        string json = JsonSerializer.Serialize(evidence);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ops.OperationEvents(EventId,CommandId,RecordType,RecordId,ActorId,Action,Outcome,OccurredAt,EvidenceJson)
            VALUES(@Id,@CommandId,@RecordType,@RecordId,@actor,@Action,@Outcome,@OccurredAt,@json);
            IF @writeAudit=1 INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(@OccurredAt,@actorText,@Action,@CorrelationId,@audit);
            """, new
        {
            evidence.Id,
            evidence.CommandId,
            evidence.RecordType,
            evidence.RecordId,
            evidence.Action,
            evidence.Outcome,
            evidence.OccurredAt,
            evidence.CorrelationId,
            actor = evidence.Initiator.Id,
            actorText = evidence.Initiator.Id.ToString("D"),
            json,
            writeAudit,
            audit = JsonSerializer.Serialize(new { evidence.SchemaVersion, evidence.Id, evidence.CommandId, evidence.RecordType, evidence.RecordId, evidence.InputVersion, evidence.Outcome })
        }, transaction, commandTimeout: 15, cancellationToken: token));
    }
}
