using FluentAssertions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.DirectoryExplorer;

public sealed class DirectoryMembershipGraphBuilderTests
{
    [Fact]
    public async Task BuildAsync_DistinguishesDirectTransitiveCyclesAndDuplicateEdges()
    {
        GraphProvider provider = new(
            ["a", "b"],
            new Dictionary<string, string[]>
            {
                ["a"] = ["c", "c"],
                ["b"] = ["c"],
                ["c"] = ["d"],
                ["d"] = ["a"]
            });
        DirectoryMembershipGraphBuilder builder = CreateBuilder(provider);

        DirectoryMembershipGraph? graph = await builder.BuildAsync("user", CancellationToken.None);

        graph.Should().NotBeNull();
        graph!.DirectGroupKeys.Select(KeyName).Should().Equal("a", "b");
        graph.MinimumDepths.Single(item => KeyName(item.Key) == "c").Value.Should().Be(2);
        graph.MinimumDepths.Single(item => KeyName(item.Key) == "d").Value.Should().Be(3);
        graph.Traversal.CycleDetected.Should().BeTrue();
        graph.Traversal.EdgesVisited.Should().Be(6);
        graph.IsAlsoTransitivelyReachable(Key("a")).Should().BeTrue();
    }

    [Fact]
    public async Task FindPaths_ReturnsMultipleDeterministicPathsAndNoUnprovenPath()
    {
        GraphProvider provider = new(
            ["a", "b"],
            new Dictionary<string, string[]>
            {
                ["a"] = ["c"],
                ["b"] = ["c"],
                ["c"] = ["target"]
            },
            additionalGroups: ["unrelated"]);
        DirectoryMembershipGraph graph = (await CreateBuilder(provider).BuildAsync("user", CancellationToken.None))!;

        IReadOnlyList<IReadOnlyList<string>> paths = graph.FindPaths(Key("target"), 5, out bool truncated);
        IReadOnlyList<IReadOnlyList<string>> unrelated = graph.FindPaths(Key("unrelated"), 5, out _);

        paths.Select(path => path.Select(KeyName)).Should().BeEquivalentTo(
            new[] { new[] { "a", "c", "target" }, new[] { "b", "c", "target" } },
            options => options.WithStrictOrdering());
        truncated.Should().BeFalse();
        unrelated.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_StopsAtConfiguredDepthAndReportsBoundary()
    {
        GraphProvider provider = new(
            ["a"],
            new Dictionary<string, string[]>
            {
                ["a"] = ["b"],
                ["b"] = ["c"]
            });
        DirectoryExplorerOptions options = Options();
        options.MaxTraversalDepth = 2;

        DirectoryMembershipGraph graph = (await CreateBuilder(provider, options).BuildAsync("user", CancellationToken.None))!;

        graph.Groups.Keys.Select(KeyName).Should().BeEquivalentTo("a", "b");
        graph.Traversal.DepthLimitReached.Should().BeTrue();
        graph.Traversal.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_StopsAtConfiguredNodeBudget()
    {
        GraphProvider provider = new(
            ["a"],
            new Dictionary<string, string[]>
            {
                ["a"] = ["b", "c"]
            });
        DirectoryExplorerOptions options = Options();
        options.MaxTraversalNodes = 2;

        DirectoryMembershipGraph graph = (await CreateBuilder(provider, options).BuildAsync("user", CancellationToken.None))!;

        graph.Traversal.NodesVisited.Should().Be(2);
        graph.Traversal.NodeLimitReached.Should().BeTrue();
        graph.Traversal.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_StopsAtConfiguredEdgeBudget()
    {
        GraphProvider provider = new(
            ["a", "b"],
            new Dictionary<string, string[]>
            {
                ["a"] = ["c"],
                ["b"] = ["d"]
            });
        DirectoryExplorerOptions options = Options();
        options.MaxTraversalEdges = 3;

        DirectoryMembershipGraph graph = (await CreateBuilder(provider, options).BuildAsync("user", CancellationToken.None))!;

        graph.Traversal.EdgesVisited.Should().Be(3);
        graph.Traversal.EdgeLimitReached.Should().BeTrue();
        graph.Traversal.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_UnknownPrincipalReturnsNullAndCancellationPropagates()
    {
        GraphProvider provider = new([], new Dictionary<string, string[]>(), principalExists: false);
        DirectoryMembershipGraphBuilder builder = CreateBuilder(provider);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        DirectoryMembershipGraph? missing = await builder.BuildAsync("missing", CancellationToken.None);
        Func<Task> cancel = () => builder.BuildAsync("user", cancelled.Token);

        missing.Should().BeNull();
        await cancel.Should().ThrowAsync<OperationCanceledException>();
    }

    private static DirectoryMembershipGraphBuilder CreateBuilder(
        IDirectoryEnrichmentProvider provider,
        DirectoryExplorerOptions? options = null) =>
        new(provider, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    private static DirectoryExplorerOptions Options() => new()
    {
        MaxTraversalDepth = 8,
        MaxTraversalNodes = 20,
        MaxTraversalEdges = 40,
        MaxMembershipPaths = 5
    };

    private static string Key(string name) => $"sid-{name}";
    private static string KeyName(string key) => key[4..];

    private sealed class GraphProvider : IDirectoryEnrichmentProvider
    {
        private readonly IReadOnlyList<string> _direct;
        private readonly IReadOnlyDictionary<string, string[]> _parents;
        private readonly IReadOnlyDictionary<string, DirectoryGroupRecord> _groups;
        private readonly bool _principalExists;

        public GraphProvider(
            IReadOnlyList<string> direct,
            IReadOnlyDictionary<string, string[]> parents,
            IReadOnlyList<string>? additionalGroups = null,
            bool principalExists = true)
        {
            _direct = direct;
            _parents = parents;
            _principalExists = principalExists;
            _groups = direct
                .Concat(parents.Keys)
                .Concat(parents.Values.SelectMany(values => values))
                .Concat(additionalGroups ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, Group, StringComparer.OrdinalIgnoreCase);
        }

        public string ProviderName => "Graph";

        public Task<DirectoryPrincipalEnrichmentRecord?> FindPrincipalAsync(string normalizedAccount, int maxSpns, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryPrincipalEnrichmentRecord?>(_principalExists ? Principal() : null);

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetPrincipalDirectMembershipGroupsAsync(
            string normalizedAccount,
            int maxResults,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(
                _principalExists ? Page(_direct, maxResults) : null);
        }

        public Task<DirectoryGroupRecord?> FindGroupAsync(string normalizedGroup, CancellationToken cancellationToken) =>
            Task.FromResult(_groups.GetValueOrDefault(normalizedGroup));

        public Task<DirectoryProviderPage<DirectoryGroupRecord>?> GetParentGroupsAsync(
            DirectoryGroupRecord group,
            int maxResults,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = group.SamAccountName!;
            return Task.FromResult<DirectoryProviderPage<DirectoryGroupRecord>?>(
                Page(_parents.GetValueOrDefault(key) ?? [], maxResults));
        }

        private DirectoryProviderPage<DirectoryGroupRecord> Page(IEnumerable<string> names, int maxResults)
        {
            string[] all = names.ToArray();
            return new DirectoryProviderPage<DirectoryGroupRecord>(
                all.Take(maxResults).Select(name => _groups[name]).ToArray(),
                all.Length > maxResults);
        }

        private static DirectoryGroupRecord Group(string name) =>
            new(Key(name), name.ToUpperInvariant(), name, $"CN={name}", null, "Security", "Global");

        private static DirectoryPrincipalEnrichmentRecord Principal() => new(
            "principal", "User", "user", "user@example.invalid", true, false, null, null, null, null, null, null, [], 0, false, "User");
    }
}
