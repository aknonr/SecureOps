using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class SqlOperationalRecordRepository
{
    /// <inheritdoc />
    public async Task<OperationalRecord> EvaluateAsync(Guid id, SdmEvaluationInput input, OperationalRecordCommandContext context,
        IAuditWriter auditWriter, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        OperationalRecord current = Map(await GetForUpdateAsync(connection, transaction, id, cancellationToken)
            ?? throw new KeyNotFoundException("Operational record was not found."));
        OperationalRecord evaluated = SdmEvaluationEvidence.Apply(current, input, DateTimeOffset.UtcNow);
        if (!ReferenceEquals(current, evaluated))
        {
            // The SQL audit insert shares the transaction; a queued writer cannot make this atomic.
            const string sql = """
                UPDATE ops.OperationalRecords SET SdmEvaluationJson = @Evidence, Classification = @Classification,
                    JiraEligible = 0, EligibilityReason = @EligibilityReason, WorkflowState = @State,
                    CorrelationId = @CorrelationId, UpdatedAt = @EvaluatedAt WHERE OperationalRecordId = @Id;
                INSERT INTO ops.OperationalRecordWorkflowHistory
                    (OperationalRecordId, WorkflowState, Actor, CorrelationId, OccurredAt, SdmEvaluationJson)
                VALUES (@Id, @State, 'system:sdm-evaluator', @CorrelationId, @EvaluatedAt, @Evidence);
                INSERT INTO audit.AuditLog (OccurredAt, Actor, Action, CorrelationId, DetailsJson)
                VALUES (@EvaluatedAt, 'system:sdm-evaluator', @Action, @CorrelationId, @Details);
                """;
            await connection.ExecuteAsync(Command(sql, new
            {
                Id = id,
                Evidence = SdmEvaluationEvidence.Serialize(evaluated.SdmEvaluation!),
                Classification = evaluated.Classification.ToString(),
                evaluated.EligibilityReason,
                State = evaluated.WorkflowState.ToString(),
                context.CorrelationId,
                evaluated.SdmEvaluation!.EvaluatedAt,
                Action = SdmEvaluationEvidence.AuditAction,
                Details = JsonSerializer.Serialize(SdmEvaluationEvidence.Audit(evaluated, context).Details)
            }, cancellationToken, transaction));
        }
        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(id, cancellationToken))!;
    }
}
