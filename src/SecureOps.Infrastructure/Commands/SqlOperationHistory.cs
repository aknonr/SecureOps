using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Commands;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Infrastructure.Commands;

/// <summary>Bounded record evidence reader; the service caller authorizes the module and this reader enforces OCO ownership.</summary>
public sealed class SqlOperationHistory(IConfiguration configuration)
{
    /// <summary>Returns immutable facts only after required read audit succeeds; never backfills old actors.</summary>
    public async Task<OperationHistoryResponse?> ReadAsync(string recordType, Guid recordId, Guid owner, string correlation, CancellationToken token)
    {
        if (!string.Equals(configuration["Access:RepositoryProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        { return new([]); }
        await using var sql = new SqlConnection(configuration.GetConnectionString("SecureOpsDb"));
        string exists = recordType switch
        {
            "Announcement" => "SELECT COUNT(*) FROM announcements.DraftRevisions WHERE Id=@recordId AND OwnerId=@owner",
            "InUse" => "SELECT COUNT(*) FROM ops.InUseRecords WHERE Id=@recordId",
            "OperationalRecord" => "SELECT COUNT(*) FROM ops.OperationalRecords WHERE OperationalRecordId=@recordId",
            _ => throw new ArgumentException("Unknown record type.", nameof(recordType))
        };
        if (await sql.ExecuteScalarAsync<int>(new CommandDefinition(exists, new { recordId, owner }, commandTimeout: 15, cancellationToken: token)) == 0)
        { return null; }
        string[] documents = (await sql.QueryAsync<string>(new CommandDefinition("""
            SELECT TOP(100) EvidenceJson FROM ops.OperationEvents WHERE RecordType=@recordType AND RecordId=@id
            ORDER BY OccurredAt DESC,EventId;
            """, new { recordType, id = recordId.ToString("D") }, commandTimeout: 15, cancellationToken: token))).ToArray();
        string? closure = recordType == "OperationalRecord" ? await sql.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT ClosureEvidenceJson FROM ops.JiraTransfers WHERE OperationalRecordId=@recordId", new { recordId }, commandTimeout: 15, cancellationToken: token)) : null;
        await sql.ExecuteAsync(new CommandDefinition("""
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(SYSUTCDATETIME(),@actor,'OperationHistoryRead',@correlation,@json);
            """, new { actor = owner.ToString("D"), correlation, json = JsonSerializer.Serialize(new { recordType, recordId, Count = documents.Length }) }, commandTimeout: 15, cancellationToken: token));
        return new(documents.Select(doc => JsonSerializer.Deserialize<OperationEvidence>(doc)!).ToArray(),
            closure is null ? null : JsonSerializer.Deserialize<SourceClosureObservation>(closure));
    }
}
