using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Sessions;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task SessionTerminalTransition_ConcurrentCalls_CommitOneAuditRow()
    {
        IConfiguration configuration = SessionConfiguration();
        SqlApplicationSessionRepository repository = new(configuration);
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ApplicationSession session = await InsertSessionAsync(connection, repository);
        string correlation = Guid.NewGuid().ToString("N");
        AuditEvent Audit(ApplicationSession terminal) => TerminalAudit(terminal, correlation);
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> EndAsync(SessionEndReason reason)
        {
            await ready.Task;
            return await repository.EndAsync(session.SessionId, DateTimeOffset.UtcNow, reason, default, Audit);
        }

        Task<bool>[] transitions = [EndAsync(SessionEndReason.Logout), EndAsync(SessionEndReason.Revoked)];
        ready.SetResult();
        bool[] results = await Task.WhenAll(transitions);

        results.Should().ContainSingle(result => result);
        ApplicationSession persisted = (await repository.GetAsync(session.SessionId, default))!;
        string reason = await connection.QuerySingleAsync<string>(
            "SELECT JSON_VALUE(DetailsJson, '$.endReason') FROM audit.AuditLog WHERE CorrelationId = @correlation", new { correlation });
        reason.Should().Be(persisted.EndReason.ToString());
        (await repository.EndAsync(session.SessionId, DateTimeOffset.UtcNow, SessionEndReason.AccessChanged, default, Audit)).Should().BeFalse();
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @correlation", new { correlation })).Should().Be(1);
    }

    [LocalResourceSqlFact]
    public async Task SessionTerminalTransition_BulkAndExpired_CommitOneAuditPerSession()
    {
        IConfiguration configuration = SessionConfiguration();
        SqlApplicationSessionRepository repository = new(configuration);
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ApplicationSession first = await InsertSessionAsync(connection, repository);
        ApplicationSession second = await InsertSessionAsync(connection, repository, first.UserId);
        string correlation = Guid.NewGuid().ToString("N");
        AuditEvent Audit(ApplicationSession terminal) => TerminalAudit(terminal, correlation);

        (await repository.EndActiveForUserAsync(first.UserId, DateTimeOffset.UtcNow, SessionEndReason.AccessDisabled, default, Audit)).Should().HaveCount(2);
        (await repository.EndActiveForUserAsync(first.UserId, DateTimeOffset.UtcNow, SessionEndReason.AccessChanged, default, Audit)).Should().BeEmpty();
        ApplicationSession expired = await InsertSessionAsync(connection, repository, expired: true);
        IReadOnlyList<ApplicationSession> ended = await repository.EndExpiredAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-30), 1000, default, Audit);
        ended.Should().Contain(item => item.SessionId == expired.SessionId);
        (await repository.EndAsync(expired.SessionId, DateTimeOffset.UtcNow, SessionEndReason.Logout, default, Audit)).Should().BeFalse();

        foreach (ApplicationSession session in new[] { first, second, expired })
        {
            (await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @correlation AND JSON_VALUE(DetailsJson, '$.sessionId') = @id",
                new { correlation, id = session.SessionId.ToString("D") })).Should().Be(1);
        }
    }

    [LocalResourceSqlFact]
    public async Task SessionTerminalTransition_AuditInsertFailure_RollsBackSessionChanges()
    {
        IConfiguration configuration = SessionConfiguration();
        SqlApplicationSessionRepository repository = new(configuration);
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ApplicationSession session = await InsertSessionAsync(connection, repository);
        string correlation = Guid.NewGuid().ToString("N");
        AuditEvent UnavailableAudit(ApplicationSession terminal) => new()
        {
            Actor = "synthetic-session",
            Action = new string('x', 129),
            CorrelationId = correlation
        };

        Func<Task> end = () => repository.EndAsync(session.SessionId, DateTimeOffset.UtcNow, SessionEndReason.Revoked, default, UnavailableAudit);
        await end.Should().ThrowAsync<AuditWriteUnavailableException>();
        (await repository.GetAsync(session.SessionId, default))!.IsActive.Should().BeTrue();

        Func<Task> bulk = () => repository.EndActiveForUserAsync(session.UserId, DateTimeOffset.UtcNow, SessionEndReason.AccessChanged, default, UnavailableAudit);
        await bulk.Should().ThrowAsync<AuditWriteUnavailableException>();
        (await repository.GetAsync(session.SessionId, default))!.IsActive.Should().BeTrue();

        ApplicationSession expired = await InsertSessionAsync(connection, repository, expired: true);
        Func<Task> expiry = () => repository.EndExpiredAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-30), 1000, default, UnavailableAudit);
        await expiry.Should().ThrowAsync<AuditWriteUnavailableException>();
        (await repository.GetAsync(expired.SessionId, default))!.IsActive.Should().BeTrue();
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @correlation", new { correlation })).Should().Be(0);
    }

    private static IConfiguration SessionConfiguration() => new ConfigurationBuilder()
        .AddConfiguration(Configuration())
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:Provider"] = "SqlServer" })
        .Build();

    private static async Task<ApplicationSession> InsertSessionAsync(SqlConnection connection, SqlApplicationSessionRepository repository, Guid? userId = null, bool expired = false)
    {
        Guid id = userId ?? Guid.NewGuid();
        if (userId is null)
        {
            await connection.ExecuteAsync("INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus) VALUES(@id,@identity,'test','Approved')",
                new { id, identity = $"synthetic-session:{id:N}" });
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ApplicationSession session = new(Guid.NewGuid(), id, now.AddHours(-2), now.AddHours(-1),
            expired ? now.AddMinutes(-1) : now.AddHours(1), null, null, "test", 1);
        await repository.InsertAsync(session, default);
        return session;
    }

    private static AuditEvent TerminalAudit(ApplicationSession session, string correlation) => new()
    {
        Actor = "synthetic-session",
        Action = "SyntheticTerminalTransition",
        CorrelationId = correlation,
        Details = new { sessionId = session.SessionId, endReason = session.EndReason.ToString() }
    };
}
