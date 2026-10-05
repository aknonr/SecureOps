using System.Security.Claims;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Domain.Sessions;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Resources;
using SecureOps.Infrastructure.Sessions;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    private static readonly string[] _terminationPaths = ["Logout", "Revoke", "Idle", "Absolute", "Sweep", "AccessDisabled"];

    [LocalResourceSqlFact]
    public async Task Sessions_SecondBatchAuditFailureRollsBackEarlierInsertAndBothSessionUpdates()
    {
        SessionFixture fixture = await SessionFixture.CreateAsync();
        ApplicationSession second = await fixture.StartAnotherAsync();
        await using SqlConnection connection = new(fixture.ConnectionString);
        string trigger = "TR_SessionBatchTest_" + Guid.NewGuid().ToString("N");
        await connection.ExecuteAsync(new CommandDefinition($"""
            CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS
            BEGIN
                IF EXISTS(SELECT 1 FROM inserted WHERE Actor = '{fixture.UserId:D}' AND Action = '{AuditActions.ApplicationSessionAccessDisabled}')
                    AND (SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = '{fixture.UserId:D}' AND Action = '{AuditActions.ApplicationSessionAccessDisabled}') >= 2
                    THROW 51092, 'Synthetic second terminal audit failure.', 1;
            END;
            """, cancellationToken: TestContext.Current.CancellationToken));
        try
        {
            (await fixture.TerminateAsync("AccessDisabled")).Should().Be(OperationalErrorCodes.AuditStoreUnavailable);
            await fixture.AssertUnchangedAsync(connection);
            (await fixture.Repository.GetAsync(second.SessionId, TestContext.Current.CancellationToken)).Should().BeEquivalentTo(second);
            (await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor=@Actor AND Action=@Action",
                new { Actor = fixture.UserId.ToString("D"), Action = AuditActions.ApplicationSessionAccessDisabled }, cancellationToken: TestContext.Current.CancellationToken))).Should().Be(0);
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition($"DROP TRIGGER audit.[{trigger}];", cancellationToken: CancellationToken.None));
        }
    }

    [LocalResourceSqlFact]
    public async Task Sessions_CancellationInsideTransactionRollsBackUpdateWithoutAudit()
    {
        SessionFixture fixture = await SessionFixture.CreateAsync();
        await using SqlConnection connection = new(fixture.ConnectionString);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Func<Task> terminate = () => fixture.Repository.EndActiveForUserAsync(fixture.UserId, DateTimeOffset.UtcNow, SessionEndReason.AccessDisabled,
            _ =>
            {
                cancellation.Cancel();
                return new AuditEvent { Actor = fixture.UserId.ToString("D"), Action = AuditActions.ApplicationSessionAccessDisabled };
            }, cancellation.Token);

        await terminate.Should().ThrowAsync<OperationCanceledException>();
        await fixture.AssertUnchangedAsync(connection);
    }

    [LocalResourceSqlFact]
    public async Task Sessions_RepositoryPreservesTrueFalseTransitionResult()
    {
        SessionFixture fixture = await SessionFixture.CreateAsync();
        AuditEvent audit = new() { Actor = fixture.UserId.ToString("D"), Action = AuditActions.ApplicationSessionLoggedOut };

        (await fixture.Repository.EndWithAuditAsync(fixture.Session.SessionId, DateTimeOffset.UtcNow, SessionEndReason.Logout, audit, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await fixture.Repository.EndWithAuditAsync(fixture.Session.SessionId, DateTimeOffset.UtcNow, SessionEndReason.Revoked, audit, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await fixture.Repository.GetAsync(fixture.Session.SessionId, TestContext.Current.CancellationToken))!.EndReason.Should().Be(SessionEndReason.Logout);
    }

    [LocalResourceSqlFact]
    public async Task Sessions_AuditInsertFailureRollsBackEveryTerminationPath()
    {
        foreach (string path in _terminationPaths)
        {
            SessionFixture fixture = await SessionFixture.CreateAsync();
            await using SqlConnection connection = new(fixture.ConnectionString);
            string trigger = "TR_SessionAuditTest_" + Guid.NewGuid().ToString("N");
            await connection.ExecuteAsync(new CommandDefinition($"""
                CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS
                BEGIN
                    IF EXISTS(SELECT 1 FROM inserted WHERE Actor = '{fixture.UserId:D}' AND Action <> '{AuditActions.ApplicationSessionStarted}' AND Action <> '{AuditActions.ApplicationSessionsViewed}')
                        THROW 51090, 'Synthetic terminal audit failure.', 1;
                END;
                """, cancellationToken: TestContext.Current.CancellationToken));
            try
            {
                (await fixture.TerminateAsync(path)).Should().Be(OperationalErrorCodes.AuditStoreUnavailable, path);
                await fixture.AssertUnchangedAsync(connection);
            }
            finally
            {
                await connection.ExecuteAsync(new CommandDefinition($"DROP TRIGGER audit.[{trigger}];", cancellationToken: CancellationToken.None));
            }
        }
    }

    [LocalResourceSqlFact]
    public async Task Sessions_UpdateFailureWritesNoTerminalAudit()
    {
        foreach (string path in _terminationPaths)
        {
            SessionFixture fixture = await SessionFixture.CreateAsync();
            await using SqlConnection connection = new(fixture.ConnectionString);
            string trigger = "TR_SessionUpdateTest_" + Guid.NewGuid().ToString("N");
            await connection.ExecuteAsync(new CommandDefinition($"""
                CREATE TRIGGER security.[{trigger}] ON security.ApplicationSessions AFTER UPDATE AS
                BEGIN
                    IF EXISTS(SELECT 1 FROM inserted WHERE UserId = '{fixture.UserId:D}' AND EndedAtUtc IS NOT NULL)
                        THROW 51091, 'Synthetic session update failure.', 1;
                END;
                """, cancellationToken: TestContext.Current.CancellationToken));
            try
            {
                (await fixture.TerminateAsync(path)).Should().Be(OperationalErrorCodes.SessionStoreUnavailable, path);
                await fixture.AssertUnchangedAsync(connection);
            }
            finally
            {
                await connection.ExecuteAsync(new CommandDefinition($"DROP TRIGGER security.[{trigger}];", cancellationToken: CancellationToken.None));
            }
        }
    }

    [LocalResourceSqlFact]
    public async Task Sessions_SuccessCommitsTerminationAndAuditTogether()
    {
        foreach (string path in _terminationPaths)
        {
            SessionFixture fixture = await SessionFixture.CreateAsync();
            await using SqlConnection connection = new(fixture.ConnectionString);

            (await fixture.TerminateAsync(path)).Should().Be(path is "Idle" or "Absolute" ? OperationalErrorCodes.SessionExpired : null, path);

            ApplicationSession ended = (await fixture.Repository.GetAsync(fixture.Session.SessionId, TestContext.Current.CancellationToken))!;
            ended.IsActive.Should().BeFalse(path);
            ended.EndReason.Should().Be(path switch
            {
                "Logout" => SessionEndReason.Logout,
                "Revoke" => SessionEndReason.Revoked,
                "Absolute" => SessionEndReason.AbsoluteTimeout,
                "AccessDisabled" => SessionEndReason.AccessDisabled,
                _ => SessionEndReason.IdleTimeout
            });
            (await fixture.TerminalAuditCountAsync(connection)).Should().Be(1, path);
        }
    }

    private sealed class SessionFixture
    {
        private readonly SessionClock _clock = new(DateTimeOffset.UtcNow);
        private readonly ClaimsPrincipal _principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "synthetic:session-sql")], "Test"));
        private readonly AccessOperationContext _context;
        private readonly ApplicationSessionService _service;

        private SessionFixture(string connectionString, SqlApplicationSessionRepository repository, ApplicationUser user, IAuditWriter audit)
        {
            ConnectionString = connectionString;
            Repository = repository;
            UserId = user.Id;
            _context = new(user.Id.ToString("D"), "synthetic-session-atomic", null);
            IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
            access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(AccessServiceResult<EnsureAccessUserResult>.Success(new(user, null, false, false))));
            _service = new(access, repository, audit, Options.Create(new SessionSecurityOptions()), _clock, NullLogger<ApplicationSessionService>.Instance);
        }

        public string ConnectionString { get; }
        public Guid UserId { get; }
        public SqlApplicationSessionRepository Repository { get; }
        public ApplicationSession Session { get; private set; } = null!;

        public async Task<ApplicationSession> StartAnotherAsync() =>
            (await _service.ValidateOrStartAsync(_principal, null, _context, TestContext.Current.CancellationToken)).Session!;

        public static async Task<SessionFixture> CreateAsync()
        {
            IConfiguration configuration = Configuration();
            string connectionString = configuration.GetConnectionString("SecureOpsDb")!;
            await using SqlConnection connection = new(connectionString);
            ResourceActor actor = await CreateActorAsync(connection);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ApplicationUser user = new(actor.UserId, "synthetic:session-sql", "test", AccessStatus.Approved, now, now, null, 1, [], []);
            DirectAuditWriter audit = new(new SqlAuditWriter(configuration), new AuditStoreHealthState());
            SessionFixture fixture = new(connectionString, new(configuration), user, audit);
            ApplicationSessionResult started = await fixture._service.ValidateOrStartAsync(fixture._principal, null, fixture._context, TestContext.Current.CancellationToken);
            started.IsSuccess.Should().BeTrue();
            fixture.Session = started.Session!;
            return fixture;
        }

        public async Task<string?> TerminateAsync(string path)
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            switch (path)
            {
                case "Logout":
                    return (await _service.LogoutAsync(Session.SessionId, _context, token)).ErrorCode;
                case "Revoke":
                    return (await _service.RevokeAsync(Session.SessionId, "Synthetic approved termination", _context, token)).ErrorCode;
                case "Idle":
                case "Absolute":
                    _clock.Advance(path == "Idle" ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(12));
                    return (await _service.ValidateOrStartAsync(_principal, Session.SessionId, _context, token)).ErrorCode;
                case "Sweep":
                    _clock.Advance(TimeSpan.FromMinutes(30));
                    return (await _service.ListActiveAsync(1, 100, _context, token)).ErrorCode;
                case "AccessDisabled":
                    return (await _service.EndUserSessionsAsync(UserId, SessionEndReason.AccessDisabled, _context, token)).ErrorCode;
                default:
                    throw new ArgumentOutOfRangeException(nameof(path));
            }
        }

        public async Task AssertUnchangedAsync(SqlConnection connection)
        {
            ApplicationSession current = (await Repository.GetAsync(Session.SessionId, TestContext.Current.CancellationToken))!;
            current.Should().BeEquivalentTo(Session);
            current.IsActive.Should().BeTrue();
            (await TerminalAuditCountAsync(connection)).Should().Be(0);
        }

        public Task<int> TerminalAuditCountAsync(SqlConnection connection) => connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM audit.AuditLog
            WHERE Actor = @Actor AND Action NOT IN (@Started, @Viewed)
                AND JSON_VALUE(DetailsJson, '$.sessionId') = @SessionId;
            """, new { Actor = UserId.ToString("D"), Started = AuditActions.ApplicationSessionStarted, Viewed = AuditActions.ApplicationSessionsViewed, SessionId = Session.SessionId.ToString("D") }, cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed class SessionClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
