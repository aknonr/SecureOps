namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Bounded traversal outcome retained with a membership graph.</summary>
public sealed record DirectoryTraversalState(
    int NodesVisited,
    int EdgesVisited,
    int MaximumDepthReached,
    bool CycleDetected,
    bool DepthLimitReached,
    bool NodeLimitReached,
    bool EdgeLimitReached,
    bool ProviderResultLimitReached)
{
    /// <summary>Whether any configured boundary prevented complete expansion.</summary>
    public bool IsTruncated => DepthLimitReached || NodeLimitReached || EdgeLimitReached || ProviderResultLimitReached;
}

/// <summary>Provider-neutral, bounded directed membership graph.</summary>
public sealed class DirectoryMembershipGraph
{
    private readonly IReadOnlyDictionary<string, DirectoryGroupRecord> _groups;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _adjacency;
    private readonly IReadOnlyList<string> _directGroupKeys;
    private readonly IReadOnlyDictionary<string, int> _minimumDepths;

    /// <summary>Initializes an immutable graph.</summary>
    public DirectoryMembershipGraph(
        IReadOnlyDictionary<string, DirectoryGroupRecord> groups,
        IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency,
        IReadOnlyList<string> directGroupKeys,
        IReadOnlyDictionary<string, int> minimumDepths,
        DirectoryTraversalState traversal)
    {
        _groups = groups;
        _adjacency = adjacency;
        _directGroupKeys = directGroupKeys;
        _minimumDepths = minimumDepths;
        Traversal = traversal;
    }

    /// <summary>All unique groups keyed by provider-stable identity.</summary>
    public IReadOnlyDictionary<string, DirectoryGroupRecord> Groups => _groups;

    /// <summary>Groups directly assigned to the source principal.</summary>
    public IReadOnlyList<string> DirectGroupKeys => _directGroupKeys;

    /// <summary>Minimum discovered membership depth for every group.</summary>
    public IReadOnlyDictionary<string, int> MinimumDepths => _minimumDepths;

    /// <summary>Traversal bounds and outcome.</summary>
    public DirectoryTraversalState Traversal { get; }

    /// <summary>Returns deterministic shortest-first simple paths to one group.</summary>
    public IReadOnlyList<IReadOnlyList<string>> FindPaths(string targetKey, int maxPaths, out bool truncated)
    {
        truncated = false;
        if (!_groups.ContainsKey(targetKey))
        {
            return [];
        }

        int stateBudget = Math.Min(100_000, Math.Max(maxPaths + 1, Traversal.EdgesVisited * (maxPaths + 1)));
        var queue = new Queue<IReadOnlyList<string>>();
        foreach (string direct in _directGroupKeys)
        {
            queue.Enqueue([direct]);
        }

        List<IReadOnlyList<string>> paths = [];
        int states = 0;
        while (queue.Count > 0 && paths.Count <= maxPaths)
        {
            if (++states > stateBudget)
            {
                truncated = true;
                break;
            }

            IReadOnlyList<string> path = queue.Dequeue();
            string current = path[^1];
            if (string.Equals(current, targetKey, StringComparison.Ordinal))
            {
                paths.Add(path);
                continue;
            }

            if (!_adjacency.TryGetValue(current, out IReadOnlyList<string>? parents))
            {
                continue;
            }

            foreach (string parent in parents)
            {
                if (path.Contains(parent, StringComparer.Ordinal))
                {
                    continue;
                }

                queue.Enqueue(path.Append(parent).ToArray());
            }
        }

        if (paths.Count > maxPaths)
        {
            truncated = true;
        }

        return paths.Take(maxPaths).ToArray();
    }

    /// <summary>Returns whether a direct group also has a simple nested path from another direct group.</summary>
    public bool IsAlsoTransitivelyReachable(string targetKey)
    {
        var pending = new Stack<string>(_directGroupKeys
            .Where(key => !string.Equals(key, targetKey, StringComparison.Ordinal))
            .Reverse());
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (string.Equals(current, targetKey, StringComparison.Ordinal))
            {
                return true;
            }

            if (_adjacency.TryGetValue(current, out IReadOnlyList<string>? parents))
            {
                foreach (string parent in parents.Reverse())
                {
                    pending.Push(parent);
                }
            }
        }

        return false;
    }

    /// <summary>Builds a deterministic internal key without exposing it through API contracts.</summary>
    public static string? GroupKey(DirectoryGroupRecord group)
    {
        string? value = First(group.StableIdentifier, group.SamAccountName, group.DistinguishedName, group.Name);
        return value is null ? null : value.Trim().ToLowerInvariant();
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
