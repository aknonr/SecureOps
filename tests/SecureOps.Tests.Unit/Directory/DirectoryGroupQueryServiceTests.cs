using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Tests.Unit.DirectoryExplorer;

public sealed class DirectoryGroupQueryServiceTests
{
    [Fact]
    public async Task PrincipalGroups_PagesWithOpaqueContinuation()
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(upn: true), out InMemoryAuditWriter audit);

        DirectoryQueryResult<DirectoryGroupPageResponse> first = await service.GetPrincipalGroupsAsync(
            new DirectoryPrincipalGroupsRequest("CONTOSO\\pam12356", Purpose, 1), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupPageResponse> second = await service.GetPrincipalGroupsAsync(
            new DirectoryPrincipalGroupsRequest("pam12356", Purpose, 1, first.Value!.ContinuationToken), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupPageResponse> third = await service.GetPrincipalGroupsAsync(
            new DirectoryPrincipalGroupsRequest("pam12356", Purpose, 1, second.Value!.ContinuationToken), Context, CancellationToken.None);

        first.Status.Should().Be(DirectoryQueryStatus.Success);
        first.Value!.Items.Should().ContainSingle(); first.Value.ContinuationToken.Should().NotBeNullOrWhiteSpace();
        second.Value!.Items.Should().ContainSingle(); second.Value.ContinuationToken.Should().NotBeNullOrWhiteSpace();
        third.Value!.Items.Should().ContainSingle(); third.Value.ContinuationToken.Should().BeNull();
        new[] { first.Value.Items[0].SamAccountName, second.Value.Items[0].SamAccountName, third.Value.Items[0].SamAccountName }
            .Should().OnlyHaveUniqueItems();
        audit.Events.Select(item => item.Action).Should().Contain(AuditActions.DirectoryGroupQueryCompleted);
    }

    [Theory]
    [InlineData("pam12356@contoso.local", true, DirectoryQueryStatus.Success)]
    [InlineData("pam12356@contoso.local", false, DirectoryQueryStatus.NotFound)]
    [InlineData("missing.user", true, DirectoryQueryStatus.NotFound)]
    [InlineData("pam*", true, DirectoryQueryStatus.Invalid)]
    public async Task PrincipalGroups_EnforcesExactEffectiveLookupBehavior(string account, bool upn, DirectoryQueryStatus expected)
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(upn), out _, upn: upn);

        DirectoryQueryResult<DirectoryGroupPageResponse> result = await service.GetPrincipalGroupsAsync(
            new DirectoryPrincipalGroupsRequest(account, Purpose), Context, CancellationToken.None);

        result.Status.Should().Be(expected);
    }

    [Fact]
    public async Task GroupLookup_ReturnsSafeExactMetadataAndNotFound()
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(), out _);

        DirectoryQueryResult<DirectoryGroupDetailResponse> found = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("Operations Announcements", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupDetailResponse> missing = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("missing-group", Purpose), Context, CancellationToken.None);

        found.Value!.Group.Category.Should().Be("Distribution");
        found.Value.Group.Scope.Should().Be("Universal");
        found.Value.Group.LookupKey.Should().Be("dist-universal");
        missing.Status.Should().Be(DirectoryQueryStatus.NotFound);
        missing.ErrorCode.Should().Be(OperationalErrorCodes.DirectoryGroupNotFound);
    }

    [Fact]
    public async Task ReadOnlyQueries_AcceptMissingBlankAndTrimmedPurpose()
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(), out InMemoryAuditWriter audit);

        DirectoryQueryResult<DirectoryGroupPageResponse> principal = await service.GetPrincipalGroupsAsync(
            new DirectoryPrincipalGroupsRequest("pam12356"), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupDetailResponse> group = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("ops-read", "   "), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMemberPageResponse> members = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest("ops-read", "  optional context  "), Context, CancellationToken.None);

        principal.Status.Should().Be(DirectoryQueryStatus.Success);
        group.Status.Should().Be(DirectoryQueryStatus.Success);
        members.Status.Should().Be(DirectoryQueryStatus.Success);
        string serialized = JsonSerializer.Serialize(audit.Events);
        serialized.Should().Contain("\"purposeLength\":16");
        serialized.Should().NotContain("optional context");
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("control")]
    public async Task GroupLookup_RejectsUnsafeOptionalPurpose(string inputKind)
    {
        CountingProvider provider = new();
        DirectoryGroupQueryService service = CreateService(provider, new InMemoryAuditWriter());
        string purpose = inputKind == "oversized" ? new string('x', 257) : "context continuation\n";

        DirectoryQueryResult<DirectoryGroupDetailResponse> result = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("ops-read", purpose), Context, CancellationToken.None);

        result.Status.Should().Be(DirectoryQueryStatus.Invalid);
        provider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task OptionalPurpose_DoesNotPartitionTheQueryCache()
    {
        CountingProvider provider = new();
        DirectoryExplorerOptions options = new()
        {
            Cache = new DirectoryExplorerCacheOptions { Enabled = true, TtlSeconds = 30, MaxEntries = 10 }
        };
        DirectoryGroupQueryService service = CreateService(provider, new InMemoryAuditWriter(), options);

        _ = await service.GetGroupAsync(new DirectoryGroupLookupRequest("ops-read", "first context"), Context, CancellationToken.None);
        _ = await service.GetGroupAsync(new DirectoryGroupLookupRequest("ops-read", "second context"), Context, CancellationToken.None);

        provider.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Members_ReturnsDirectTypesAndRejectsPageAboveMaximum()
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(), out _);

        DirectoryQueryResult<DirectoryMemberPageResponse> found = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest("ops-read", Purpose, 100), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMemberPageResponse> invalid = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest("ops-read", Purpose, 101), Context, CancellationToken.None);

        found.Value!.Items.Select(item => item.MemberType).Should().BeEquivalentTo("User", "Group", "Computer");
        found.Value.Items.Single(item => item.MemberType == "Group").LookupKey.Should().Be("nested-ops");
        found.Value.Items.Single(item => item.MemberType == "User").LookupKey.Should().Be("pam12356");
        invalid.Status.Should().Be(DirectoryQueryStatus.Invalid);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Members_AcceptsUiFriendlyBoundedPageSizes(int pageSize)
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(), out _);

        DirectoryQueryResult<DirectoryMemberPageResponse> result = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest("ops-read", Purpose, pageSize), Context, CancellationToken.None);

        result.Status.Should().Be(DirectoryQueryStatus.Success);
        result.Value!.PageSize.Should().Be(pageSize);
    }

    [Fact]
    public async Task OverviewNameWithSpaces_ReturnsSamLookupKeyThatDirectMembersAccepts()
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(), out _);
        DirectoryQueryResult<DirectoryGroupDetailResponse> overview = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("Operations Readers", Purpose), Context, CancellationToken.None);

        DirectoryQueryResult<DirectoryMemberPageResponse> members = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest(overview.Value!.Group.LookupKey, Purpose, 25),
            Context,
            CancellationToken.None);

        overview.Status.Should().Be(DirectoryQueryStatus.Success);
        overview.Value.Group.Name.Should().NotBe(overview.Value.Group.SamAccountName);
        overview.Value.Group.LookupKey.Should().Be("ops-read");
        members.Status.Should().Be(DirectoryQueryStatus.Success);
        members.Value!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task OverviewCanRemainSuccessfulWhenMemberSectionFails()
    {
        OverviewOnlyProvider provider = new();
        DirectoryGroupQueryService service = CreateService(provider, out _);

        DirectoryQueryResult<DirectoryGroupDetailResponse> overview = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("similar-group-a", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMemberPageResponse> members = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest(overview.Value!.Group.LookupKey, Purpose), Context, CancellationToken.None);

        overview.Status.Should().Be(DirectoryQueryStatus.Success);
        overview.Value.Group.Name.Should().Be("Similar Group A");
        overview.Value.Group.LookupKey.Should().Be("similar-group-a");
        members.Status.Should().Be(DirectoryQueryStatus.ProviderUnavailable);
    }

    [Fact]
    public async Task OverviewCanRemainSuccessfulWhenDirectMembersAreEmpty()
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(), out _);

        DirectoryQueryResult<DirectoryGroupDetailResponse> overview = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("Operations Announcements", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMemberPageResponse> members = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest(overview.Value!.Group.LookupKey, Purpose), Context, CancellationToken.None);

        overview.Status.Should().Be(DirectoryQueryStatus.Success);
        members.Status.Should().Be(DirectoryQueryStatus.Success);
        members.Value!.Items.Should().BeEmpty();
        members.Value.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public async Task OverviewCanRemainSuccessfulWhenDirectMembersTimeOut()
    {
        DirectoryExplorerOptions options = new()
        {
            ProviderTimeoutSeconds = 1,
            Cache = new DirectoryExplorerCacheOptions { Enabled = false }
        };
        DirectoryGroupQueryService service = CreateService(
            new TimeoutMembersProvider(), new InMemoryAuditWriter(), options);

        DirectoryQueryResult<DirectoryGroupDetailResponse> overview = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("timeout-group", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMemberPageResponse> members = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest(overview.Value!.Group.LookupKey, Purpose), Context, CancellationToken.None);

        overview.Status.Should().Be(DirectoryQueryStatus.Success);
        members.Status.Should().Be(DirectoryQueryStatus.ProviderTimeout);
        members.ErrorCode.Should().Be(OperationalErrorCodes.DirectoryProviderTimeout);
    }

    [Fact]
    public async Task CanonicalKeys_CoverEqualDifferentSimilarNestedAndUserNavigationCases()
    {
        DirectoryGroupQueryService service = CreateService(new NavigationProvider(), out _);

        DirectoryQueryResult<DirectoryGroupDetailResponse> equal = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("same-name", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupDetailResponse> spaced = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("Display Name With Spaces", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupDetailResponse> similarA = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("Very Similar Group", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupDetailResponse> similarB = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("Very Similar Group 2", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryGroupPageResponse> fromUser = await service.GetPrincipalGroupsAsync(
            new DirectoryPrincipalGroupsRequest("pam12356", Purpose), Context, CancellationToken.None);
        DirectoryQueryResult<DirectoryMemberPageResponse> nested = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest("different-sam", Purpose), Context, CancellationToken.None);

        equal.Value!.Group.LookupKey.Should().Be("same-name");
        spaced.Value!.Group.LookupKey.Should().Be("different-sam");
        similarA.Value!.Group.LookupKey.Should().Be("similar-a");
        similarB.Value!.Group.LookupKey.Should().Be("similar-b");
        similarA.Value.Group.LookupKey.Should().NotBe(similarB.Value.Group.LookupKey);
        fromUser.Value!.Items.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.LookupKey));
        nested.Value!.Items.Single().LookupKey.Should().Be("nested-sam");
    }

    [Fact]
    public async Task ProviderFailure_IsSafeAndCancellationPropagates()
    {
        DirectoryGroupQueryService service = CreateService(new FailingProvider(), out _);

        DirectoryQueryResult<DirectoryGroupDetailResponse> failed = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("ops-read", Purpose), Context, CancellationToken.None);
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        Func<Task> cancel = () => service.GetGroupAsync(
            new DirectoryGroupLookupRequest("ops-read", Purpose, true), Context, cancelled.Token);

        failed.Status.Should().Be(DirectoryQueryStatus.ProviderUnavailable);
        failed.ErrorCode.Should().Be(OperationalErrorCodes.DirectoryProviderUnavailable);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Audit_ContainsHashesAndCountsButNotDirectoryPayload()
    {
        DirectoryGroupQueryService service = CreateService(CreateMock(), out InMemoryAuditWriter audit);

        _ = await service.GetGroupMembersAsync(
            new DirectoryGroupMembersRequest("ops-read", Purpose), Context, CancellationToken.None);

        string serialized = JsonSerializer.Serialize(audit.Events);
        serialized.Should().Contain("targetHash").And.Contain("resultCount");
        serialized.Should().NotContain("ops-read").And.NotContain("pam12356").And.NotContain(Purpose);
    }

    [Fact]
    public async Task AuditFailure_FailsClosedBeforeProviderInvocation()
    {
        CountingProvider provider = new();
        DirectoryGroupQueryService service = CreateService(provider, new ThrowingAuditWriter());

        DirectoryQueryResult<DirectoryGroupDetailResponse> result = await service.GetGroupAsync(
            new DirectoryGroupLookupRequest("ops-read", Purpose), Context, CancellationToken.None);

        result.Status.Should().Be(DirectoryQueryStatus.AuditUnavailable);
        provider.Calls.Should().Be(0);
    }

    private const string Purpose = "Approved synthetic incident verification";
    private static readonly DirectoryQueryExecutionContext Context = new("CONTOSO\\lead.user", "10.0.0.5", "directory-test");

    private static MockDirectoryGroupProvider CreateMock(bool upn = false) =>
        new(Options.Create(new IdentityLookupOptions { EnableUpnLookup = upn }));

    private static DirectoryGroupQueryService CreateService(
        IDirectoryGroupProvider provider,
        out InMemoryAuditWriter audit,
        bool upn = false)
    {
        audit = new InMemoryAuditWriter();
        return CreateService(provider, audit, upn);
    }

    private static DirectoryGroupQueryService CreateService(
        IDirectoryGroupProvider provider,
        IAuditWriter audit,
        bool upn = false) =>
        CreateService(provider, audit, new DirectoryExplorerOptions { Cache = new DirectoryExplorerCacheOptions { Enabled = false } }, upn);

    private static DirectoryGroupQueryService CreateService(
        IDirectoryGroupProvider provider,
        IAuditWriter audit,
        DirectoryExplorerOptions directoryOptions,
        bool upn = false)
    {
        IOptions<DirectoryExplorerOptions> directory = Options.Create(directoryOptions);
        return new DirectoryGroupQueryService(
            new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions { EnableUpnLookup = upn })),
            new DirectoryExactInputNormalizer(directory),
            provider,
            new TestContinuationTokenCodec(),
            new DirectoryQueryCache(directory, TimeProvider.System),
            audit,
            directory,
            NullLogger<DirectoryGroupQueryService>.Instance);
    }

    private sealed class FailingProvider : IDirectoryGroupProvider
    {
        public string ProviderName => "Failing";
        public bool SupportsUpnLookup => false;
        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) => throw new DirectoryProviderUnavailableException();
        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); throw new DirectoryProviderUnavailableException();
        }
        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) => throw new DirectoryProviderUnavailableException();
    }

    private sealed class CountingProvider : IDirectoryGroupProvider
    {
        public int Calls { get; private set; }
        public string ProviderName => "Counting";
        public bool SupportsUpnLookup => false;
        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) { Calls++; return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null); }
        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) { Calls++; return Task.FromResult<DirectoryGroupRecord?>(null); }
        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) { Calls++; return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null); }
    }

    private sealed class OverviewOnlyProvider : IDirectoryGroupProvider
    {
        public string ProviderName => "OverviewOnly";
        public bool SupportsUpnLookup => false;
        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);
        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryGroupRecord?>(new DirectoryGroupRecord(
                "S-1-5-21-9001", "Similar Group A", "similar-group-a",
                "CN=Similar Group A,OU=Groups,DC=example,DC=invalid", null, "Security", "Global"));
        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            throw new DirectoryProviderUnavailableException();
    }

    private sealed class NavigationProvider : IDirectoryGroupProvider
    {
        private static readonly DirectoryGroupRecord[] Groups =
        [
            new("S-1", "same-name", "same-name", null, null, "Security", "Global"),
            new("S-2", "Display Name With Spaces", "different-sam", null, null, "Security", "Global"),
            new("S-3", "Very Similar Group", "similar-a", null, null, "Security", "Global"),
            new("S-4", "Very Similar Group 2", "similar-b", null, null, "Security", "Global")
        ];

        public string ProviderName => "Navigation";
        public bool SupportsUpnLookup => false;
        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(
                new DirectoryProviderPage<DirectoryGroupRecord>(Groups.Take(pageSize).ToArray(), false));
        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryGroupRecord?>(Groups.SingleOrDefault(group =>
                string.Equals(group.Name, normalizedGroup, StringComparison.OrdinalIgnoreCase)
                || string.Equals(group.SamAccountName, normalizedGroup, StringComparison.OrdinalIgnoreCase)));
        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(
                new DirectoryProviderPage<DirectoryMemberRecord>(
                    [new DirectoryMemberRecord("S-5", "Nested Display", "nested-sam", null, "Group")],
                    false));
    }

    private sealed class TimeoutMembersProvider : IDirectoryGroupProvider
    {
        public string ProviderName => "TimeoutMembers";
        public bool SupportsUpnLookup => false;
        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);
        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryGroupRecord?>(new DirectoryGroupRecord(
                "S-6", "Timeout Group", "timeout-group", null, null, "Security", "Global"));
        public async Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("Synthetic audit failure");
    }

    private sealed class TestContinuationTokenCodec : IDirectoryContinuationTokenCodec
    {
        public string Create(string operation, string normalizedTarget, int offset) => $"{operation}|{normalizedTarget}|{offset}";

        public bool TryRead(string? token, string operation, string normalizedTarget, out int offset)
        {
            offset = 0;
            if (string.IsNullOrWhiteSpace(token))
            {
                return true;
            }

            string[] parts = token.Split('|');
            return parts.Length == 3
                && parts[0] == operation
                && parts[1] == normalizedTarget
                && int.TryParse(parts[2], out offset);
        }
    }
}
