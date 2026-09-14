using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.DirectoryExplorer;

public sealed class DirectoryProviderTests
{
    [Theory]
    [InlineData("pam12356")]
    [InlineData("pam12356@contoso.local")]
    public async Task Mock_GetPrincipalGroups_SupportsExactPamAndEnabledUpn(string account)
    {
        MockDirectoryGroupProvider provider = CreateMock(upn: true);

        DirectoryProviderPage<DirectoryGroupRecord>? result = await provider.GetPrincipalDirectGroupsAsync(
            account, 0, 10, 100, CancellationToken.None);

        result!.Items.Should().HaveCount(3);
        result.Items.Should().ContainSingle(group => group.MembershipKind == "Primary");
        result.Items.Should().OnlyContain(group => group.Category == "Security" || group.Category == "Distribution");
    }

    [Fact]
    public async Task Mock_GetPrincipalGroups_ReturnsEmptyAndUnknownDistinctly()
    {
        MockDirectoryGroupProvider provider = CreateMock();

        DirectoryProviderPage<DirectoryGroupRecord>? empty = await provider.GetPrincipalDirectGroupsAsync(
            "zero.groups", 0, 10, 100, CancellationToken.None);
        DirectoryProviderPage<DirectoryGroupRecord>? missing = await provider.GetPrincipalDirectGroupsAsync(
            "missing.user", 0, 10, 100, CancellationToken.None);

        empty.Should().NotBeNull();
        empty!.Items.Should().BeEmpty();
        missing.Should().BeNull();
    }

    [Theory]
    [InlineData("ops-read", "Security", "Global")]
    [InlineData("Operations Announcements", "Distribution", "Universal")]
    [InlineData("domain-local-empty", "Security", "DomainLocal")]
    public async Task Mock_FindGroup_ReturnsExactCategoryAndScope(string input, string category, string scope)
    {
        DirectoryGroupRecord? result = await CreateMock().FindGroupAsync(input, CancellationToken.None);

        result!.Category.Should().Be(category);
        result.Scope.Should().Be(scope);
    }

    [Fact]
    public async Task Mock_GetMembers_ReturnsDirectTypedMembersWithoutExpansion()
    {
        DirectoryProviderPage<DirectoryMemberRecord>? result = await CreateMock().GetDirectMembersAsync(
            "ops-read", 0, 10, 100, CancellationToken.None);

        result!.Items.Select(member => member.MemberType).Should().BeEquivalentTo("User", "Group", "Computer");
        result.Items.Should().ContainSingle(member => member.MemberType == "Group" && member.SamAccountName == "nested-ops");
        result.Items.Should().NotContain(member => member.SamAccountName == "nested-child");
    }

    [Fact]
    public async Task Mock_GetMembers_PagesAndReturnsEmptyGroup()
    {
        MockDirectoryGroupProvider provider = CreateMock();

        DirectoryProviderPage<DirectoryMemberRecord>? first = await provider.GetDirectMembersAsync(
            "ops-read", 0, 2, 100, CancellationToken.None);
        DirectoryProviderPage<DirectoryMemberRecord>? second = await provider.GetDirectMembersAsync(
            "ops-read", 2, 2, 100, CancellationToken.None);
        DirectoryProviderPage<DirectoryMemberRecord>? empty = await provider.GetDirectMembersAsync(
            "domain-local-empty", 0, 2, 100, CancellationToken.None);

        first!.Items.Should().HaveCount(2);
        first.HasMore.Should().BeTrue();
        second!.Items.Should().ContainSingle();
        second.HasMore.Should().BeFalse();
        empty!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveDirectoryProvider_UsesSamFirstAndUpnOnlyWhenEnabled()
    {
        FakeActiveDirectoryGroupClient client = new() { UpnResult = Page() };
        ActiveDirectoryDirectoryGroupProvider provider = CreateActive(client, upn: true);

        DirectoryProviderPage<DirectoryGroupRecord>? result = await provider.GetPrincipalDirectGroupsAsync(
            "pam12356@contoso.local", 0, 10, 100, CancellationToken.None);

        result.Should().NotBeNull();
        client.SamInputs.Should().ContainSingle("pam12356@contoso.local");
        client.UpnInputs.Should().ContainSingle("pam12356@contoso.local");
    }

    [Fact]
    public async Task ActiveDirectoryProvider_WithUpnDisabled_PreservesExactSamBehavior()
    {
        FakeActiveDirectoryGroupClient client = new() { SamResult = Page() };
        ActiveDirectoryDirectoryGroupProvider provider = CreateActive(client, upn: false);

        DirectoryProviderPage<DirectoryGroupRecord>? result = await provider.GetPrincipalDirectGroupsAsync(
            "pam12356", 0, 10, 100, CancellationToken.None);

        result.Should().NotBeNull();
        client.SamInputs.Should().ContainSingle("pam12356");
        client.UpnInputs.Should().BeEmpty();
        provider.SupportsUpnLookup.Should().BeFalse();
    }

    private static MockDirectoryGroupProvider CreateMock(bool upn = false) =>
        new(Options.Create(new IdentityLookupOptions { EnableUpnLookup = upn }));

    private static ActiveDirectoryDirectoryGroupProvider CreateActive(FakeActiveDirectoryGroupClient client, bool upn) =>
        new(Options.Create(new IdentityLookupOptions { EnableUpnLookup = upn }), client);

    private static DirectoryProviderPage<DirectoryGroupRecord> Page() =>
        new([new DirectoryGroupRecord("id", "Group", "group", "CN=Group", null, "Security", "Global")], false);

    private sealed class FakeActiveDirectoryGroupClient : IActiveDirectoryGroupClient
    {
        public List<string> SamInputs { get; } = [];
        public List<string> UpnInputs { get; } = [];
        public DirectoryProviderPage<DirectoryGroupRecord>? SamResult { get; init; }
        public DirectoryProviderPage<DirectoryGroupRecord>? UpnResult { get; init; }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsBySamAccountNameAsync(string account, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken)
        {
            SamInputs.Add(account);
            return Task.FromResult(SamResult);
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsByUpnAsync(string userPrincipalName, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken)
        {
            UpnInputs.Add(userPrincipalName);
            return Task.FromResult(UpnResult);
        }

        public Task<DirectoryGroupRecord?> FindGroupAsync(string group, CancellationToken cancellationToken) => Task.FromResult<DirectoryGroupRecord?>(null);
        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(string group, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) => Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null);
    }
}
