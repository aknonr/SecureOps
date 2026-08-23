using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Tests.Unit.DirectoryExplorer;

public sealed class DirectoryEnrichmentQueryServiceTests
{
    [Fact]
    public async Task Memberships_SeparatesDirectTransitiveAndReportsCycleEvidence()
    {
        DirectoryEnrichmentQueryService service = CreateMockService(out _);

        DirectoryQueryResult<DirectoryPrincipalMembershipsResponse> result = await service.GetMembershipsAsync(
            new DirectoryPrincipalEnrichmentRequest("pam12356", Purpose), Context, CancellationToken.None);

        result.Status.Should().Be(DirectoryQueryStatus.Success);
        result.Value!.DirectGroups.Select(item => item.Group.SamAccountName)
            .Should().Equal("ops-read", "dist-universal");
        result.Value.TransitiveGroups.Select(item => item.Group.SamAccountName)
            .Should().Equal("nested-ops", "platform-privileged");
        result.Value.DirectGroups.Single(item => item.Group.SamAccountName == "ops-read")
            .AlsoTransitivelyReachable.Should().BeTrue();
        result.Value.Traversal.CycleDetected.Should().BeTrue();
    }

    [Fact]
    public async Task MembershipPaths_ReturnsMultipleProvenPathsAndSafeNonMembership()
    {
        DirectoryEnrichmentQueryService service = CreateMockService(out _);

        DirectoryQueryResult<DirectoryMembershipPathResponse> member = await service.GetMembershipPathsAsync(
            new DirectoryMembershipPathRequest("pam12356", "nested-ops", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMembershipPathResponse> notMember = await service.GetMembershipPathsAsync(
            new DirectoryMembershipPathRequest("pam12356", "unrelated-group", Purpose), Context, CancellationToken.None);

        member.Value!.IsMember.Should().BeTrue();
        member.Value.IsDirect.Should().BeFalse();
        member.Value.Paths.Should().HaveCount(2);
        member.Value.Paths.Select(path => path.Groups.Select(group => group.SamAccountName)).Should().BeEquivalentTo(
            new[] { new[] { "ops-read", "nested-ops" }, new[] { "dist-universal", "nested-ops" } },
            options => options.WithStrictOrdering());
        notMember.Value!.IsMember.Should().BeFalse();
        notMember.Value.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task MembershipPaths_UnknownPrincipalAndTargetRemainDistinctNotFoundResults()
    {
        DirectoryEnrichmentQueryService service = CreateMockService(out _);

        DirectoryQueryResult<DirectoryMembershipPathResponse> principal = await service.GetMembershipPathsAsync(
            new DirectoryMembershipPathRequest("missing.user", "ops-read", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMembershipPathResponse> target = await service.GetMembershipPathsAsync(
            new DirectoryMembershipPathRequest("pam12356", "missing-group", Purpose), Context, CancellationToken.None);

        principal.ErrorCode.Should().Be(OperationalErrorCodes.DirectoryPrincipalNotFound);
        target.ErrorCode.Should().Be(OperationalErrorCodes.DirectoryGroupNotFound);
    }

    [Fact]
    public async Task AccountHealth_UsesNullableEvidenceAndCalculatesPasswordAgeWithoutExtendingMeaning()
    {
        DateTimeOffset now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        DirectoryPrincipalEnrichmentRecord evidence = Principal(
            passwordLastSet: now.AddDays(-10),
            enabled: true,
            locked: true,
            passwordNeverExpires: true,
            accountExpires: now.AddDays(30),
            mustChangePassword: false,
            lastLogonTimestamp: now.AddDays(-2));
        DirectoryEnrichmentQueryService service = CreateService(new RecordProvider(evidence), Options(), now, out _);

        DirectoryQueryResult<DirectoryAccountHealthResponse> result = await service.GetAccountHealthAsync(
            new DirectoryPrincipalEnrichmentRequest("sample.user", Purpose), Context, CancellationToken.None);

        result.Value!.Enabled.Should().BeTrue();
        result.Value.Locked.Should().BeTrue();
        result.Value.PasswordAgeDays.Should().Be(10);
        result.Value.PasswordNeverExpires.Should().BeTrue();
        result.Value.AccountExpiresUtc.Should().Be(now.AddDays(30));
        result.Value.MustChangePassword.Should().BeFalse();
        result.Value.LastLogonTimestampUtc.Should().Be(now.AddDays(-2));
        result.Value.LastLogonTimestampIsApproximate.Should().BeTrue();
    }

    [Fact]
    public async Task AccountHealth_MissingTimestampsRemainNull()
    {
        DirectoryEnrichmentQueryService service = CreateService(
            new RecordProvider(Principal()), Options(), Now, out _);

        DirectoryQueryResult<DirectoryAccountHealthResponse> result = await service.GetAccountHealthAsync(
            new DirectoryPrincipalEnrichmentRequest("sample.user", Purpose), Context, CancellationToken.None);

        result.Value!.PasswordLastSetUtc.Should().BeNull();
        result.Value.PasswordAgeDays.Should().BeNull();
        result.Value.AccountExpiresUtc.Should().BeNull();
        result.Value.MustChangePassword.Should().BeNull();
        result.Value.LastLogonTimestampUtc.Should().BeNull();
    }

    [Fact]
    public async Task AccountHealth_ProviderTimeoutCancelsWorkAndReturnsSafeUnavailableResult()
    {
        DirectoryExplorerOptions options = Options();
        options.ProviderTimeoutSeconds = 1;
        var provider = new BlockingProvider();
        DirectoryEnrichmentQueryService service = CreateService(provider, options, Now, out _);

        DirectoryQueryResult<DirectoryAccountHealthResponse> result = await service.GetAccountHealthAsync(
            new DirectoryPrincipalEnrichmentRequest("sample.user", Purpose), Context, CancellationToken.None);

        result.Status.Should().Be(DirectoryQueryStatus.ProviderUnavailable);
        provider.CancellationObserved.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 2, 0, false)]
    [InlineData(1, 2, 1, false)]
    [InlineData(3, 2, 2, true)]
    public async Task ServiceEvidence_HandlesZeroOneMultipleAndBoundedSpns(
        int available,
        int maximum,
        int returned,
        bool truncated)
    {
        string[] spns = Enumerable.Range(1, available).Select(index => $"HTTP/service-{index}.example.invalid").ToArray();
        DirectoryExplorerOptions options = Options();
        options.MaxSpnsPerPrincipal = maximum;
        DirectoryEnrichmentQueryService service = CreateService(
            new RecordProvider(Principal(spns: spns)), options, Now, out _);

        DirectoryQueryResult<DirectoryServiceEvidenceResponse> result = await service.GetServiceEvidenceAsync(
            new DirectoryPrincipalEnrichmentRequest("sample.user", Purpose), Context, CancellationToken.None);

        result.Value!.ServicePrincipalNameCount.Should().Be(available);
        result.Value.ServicePrincipalNames.Should().HaveCount(returned);
        result.Value.ServicePrincipalNamesTruncated.Should().Be(truncated);
        result.Value.AccountTypeEvidence.Should().Be("User");
    }

    [Fact]
    public async Task PrivilegedMemberships_ReturnsDirectTransitiveAndMissingConfiguredEvidence()
    {
        DirectoryExplorerOptions options = Options();
        options.PrivilegedGroupIdentifiers = ["ops-read", "platform-privileged", "missing-group"];
        DirectoryEnrichmentQueryService service = CreateMockService(out _, options);

        DirectoryQueryResult<DirectoryPrivilegedMembershipResponse> result = await service.GetPrivilegedMembershipsAsync(
            new DirectoryPrincipalEnrichmentRequest("pam12356", Purpose), Context, CancellationToken.None);

        result.Value!.Groups.Single(item => item.ConfiguredIdentifier == "ops-read").Direct.Should().BeTrue();
        result.Value.Groups.Single(item => item.ConfiguredIdentifier == "ops-read").Transitive.Should().BeTrue();
        result.Value.Groups.Single(item => item.ConfiguredIdentifier == "platform-privileged").Transitive.Should().BeTrue();
        result.Value.Groups.Single(item => item.ConfiguredIdentifier == "missing-group").GroupFound.Should().BeFalse();
    }

    [Fact]
    public async Task Audit_ContainsCountsAndLimitsButNoGraphHealthOrSpnPayload()
    {
        DirectoryEnrichmentQueryService service = CreateMockService(out InMemoryAuditWriter audit);

        _ = await service.GetServiceEvidenceAsync(
            new DirectoryPrincipalEnrichmentRequest("pam12356", Purpose), Context, CancellationToken.None);

        string json = JsonSerializer.Serialize(audit.Events);
        json.Should().Contain("DirectCount").And.Contain("EdgesVisited").And.Contain("limitReached");
        json.Should().NotContain("pam12356").And.NotContain("nested-ops").And.NotContain("MSSQLSvc")
            .And.NotContain("PasswordLastSetUtc").And.NotContain(Purpose);
    }

    [Theory]
    [InlineData("pam*")]
    [InlineData("CN=User,DC=example")]
    public async Task Enrichment_RejectsSearchAndRawDirectoryInput(string account)
    {
        DirectoryEnrichmentQueryService service = CreateMockService(out _);

        DirectoryQueryResult<DirectoryPrincipalMembershipsResponse> result = await service.GetMembershipsAsync(
            new DirectoryPrincipalEnrichmentRequest(account, Purpose), Context, CancellationToken.None);

        result.Status.Should().Be(DirectoryQueryStatus.Invalid);
    }

    private static DirectoryEnrichmentQueryService CreateMockService(
        out InMemoryAuditWriter audit,
        DirectoryExplorerOptions? directoryOptions = null)
    {
        IOptions<IdentityLookupOptions> identity = Microsoft.Extensions.Options.Options.Create(
            new IdentityLookupOptions { EnableUpnLookup = true });
        return CreateService(new MockDirectoryEnrichmentProvider(identity), directoryOptions ?? Options(), Now, out audit);
    }

    private static DirectoryEnrichmentQueryService CreateService(
        IDirectoryEnrichmentProvider provider,
        DirectoryExplorerOptions directoryOptions,
        DateTimeOffset now,
        out InMemoryAuditWriter audit)
    {
        audit = new InMemoryAuditWriter();
        IOptions<DirectoryExplorerOptions> options = Microsoft.Extensions.Options.Options.Create(directoryOptions);
        return new DirectoryEnrichmentQueryService(
            new IdentityAccountNormalizer(Microsoft.Extensions.Options.Options.Create(new IdentityLookupOptions { EnableUpnLookup = true })),
            new DirectoryExactInputNormalizer(options),
            provider,
            new DirectoryMembershipGraphBuilder(provider, options),
            new DirectoryQueryCache(options, new FixedTimeProvider(now)),
            audit,
            options,
            new FixedTimeProvider(now),
            NullLogger<DirectoryEnrichmentQueryService>.Instance);
    }

    private static DirectoryExplorerOptions Options() => new()
    {
        Cache = new DirectoryExplorerCacheOptions { Enabled = false },
        MaxTraversalDepth = 8,
        MaxTraversalNodes = 50,
        MaxTraversalEdges = 100,
        MaxMembershipPaths = 5,
        MaxSpnsPerPrincipal = 50
    };

    private static DirectoryPrincipalEnrichmentRecord Principal(
        DateTimeOffset? passwordLastSet = null,
        bool? enabled = true,
        bool? locked = false,
        bool? passwordNeverExpires = null,
        DateTimeOffset? accountExpires = null,
        bool? mustChangePassword = null,
        DateTimeOffset? lastLogonTimestamp = null,
        IReadOnlyList<string>? spns = null) => new(
            "principal-id",
            "Sample User",
            "sample.user",
            "sample.user@example.invalid",
            enabled,
            locked,
            passwordLastSet,
            passwordNeverExpires,
            accountExpires,
            mustChangePassword,
            lastLogonTimestamp,
            null,
            spns ?? [],
            spns?.Count ?? 0,
            false,
            "User");

    private const string Purpose = "Approved synthetic directory verification";
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DirectoryQueryExecutionContext Context = new(
        "CONTOSO\\lead.user", "10.0.0.5", "phase2-test");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordProvider(DirectoryPrincipalEnrichmentRecord? principal) : IDirectoryEnrichmentProvider
    {
        public string ProviderName => "Record";

        public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(
            string normalizedAccount,
            int maxSpns,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (principal is null)
            {
                return Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(null);
            }

            IReadOnlyList<string> all = principal.ServicePrincipalNames;
            return Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(principal with
            {
                ServicePrincipalNames = all.Take(maxSpns).ToArray(),
                ServicePrincipalNameCount = all.Count,
                ServicePrincipalNamesTruncated = all.Count > maxSpns
            });
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
            string normalizedAccount,
            int maxResults,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(
                principal is null ? null : new DirectoryProviderPage<DirectoryGroupRecord>([], false));

        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryGroupRecord?>(null);

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
            DirectoryGroupRecord group,
            int maxResults,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(
                new DirectoryProviderPage<DirectoryGroupRecord>([], false));
    }

    private sealed class BlockingProvider : IDirectoryEnrichmentProvider
    {
        public string ProviderName => "Blocking";
        public bool CancellationObserved { get; private set; }

        public async Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(
            string normalizedAccount,
            int maxSpns,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
            string normalizedAccount,
            int maxResults,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);

        public Task<DirectoryGroupRecord?> FindGroupAsync(
            string normalizedGroup,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryGroupRecord?>(null);

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
            DirectoryGroupRecord group,
            int maxResults,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);
    }
}
