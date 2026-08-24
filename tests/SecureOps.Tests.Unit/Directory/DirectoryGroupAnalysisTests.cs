using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Tests.Unit.DirectoryExplorer;

public sealed class DirectoryGroupAnalysisTests
{
    [Fact]
    public async Task SecurityGlobalFixture_PreservesTwelveDirectUsers()
    {
        DirectoryGroupAnalysisResponse result = (await Builder().BuildAsync("security-global-12", default))!;

        result.Overview.Category.Should().Be("Security");
        result.Overview.Scope.Should().Be("Global");
        result.DirectMembers.Should().HaveCount(12).And.OnlyContain(member => member.MemberType == "User");
        result.EffectiveMembers.Should().HaveCount(12);
        result.DirectNestedGroups.Should().BeEmpty();
        result.DirectMembersIncludePrimaryGroupMembers.Should().BeFalse();
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task NestedFixture_SeparatesDirectNestedEffectiveAndTopologyEvidence()
    {
        DirectoryGroupAnalysisResponse result = (await Builder().BuildAsync("group-a", default))!;

        result.DirectMembers.Select(member => member.MemberType).Should().BeEquivalentTo("User", "Group");
        result.DirectNestedGroups.Select(group => group.SamAccountName).Should().Equal("group-b");
        result.EffectiveMembers.Select(member => member.SamAccountName)
            .Should().BeEquivalentTo("leaf-a", "host-b$", "leaf-c");
        result.TopologyNodes.Select(node => node.Group.SamAccountName)
            .Should().BeEquivalentTo("group-a", "group-b", "group-c");
        result.TopologyEdges.Should().HaveCount(2);
        result.DescendantTraversal.CycleDetected.Should().BeFalse();
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task MixedFixture_PreservesUserComputerAndNestedGroupTypes()
    {
        DirectoryGroupAnalysisResponse result = (await Builder().BuildAsync("mixed-group", default))!;

        result.DirectMembers.Select(member => member.MemberType)
            .Should().BeEquivalentTo("User", "Computer", "Group");
        result.DirectNestedGroups.Select(group => group.SamAccountName).Should().Equal("group-c");
    }

    [Fact]
    public async Task ParentFixture_SeparatesDirectAndTransitiveParentsWithoutFalseCycle()
    {
        DirectoryGroupAnalysisResponse result = (await Builder().BuildAsync("group-a", default))!;

        result.ParentMemberships.DirectParents.Select(group => group.Group.SamAccountName)
            .Should().Equal("parent-one");
        result.ParentMemberships.TransitiveParents.Select(group => group.Group.SamAccountName)
            .Should().Equal("parent-two");
        result.ParentMemberships.DirectParents[0].Group.MembershipKind.Should().Be("Direct");
        result.ParentMemberships.TransitiveParents[0].Group.MembershipKind.Should().Be("Transitive");
        result.ParentMemberships.Traversal.CycleDetected.Should().BeFalse();
    }

    [Fact]
    public async Task CycleAndLargeFixtures_ReportExplicitPartialEvidence()
    {
        DirectoryGroupAnalysisResponse cycle = (await Builder().BuildAsync("cycle-a", default))!;
        DirectoryExplorerOptions bounded = Options();
        bounded.MaxEffectiveMembers = 2;
        DirectoryGroupAnalysisResponse large = (await Builder(bounded).BuildAsync("large-group", default))!;

        cycle.DescendantTraversal.CycleDetected.Should().BeTrue();
        cycle.IsComplete.Should().BeTrue();
        large.IsComplete.Should().BeFalse();
        large.EffectiveMembers.Should().HaveCount(2);
        large.DescendantTraversal.NodeLimitReached.Should().BeTrue();
        large.DescendantTraversal.IsTruncated.Should().BeTrue();
    }

    [Theory]
    [InlineData("empty-group", "Security")]
    [InlineData("distribution-group", "Distribution")]
    public async Task EmptyAndDistributionFixtures_AreSuccessfulEmptyEvidence(string group, string category)
    {
        DirectoryGroupAnalysisResponse result = (await Builder().BuildAsync(group, default))!;

        result.Overview.Category.Should().Be(category);
        result.DirectMembers.Should().BeEmpty();
        result.EffectiveMembers.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task CsvExport_IsAuthorizedBoundaryReadyBoundedDeterministicAndFormulaSafe()
    {
        FixtureProvider provider = new();
        DirectoryExplorerOptions settings = Options();
        IOptions<DirectoryExplorerOptions> options = Microsoft.Extensions.Options.Options.Create(settings);
        InMemoryAuditWriter audit = new();
        DirectoryGroupAnalysisService service = new(
            new DirectoryExactInputNormalizer(options),
            new DirectoryGroupAnalysisBuilder(provider, provider, options),
            new DirectoryQueryCache(options, TimeProvider.System),
            audit,
            options,
            NullLogger<DirectoryGroupAnalysisService>.Instance);

        DirectoryQueryResult<DirectoryGroupExportResult> result = await service.ExportAsync(
            new DirectoryGroupExportRequest("csv-safe", "DirectMembers", "Csv"),
            new DirectoryQueryExecutionContext("CONTOSO\\admin", "192.0.2.10", "analysis-test"),
            default);

        result.Status.Should().Be(DirectoryQueryStatus.Success);
        result.Value!.RowCount.Should().Be(2);
        string csv = Encoding.UTF8.GetString(result.Value.Content);
        csv.Should().Contain("\"'=Formula Name\"").And.Contain("\"'+formula-account\"");
        string auditJson = JsonSerializer.Serialize(audit.Events);
        auditJson.Should().Contain(AuditActions.DirectoryGroupMembershipExported)
            .And.Contain("DirectMembers").And.Contain("RowCount")
            .And.NotContain("Formula Name").And.NotContain("formula-account");
    }

    [Fact]
    public async Task CsvExport_RejectsPartialTraversal()
    {
        FixtureProvider provider = new();
        DirectoryExplorerOptions settings = Options();
        settings.MaxEffectiveMembers = 2;
        IOptions<DirectoryExplorerOptions> options = Microsoft.Extensions.Options.Options.Create(settings);
        DirectoryGroupAnalysisService service = new(
            new DirectoryExactInputNormalizer(options),
            new DirectoryGroupAnalysisBuilder(provider, provider, options),
            new DirectoryQueryCache(options, TimeProvider.System),
            new InMemoryAuditWriter(),
            options,
            NullLogger<DirectoryGroupAnalysisService>.Instance);

        DirectoryQueryResult<DirectoryGroupExportResult> result = await service.ExportAsync(
            new DirectoryGroupExportRequest("large-group", "EffectiveMembers", "Csv"),
            new DirectoryQueryExecutionContext("CONTOSO\\admin", null, "analysis-test"),
            default);

        result.Status.Should().Be(DirectoryQueryStatus.LimitExceeded);
        result.ErrorCode.Should().Be("DirectoryTraversalPartial");
    }

    [Fact]
    public async Task AnalysisTimeout_ReturnsDistinctSafeProviderTimeout()
    {
        BlockingFixtureProvider provider = new();
        DirectoryExplorerOptions settings = Options();
        settings.TraversalTimeoutSeconds = 1;
        IOptions<DirectoryExplorerOptions> options = Microsoft.Extensions.Options.Options.Create(settings);
        DirectoryGroupAnalysisService service = new(
            new DirectoryExactInputNormalizer(options),
            new DirectoryGroupAnalysisBuilder(provider, provider, options),
            new DirectoryQueryCache(options, TimeProvider.System),
            new InMemoryAuditWriter(),
            options,
            NullLogger<DirectoryGroupAnalysisService>.Instance);

        DirectoryQueryResult<DirectoryGroupAnalysisResponse> result = await service.AnalyzeAsync(
            new DirectoryGroupAnalysisRequest("blocking-group"),
            new DirectoryQueryExecutionContext("CONTOSO\\admin", null, "analysis-timeout"),
            default);

        result.Status.Should().Be(DirectoryQueryStatus.ProviderTimeout);
        result.ErrorCode.Should().Be("DirectoryProviderTimeout");
        provider.CancellationObserved.Should().BeTrue();
    }

    private static DirectoryGroupAnalysisBuilder Builder(DirectoryExplorerOptions? settings = null)
    {
        FixtureProvider provider = new();
        return new DirectoryGroupAnalysisBuilder(
            provider,
            provider,
            Microsoft.Extensions.Options.Options.Create(settings ?? Options()));
    }

    private static DirectoryExplorerOptions Options() => new()
    {
        Cache = new DirectoryExplorerCacheOptions { Enabled = false },
        MaxTraversalDepth = 8,
        MaxTraversalNodes = 100,
        MaxTraversalEdges = 200,
        MaxEffectiveMembers = 50,
        MaxExportRows = 50
    };

    private sealed class FixtureProvider : IDirectoryGroupProvider, IDirectoryEnrichmentProvider
    {
        private readonly IReadOnlyDictionary<string, DirectoryGroupRecord> _groups = Groups();
        private readonly IReadOnlyDictionary<string, DirectoryMemberRecord[]> _members = Members();
        private readonly IReadOnlyDictionary<string, string[]> _parents = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["group-a"] = ["parent-one"],
            ["parent-one"] = ["parent-two"]
        };

        public string ProviderName => "RealEvidenceFixture";
        public bool SupportsUpnLookup => false;

        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) =>
            Task.FromResult(_groups.GetValueOrDefault(normalizedGroup));

        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(
            string normalizedGroup,
            int offset,
            int pageSize,
            int resultLimit,
            CancellationToken cancellationToken)
        {
            if (!_members.TryGetValue(normalizedGroup, out DirectoryMemberRecord[]? rows))
            {
                return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null);
            }

            DirectoryMemberRecord[] bounded = rows.Take(resultLimit + 1).ToArray();
            if (bounded.Length > resultLimit)
            {
                throw new DirectoryQueryLimitExceededException();
            }

            return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(
                new(bounded.Skip(offset).Take(pageSize).ToArray(), offset + pageSize < bounded.Length));
        }

        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersForAnalysisAsync(
            string normalizedGroup,
            int maxResults,
            CancellationToken cancellationToken)
        {
            if (!_members.TryGetValue(normalizedGroup, out DirectoryMemberRecord[]? rows))
            {
                return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null);
            }

            return Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(
                new(rows.Take(maxResults).ToArray(), rows.Length > maxResults));
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
            DirectoryGroupRecord group,
            int maxResults,
            CancellationToken cancellationToken)
        {
            string[] keys = group.SamAccountName is not null && _parents.TryGetValue(group.SamAccountName, out string[]? parents)
                ? parents
                : [];
            return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(
                new(keys.Take(maxResults).Select(key => _groups[key]).ToArray(), keys.Length > maxResults));
        }

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(
            string normalizedAccount,
            int offset,
            int pageSize,
            int resultLimit,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);

        public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(
            string normalizedAccount,
            int maxSpns,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(null);

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
            string normalizedAccount,
            int maxResults,
            CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);

        private static IReadOnlyDictionary<string, DirectoryGroupRecord> Groups()
        {
            string[] names =
            [
                "security-global-12", "group-a", "group-b", "group-c", "cycle-a", "cycle-b", "cycle-c",
                "large-group", "empty-group", "distribution-group", "mixed-group", "parent-one", "parent-two", "csv-safe"
            ];
            return names.ToDictionary(
                name => name,
                name => Group(name, name == "distribution-group" ? "Distribution" : "Security"),
                StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, DirectoryMemberRecord[]> Members() =>
            new Dictionary<string, DirectoryMemberRecord[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["security-global-12"] = Enumerable.Range(1, 12).Select(index => User($"member-{index:00}")).ToArray(),
                ["group-a"] = [User("leaf-a"), Nested("group-b")],
                ["group-b"] = [Computer("host-b$"), Nested("group-c")],
                ["group-c"] = [User("leaf-c")],
                ["cycle-a"] = [Nested("cycle-b")],
                ["cycle-b"] = [Nested("cycle-c")],
                ["cycle-c"] = [Nested("cycle-a")],
                ["large-group"] = Enumerable.Range(1, 8).Select(index => User($"large-{index:00}")).ToArray(),
                ["empty-group"] = [],
                ["distribution-group"] = [],
                ["mixed-group"] = [User("mixed-user"), Computer("mixed-host$"), Nested("group-c")],
                ["parent-one"] = [],
                ["parent-two"] = [],
                ["csv-safe"] =
                [
                    new("csv-1", "=Formula Name", "safe-account", null, "User"),
                    new("csv-2", "Safe Name", "+formula-account", null, "User")
                ]
            };

        private static DirectoryGroupRecord Group(string name, string category) => new(
            $"sid-{name}", name, name, $"CN={name},OU=Groups,DC=example,DC=invalid", null,
            category, category == "Distribution" ? "Universal" : "Global");

        private static DirectoryMemberRecord User(string account) =>
            new($"sid-{account}", account, account, null, "User");

        private static DirectoryMemberRecord Computer(string account) =>
            new($"sid-{account}", account, account, null, "Computer");

        private static DirectoryMemberRecord Nested(string account) =>
            new($"sid-{account}", account, account, null, "Group");
    }

    private sealed class BlockingFixtureProvider : IDirectoryGroupProvider, IDirectoryEnrichmentProvider
    {
        public string ProviderName => "BlockingFixture";
        public bool SupportsUpnLookup => false;
        public bool CancellationObserved { get; private set; }

        public async Task<DirectoryGroupRecord?> FindGroupAsync(
            string normalizedGroup,
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

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectGroupsAsync(
            string normalizedAccount, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);

        public Task<DirectoryProviderPage<DirectoryMemberRecord>?> GetDirectMembersAsync(
            string normalizedGroup, int offset, int pageSize, int resultLimit, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryMemberRecord>?>(null);

        public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(
            string normalizedAccount, int maxSpns, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(null);

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
            string normalizedAccount, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
            DirectoryGroupRecord group, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(null);
    }
}
