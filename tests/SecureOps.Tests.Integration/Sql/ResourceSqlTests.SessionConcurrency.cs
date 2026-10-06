using System.Security.Claims;
using System.Text.Json;
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
using SecureOps.Infrastructure.Sessions;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    public static TheoryData<string, string> SessionRacePaths => new()
    {
        { "Logout", "Revoke" }, { "Logout", "Idle" }, { "Logout", "Absolute" },
        { "Logout", "AccessChanged" }, { "Logout", "AccessDisabled" }, { "Logout", "Sweep" },
        { "Revoke", "Idle" }, { "Revoke", "Absolute" }, { "Revoke", "AccessChanged" },
        { "Revoke", "AccessDisabled" }, { "Revoke", "Sweep" }, { "Idle", "Absolute" },
        { "Idle", "AccessDisabled" }, { "Absolute", "AccessChanged" }, { "Idle", "Sweep" }
    };

    [Theory]
    [MemberData(nameof(SessionRacePaths))]
    public async Task Sessions_ConcurrentInMemoryTermination_ReturnsWinnerAndWritesOnlyWinningAudit(string first, string second)
        => await AssertSessionRaceAsync(await SessionRaceFixture.CreateAsync(false), first, second);

    [LocalResourceSqlTheory]
    [MemberData(nameof(SessionRacePaths))]
    public async Task Sessions_ConcurrentSqlTermination_ReturnsWinnerAndWritesOnlyWinningAudit(string first, string second)
        => await AssertSessionRaceAsync(await SessionRaceFixture.CreateAsync(true), first, second);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sessions_ConcurrentInMemoryRepository_OnlyOneContenderAudits(bool nonAtomic)
    {
        SessionRaceFixture fixture = await SessionRaceFixture.CreateAsync(false, nonAtomic);
        await AssertRepositoryRaceAsync(fixture);
    }

    [LocalResourceSqlFact]
    public async Task Sessions_ConcurrentSqlRepositories_OnlyOneContenderAudits()
        => await AssertRepositoryRaceAsync(await SessionRaceFixture.CreateAsync(true));

    [Theory]
    [InlineData("Logout")]
    [InlineData("Revoke")]
    public async Task Sessions_InMemoryTermination_FirstReadAlreadyEndedReturnsCommittedState(string path)
        => await AssertAlreadyEndedAsync(await SessionRaceFixture.CreateAsync(false), path);

    [LocalResourceSqlTheory]
    [InlineData("Logout")]
    [InlineData("Revoke")]
    public async Task Sessions_SqlTermination_FirstReadAlreadyEndedReturnsCommittedState(string path)
        => await AssertAlreadyEndedAsync(await SessionRaceFixture.CreateAsync(true), path);

    private static async Task AssertAlreadyEndedAsync(SessionRaceFixture fixture, string path)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        (await fixture.TerminateAsync("AccessDisabled", fixture.Repository(), token)).EndedCount.Should().Be(1);
        ApplicationSession persisted = (await fixture.Repository().GetAsync(fixture.Session.SessionId, token))!;

        RaceOutcome loser = await fixture.TerminateAsync(path, fixture.Repository(), token);

        loser.Session!.Disposition.Should().Be(ApplicationSessionDisposition.Ended);
        loser.Session.Session.Should().BeEquivalentTo(persisted);
        loser.Error.Should().BeNull();
        (await fixture.TerminalAuditsAsync()).Should().ContainSingle();
    }

    public static TheoryData<string, string> SessionRereadFailures => new(
        from path in new[] { "Logout", "Revoke", "Idle" }
        from failure in new[] { "Missing", "Active", "Reasonless", "Exception" }
        select (path, failure));

    [Theory]
    [MemberData(nameof(SessionRereadFailures))]
    public async Task Sessions_LosingTermination_UnverifiableRereadFailsClosed(string path, string failure)
    {
        SessionRaceFixture fixture = await SessionRaceFixture.CreateAsync(false);
        IApplicationSessionRepository repository = Substitute.For<IApplicationSessionRepository>();
        ApplicationSession? reread = failure switch
        {
            "Missing" => null,
            "Active" => fixture.Session,
            _ => fixture.Session with { EndedAtUtc = fixture.Now, EndReason = null }
        };
        repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<ApplicationSession?>(fixture.Session),
            _ => failure == "Exception" ? Task.FromException<ApplicationSession?>(new IOException("Synthetic reread failure."))
                : Task.FromResult(reread));
        repository.EndWithAuditAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<SessionEndReason>(), Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .Returns(false);

        RaceOutcome outcome = await fixture.TerminateAsync(path, repository, TestContext.Current.CancellationToken);

        outcome.Session!.Disposition.Should().Be(ApplicationSessionDisposition.StoreUnavailable);
        outcome.Error.Should().Be(OperationalErrorCodes.SessionStoreUnavailable);
        outcome.Session.Session.Should().BeNull();
        (await fixture.TerminalAuditsAsync()).Should().BeEmpty();
    }

    private static async Task AssertRepositoryRaceAsync(SessionRaceFixture fixture)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>[] contenders = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            await start.Task.WaitAsync(token);
            SessionEndReason reason = index % 2 == 0 ? SessionEndReason.Logout : SessionEndReason.Revoked;
            return await fixture.Repository().EndWithAuditAsync(fixture.Session.SessionId, fixture.Now, reason,
                new AuditEvent
                {
                    Actor = fixture.User.Id.ToString("D"),
                    Action = TerminalAction(reason),
                    Details = new { sessionId = fixture.Session.SessionId, endReason = reason.ToString() }
                }, token);
        }, token)).ToArray();
        start.SetResult();
        bool[] transitions = await Task.WhenAll(contenders).WaitAsync(TimeSpan.FromSeconds(30), token);

        transitions.Count(ended => ended).Should().Be(1);
        ApplicationSession persisted = (await fixture.Repository().GetAsync(fixture.Session.SessionId, token))!;
        (await fixture.TerminalAuditsAsync()).Should().ContainSingle().Which
            .Should().Be(new TerminalAudit(TerminalAction(persisted.EndReason!.Value), persisted.EndReason.Value.ToString()));
    }

    private static async Task AssertSessionRaceAsync(SessionRaceFixture fixture, string first, string second)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int remaining = 2;
        async Task RendezvousAsync(CancellationToken cancellation)
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            { ready.SetResult(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellation);
        }

        // Both services reach persistence with Active snapshots before the real repositories contend.
        IApplicationSessionRepository CoordinatedRepository()
        {
            IApplicationSessionRepository inner = fixture.Repository();
            IApplicationSessionRepository repository = Substitute.For<IApplicationSessionRepository>();
            repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => inner.GetAsync(call.Arg<Guid>(), call.Arg<CancellationToken>()));
            repository.EndWithAuditAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<SessionEndReason>(), Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    await RendezvousAsync(call.Arg<CancellationToken>());
                    return await inner.EndWithAuditAsync(call.Arg<Guid>(), call.Arg<DateTimeOffset>(), call.Arg<SessionEndReason>(), call.Arg<AuditEvent>(), call.Arg<CancellationToken>());
                });
            repository.EndActiveForUserAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<SessionEndReason>(), Arg.Any<Func<ApplicationSession, AuditEvent>>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    await RendezvousAsync(call.Arg<CancellationToken>());
                    return await inner.EndActiveForUserAsync(call.Arg<Guid>(), call.Arg<DateTimeOffset>(), call.Arg<SessionEndReason>(), call.Arg<Func<ApplicationSession, AuditEvent>>(), call.Arg<CancellationToken>());
                });
            repository.EndExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<Func<ApplicationSession, AuditEvent>>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    await RendezvousAsync(call.Arg<CancellationToken>());
                    return await inner.EndExpiredAsync(call.ArgAt<DateTimeOffset>(0), call.ArgAt<DateTimeOffset>(1), call.Arg<int>(), call.Arg<Func<ApplicationSession, AuditEvent>>(), call.Arg<CancellationToken>());
                });
            repository.ListActiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => inner.ListActiveAsync(call.ArgAt<DateTimeOffset>(0), call.ArgAt<DateTimeOffset>(1), call.ArgAt<int>(2), call.ArgAt<int>(3), call.Arg<CancellationToken>()));
            return repository;
        }

        Task<RaceOutcome>[] contenders = new[] { first, second }.Select(path => Task.Run(
            () => fixture.TerminateAsync(path, CoordinatedRepository(), token), token)).ToArray();
        RaceOutcome[] outcomes = await Task.WhenAll(contenders).WaitAsync(TimeSpan.FromSeconds(30), token);
        ApplicationSession persisted = (await fixture.Repository().GetAsync(fixture.Session.SessionId, token))!;
        persisted.IsActive.Should().BeFalse();
        foreach ((RaceOutcome outcome, string path) in outcomes.Zip(new[] { first, second }))
        {
            if (path is "AccessDisabled" or "Sweep")
            {
                outcome.Error.Should().BeNull();
                outcome.EndedCount.Should().BeInRange(0, 1);
                continue;
            }

            outcome.Session!.Session.Should().BeEquivalentTo(persisted);
            if (path is "Logout" or "Revoke")
            {
                outcome.Session.Disposition.Should().Be(ApplicationSessionDisposition.Ended);
                outcome.Session.ErrorCode.Should().BeNull();
            }
            else
            {
                outcome.Session.Disposition.Should().Be(persisted.EndReason switch
                {
                    SessionEndReason.IdleTimeout => ApplicationSessionDisposition.IdleExpired,
                    SessionEndReason.AbsoluteTimeout => ApplicationSessionDisposition.AbsoluteExpired,
                    SessionEndReason.AccessChanged => ApplicationSessionDisposition.AccessChanged,
                    SessionEndReason.AccessDisabled => ApplicationSessionDisposition.AccessDisabled,
                    _ => ApplicationSessionDisposition.Revoked
                });
                outcome.Session.ErrorCode.Should().Be(persisted.EndReason switch
                {
                    SessionEndReason.IdleTimeout or SessionEndReason.AbsoluteTimeout => OperationalErrorCodes.SessionExpired,
                    SessionEndReason.AccessDisabled => OperationalErrorCodes.AccessDisabled,
                    _ => OperationalErrorCodes.SessionRevoked
                });
            }
        }
        (await fixture.TerminalAuditsAsync()).Should().ContainSingle().Which
            .Should().Be(new TerminalAudit(TerminalAction(persisted.EndReason!.Value), persisted.EndReason.Value.ToString()));
    }

    private static string TerminalAction(SessionEndReason reason) => reason switch
    {
        SessionEndReason.Logout => AuditActions.ApplicationSessionLoggedOut,
        SessionEndReason.Revoked => AuditActions.ApplicationSessionRevoked,
        SessionEndReason.IdleTimeout => AuditActions.ApplicationSessionIdleTimedOut,
        SessionEndReason.AbsoluteTimeout => AuditActions.ApplicationSessionAbsoluteTimedOut,
        SessionEndReason.AccessChanged => AuditActions.ApplicationSessionAccessChanged,
        _ => AuditActions.ApplicationSessionAccessDisabled
    };

    private sealed record RaceOutcome(ApplicationSessionResult? Session, int? EndedCount, string? Error);
    private sealed record TerminalAudit(string Action, string EndReason);

    private sealed class SessionRaceFixture(IConfiguration? configuration, InMemoryApplicationSessionRepository? memory, InMemoryAuditWriter? memoryAudit, IAuditWriter audit, ApplicationUser user, ApplicationSession session)
    {
        public ApplicationUser User { get; } = user;
        public ApplicationSession Session { get; } = session;
        public DateTimeOffset Now => Session.StartedAtUtc.AddMinutes(1);
        public IApplicationSessionRepository Repository() => configuration is null ? memory! : new SqlApplicationSessionRepository(configuration);

        public static async Task<SessionRaceFixture> CreateAsync(bool sql, bool nonAtomic = false)
        {
            IConfiguration? configuration = sql ? Configuration() : null;
            var userId = Guid.NewGuid();
            if (sql)
            {
                await using SqlConnection connection = new(configuration!.GetConnectionString("SecureOpsDb"));
                userId = (await CreateActorAsync(connection)).UserId;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ApplicationUser user = new(userId, "synthetic:session-race", "test", AccessStatus.Approved, now, now, null, 1, [], []);
            ApplicationSession session = new(Guid.NewGuid(), userId, now, now, now.AddHours(8), null, null, "Test", 1);
            InMemoryAuditWriter? memoryAudit = sql ? null : new();
            IAuditWriter audit = sql ? new DirectAuditWriter(new SqlAuditWriter(configuration!), new AuditStoreHealthState())
                : nonAtomic ? new NonAtomicRaceAuditWriter(memoryAudit!) : memoryAudit!;
            SessionRaceFixture fixture = new(configuration, sql ? null : new(audit), memoryAudit, audit, user, session);
            await fixture.Repository().InsertAsync(session, TestContext.Current.CancellationToken);
            return fixture;
        }

        public async Task<RaceOutcome> TerminateAsync(string path, IApplicationSessionRepository repository, CancellationToken token)
        {
            IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
            access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
                .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(User with { Version = path == "AccessChanged" ? 2 : 1 }, null, false, false)));
            DateTimeOffset now = path == "Absolute" ? Session.StartedAtUtc.AddHours(12)
                : path is "Idle" or "Sweep" ? Session.StartedAtUtc.AddMinutes(30) : Now;
            ApplicationSessionService service = new(access, repository, audit, Options.Create(new SessionSecurityOptions()),
                new SessionClock(now), NullLogger<ApplicationSessionService>.Instance);
            AccessOperationContext context = new(User.Id.ToString("D"), "synthetic-race-" + path, null);
            ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, User.CorporateIdentity)], "Test"));
            if (path == "AccessDisabled")
            {
                ApplicationSessionTerminationResult batch = await service.EndUserSessionsAsync(User.Id, SessionEndReason.AccessDisabled, context, token);
                return new(null, batch.EndedCount, batch.ErrorCode);
            }
            if (path == "Sweep")
            {
                ApplicationSessionListResult sweep = await service.ListActiveAsync(1, 100, context, token);
                return new(null, 0, sweep.ErrorCode);
            }
            ApplicationSessionResult result = path switch
            {
                "Logout" => await service.LogoutAsync(Session.SessionId, context, token),
                "Revoke" => await service.RevokeAsync(Session.SessionId, "Synthetic termination", context, token),
                _ => await service.ValidateOrStartAsync(principal, Session.SessionId, context, token)
            };
            return new(result, null, result.ErrorCode);
        }

        public async Task<TerminalAudit[]> TerminalAuditsAsync()
        {
            if (configuration is null)
            {
                return memoryAudit!.Events.Where(item => item.Action != AuditActions.ApplicationSessionsViewed)
                    .Select(item => new TerminalAudit(item.Action, JsonSerializer.SerializeToElement(item.Details).GetProperty("endReason").GetString()!)).ToArray();
            }
            await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
            return (await connection.QueryAsync<TerminalAudit>(new CommandDefinition("""
                SELECT Action, JSON_VALUE(CASE WHEN ISJSON(DetailsJson) = 1 THEN DetailsJson END, '$.endReason') AS EndReason
                FROM audit.AuditLog WHERE Actor = @Actor
                    AND JSON_VALUE(CASE WHEN ISJSON(DetailsJson) = 1 THEN DetailsJson END, '$.sessionId') = @SessionId;
                """, new { Actor = User.Id.ToString("D"), SessionId = Session.SessionId.ToString("D") }, cancellationToken: TestContext.Current.CancellationToken))).ToArray();
        }
    }

    private sealed class NonAtomicRaceAuditWriter(IAuditWriter inner) : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => inner.WriteAsync(auditEvent, cancellationToken);
    }
}

public sealed class LocalResourceSqlTheoryAttribute : TheoryAttribute
{
    public LocalResourceSqlTheoryAttribute([System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null, [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECUREOPS_SQL_TEST_CONNECTION")))
        { Skip = "NOT RUN: explicit isolated LocalDB connection is unavailable."; }
    }
}
