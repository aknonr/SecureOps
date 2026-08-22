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

    private sealed class Fixture
    {
        private readonly IApplicationAccessService _access = Substitute.For<IApplicationAccessService>();

        public Fixture(SessionSecurityOptions? options = null)
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
                Audit,
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

    private sealed class CountingRepository : IApplicationSessionRepository
    {
        private readonly InMemoryApplicationSessionRepository _inner = new();
        public int TouchCalls { get; private set; }
        public Task InsertAsync(ApplicationSession session, CancellationToken cancellationToken) => _inner.InsertAsync(session, cancellationToken);
        public Task<ApplicationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken) => _inner.GetAsync(sessionId, cancellationToken);
        public Task<bool> TouchAsync(Guid sessionId, DateTimeOffset lastSeenAtUtc, DateTimeOffset persistBeforeUtc, CancellationToken cancellationToken)
        {
            TouchCalls++;
            return _inner.TouchAsync(sessionId, lastSeenAtUtc, persistBeforeUtc, cancellationToken);
        }
        public Task<bool> EndAsync(Guid sessionId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken) => _inner.EndAsync(sessionId, endedAtUtc, reason, cancellationToken);
        public Task<IReadOnlyList<ApplicationSession>> EndActiveForUserAsync(Guid userId, DateTimeOffset endedAtUtc, SessionEndReason reason, CancellationToken cancellationToken) => _inner.EndActiveForUserAsync(userId, endedAtUtc, reason, cancellationToken);
        public Task<IReadOnlyList<ApplicationSession>> ListActiveAsync(DateTimeOffset absoluteCutoffUtc, DateTimeOffset idleCutoffUtc, int skip, int take, CancellationToken cancellationToken) => _inner.ListActiveAsync(absoluteCutoffUtc, idleCutoffUtc, skip, take, cancellationToken);
    }

    public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
