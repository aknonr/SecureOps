using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Builds deterministic membership graphs within configured resource bounds.</summary>
public sealed class DirectoryMembershipGraphBuilder
{
    private readonly IDirectoryEnrichmentProvider _provider;
    private readonly DirectoryExplorerOptions _options;

    /// <summary>Initializes the graph builder.</summary>
    public DirectoryMembershipGraphBuilder(
        IDirectoryEnrichmentProvider provider,
        IOptions<DirectoryExplorerOptions> options)
    {
        _provider = provider;
        _options = options.Value;
    }

    /// <summary>Returns a bounded graph or null when the exact source principal does not exist.</summary>
    public async Task<DirectoryMembershipGraph?> BuildAsync(
        string normalizedAccount,
        CancellationToken cancellationToken)
    {
        int initialLimit = Math.Min(_options.MaxTraversalNodes, _options.MaxTraversalEdges);
        DirectoryProviderPage<DirectoryGroupRecord>? directPage =
            await _provider.GetPrincipalDirectMembershipGroupsAsync(
                normalizedAccount,
                initialLimit,
                cancellationToken);
        if (directPage is null)
        {
            return null;
        }

        var groups = new Dictionary<string, DirectoryGroupRecord>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var minimumDepths = new Dictionary<string, int>(StringComparer.Ordinal);
        var directKeys = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        int edgeCount = 0;
        bool cycleDetected = false;
        bool depthLimitReached = false;
        bool nodeLimitReached = false;
        bool edgeLimitReached = false;
        bool providerLimitReached = directPage.HasMore || directPage.IsPartial;

        foreach (DirectoryGroupRecord group in Order(directPage.Items))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? key = DirectoryMembershipGraph.GroupKey(group);
            if (key is null || directKeys.Contains(key))
            {
                continue;
            }

            if (groups.Count >= _options.MaxTraversalNodes)
            {
                nodeLimitReached = true;
                break;
            }

            if (edgeCount >= _options.MaxTraversalEdges)
            {
                edgeLimitReached = true;
                break;
            }

            groups[key] = group;
            minimumDepths[key] = 1;
            directKeys.Add(key);
            queue.Enqueue(key);
            edgeCount++;
        }

        while (queue.Count > 0 && !nodeLimitReached && !edgeLimitReached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentKey = queue.Dequeue();
            int currentDepth = minimumDepths[currentKey];
            if (currentDepth >= _options.MaxTraversalDepth)
            {
                depthLimitReached = true;
                continue;
            }

            int remainingEdges = _options.MaxTraversalEdges - edgeCount;
            if (remainingEdges <= 0)
            {
                edgeLimitReached = true;
                break;
            }

            DirectoryProviderPage<DirectoryGroupRecord>? parentPage = await _provider.GetParentGroupsAsync(
                groups[currentKey],
                remainingEdges,
                cancellationToken);
            if (parentPage is null)
            {
                throw new DirectoryProviderUnavailableException();
            }

            providerLimitReached |= parentPage.HasMore || parentPage.IsPartial;
            foreach (DirectoryGroupRecord parent in Order(parentPage.Items))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? parentKey = DirectoryMembershipGraph.GroupKey(parent);
                if (parentKey is null)
                {
                    continue;
                }

                HashSet<string> parents = adjacency.GetValueOrDefault(currentKey) ?? [];
                adjacency[currentKey] = parents;
                if (parents.Contains(parentKey))
                {
                    continue;
                }

                if (!groups.ContainsKey(parentKey) && groups.Count >= _options.MaxTraversalNodes)
                {
                    nodeLimitReached = true;
                    break;
                }

                if (edgeCount >= _options.MaxTraversalEdges)
                {
                    edgeLimitReached = true;
                    break;
                }

                cycleDetected |= string.Equals(currentKey, parentKey, StringComparison.Ordinal)
                    || PathExists(parentKey, currentKey, adjacency);
                parents.Add(parentKey);
                edgeCount++;

                if (!groups.ContainsKey(parentKey))
                {
                    groups[parentKey] = parent;
                    minimumDepths[parentKey] = currentDepth + 1;
                    queue.Enqueue(parentKey);
                }
            }
        }

        int maximumDepth = minimumDepths.Count == 0 ? 0 : minimumDepths.Values.Max();
        var immutableAdjacency = adjacency.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        DirectoryTraversalState traversal = new(
            groups.Count,
            edgeCount,
            maximumDepth,
            cycleDetected,
            depthLimitReached,
            nodeLimitReached,
            edgeLimitReached,
            providerLimitReached);
        return new DirectoryMembershipGraph(
            groups,
            immutableAdjacency,
            directKeys.ToArray(),
            minimumDepths,
            traversal);
    }

    private static IEnumerable<DirectoryGroupRecord> Order(IEnumerable<DirectoryGroupRecord> groups) => groups
        .OrderBy(group => DirectoryMembershipGraph.GroupKey(group), StringComparer.Ordinal)
        .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase);

    private static bool PathExists(
        string source,
        string target,
        IReadOnlyDictionary<string, HashSet<string>> adjacency)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(source);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return true;
            }

            if (adjacency.TryGetValue(current, out HashSet<string>? parents))
            {
                foreach (string parent in parents)
                {
                    pending.Push(parent);
                }
            }
        }

        return false;
    }
}
