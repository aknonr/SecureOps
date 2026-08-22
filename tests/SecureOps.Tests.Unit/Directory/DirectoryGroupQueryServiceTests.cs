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

        first.Status.Should().Be(DirectoryQueryStatus.Success);
        first.Value!.Items.Should().ContainSingle(); first.Value.ContinuationToken.Should().NotBeNullOrWhiteSpace();
        second.Value!.Items.Should().ContainSingle(); second.Value.ContinuationToken.Should().BeNull();
        first.Value.Items[0].SamAccountName.Should().NotBe(second.Value.Items[0].SamAccountName);
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
        missing.Status.Should().Be(DirectoryQueryStatus.NotFound);
        missing.ErrorCode.Should().Be(OperationalErrorCodes.DirectoryGroupNotFound);
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
        invalid.Status.Should().Be(DirectoryQueryStatus.Invalid);
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
        bool upn = false)
    {
        DirectoryExplorerOptions directoryOptions = new() { Cache = new DirectoryExplorerCacheOptions { Enabled = false } };
        IOptions<DirectoryExplorerOptions> directory = Options.Create(directoryOptions);
        return new DirectoryGroupQueryService(
            new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions { EnableUpnLookup = upn })),
            new DirectoryExactInputNormalizer(directory),
            provider,
            new DirectoryContinuationTokenCodec(directory, TimeProvider.System),
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

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("Synthetic audit failure");
    }
}
