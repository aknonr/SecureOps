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
    [InlineData("idle")]
    [InlineData("absolute")]
    [InlineData("access")]
    public async Task TerminalTransition_ValidationAndRevoke_ReturnPersistedReason(string validation)
    {
        Fixture fixture = new();
        Guid sessionId = (await fixture.StartAsync()).Session!.SessionId;
        if (validation == "access")
        {
            fixture.User = fixture.User with { Version = fixture.User.Version + 1 };
        }
        else
        {
            fixture.Time.Advance(validation == "absolute" ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(30));
        }

        TaskCompletionSource bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrivals = 0;
        fixture.Repository.BeforeEnd = () =>
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                bothReady.TrySetResult();
            }

            return bothReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
        };

        ApplicationSessionResult[] results = await Task.WhenAll(
            fixture.ValidateAsync(sessionId),
            fixture.Service.RevokeAsync(sessionId, "Synthetic reason", Fixture.Context, default));

        ApplicationSession persisted = (await fixture.Repository.GetAsync(sessionId, default))!;
        results.Should().OnlyContain(result => result.Session == persisted);
        ApplicationSessionDisposition expected = persisted.EndReason switch
        {
            SessionEndReason.IdleTimeout => ApplicationSessionDisposition.IdleExpired,
            SessionEndReason.AbsoluteTimeout => ApplicationSessionDisposition.AbsoluteExpired,
            SessionEndReason.AccessChanged => ApplicationSessionDisposition.AccessChanged,
            _ => ApplicationSessionDisposition.Revoked
        };
        results[0].Disposition.Should().Be(expected);
        fixture.Audit.Events.Count(item => item.Action != AuditActions.ApplicationSessionStarted).Should().Be(1);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("revoke")]
    [InlineData("validation")]
    [InlineData("user")]
    [InlineData("expired")]
    public async Task TerminalTransition_AtomicRepository_DoesNotWriteAuditAgain(string operation)
    {
        Fixture fixture = new();
        Guid sessionId = (await fixture.StartAsync()).Session!.SessionId;
        fixture.Repository.WritesAuditAtomically = true;
        fixture.Time.Advance(TimeSpan.FromMinutes(30));

        if (operation == "user")
        {
            (await fixture.Service.EndUserSessionsAsync(fixture.User.Id, SessionEndReason.AccessChanged, Fixture.Context, default)).IsSuccess.Should().BeTrue();
        }
        else if (operation == "expired")
        {
            (await fixture.Service.ListActiveAsync(1, 50, Fixture.Context, default)).IsSuccess.Should().BeTrue();
        }
        else
        {
            ApplicationSessionResult result = operation switch
            {
                "logout" => await fixture.Service.LogoutAsync(sessionId, Fixture.Context, default),
                "revoke" => await fixture.Service.RevokeAsync(sessionId, "Synthetic reason", Fixture.Context, default),
                _ => await fixture.ValidateAsync(sessionId)
            };
            result.Session!.IsActive.Should().BeFalse();
        }

        fixture.Repository.TerminalAudits.Should().ContainSingle();
        fixture.Audit.Events.Should().OnlyContain(item => item.Action == AuditActions.ApplicationSessionStarted || item.Action == AuditActions.ApplicationSessionsViewed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TerminalTransition_ConcurrentCalls_AuditOnceAndReturnPersistedReason(bool firstRevokes, bool secondRevokes)
    {
        Fixture fixture = new();
        Guid sessionId = (await fixture.StartAsync()).Session!.SessionId;
        TaskCompletionSource bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrivals = 0;
        fixture.Repository.BeforeEnd = () =>
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                bothReady.TrySetResult();
            }

            return bothReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
        };
        Task<ApplicationSessionResult> EndAsync(bool revoke) => revoke
            ? fixture.Service.RevokeAsync(sessionId, "Synthetic reason", Fixture.Context, default)
            : fixture.Service.LogoutAsync(sessionId, Fixture.Context, default);

        ApplicationSessionResult[] results = await Task.WhenAll(EndAsync(firstRevokes), EndAsync(secondRevokes));

        ApplicationSession persisted = (await fixture.Repository.GetAsync(sessionId, default))!;
        results.Should().ContainSingle(result => result.Disposition == ApplicationSessionDisposition.Ended);
        results.Should().ContainSingle(result => result.Disposition == ApplicationSessionDisposition.Revoked);
        results.Should().OnlyContain(result => result.Session == persisted);
        fixture.Audit.Events.Count(item => item.Action != AuditActions.ApplicationSessionStarted).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalTransition_DuplicateCall_ReturnsPersistedReasonWithoutAnotherAudit(bool revokeFirst)
    {
        Fixture fixture = new();
        Guid sessionId = (await fixture.StartAsync()).Session!.SessionId;
        ApplicationSessionResult winner = revokeFirst
            ? await fixture.Service.RevokeAsync(sessionId, "Synthetic reason", Fixture.Context, default)
            : await fixture.Service.LogoutAsync(sessionId, Fixture.Context, default);

        ApplicationSessionResult loser = revokeFirst
            ? await fixture.Service.LogoutAsync(sessionId, Fixture.Context, default)
            : await fixture.Service.RevokeAsync(sessionId, "Synthetic reason", Fixture.Context, default);

        loser.Disposition.Should().Be(ApplicationSessionDisposition.Revoked);
        loser.Session.Should().Be(winner.Session);
        fixture.Audit.Events.Count(item => item.Action != AuditActions.ApplicationSessionStarted).Should().Be(1);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("revoke")]
    [InlineData("validation")]
    [InlineData("disabled")]
    [InlineData("user")]
    [InlineData("expired")]
    public async Task TerminalTransition_NonAtomicAuditFailure_ReportsAuditUnavailable(string operation)
    {
        IAuditWriter audit = Substitute.For<IAuditWriter>();
        audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(call =>
            ((AuditEvent)call[0]).Action is AuditActions.ApplicationSessionStarted or AuditActions.ApplicationSessionsViewed
                ? Task.CompletedTask
                : Task.FromException(new AuditWriteUnavailableException("Synthetic audit failure.")));
        Fixture fixture = new(auditWriter: audit);
        Guid sessionId = (await fixture.StartAsync()).Session!.SessionId;
        fixture.Time.Advance(TimeSpan.FromMinutes(30));
        string? error;
        if (operation == "user")
        {
            error = (await fixture.Service.EndUserSessionsAsync(fixture.User.Id, SessionEndReason.AccessChanged, Fixture.Context, default)).ErrorCode;
        }
        else if (operation == "expired")
        {
            error = (await fixture.Service.ListActiveAsync(1, 50, Fixture.Context, default)).ErrorCode;
        }
        else
        {
            if (operation == "disabled")
            {
                fixture.User = fixture.User with { Status = AccessStatus.Disabled };
            }

            ApplicationSessionResult result = operation switch
            {
                "logout" => await fixture.Service.LogoutAsync(sessionId, Fixture.Context, default),
                "revoke" => await fixture.Service.RevokeAsync(sessionId, "Synthetic reason", Fixture.Context, default),
                _ => await fixture.ValidateAsync(sessionId)
            };
            result.Disposition.Should().Be(ApplicationSessionDisposition.AuditUnavailable);
            error = result.ErrorCode;
        }

        error.Should().Be(OperationalErrorCodes.AuditStoreUnavailable);
        (await fixture.Repository.GetAsync(sessionId, default))!.IsActive.Should().BeFalse();
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
        (await fixture.Repository.GetAsync(started.Session.SessionId, default))!.EndReason.Should().Be(SessionEndReason.IdleTimeout);
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
        (await fixture.Repository.GetAsync(started.Session.SessionId, default))!.EndReason.Should().Be(SessionEndReason.AbsoluteTimeout);
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
        (await fixture.Repository.GetAsync(started.Session.SessionId, default))!.LastSeenAtUtc.Should().Be(Fixture.StartTime.AddMinutes(5));
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
        (await fixture.Repository.GetAsync(started.Session.SessionId, default))!.EndReason.Should().Be(SessionEndReason.AccessDisabled);
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
        (await fixture.Repository.GetAsync(started.Session.SessionId, default))!.EndReason
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

        ApplicationSessionResult revoked = await fixture.Service.RevokeAsync(started.Session!.SessionId, reason, Fixture.Context, default);
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

        ApplicationSessionListResult result = await fixture.Service.ListActiveAsync(1, 50, Fixture.Context, default);

        result.IsSuccess.Should().BeTrue();
        result.Sessions.Should().BeEmpty();
        (await fixture.Repository.GetAsync(started.Session!.SessionId, default))!.EndReason
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
            Repository = new CountingRepository();
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
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.FromException(new AuditWriteUnavailableException("Synthetic audit failure."));
    }

    private sealed class CountingRepository : IApplicationSessionRepository
    {
        private readonly InMemoryApplicationSessionRepository _inner = new();
        public bool WritesAuditAtomically { get; set; }
        public List<AuditEvent> TerminalAudits { get; } = [];
        public Func<Task>? BeforeEnd { get; set; }
        public int TouchCalls { get; private set; }
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
        public async Task<bool> EndAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken, Func<ApplicationSession, AuditEvent>? auditFactory = null)
        {
            if (BeforeEnd is not null)
            {
                await BeforeEnd();
            }

            bool ended = await _inner.EndAsync(sessionId, endedAtUtc, reason, cancellationToken, auditFactory);
            if (ended)
            {
                CaptureAudit((await _inner.GetAsync(sessionId, cancellationToken))!, auditFactory);
            }

            return ended;
        }
        public async Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken, Func<ApplicationSession, AuditEvent>? auditFactory = null)
        {
            IReadOnlyList<ApplicationSession> ended = await _inner.EndActiveForUserAsync(userId, endedAtUtc, reason, cancellationToken, auditFactory);
            foreach (ApplicationSession session in ended)
            {
                CaptureAudit(session with { EndedAtUtc = endedAtUtc, EndReason = reason }, auditFactory);
            }

            return ended;
        }
        public async Task<IReadOnlyList<ApplicationSession>> EndExpiredAsync(DateTimeOffset nowUtc, DateTimeOffset idleCutoffUtc, int maximumCount, CancellationToken cancellationToken, Func<ApplicationSession, AuditEvent>? auditFactory = null)
        {
            IReadOnlyList<ApplicationSession> ended = await _inner.EndExpiredAsync(nowUtc, idleCutoffUtc, maximumCount, cancellationToken, auditFactory);
            foreach (ApplicationSession session in ended)
            {
                CaptureAudit(session, auditFactory);
            }

            return ended;
        }
        private void CaptureAudit(ApplicationSession session, Func<ApplicationSession, AuditEvent>? auditFactory)
        {
            if (WritesAuditAtomically)
            {
                TerminalAudits.Add(auditFactory!(session));
            }
        }
        public Task<IReadOnlyList<ApplicationSession>> ListActiveAsync(DateTimeOffset absoluteCutoffUtc, DateTimeOffset idleCutoffUtc, int skip, int take, CancellationToken cancellationToken) => _inner.ListActiveAsync(absoluteCutoffUtc, idleCutoffUtc, skip, take, cancellationToken);
    }

    public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
