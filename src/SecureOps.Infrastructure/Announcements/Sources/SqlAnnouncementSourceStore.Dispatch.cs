using Dapper;
using Microsoft.Data.SqlClient;

namespace SecureOps.Infrastructure.Announcements.Sources;

public sealed partial class SqlAnnouncementSourceStore
{
    /// <summary>Bounded durable intents, including abandoned executions. Terminal jobs are excluded.</summary>
    public async Task<IReadOnlyList<Guid>> DueAsync(CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        return (await connection.QueryAsync<Guid>(Command("""
            SELECT TOP(100) JobId FROM announcements.SourceJobs
            WHERE DispatchAfter<=SYSUTCDATETIME() AND (State=N'Queued' OR
                (State=N'Running' AND (LeaseUntil IS NULL OR LeaseUntil<=SYSUTCDATETIME())))
            ORDER BY DispatchAfter,JobId;
            """, new { }, null, token))).AsList();
    }

    /// <summary>A short dispatch reservation bounds duplicates and recovers even without acknowledgement.</summary>
    public async Task<DateTimeOffset?> ReserveDispatchAsync(Guid jobId, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        return await connection.QuerySingleOrDefaultAsync<DateTimeOffset?>(Command("""
            DECLARE @reserved TABLE (DispatchAfter datetimeoffset);
            UPDATE announcements.SourceJobs SET DispatchAfter=DATEADD(second,60,SYSUTCDATETIME())
            OUTPUT inserted.DispatchAfter INTO @reserved WHERE JobId=@jobId AND DispatchAfter<=SYSUTCDATETIME()
                AND (State=N'Queued' OR (State=N'Running' AND (LeaseUntil IS NULL OR LeaseUntil<=SYSUTCDATETIME())));
            SELECT DispatchAfter FROM @reserved;
            """, new { jobId }, null, token));
    }

    /// <summary>A late acknowledgement cannot overwrite a newer dispatch reservation.</summary>
    public async Task AcknowledgeDispatchAsync(Guid jobId, DateTimeOffset reservation, string hangfireJobId, CancellationToken token)
    {
        await using SqlConnection connection = new(_connection);
        await connection.ExecuteAsync(Command("""
            UPDATE announcements.SourceJobs SET HangfireJobId=@hangfireJobId
            WHERE JobId=@jobId AND DispatchAfter=@reservation;
            """, new { jobId, reservation, hangfireJobId }, null, token));
    }
}
