using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Commands;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Commands;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

public sealed partial class SqlOperationalRecordRepository
{
    private static async Task AppendWorkflowEventAsync(SqlConnection sql, SqlTransaction transaction, Guid id,
        OperationalRecordWorkflowState state, string actor, string correlation, CancellationToken token)
    {
        TransferEvidence transfer = await sql.QuerySingleAsync<TransferEvidence>(Command("""
            SELECT t.JiraTransferId AS CommandId,t.InitiatorJson,t.ClosureEvidenceJson,t.JiraIssueKey,t.ReconciliationRequired,
                t.InputVersion AS Version
            FROM ops.JiraTransfers t JOIN ops.OperationalRecords r ON r.OperationalRecordId=t.OperationalRecordId WHERE t.OperationalRecordId=@id;
            """, new { id }, token, transaction));
        OperationActor? initiator = transfer.InitiatorJson is null ? null : JsonSerializer.Deserialize<OperationActor>(transfer.InitiatorJson);
        if (state == OperationalRecordWorkflowState.CreateRequested)
        {
            // A new explicit create attempt resolves a real saved actor. No identity/profile backfill for old transfers.
            initiator = await sql.QuerySingleOrDefaultAsync<OperationActor>(Command("""
                SELECT u.UserId AS Id,'Human' AS Kind,u.DisplayName,u.LoginName AS Account,u.Mail FROM security.Users u
                WHERE u.CorporateIdentity=@actor AND u.AccessStatus='Approved' AND EXISTS(
                    SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                    WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value='OperationalRecords.CreateJira');
                """, new { actor }, token, transaction)) ?? throw new InvalidOperationException("Persisted authorized workflow actor required.");
            await sql.ExecuteAsync(Command("UPDATE ops.JiraTransfers SET InitiatorJson=@json WHERE OperationalRecordId=@id",
                new { id, json = JsonSerializer.Serialize(initiator) }, token, transaction));
        }
        if (initiator is null || transfer.Version is null)
        { return; }
        SourceClosureObservation? closure = transfer.ClosureEvidenceJson is null ? null : JsonSerializer.Deserialize<SourceClosureObservation>(transfer.ClosureEvidenceJson);
        string outcome = state switch
        {
            OperationalRecordWorkflowState.CreateRequested => "Requested",
            OperationalRecordWorkflowState.CreatingJira or OperationalRecordWorkflowState.ClosingOperationalRecord => "Dispatched",
            OperationalRecordWorkflowState.JiraCreated => "Acknowledged",
            OperationalRecordWorkflowState.Completed => closure?.State == "VerifiedClosed" ? "VerifiedClosed" : "Uncertain",
            _ => transfer.ReconciliationRequired ? "Uncertain" : "Failed"
        };
        await SqlOperationEvidence.AppendAsync(sql, transaction, new(Guid.NewGuid(), transfer.CommandId, "OperationalRecord", id.ToString("D"),
            state.ToString(), transfer.Version.Value, DateTimeOffset.UtcNow, outcome, initiator,
            new("SecureOps.Api", Environment.MachineName + ":" + Environment.ProcessId, Environment.UserDomainName + "\\" + Environment.UserName),
            null, correlation, ExternalReference: transfer.JiraIssueKey, SourceCloser: closure?.AuthoritativeCloser,
            VerificationExecutor: outcome == "VerifiedClosed" ? new("SourceClosureVerifier", Environment.MachineName + ":" + Environment.ProcessId,
                Environment.UserDomainName + "\\" + Environment.UserName) : null), token);
    }
    private sealed record TransferEvidence(Guid CommandId, string? InitiatorJson, string? ClosureEvidenceJson,
        string? JiraIssueKey, bool ReconciliationRequired, long? Version);
}
