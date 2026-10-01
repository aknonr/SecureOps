using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Builds bounded child and parent group evidence from explicit directory edges.</summary>
public sealed class DirectoryGroupAnalysisBuilder
{
    private readonly IDirectoryGroupProvider _groups;
    private readonly IDirectoryEnrichmentProvider _enrichment;
    private readonly DirectoryExplorerOptions _options;

    /// <summary>Initializes the builder.</summary>
    public DirectoryGroupAnalysisBuilder(
        IDirectoryGroupProvider groups,
        IDirectoryEnrichmentProvider enrichment,
        IOptions<DirectoryExplorerOptions> options)
    {
        _groups = groups;
        _enrichment = enrichment;
        _options = options.Value;
    }

    /// <summary>Gets the selected provider name for cache partitioning and diagnostics.</summary>
    public string ProviderName => _groups.ProviderName;

    /// <summary>Builds one complete-or-explicitly-partial analysis.</summary>
    public async Task<DirectoryGroupAnalysisResponse?> BuildAsync(
        string normalizedGroup,
        CancellationToken cancellationToken)
    {
        DirectoryGroupRecord? root = await _groups.FindGroupAsync(normalizedGroup, cancellationToken);
        if (root is null)
        {
            return null;
        }

        DescendantResult descendants = await DescendantsAsync(root, cancellationToken);
        ParentResult parents = await ParentsAsync(root, cancellationToken);
        bool complete = !descendants.Traversal.IsTruncated && !parents.Traversal.IsTruncated;
        return new DirectoryGroupAnalysisResponse(
            Detail(root),
            descendants.DirectMembers.Select(Member).ToArray(),
            descendants.DirectNestedGroups.Select(Group).ToArray(),
            descendants.EffectiveMembers.Select(Member).ToArray(),
            descendants.TopologyNodes,
            descendants.TopologyEdges,
            new DirectoryGroupParentMembershipsDto(
                parents.Direct.Select(item => new DirectoryMembershipGroupDto(Group(item), 1, false)).ToArray(),
                parents.Transitive.Select(item => new DirectoryMembershipGroupDto(Group(item.Group), item.Depth, false)).ToArray(),
                Traversal(parents.Traversal)),
            Traversal(descendants.Traversal),
            complete);
    }

    private async Task<DescendantResult> DescendantsAsync(
        DirectoryGroupRecord root,
        CancellationToken cancellationToken)
    {
        string rootKey = Key(root) ?? throw new DirectoryProviderUnavailableException();
        var groups = new Dictionary<string, DirectoryGroupRecord>(StringComparer.Ordinal) { [rootKey] = root };
        var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [rootKey] = 0 };
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        var effective = new Dictionary<string, DirectoryMemberRecord>(StringComparer.Ordinal);
        var directMembers = new List<DirectoryMemberRecord>();
        var directNested = new Dictionary<string, DirectoryGroupRecord>(StringComparer.Ordinal);
        queue.Enqueue(rootKey);
        int edges = 0;
        bool cycle = false;
        bool depthLimit = false;
        bool nodeLimit = false;
        bool edgeLimit = false;
        bool providerLimit = false;

        while (queue.Count > 0 && !nodeLimit && !edgeLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentKey = queue.Dequeue();
            int depth = depths[currentKey];
            if (depth >= _options.MaxTraversalDepth)
            {
                depthLimit = true;
                continue;
            }

            string? identifier = Identifier(groups[currentKey]);
            if (identifier is null)
            {
                providerLimit = true;
                continue;
            }

            int remainingEdges = _options.MaxTraversalEdges - edges;
            if (remainingEdges <= 0)
            {
                edgeLimit = true;
                break;
            }

            DirectoryProviderPage<DirectoryMemberRecord>? page =
                await _groups.GetDirectMembersForAnalysisAsync(identifier, remainingEdges, cancellationToken);
            if (page is null)
            {
                throw new DirectoryProviderUnavailableException();
            }

            providerLimit |= page.HasMore || page.IsPartial;
            foreach (DirectoryMemberRecord member in Order(page.Items))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (edges >= _options.MaxTraversalEdges)
                {
                    edgeLimit = true;
                    break;
                }

                edges++;
                if (depth == 0)
                {
                    directMembers.Add(member);
                }

                if (!string.Equals(member.MemberType, "Group", StringComparison.Ordinal))
                {
                    string? memberKey = MemberKey(member);
                    if (memberKey is not null && !effective.ContainsKey(memberKey))
                    {
                        if (effective.Count >= _options.MaxEffectiveMembers
                            || groups.Count + effective.Count >= _options.MaxTraversalNodes)
                        {
                            nodeLimit = true;
                            break;
                        }

                        effective[memberKey] = member;
                    }

                    continue;
                }

                string? groupIdentifier = First(member.SamAccountName, member.Name);
                DirectoryGroupRecord? child = groupIdentifier is null
                    ? null
                    : await _groups.FindGroupAsync(groupIdentifier, cancellationToken);
                string? childKey = child is null ? null : Key(child);
                if (child is null || childKey is null)
                {
                    providerLimit = true;
                    continue;
                }

                if (depth == 0)
                {
                    directNested[childKey] = child;
                }

                HashSet<string> children = adjacency.GetValueOrDefault(currentKey) ?? [];
                adjacency[currentKey] = children;
                if (!children.Add(childKey))
                {
                    continue;
                }

                cycle |= string.Equals(currentKey, childKey, StringComparison.Ordinal)
                    || PathExists(childKey, currentKey, adjacency);
                if (!groups.ContainsKey(childKey))
                {
                    if (groups.Count + effective.Count >= _options.MaxTraversalNodes)
                    {
                        nodeLimit = true;
                        break;
                    }

                    groups[childKey] = child;
                    depths[childKey] = depth + 1;
                    queue.Enqueue(childKey);
                }
            }
        }

        DirectoryTraversalState traversal = new(
            groups.Count + effective.Count,
            edges,
            depths.Values.DefaultIfEmpty(0).Max(),
            cycle,
            depthLimit,
            nodeLimit,
            edgeLimit,
            providerLimit);
        DirectoryGroupTopologyNodeDto[] nodes = groups
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DirectoryGroupTopologyNodeDto(pair.Key, Group(pair.Value), depths[pair.Key]))
            .ToArray();
        DirectoryGroupTopologyEdgeDto[] topologyEdges = adjacency
            .SelectMany(pair => pair.Value.Select(child => new DirectoryGroupTopologyEdgeDto(pair.Key, child)))
            .OrderBy(edge => edge.ParentNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.ChildNodeId, StringComparer.Ordinal)
            .ToArray();
        return new DescendantResult(
            directMembers,
            directNested.Values.OrderBy(Key, StringComparer.Ordinal).ToArray(),
            effective.Values.OrderBy(MemberKey, StringComparer.Ordinal).ToArray(),
            nodes,
            topologyEdges,
            traversal);
    }

    private async Task<ParentResult> ParentsAsync(
        DirectoryGroupRecord root,
        CancellationToken cancellationToken)
    {
        string rootKey = Key(root) ?? throw new DirectoryProviderUnavailableException();
        var known = new Dictionary<string, DirectoryGroupRecord>(StringComparer.Ordinal) { [rootKey] = root };
        var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [rootKey] = 0 };
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        var direct = new Dictionary<string, DirectoryGroupRecord>(StringComparer.Ordinal);
        int edges = 0;
        bool cycle = false;
        bool depthLimit = false;
        bool nodeLimit = false;
        bool edgeLimit = false;
        bool providerLimit = false;
        queue.Enqueue(rootKey);

        while (queue.Count > 0 && !nodeLimit && !edgeLimit)
        {
            string currentKey = queue.Dequeue();
            int depth = depths[currentKey];
            if (depth >= _options.MaxTraversalDepth)
            {
                depthLimit = true;
                continue;
            }

            int remaining = _options.MaxTraversalEdges - edges;
            if (remaining <= 0)
            {
                edgeLimit = true;
                break;
            }

            DirectoryProviderPage<DirectoryGroupRecord>? page = await _enrichment.GetParentGroupsAsync(
                known[currentKey], remaining, cancellationToken);
            if (page is null)
            {
                throw new DirectoryProviderUnavailableException();
            }

            providerLimit |= page.HasMore || page.IsPartial;
            foreach (DirectoryGroupRecord parent in page.Items.OrderBy(Key, StringComparer.Ordinal))
            {
                string? parentKey = Key(parent);
                if (parentKey is null)
                {
                    providerLimit = true;
                    continue;
                }

                if (++edges > _options.MaxTraversalEdges)
                {
                    edgeLimit = true;
                    break;
                }

                if (depth == 0)
                {
                    direct[parentKey] = parent with { MembershipKind = "Direct" };
                }

                HashSet<string> parents = adjacency.GetValueOrDefault(currentKey) ?? [];
                adjacency[currentKey] = parents;
                if (!parents.Add(parentKey))
                {
                    continue;
                }

                cycle |= string.Equals(currentKey, parentKey, StringComparison.Ordinal)
                    || PathExists(parentKey, currentKey, adjacency);
                if (known.ContainsKey(parentKey))
                {
                    continue;
                }

                if (known.Count >= _options.MaxTraversalNodes)
                {
                    nodeLimit = true;
                    break;
                }

                known[parentKey] = parent with { MembershipKind = depth == 0 ? "Direct" : "Transitive" };
                depths[parentKey] = depth + 1;
                queue.Enqueue(parentKey);
            }
        }

        DirectoryTraversalState traversal = new(
            known.Count,
            edges,
            depths.Values.DefaultIfEmpty(0).Max(),
            cycle,
            depthLimit,
            nodeLimit,
            edgeLimit,
            providerLimit);
        ParentDepth[] transitive = known
            .Where(pair => depths[pair.Key] >= 2)
            .Select(pair => new ParentDepth(pair.Value, depths[pair.Key]))
            .OrderBy(item => item.Depth)
            .ThenBy(item => Key(item.Group), StringComparer.Ordinal)
            .ToArray();
        return new ParentResult(direct.Values.OrderBy(Key, StringComparer.Ordinal).ToArray(), transitive, traversal);
    }

    private static DirectoryGroupDetailDto Detail(DirectoryGroupRecord group) => new(
        group.StableIdentifier, group.Name, group.SamAccountName, group.DistinguishedName,
        group.Description, group.Category, group.Scope, group.ManagedBy, group.DirectMemberCount,
        group.ManagedByDisplayName, group.CreatedAtUtc, group.ChangedAtUtc, group.SamAccountName);

    private static DirectoryGroupSummaryDto Group(DirectoryGroupRecord group) => new(
        group.StableIdentifier, group.Name, group.SamAccountName, group.DistinguishedName,
        group.Description, group.Category, group.Scope, group.MembershipKind, group.SamAccountName);

    private static DirectoryMemberDto Member(DirectoryMemberRecord member) => new(
        member.StableIdentifier, member.Name, member.SamAccountName, member.DistinguishedName,
        member.MemberType, member.SamAccountName);

    private static DirectoryTraversalMetadataDto Traversal(DirectoryTraversalState state) => new(
        state.NodesVisited, state.EdgesVisited, state.MaximumDepthReached, state.CycleDetected,
        state.DepthLimitReached, state.NodeLimitReached, state.EdgeLimitReached,
        state.ProviderResultLimitReached, state.IsTruncated);

    private static IEnumerable<DirectoryMemberRecord> Order(IEnumerable<DirectoryMemberRecord> members) => members
        .OrderBy(MemberKey, StringComparer.Ordinal)
        .ThenBy(member => member.MemberType, StringComparer.Ordinal);

    private static bool PathExists(string source, string target, IReadOnlyDictionary<string, HashSet<string>> adjacency)
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

            if (adjacency.TryGetValue(current, out HashSet<string>? children))
            {
                foreach (string child in children)
                {
                    pending.Push(child);
                }
            }
        }

        return false;
    }

    private static string? Key(DirectoryGroupRecord group) => DirectoryMembershipGraph.GroupKey(group);
    private static string? MemberKey(DirectoryMemberRecord member) =>
        First(member.StableIdentifier, member.SamAccountName, member.DistinguishedName, member.Name)?.ToLowerInvariant();
    private static string? Identifier(DirectoryGroupRecord group) => First(group.SamAccountName, group.Name);
    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record DescendantResult(
        IReadOnlyList<DirectoryMemberRecord> DirectMembers,
        IReadOnlyList<DirectoryGroupRecord> DirectNestedGroups,
        IReadOnlyList<DirectoryMemberRecord> EffectiveMembers,
        IReadOnlyList<DirectoryGroupTopologyNodeDto> TopologyNodes,
        IReadOnlyList<DirectoryGroupTopologyEdgeDto> TopologyEdges,
        DirectoryTraversalState Traversal);
    private sealed record ParentDepth(DirectoryGroupRecord Group, int Depth);
    private sealed record ParentResult(
        IReadOnlyList<DirectoryGroupRecord> Direct,
        IReadOnlyList<ParentDepth> Transitive,
        DirectoryTraversalState Traversal);
}
