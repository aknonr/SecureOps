using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

public sealed partial class SqlInUseExecutionStore
{
    /// <summary>Records one local human confirmation; never dispatches, releases the duplicate fence or claims source verification.</summary>
    public async Task<InUseResult<InUseExecution>> ConfirmClosureAsync(Guid recordId, Guid actor, string label,
        ConfirmInUseClosureRequest request, CancellationToken token)
    {
        if (!Configured)
        { return InUseResult<InUseExecution>.Fail("InUseCompletionUnavailable"); }
        await using SqlConnection sql = new(Connection);
        await sql.OpenAsync(token);
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await SqlAccessRepository.LockAdministrationAsync(sql, tx, token);
        if (!await AuthorizedAsync(sql, tx, actor, token))
        { return InUseResult<InUseExecution>.Fail("AccessDenied"); }
        Row? row = await sql.QuerySingleOrDefaultAsync<Row>(Command(
            "SELECT * FROM ops.InUseExecutions WITH(UPDLOCK,HOLDLOCK) WHERE OperationId=@OperationId AND RecordId=@recordId;",
            new { request.OperationId, recordId }, tx, token));
        if (row is null)
        { return InUseResult<InUseExecution>.Fail("InUseNotFound"); }
        InUseExecution operation = await MapAsync(sql, tx, row, token);
        if (row.Revision != request.ExpectedRevision || request.SourceCode != operation.SourceCode
            || !InUseClosureVerification.CanConfirm(operation))
        { return new(null, "InUseConflict", "İşlem veya doğrulama durumu değişti. Kayıtlı sonucu yenileyin; kapatma isteğini tekrarlamayın."); }
        InUseStepEvidence evidence = new("ManualVerification", "ManuallyConfirmed", null,
            operation.VerificationMode == "WasasActivityManual" ? "OperatorAttestedWasasActivityCompleted" : "OperatorAttestedSourceClosed",
            DateTimeOffset.UtcNow, "SecureOps.Api")
        { ConfirmedBy = actor, ConfirmedByLabel = label };
        await UpdateAsync(sql, tx, row, Read<InUseExecutionIntent>(row.IntentJson), row.State, row.Step, evidence, token);
        await tx.CommitAsync(token);
        return new(operation with { Revision = operation.Revision + 1, Evidence = [.. operation.Evidence, evidence] });
    }
}
