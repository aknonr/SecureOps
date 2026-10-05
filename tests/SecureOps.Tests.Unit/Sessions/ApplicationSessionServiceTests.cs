using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
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

namespace SecureOps.Tests.Unit.Sessions;

public sealed class ApplicationSessionServiceTests
{
    [Theory]
    [InlineData("Logout")]
    [InlineData("Revoke")]
    [InlineData("Idle")]
    [InlineData("Absolute")]
    [InlineData("Sweep")]
    [InlineData("AccessDisabled")]
    public async Task Termination_AuditFailureLeavesSessionActive(string path)
    {
        ToggleAuditWriter audit = new();
        Fixture fixture = new(auditWriter: audit);
        ApplicationSession session = (await fixture.StartAsync()).Session!;
        audit.FailTerminalWrite = true;

        string? error = await fixture.TerminateAsync(path, session.SessionId);

        error.Should().Be(OperationalErrorCodes.AuditStoreUnavailable);
        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        audit.Inner.Events.Should().NotContain(item => IsTerminal(item.Action));
    }

    [Theory]
    [InlineData("Logout")]
    [InlineData("Revoke")]
    [InlineData("Idle")]
    [InlineData("Absolute")]
    [InlineData("Sweep")]
    [InlineData("AccessDisabled")]
    public async Task Termination_UpdateFailureWritesNoTerminalAudit(string path)
    {
        Fixture fixture = new();
        ApplicationSession session = (await fixture.StartAsync()).Session!;
        fixture.Repository.FailTermination = true;

        string? error = await fixture.TerminateAsync(path, session.SessionId);

        error.Should().Be(OperationalErrorCodes.SessionStoreUnavailable);
        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        fixture.Audit.Events.Should().NotContain(item => IsTerminal(item.Action));
    }

    [Theory]
    [InlineData("Logout")]
    [InlineData("Revoke")]
    [InlineData("Idle")]
    [InlineData("Absolute")]
    [InlineData("Sweep")]
    [InlineData("AccessDisabled")]
    public async Task Termination_SuccessCommitsStateAndExactlyOneAudit(string path)
    {
        Fixture fixture = new();
        ApplicationSession session = (await fixture.StartAsync()).Session!;

        string? error = await fixture.TerminateAsync(path, session.SessionId);

        error.Should().Be(path is "Idle" or "Absolute" ? OperationalErrorCodes.SessionExpired : null);
        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeFalse();
        fixture.Audit.Events.Should().ContainSingle(item => IsTerminal(item.Action));
    }

    private static bool IsTerminal(string action) => action != AuditActions.ApplicationSessionStarted
        && action != AuditActions.ApplicationSessionsViewed;

    [Theory]
    [InlineData("Sweep")]
    [InlineData("AccessDisabled")]
    public async Task BatchTermination_AuditFailurePublishesNeitherSession(string path)
    {
        ToggleAuditWriter audit = new();
        Fixture fixture = new(auditWriter: audit);
        ApplicationSession first = (await fixture.StartAsync()).Session!;
        ApplicationSession second = (await fixture.StartAsync()).Session!;
        audit.FailTerminalWrite = true;

        (await fixture.TerminateAsync(path, first.SessionId)).Should().Be(OperationalErrorCodes.AuditStoreUnavailable);

        (await fixture.Repository.GetAsync(first.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        (await fixture.Repository.GetAsync(second.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        audit.Inner.Events.Should().NotContain(item => IsTerminal(item.Action));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchTermination_CancellationBeforeAuditPublishesNeitherStateNorAudit(bool nonAtomic)
    {
        NonAtomicAuditWriter writer = new();
        Fixture fixture = new(auditWriter: nonAtomic ? writer : null);
        ApplicationSession session = (await fixture.StartAsync()).Session!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Func<Task> terminate = () => fixture.Repository.EndActiveForUserAsync(fixture.User.Id, fixture.Time.GetUtcNow(), SessionEndReason.AccessDisabled,
            _ =>
            {
                cancellation.Cancel();
                return new AuditEvent { Actor = "synthetic", Action = AuditActions.ApplicationSessionAccessDisabled };
            }, cancellation.Token);

        await terminate.Should().ThrowAsync<OperationCanceledException>();

        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        fixture.Audit.Events.Should().NotContain(item => IsTerminal(item.Action));
        writer.Inner.Events.Should().NotContain(item => IsTerminal(item.Action));
    }

    [Fact]
    public async Task Termination_NonAtomicPendingAuditIsAwaitedAndAsyncFailureKeepsSessionActive()
    {
        TaskCompletionSource pendingAudit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IAuditWriter writer = Substitute.For<IAuditWriter>();
        writer.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        writer.WriteAsync(Arg.Is<AuditEvent>(item => item.Action == AuditActions.ApplicationSessionLoggedOut),
            Arg.Any<CancellationToken>()).Returns(pendingAudit.Task);
        Fixture fixture = new(auditWriter: writer);
        ApplicationSession session = (await fixture.StartAsync()).Session!;

        Task<string?> termination = fixture.TerminateAsync("Logout", session.SessionId);
        try
        {
            termination.IsCompleted.Should().BeFalse("session termination must await the audit write");
        }
        finally
        {
            pendingAudit.SetException(new IOException("Synthetic asynchronous audit failure."));
        }

        (await termination).Should().Be(OperationalErrorCodes.AuditStoreUnavailable);
        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("Logout")]
    [InlineData("Revoke")]
    [InlineData("Idle")]
    [InlineData("Absolute")]
    [InlineData("Sweep")]
    [InlineData("AccessDisabled")]
    public async Task Termination_NonAtomicAuditWriterWritesAuditBeforePublishingState(string path)
    {
        NonAtomicAuditWriter writer = new();
        Fixture fixture = new(auditWriter: writer);
        ApplicationSession session = (await fixture.StartAsync()).Session!;

        (await fixture.TerminateAsync(path, session.SessionId)).Should().Be(
            path is "Idle" or "Absolute" ? OperationalErrorCodes.SessionExpired : null);

        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeFalse();
        writer.Inner.Events.Should().ContainSingle(item => IsTerminal(item.Action));
    }

    [Theory]
    [InlineData("Logout")]
    [InlineData("Revoke")]
    [InlineData("Idle")]
    [InlineData("Absolute")]
    [InlineData("Sweep")]
    [InlineData("AccessDisabled")]
    public async Task Termination_NonAtomicAuditFailureLeavesSessionActive(string path)
    {
        NonAtomicAuditWriter writer = new();
        Fixture fixture = new(auditWriter: writer);
        ApplicationSession session = (await fixture.StartAsync()).Session!;
        writer.FailOnTerminalWrite = 1;

        (await fixture.TerminateAsync(path, session.SessionId)).Should().Be(OperationalErrorCodes.AuditStoreUnavailable);

        writer.TerminalWriteAttempts.Should().Be(1);
        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        writer.Inner.Events.Should().NotContain(item => IsTerminal(item.Action));
    }

    [Theory]
    [InlineData("Sweep")]
    [InlineData("AccessDisabled")]
    public async Task BatchTermination_NonAtomicSecondAuditFailureKeepsAllSessionsActiveAndRetainsWrittenPrefix(string path)
    {
        NonAtomicAuditWriter writer = new();
        Fixture fixture = new(auditWriter: writer);
        ApplicationSession first = (await fixture.StartAsync()).Session!;
        ApplicationSession second = (await fixture.StartAsync()).Session!;
        writer.FailOnTerminalWrite = 2;

        (await fixture.TerminateAsync(path, first.SessionId)).Should().Be(OperationalErrorCodes.AuditStoreUnavailable);

        writer.TerminalWriteAttempts.Should().Be(2);
        (await fixture.Repository.GetAsync(first.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        (await fixture.Repository.GetAsync(second.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
        writer.Inner.Events.Should().ContainSingle(item => IsTerminal(item.Action));
    }

    [Fact]
    public async Task RepositoryTermination_NonAtomicFailureWrapsOriginalExceptionAndPreservesActiveState()
    {
        NonAtomicAuditWriter writer = new();
        Fixture fixture = new(auditWriter: writer);
        ApplicationSession session = (await fixture.StartAsync()).Session!;
        writer.FailOnTerminalWrite = 1;
        Func<Task> terminate = () => fixture.Repository.EndWithAuditAsync(session.SessionId,
            fixture.Time.GetUtcNow(), SessionEndReason.Logout,
            new AuditEvent { Actor = "synthetic", Action = AuditActions.ApplicationSessionLoggedOut },
            TestContext.Current.CancellationToken);

        (await terminate.Should().ThrowAsync<AuditWriteUnavailableException>()).Which.InnerException
            .Should().BeOfType<IOException>();
        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RepositoryTermination_PreservesActualTransitionBooleanWithoutChangingLosingResultPolicy()
    {
        Fixture fixture = new();
        ApplicationSession session = (await fixture.StartAsync()).Session!;
        AuditEvent audit = new() { Actor = "synthetic", Action = AuditActions.ApplicationSessionLoggedOut };

        bool first = await fixture.Repository.EndWithAuditAsync(session.SessionId, fixture.Time.GetUtcNow(), SessionEndReason.Logout, audit, TestContext.Current.CancellationToken);
        bool second = await fixture.Repository.EndWithAuditAsync(session.SessionId, fixture.Time.GetUtcNow(), SessionEndReason.Revoked, audit, TestContext.Current.CancellationToken);

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await fixture.Repository.GetAsync(session.SessionId, TestContext.Current.CancellationToken))!.EndReason.Should().Be(SessionEndReason.Logout);
    }

    [Fact]
    public async Task Validate_IdleTimeoutEndsSessionAndAuditsOnce()
    {
        Fixture fixture = new();
        ApplicationSessionResult started = await fixture.StartAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(30));

        ApplicationSessionResult result = await fixture.ValidateAsync(started.Session!.SessionId);

        result.Disposition.Should().Be(ApplicationSessionDisposition.IdleExpired);
        result.ErrorCode.Should().Be(OperationalErrorCodes.SessionExpired);
        (await fixture.Repository.GetAsync(started.Session.SessionId, TestContext.Current.CancellationToken))!.EndReason.Should().Be(SessionEndReason.IdleTimeout);
        fixture.Audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionIdleTimedOut).Should().Be(1);
    }

    [Fact]
    public async Task Validate_SlidingActivityNeverExtendsAbsoluteLifetime()
    {
        Fixture fixture = new(new SessionSecurityOptions
        {
            IdleTimeoutMinutes = 360,
            AbsoluteLifetimeHours = 12,
            ActivityPersistenceIntervalMinutes = 5
        });
        ApplicationSessionResult started = await fixture.StartAsync();
        DateTimeOffset absoluteExpiry = started.Session!.AbsoluteExpiresAtUtc;

        fixture.Time.Advance(TimeSpan.FromHours(5));
        (await fixture.ValidateAsync(started.Session.SessionId)).Disposition.Should().Be(ApplicationSessionDisposition.Active);
        fixture.Time.Advance(TimeSpan.FromHours(5));
        ApplicationSessionResult active = await fixture.ValidateAsync(started.Session.SessionId);
        fixture.Time.Advance(TimeSpan.FromHours(2));
        ApplicationSessionResult expired = await fixture.ValidateAsync(started.Session.SessionId);

        active.Session!.AbsoluteExpiresAtUtc.Should().Be(absoluteExpiry);
        expired.Disposition.Should().Be(ApplicationSessionDisposition.AbsoluteExpired);
        (await fixture.Repository.GetAsync(started.Session.SessionId, TestContext.Current.CancellationToken))!.EndReason.Should().Be(SessionEndReason.AbsoluteTimeout);
    }

    [Fact]
    public async Task Validate_ThrottlesPersistedLastSeenUpdates()
    {
        Fixture fixture = new();
        ApplicationSessionResult started = await fixture.StartAsync();

        fixture.Time.Advance(TimeSpan.FromMinutes(4));
        _ = await fixture.ValidateAsync(started.Session!.SessionId);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        _ = await fixture.ValidateAsync(started.Session.SessionId);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        _ = await fixture.ValidateAsync(started.Session.SessionId);

        fixture.Repository.TouchCalls.Should().Be(1);
        (await fixture.Repository.GetAsync(started.Session.SessionId, TestContext.Current.CancellationToken))!.LastSeenAtUtc.Should().Be(Fixture.StartTime.AddMinutes(5));
    }

    [Fact]
    public async Task Validate_AccessDisabledEndsEffectiveSession()
    {
        Fixture fixture = new();
        ApplicationSessionResult started = await fixture.StartAsync();
        fixture.User = fixture.User with
        {
            Status = AccessStatus.Disabled,
            DisabledAt = fixture.Time.GetUtcNow(),
            Version = fixture.User.Version + 1,
            Roles = [],
            Capabilities = []
        };

        ApplicationSessionResult result = await fixture.ValidateAsync(started.Session!.SessionId);

        result.Disposition.Should().Be(ApplicationSessionDisposition.AccessDisabled);
        (await fixture.Repository.GetAsync(started.Session.SessionId, TestContext.Current.CancellationToken))!.EndReason.Should().Be(SessionEndReason.AccessDisabled);
        fixture.Audit.Events.Should().Contain(item => item.Action == AuditActions.ApplicationSessionAccessDisabled);
    }

    [Fact]
    public async Task Validate_AccessVersionChangeEndsExistingSessionWithoutReplacement()
    {
        Fixture fixture = new();
        ApplicationSessionResult started = await fixture.StartAsync();
        fixture.User = fixture.User with { Version = fixture.User.Version + 1 };

        ApplicationSessionResult result = await fixture.ValidateAsync(started.Session!.SessionId);

        result.Disposition.Should().Be(ApplicationSessionDisposition.AccessChanged);
        result.ErrorCode.Should().Be(OperationalErrorCodes.SessionRevoked);
        (await fixture.Repository.GetAsync(started.Session.SessionId, TestContext.Current.CancellationToken))!.EndReason
            .Should().Be(SessionEndReason.AccessChanged);
        fixture.Audit.Events.Count(item => item.Action == AuditActions.ApplicationSessionStarted).Should().Be(1);
        fixture.Audit.Events.Should().Contain(item => item.Action == AuditActions.ApplicationSessionAccessChanged);
    }

    [Fact]
    public async Task Revoke_EndsExactSessionAndDoesNotAuditRawReason()
    {
        Fixture fixture = new();
        ApplicationSessionResult started = await fixture.StartAsync();
        const string reason = "Approved operational revocation reference SECRET-VALUE";

        ApplicationSessionResult revoked = await fixture.Service.RevokeAsync(started.Session!.SessionId, reason, Fixture.Context, TestContext.Current.CancellationToken);
        ApplicationSessionResult replay = await fixture.ValidateAsync(started.Session.SessionId);
        string auditJson = JsonSerializer.Serialize(fixture.Audit.Events);

        revoked.Disposition.Should().Be(ApplicationSessionDisposition.Ended);
        revoked.Session!.EndReason.Should().Be(SessionEndReason.Revoked);
        replay.Disposition.Should().Be(ApplicationSessionDisposition.Revoked);
        auditJson.Should().Contain("reasonHash")
            .And.NotContain(reason)
            .And.NotContain("192.0.2.10")
            .And.NotContain("Cookie")
            .And.NotContain("Password");
    }

    [Fact]
    public async Task Validate_EmptyStoreRejectsPresentedSessionWithoutCreatingReplacement()
    {
        Fixture fixture = new();

        ApplicationSessionResult result = await fixture.ValidateAsync(Guid.NewGuid());

        result.Disposition.Should().Be(ApplicationSessionDisposition.Revoked);
        result.ErrorCode.Should().Be(OperationalErrorCodes.SessionRevoked);
        fixture.Audit.Events.Should().NotContain(item => item.Action == AuditActions.ApplicationSessionStarted);
    }

    [Fact]
    public async Task ListActive_TransitionsUnvisitedExpiredSessionsBeforeReturningPage()
    {
        Fixture fixture = new();
        ApplicationSessionResult started = await fixture.StartAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(30));

        ApplicationSessionListResult result = await fixture.Service.ListActiveAsync(1, 50, Fixture.Context, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Sessions.Should().BeEmpty();
        (await fixture.Repository.GetAsync(started.Session!.SessionId, TestContext.Current.CancellationToken))!.EndReason
            .Should().Be(SessionEndReason.IdleTimeout);
        fixture.Audit.Events.Should().Contain(item => item.Action == AuditActions.ApplicationSessionIdleTimedOut);
    }

    [Fact]
    public async Task Start_WhenRequiredAuditFails_EndsPersistedSessionAndRequiresReauthentication()
    {
        Fixture fixture = new(auditWriter: new FailingAuditWriter());

        ApplicationSessionResult result = await fixture.StartAsync();

        result.Disposition.Should().Be(ApplicationSessionDisposition.AuditUnavailable);
        result.ErrorCode.Should().Be(OperationalErrorCodes.AuditStoreUnavailable);
        ApplicationSession persisted = (await fixture.Repository.GetAsync(
            fixture.Repository.LastInsertedSessionId!.Value,
            CancellationToken.None))!;
        persisted.EndReason.Should().Be(SessionEndReason.AuditFailure);
        persisted.IsActive.Should().BeFalse();
    }

    private sealed class Fixture
    {
        private readonly IApplicationAccessService _access = Substitute.For<IApplicationAccessService>();

        public Fixture(SessionSecurityOptions? options = null, IAuditWriter? auditWriter = null)
        {
            options ??= new SessionSecurityOptions();
            Time = new ManualTimeProvider(StartTime);
            Audit = new InMemoryAuditWriter();
            Repository = new CountingRepository(auditWriter ?? Audit);
            User = new ApplicationUser(
                Guid.NewGuid(),
                "CONTOSO\\session.user",
                "windows-negotiate",
                AccessStatus.Approved,
                StartTime,
                StartTime,
                null,
                2,
                ["Operator"],
                ["OperationalRecords.View"]);
            _access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(AccessServiceResult<EnsureAccessUserResult>.Success(new EnsureAccessUserResult(User, null, false, false))));
            Service = new ApplicationSessionService(
                _access,
                Repository,
                auditWriter ?? Audit,
                Options.Create(options),
                Time,
                NullLogger<ApplicationSessionService>.Instance);
        }

        public static readonly DateTimeOffset StartTime = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        public static readonly AccessOperationContext Context = new("CONTOSO\\session.user", "session-correlation", "192.0.2.10");
        public static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "CONTOSO\\session.user")], "Negotiate"));
        public ApplicationUser User { get; set; }
        public ManualTimeProvider Time { get; }
        public CountingRepository Repository { get; }
        public InMemoryAuditWriter Audit { get; }
        public ApplicationSessionService Service { get; }

        public Task<ApplicationSessionResult> StartAsync() => Service.ValidateOrStartAsync(Principal, null, Context, default);
        public Task<ApplicationSessionResult> ValidateAsync(Guid sessionId) => Service.ValidateOrStartAsync(Principal, sessionId, Context, default);

        public async Task<string?> TerminateAsync(string path, Guid sessionId)
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            switch (path)
            {
                case "Logout":
                    return (await Service.LogoutAsync(sessionId, Context, token)).ErrorCode;
                case "Revoke":
                    return (await Service.RevokeAsync(sessionId, "Synthetic approved termination", Context, token)).ErrorCode;
                case "Idle":
                case "Absolute":
                    Time.Advance(path == "Idle" ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(12));
                    return (await ValidateAsync(sessionId)).ErrorCode;
                case "Sweep":
                    Time.Advance(TimeSpan.FromMinutes(30));
                    return (await Service.ListActiveAsync(1, 50, Context, token)).ErrorCode;
                case "AccessDisabled":
                    return (await Service.EndUserSessionsAsync(User.Id, SessionEndReason.AccessDisabled, Context, token)).ErrorCode;
                default:
                    throw new ArgumentOutOfRangeException(nameof(path));
            }
        }
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.FromException(new AuditWriteUnavailableException("Synthetic audit failure."));
    }

    private sealed class NonAtomicAuditWriter : IAuditWriter
    {
        public InMemoryAuditWriter Inner { get; } = new();
        public int FailOnTerminalWrite { get; set; }
        public int TerminalWriteAttempts { get; private set; }

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTerminal(auditEvent.Action) && ++TerminalWriteAttempts == FailOnTerminalWrite)
            {
                throw new IOException("Synthetic file-like audit failure.");
            }

            return Inner.WriteAsync(auditEvent, cancellationToken);
        }
    }

    private sealed class ToggleAuditWriter : IAtomicAuditWriter
    {
        public InMemoryAuditWriter Inner { get; } = new();
        public bool FailTerminalWrite { get; set; }
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            FailTerminalWrite && IsTerminal(auditEvent.Action)
                ? Task.FromException(new AuditWriteUnavailableException("Synthetic terminal audit failure."))
                : Inner.WriteAsync(auditEvent, cancellationToken);

        public Task WriteBatchAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken) =>
            FailTerminalWrite && events.Any(item => IsTerminal(item.Action))
                ? Task.FromException(new AuditWriteUnavailableException("Synthetic terminal audit failure."))
                : Inner.WriteBatchAsync(events, cancellationToken);
    }

    private sealed class CountingRepository(IAuditWriter auditWriter) : IApplicationSessionRepository
    {
        private readonly InMemoryApplicationSessionRepository _inner = new(auditWriter);
        public int TouchCalls { get; private set; }
        public bool FailTermination { get; set; }
        public Guid? LastInsertedSessionId { get; private set; }
        public Task InsertAsync(ApplicationSession session, CancellationToken cancellationToken)
        {
            LastInsertedSessionId = session.SessionId;
            return _inner.InsertAsync(session, cancellationToken);
        }
        public Task<ApplicationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken) => _inner.GetAsync(sessionId, cancellationToken);
        public Task<bool> TouchAsync(Guid sessionId, DateTimeOffset lastSeenAtUtc, DateTimeOffset persistBeforeUtc, CancellationToken cancellationToken)
        {
            TouchCalls++;
            return _inner.TouchAsync(sessionId, lastSeenAtUtc, persistBeforeUtc, cancellationToken);
        }
        public Task<bool> EndAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken) => FailTermination
            ? Task.FromException<bool>(new InvalidOperationException("Synthetic session update failure."))
            : _inner.EndAsync(sessionId, endedAtUtc, reason, cancellationToken);
        public Task<bool> EndWithAuditAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, AuditEvent auditEvent, CancellationToken cancellationToken) => FailTermination
            ? Task.FromException<bool>(new InvalidOperationException("Synthetic session update failure."))
            : _inner.EndWithAuditAsync(sessionId, endedAtUtc, reason, auditEvent, cancellationToken);
        public Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, Func<ApplicationSession, AuditEvent> auditFactory, CancellationToken cancellationToken) => FailTermination
            ? Task.FromException<IReadOnlyList<ApplicationSession>>(new InvalidOperationException("Synthetic session update failure."))
            : _inner.EndActiveForUserAsync(userId, endedAtUtc, reason, auditFactory, cancellationToken);
        public Task<IReadOnlyList<ApplicationSession>> EndExpiredAsync(DateTimeOffset nowUtc, DateTimeOffset idleCutoffUtc, int maximumCount, Func<ApplicationSession, AuditEvent> auditFactory, CancellationToken cancellationToken) => FailTermination
            ? Task.FromException<IReadOnlyList<ApplicationSession>>(new InvalidOperationException("Synthetic session update failure."))
            : _inner.EndExpiredAsync(nowUtc, idleCutoffUtc, maximumCount, auditFactory, cancellationToken);
        public Task<IReadOnlyList<ApplicationSession>> ListActiveAsync(DateTimeOffset absoluteCutoffUtc, DateTimeOffset idleCutoffUtc, int skip, int take, CancellationToken cancellationToken) => _inner.ListActiveAsync(absoluteCutoffUtc, idleCutoffUtc, skip, take, cancellationToken);
    }

    public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
