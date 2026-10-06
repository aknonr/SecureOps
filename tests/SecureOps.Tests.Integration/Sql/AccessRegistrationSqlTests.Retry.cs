using System.Diagnostics;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Access;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class AccessRegistrationSqlTests
{
    [AccessRegistrationSqlFact]
    public async Task EnsureUserAsync_OneDeadlock_RetriesRolledBackRegistrationOnce()
        => await VerifyDeadlockRetryAsync(1);

    [AccessRegistrationSqlFact]
    public async Task EnsureUserAsync_TwoDeadlocks_PropagatesSecondVictimWithoutPartialRows()
        => await VerifyDeadlockRetryAsync(2);

    private async Task VerifyDeadlockRetryAsync(int victims)
    {
        string connectionString = RegistrationConnection();
        string suffix = Guid.NewGuid().ToString("N");
        string identity = $"g34-retry:{suffix}";
        string table = $"security.G34Locks_{suffix}";
        string sequence = $"security.G34Attempts_{suffix}";
        string trigger = $"security.TR_G34Retry_{suffix}";
        await using var sql = new SqlConnection(connectionString);
        await sql.ExecuteAsync($"CREATE TABLE {table} (Id int PRIMARY KEY, Value int NOT NULL); INSERT INTO {table} VALUES (1,0),(2,0),(3,0),(4,0); CREATE SEQUENCE {sequence} AS int START WITH 1;");
        await sql.ExecuteAsync($"""
            CREATE TRIGGER {trigger} ON security.Users AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE CorporateIdentity = '{identity}')
                BEGIN
                    DECLARE @Attempt int = NEXT VALUE FOR {sequence};
                    IF @Attempt <= {victims}
                    BEGIN
                        SET DEADLOCK_PRIORITY LOW;
                        UPDATE {table} WITH (ROWLOCK) SET Value = Value + 1 WHERE Id = 2 * @Attempt;
                        DECLARE @Value int;
                        SELECT @Value = Value FROM {table} WITH (UPDLOCK, ROWLOCK) WHERE Id = 2 * @Attempt - 1;
                    END;
                END;
            END;
            """);
        var blockers = new List<(SqlConnection Connection, SqlTransaction Transaction, int Session)>();
        Task<EnsureAccessUserResult>? registration = null;
        int requestRows = await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM security.AccessRequests;");
        int historyRows = await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM security.AccessRequestHistory;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            for (int attempt = 1; attempt <= victims; attempt++)
            {
                var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(timeout.Token);
                var transaction = (SqlTransaction)await connection.BeginTransactionAsync(timeout.Token);
                int session = await connection.ExecuteScalarAsync<int>("SELECT @@SPID;", transaction: transaction);
                blockers.Add((connection, transaction, session));
                await connection.ExecuteAsync($"SET DEADLOCK_PRIORITY HIGH; UPDATE {table} WITH (ROWLOCK) SET Value = Value + 1 WHERE Id = @Id;", new { Id = 2 * attempt - 1 }, transaction);
            }
            SqlAccessRepository repository = RegistrationRepository(connectionString);
            registration = repository.EnsureUserAsync(new CorporatePrincipal(identity, "synthetic"), true, TimeSpan.FromMinutes(5), timeout.Token);
            for (int attempt = 1; attempt <= victims; attempt++)
            {
                (SqlConnection connection, SqlTransaction transaction, int session) = blockers[attempt - 1];
                var watch = Stopwatch.StartNew();
                while (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @Session", new { Session = session }) == 0)
                {
                    if (registration.IsCompleted || watch.Elapsed > TimeSpan.FromSeconds(20))
                    {
                        throw new InvalidOperationException("Registration did not reach the controlled lock cycle.");
                    }
                }
                // Both transactions now own one key and request the other's key: a real engine 1205.
                await connection.ExecuteAsync($"UPDATE {table} WITH (ROWLOCK) SET Value = Value + 1 WHERE Id = @Id;", new { Id = 2 * attempt }, transaction, commandTimeout: 20);
                await transaction.CommitAsync(timeout.Token);
            }
            if (victims == 1)
            {
                EnsureAccessUserResult result = await registration;
                result.UserCreated.Should().BeTrue();
                result.RequestCreated.Should().BeTrue();
                (await repository.ListRequestsForUserAsync(result.User.Id, default)).Should().ContainSingle();
                (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM security.AccessRequestHistory WHERE AccessRequestId=@Id", new { Id = result.LatestRequest!.Id })).Should().Be(1);
            }
            else
            {
                await FluentActions.Awaiting(async () => await registration).Should().ThrowAsync<SqlException>().Where(exception => exception.Number == 1205);
                (await repository.GetUserAsync(identity, default)).Should().BeNull();
                (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM security.AccessRequests;")).Should().Be(requestRows);
                (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM security.AccessRequestHistory;")).Should().Be(historyRows);
            }
            int attempts = await sql.ExecuteScalarAsync<int>("SELECT CONVERT(int, current_value) FROM sys.sequences WHERE object_id=OBJECT_ID(@Sequence)", new { Sequence = sequence });
            attempts.Should().Be(2, "only one registration retry is permitted");
            output.WriteLine($"ControlledVictims={victims}; RegistrationAttempts={attempts}");
        }
        finally
        {
            timeout.Cancel();
            foreach ((SqlConnection Connection, SqlTransaction Transaction, int Session) blocker in blockers)
            {
                await blocker.Transaction.DisposeAsync();
                await blocker.Connection.DisposeAsync();
            }
            if (registration is not null)
            {
                try
                { await registration; }
                catch (Exception) { /* Observe failures before removing test objects. */ }
            }
            await sql.ExecuteAsync($"DROP TRIGGER {trigger}; DROP SEQUENCE {sequence}; DROP TABLE {table};");
        }
    }

    [AccessRegistrationSqlFact]
    public async Task EnsureUserAsync_NonDeadlockSqlError_PropagatesWithoutRetryOrPartialRows()
    {
        string connectionString = RegistrationConnection();
        string suffix = Guid.NewGuid().ToString("N");
        string identity = $"g34-error:{suffix}";
        string sequence = $"security.G34Attempts_{suffix}";
        string trigger = $"security.TR_G34Failure_{suffix}";
        await using var sql = new SqlConnection(connectionString);
        await sql.ExecuteAsync($"CREATE SEQUENCE {sequence} AS int START WITH 1;");
        await sql.ExecuteAsync($"""
            CREATE TRIGGER {trigger} ON security.Users AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE CorporateIdentity = '{identity}')
                BEGIN
                    DECLARE @Attempt int = NEXT VALUE FOR {sequence};
                    THROW 51390, 'Synthetic registration validation failure.', 1;
                END;
            END;
            """);
        try
        {
            SqlAccessRepository repository = RegistrationRepository(connectionString);
            await FluentActions.Awaiting(() => repository.EnsureUserAsync(new CorporatePrincipal(identity, "synthetic"), true, TimeSpan.FromMinutes(5), default))
                .Should().ThrowAsync<SqlException>().Where(exception => exception.Number == 51390);
            (await repository.GetUserAsync(identity, default)).Should().BeNull();
            (await sql.ExecuteScalarAsync<int>("SELECT CONVERT(int, current_value) FROM sys.sequences WHERE object_id=OBJECT_ID(@Sequence)", new { Sequence = sequence })).Should().Be(1);
        }
        finally { await sql.ExecuteAsync($"DROP TRIGGER {trigger}; DROP SEQUENCE {sequence};"); }
    }

    private static string RegistrationConnection()
    {
        string connectionString = Environment.GetEnvironmentVariable("SECUREOPS_ACCESS_REGISTRATION_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(connectionString);
        guard.DataSource.Should().Be("(localdb)\\SecureOpsAccessG34");
        guard.InitialCatalog.Should().StartWith("SecureOps_AccessG34_");
        return connectionString;
    }

    private static SqlAccessRepository RegistrationRepository(string connectionString)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = connectionString }).Build());

    [AccessRegistrationSqlFact]
    public async Task EnsureUserAsync_RejectedOrDisabledUser_DoesNotCreateReplacementRequestOrGrantAccess()
    {
        string connectionString = RegistrationConnection();
        SqlAccessRepository repository = RegistrationRepository(connectionString);
        await using var sql = new SqlConnection(connectionString);
        var rejectedPrincipal = new CorporatePrincipal($"g34-rejected:{Guid.NewGuid():N}", "synthetic");
        EnsureAccessUserResult initial = await repository.EnsureUserAsync(rejectedPrincipal, true, TimeSpan.FromMinutes(5), default);
        await sql.ExecuteAsync("UPDATE security.AccessRequests SET Status='Rejected', DecidedByCorporateIdentity='system:synthetic' WHERE AccessRequestId=@Id", new { Id = initial.LatestRequest!.Id });
        EnsureAccessUserResult rejected = await repository.EnsureUserAsync(rejectedPrincipal, true, TimeSpan.FromMinutes(5), default);
        rejected.RequestCreated.Should().BeFalse();
        rejected.LatestRequest!.Id.Should().Be(initial.LatestRequest.Id);
        rejected.PendingRequest.Should().BeNull();
        rejected.User.Capabilities.Should().BeEmpty();
        (await repository.ListRequestsForUserAsync(initial.User.Id, default)).Should().ContainSingle();

        var disabledPrincipal = new CorporatePrincipal($"g34-disabled:{Guid.NewGuid():N}", "synthetic");
        EnsureAccessUserResult noRequest = await repository.EnsureUserAsync(disabledPrincipal, false, TimeSpan.FromMinutes(5), default);
        noRequest.LatestRequest.Should().BeNull();
        await sql.ExecuteAsync("UPDATE security.Users SET AccessStatus='Disabled' WHERE UserId=@Id", new { noRequest.User.Id });
        EnsureAccessUserResult disabled = await repository.EnsureUserAsync(disabledPrincipal, true, TimeSpan.FromMinutes(5), default);
        disabled.RequestCreated.Should().BeFalse();
        disabled.LatestRequest.Should().BeNull();
        disabled.User.Status.Should().Be(SecureOps.Domain.Access.AccessStatus.Disabled);
        disabled.User.Capabilities.Should().BeEmpty();
    }

    [AccessRegistrationSqlFact]
    public async Task EnsureUserAsync_Cancelled_DoesNotPersistRegistration()
    {
        SqlAccessRepository repository = RegistrationRepository(RegistrationConnection());
        var principal = new CorporatePrincipal($"g34-cancelled:{Guid.NewGuid():N}", "synthetic");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => repository.EnsureUserAsync(principal, true, TimeSpan.FromMinutes(5), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        (await repository.GetUserAsync(principal.Identifier, default)).Should().BeNull();
    }
}
