namespace SecureOps.Shared.Contracts.Directory;

/// <summary>Exact bounded group-analysis request.</summary>
public sealed record DirectoryGroupAnalysisRequest(string? Group, string? Purpose = null, bool Refresh = false);

/// <summary>One group node in a bounded nested topology.</summary>
public sealed record DirectoryGroupTopologyNodeDto(string NodeId, DirectoryGroupSummaryDto Group, int MinimumDepth);

/// <summary>One proven explicit group-to-nested-group edge.</summary>
public sealed record DirectoryGroupTopologyEdgeDto(string ParentNodeId, string ChildNodeId);

/// <summary>Direct and transitive parent-group evidence.</summary>
public sealed record DirectoryGroupParentMembershipsDto(
    IReadOnlyList<DirectoryMembershipGroupDto> DirectParents,
    IReadOnlyList<DirectoryMembershipGroupDto> TransitiveParents,
    DirectoryTraversalMetadataDto Traversal);

/// <summary>Complete bounded group evidence with explicit direct/effective/topology semantics.</summary>
public sealed record DirectoryGroupAnalysisResponse(
    DirectoryGroupDetailDto Overview,
    IReadOnlyList<DirectoryMemberDto> DirectMembers,
    IReadOnlyList<DirectoryGroupSummaryDto> DirectNestedGroups,
    IReadOnlyList<DirectoryMemberDto> EffectiveMembers,
    IReadOnlyList<DirectoryGroupTopologyNodeDto> TopologyNodes,
    IReadOnlyList<DirectoryGroupTopologyEdgeDto> TopologyEdges,
    DirectoryGroupParentMembershipsDto ParentMemberships,
    DirectoryTraversalMetadataDto DescendantTraversal,
    bool IsComplete,
    bool DirectMembersIncludePrimaryGroupMembers = false);

/// <summary>Bounded membership export request.</summary>
public sealed record DirectoryGroupExportRequest(
    string? Group,
    string? Mode,
    string? Format,
    string? Purpose = null,
    bool Refresh = false);
