using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Api;

public sealed class OidcFirstAdminBootstrapTests
{
    private const string _issuer = "https://identity.example.test";
    private const string _loginName = "bootstrap.operator";
    private const string _systemActor = "system:oidc-first-admin-bootstrap";
    private const string _bootstrapReason = "One-time validated OIDC first-Admin bootstrap.";

    [Fact]
    public async Task DisabledByDefault_LeavesEligibleOidcUserPending()
    {
        Fixture fixture = new(enabled: false);

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(OidcPrincipal("subject-disabled"));

        result.Value!.User.Status.Should().Be(AccessStatus.Pending);
        result.Value.User.Roles.Should().BeEmpty();
        fixture.BootstrapStore.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NonOidcDemoActor_CannotEnterRealBootstrapPath()
    {
        Fixture fixture = new();
        ClaimsPrincipal demo = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "demo:platform-admin"), new Claim("secureops:auth_source", "demo-api-bridge")],
            "Demo",
            ClaimTypes.Name,
            ClaimTypes.Role));

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(demo);

        result.Value!.User.Status.Should().Be(AccessStatus.Pending);
        fixture.BootstrapStore.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("https://other-issuer.example.test", _loginName)]
    [InlineData(_issuer, "other.operator")]
    public async Task ExactIssuerOrLoginMismatch_LeavesUserPending(string issuer, string loginName)
    {
        Fixture fixture = new();

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(OidcPrincipal("subject-mismatch", issuer, loginName));

        result.Value!.User.Status.Should().Be(AccessStatus.Pending);
        fixture.BootstrapStore.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task LoginNameUsesExistingCaseInsensitiveExactAccountSemantics()
    {
        Fixture fixture = new();

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(
            OidcPrincipal("subject-case", loginName: "BOOTSTRAP.OPERATOR"));

        result.Value!.User.Roles.Should().ContainSingle("Admin");
        fixture.BootstrapStore.AppliedCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MissingSubjectOrLoginName_FailsClosed(bool includeSubject, bool includeLoginName)
    {
        Fixture fixture = new();
        List<Claim> claims =
        [
            new(ExternalIdentityClaimTypes.StableIdentifier, "oidc:synthetic-invalid"),
            new(ExternalIdentityClaimTypes.Issuer, _issuer),
            new(ExternalIdentityClaimTypes.AuthenticationProvider, "OIDC"),
            new("secureops:auth_source", "oidc")
        ];
        if (includeSubject)
        {
            claims.Add(new Claim(ExternalIdentityClaimTypes.Subject, "subject-missing-claim"));
        }
        if (includeLoginName)
        {
            claims.Add(new Claim(ExternalIdentityClaimTypes.LoginName, _loginName));
        }
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "OIDC"));

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(principal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(OperationalErrorCodes.AccessDenied);
        fixture.BootstrapStore.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EligibleFirstOidcUser_ReceivesAdminThroughNormalCapabilitiesAndAudit()
    {
        Fixture fixture = new();

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(OidcPrincipal("subject-first"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.User.Status.Should().Be(AccessStatus.Approved);
        result.Value.User.Roles.Should().ContainSingle("Admin");
        result.Value.User.Capabilities.Should().Contain(Capabilities.AccessManageUsers);
        fixture.Audit.Events.Should().Contain(audit =>
            audit.Action == AuditActions.FirstAdminBootstrapped
            && audit.Actor == _systemActor);
    }

    [Fact]
    public async Task SecondEligibleIdentity_CannotBootstrapAfterAdminEverExisted()
    {
        Fixture fixture = new();
        _ = await fixture.CurrentAsync(OidcPrincipal("subject-first"));

        AccessServiceResult<EnsureAccessUserResult> second = await fixture.CurrentAsync(OidcPrincipal("subject-second"));

        second.Value!.User.Status.Should().Be(AccessStatus.Pending);
        second.Value.User.Roles.Should().BeEmpty();
        fixture.BootstrapStore.AppliedCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentEligibleFirstLogins_CreateExactlyOneAdmin()
    {
        Fixture fixture = new();

        AccessServiceResult<EnsureAccessUserResult>[] results = await Task.WhenAll(
            fixture.CurrentAsync(OidcPrincipal("subject-race-one")),
            fixture.CurrentAsync(OidcPrincipal("subject-race-two")));

        results.Count(result => result.Value!.User.Roles.Contains("Admin")).Should().Be(1);
        fixture.BootstrapStore.AppliedCount.Should().Be(1);
    }

    [Fact]
    public async Task RevokingOnlyAdmin_DoesNotReenableBootstrapWhileConfigRemainsEnabled()
    {
        Fixture fixture = new();
        ApplicationUser admin = (await fixture.CurrentAsync(OidcPrincipal("subject-first"))).Value!.User;
        AccessMutationResult revoked = await fixture.Repository.ReplaceRolesAsync(
            admin.Id,
            [],
            admin.Version,
            "system:test-admin-review",
            CancellationToken.None);

        AccessServiceResult<EnsureAccessUserResult> second = await fixture.CurrentAsync(OidcPrincipal("subject-after-revoke"));

        revoked.Disposition.Should().Be(AccessMutationDisposition.Applied);
        second.Value!.User.Status.Should().Be(AccessStatus.Pending);
        fixture.BootstrapStore.AppliedCount.Should().Be(1);
    }

    [Fact]
    public async Task BootstrapAuditFailure_LeavesUserPendingWithoutAdminAssignment()
    {
        Fixture fixture = new(failAudit: true);

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(OidcPrincipal("subject-audit-failure"));
        ApplicationUser user = (await fixture.Repository.ListUsersAsync(CancellationToken.None)).Single();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(OperationalErrorCodes.PersistenceUnavailable);
        user.Status.Should().Be(AccessStatus.Pending);
        user.Roles.Should().BeEmpty();
        fixture.Audit.Events.Should().NotContain(audit => audit.Action == AuditActions.FirstAdminBootstrapped);
    }

    [Fact]
    public async Task SqlUnavailable_FailsClosedWithoutAdminAssignment()
    {
        Fixture fixture = new(storeUnavailable: true);

        AccessServiceResult<EnsureAccessUserResult> result = await fixture.CurrentAsync(OidcPrincipal("subject-sql-failure"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(OperationalErrorCodes.PersistenceUnavailable);
        (await fixture.Repository.ListUsersAsync(CancellationToken.None)).Single().Roles.Should().BeEmpty();
    }

    private static ClaimsPrincipal OidcPrincipal(
        string subject,
        string issuer = _issuer,
        string loginName = _loginName)
    {
        OidcExternalIdentityNormalizer normalizer = new(Options.Create(new OidcOptions()));
        ClaimsPrincipal raw = new(new ClaimsIdentity(
            [new Claim("iss", issuer), new Claim("sub", subject), new Claim("loginname", loginName)],
            "Bearer"));
        return normalizer.Normalize(raw).Principal!;
    }

    private sealed class Fixture
    {
        private readonly IOptions<AccessOptions> _accessOptions;

        public Fixture(bool enabled = true, bool failAudit = false, bool storeUnavailable = false)
        {
            Repository = new InMemoryAccessRepository();
            Audit = new InMemoryAuditWriter();
            BootstrapStore = new DeterministicBootstrapStore(Repository, Audit, failAudit, storeUnavailable);
            _accessOptions = Options.Create(new AccessOptions());
            Service = new ApplicationAccessService(
                new CorporatePrincipalResolver(_accessOptions),
                Repository,
                BootstrapStore,
                new NullProfileResolver(),
                Audit,
                _accessOptions,
                Options.Create(new BootstrapAdminOptions
                {
                    Enabled = enabled,
                    LoginName = _loginName,
                    AllowedIssuer = _issuer
                }),
                Options.Create(new SessionSecurityOptions()),
                NullLogger<ApplicationAccessService>.Instance);
        }

        public InMemoryAccessRepository Repository { get; }
        public InMemoryAuditWriter Audit { get; }
        public DeterministicBootstrapStore BootstrapStore { get; }
        public ApplicationAccessService Service { get; }

        public Task<AccessServiceResult<EnsureAccessUserResult>> CurrentAsync(ClaimsPrincipal principal) =>
            Service.GetCurrentAsync(
                principal,
                new AccessOperationContext("synthetic-request-actor", "bootstrap-test-correlation", null),
                CancellationToken.None);
    }

    private sealed class DeterministicBootstrapStore(
        InMemoryAccessRepository repository,
        InMemoryAuditWriter audit,
        bool failAudit,
        bool storeUnavailable) : IFirstAdminBootstrapStore
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _adminEverProvisioned;

        public int CallCount { get; private set; }
        public int AppliedCount { get; private set; }

        public async Task<FirstAdminBootstrapDisposition> TryGrantAsync(
            FirstAdminBootstrapCommand command,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                CallCount++;
                if (storeUnavailable)
                {
                    throw new InvalidOperationException("Synthetic SQL outage.");
                }
                if (_adminEverProvisioned)
                {
                    return FirstAdminBootstrapDisposition.AlreadyProvisioned;
                }
                if (failAudit)
                {
                    throw new AuditWriteUnavailableException("Synthetic atomic audit failure.");
                }

                AccessMutationResult mutation = await repository.DecideRequestAsync(
                    command.AccessRequestId,
                    AccessRequestStatus.Approved,
                    command.AccessRequestVersion,
                    _systemActor,
                    ["Admin"],
                    _bootstrapReason,
                    cancellationToken);
                if (mutation.Disposition != AccessMutationDisposition.Applied)
                {
                    return FirstAdminBootstrapDisposition.NotEligible;
                }

                await audit.WriteAsync(new AuditEvent
                {
                    Actor = _systemActor,
                    Action = AuditActions.FirstAdminBootstrapped,
                    CorrelationId = command.CorrelationId,
                    Details = new { targetUserId = command.UserId, role = "Admin", bootstrapMechanism = "ValidatedOidcFirstAdmin" }
                }, cancellationToken);
                _adminEverProvisioned = true;
                AppliedCount++;
                return FirstAdminBootstrapDisposition.Applied;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private sealed class NullProfileResolver : IAccessIdentityProfileResolver
    {
        public Task<AccessIdentityProfile?> ResolveAsync(string corporateIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<AccessIdentityProfile?>(null);
    }
}
