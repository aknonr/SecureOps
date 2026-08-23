using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.DirectoryExplorer;

public sealed class DirectoryEnrichmentProviderTests
{
    [Fact]
    public async Task ActiveDirectory_PrincipalEvidenceUsesSamFirstAndApprovedUpnFallback()
    {
        FakeClient client = new() { UpnPrincipal = Principal() };
        ActiveDirectoryDirectoryEnrichmentProvider provider = CreateProvider(client, enableUpn: true);

        DirectoryPrincipalEnrichmentRecord? result = await provider.FindPrincipalAsync(
            "sample.user@example.invalid", 10, CancellationToken.None);

        result.Should().NotBeNull();
        client.SamPrincipalInputs.Should().ContainSingle("sample.user@example.invalid");
        client.UpnPrincipalInputs.Should().ContainSingle("sample.user@example.invalid");
    }

    [Fact]
    public async Task ActiveDirectory_UpnDisabledDoesNotAttemptFallback()
    {
        FakeClient client = new() { UpnPrincipal = Principal() };
        ActiveDirectoryDirectoryEnrichmentProvider provider = CreateProvider(client, enableUpn: false);

        DirectoryPrincipalEnrichmentRecord? result = await provider.FindPrincipalAsync(
            "sample.user@example.invalid", 10, CancellationToken.None);

        result.Should().BeNull();
        client.SamPrincipalInputs.Should().ContainSingle();
        client.UpnPrincipalInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveDirectory_PamStyleExactSamAccountBehaviorIsUnchanged()
    {
        FakeClient client = new() { SamGroups = Page(Group("ops-read")) };
        ActiveDirectoryDirectoryEnrichmentProvider provider = CreateProvider(client, enableUpn: true);

        DirectoryProviderPage<DirectoryGroupRecord>? result =
            await provider.GetPrincipalDirectMembershipGroupsAsync("pam12356", 20, CancellationToken.None);

        result!.Items.Should().ContainSingle(item => item.SamAccountName == "ops-read");
        client.SamGroupInputs.Should().ContainSingle("pam12356");
        client.UpnGroupInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveDirectory_UnsafeAccountIsRejectedBeforeClientAccess()
    {
        FakeClient client = new();
        ActiveDirectoryDirectoryEnrichmentProvider provider = CreateProvider(client, enableUpn: true);

        Func<Task> act = () => provider.FindPrincipalAsync("sample*", 10, CancellationToken.None);

        await act.Should().ThrowAsync<IdentityProviderInputRejectedException>();
        client.SamPrincipalInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task Mock_ProvidesDeterministicUpnHealthSpnAndParentEvidence()
    {
        MockDirectoryEnrichmentProvider provider = new(
            Options.Create(new IdentityLookupOptions { EnableUpnLookup = true }));

        DirectoryPrincipalEnrichmentRecord? principal = await provider.FindPrincipalAsync(
            "pam12356@contoso.local", 1, CancellationToken.None);
        DirectoryProviderPage<DirectoryGroupRecord>? direct =
            await provider.GetPrincipalDirectMembershipGroupsAsync("pam12356", 10, CancellationToken.None);
        DirectoryProviderPage<DirectoryGroupRecord>? parents = await provider.GetParentGroupsAsync(
            direct!.Items.Single(item => item.SamAccountName == "ops-read"), 10, CancellationToken.None);

        principal!.Enabled.Should().BeTrue();
        principal.ServicePrincipalNames.Should().ContainSingle();
        principal.ServicePrincipalNamesTruncated.Should().BeTrue();
        parents!.Items.Should().ContainSingle(item => item.SamAccountName == "nested-ops");
    }

    private static ActiveDirectoryDirectoryEnrichmentProvider CreateProvider(FakeClient client, bool enableUpn) =>
        new(Options.Create(new IdentityLookupOptions
        {
            EnableUpnLookup = enableUpn,
            MaxAccountLength = 128,
            AllowedAccountPattern = "^[a-zA-Z0-9._@-]+$"
        }), client);

    private static DirectoryPrincipalEnrichmentRecord Principal() => new(
        "principal", "Sample", "sample.user", "sample.user@example.invalid", true, false,
        null, null, null, null, null, null, [], 0, false, "User");

    private static DirectoryGroupRecord Group(string account) =>
        new("group", "Group", account, "CN=Group", null, "Security", "Global");

    private static DirectoryProviderPage<DirectoryGroupRecord> Page(params DirectoryGroupRecord[] groups) =>
        new(groups, false);

    private sealed class FakeClient : IActiveDirectoryEnrichmentClient
    {
        public List<string> SamPrincipalInputs { get; } = [];
        public List<string> UpnPrincipalInputs { get; } = [];
        public List<string> SamGroupInputs { get; } = [];
        public List<string> UpnGroupInputs { get; } = [];
        public DirectoryPrincipalEnrichmentRecord? SamPrincipal { get; init; }
        public DirectoryPrincipalEnrichmentRecord? UpnPrincipal { get; init; }
        public DirectoryProviderPage<DirectoryGroupRecord>? SamGroups { get; init; }
        public DirectoryProviderPage<DirectoryGroupRecord>? UpnGroups { get; init; }

        public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalBySamAccountNameAsync(
            string account, int maxSpns, CancellationToken cancellationToken)
        {
            SamPrincipalInputs.Add(account);
            return Task.FromResult(SamPrincipal);
        }

        public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalByUpnAsync(
            string userPrincipalName, int maxSpns, CancellationToken cancellationToken)
        {
            UpnPrincipalInputs.Add(userPrincipalName);
            return Task.FromResult(UpnPrincipal);
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalMembershipGroupsBySamAccountNameAsync(
            string account, int maxResults, CancellationToken cancellationToken)
        {
            SamGroupInputs.Add(account);
            return Task.FromResult(SamGroups);
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalMembershipGroupsByUpnAsync(
            string userPrincipalName, int maxResults, CancellationToken cancellationToken)
        {
            UpnGroupInputs.Add(userPrincipalName);
            return Task.FromResult(UpnGroups);
        }

        public Task<DirectoryGroupRecord?> FindEnrichmentGroupAsync(string group, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryGroupRecord?>(null);

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
            DirectoryGroupRecord group, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(new DirectoryProviderPage<DirectoryGroupRecord>([], false));
    }
}
