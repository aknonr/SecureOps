namespace SecureOps.Shared.Contracts.Directory;

/// <summary>Exact principal enrichment request.</summary>
public sealed record DirectoryPrincipalEnrichmentRequest(
    string? Account,
    string? Purpose = null,
    bool Refresh = false);

/// <summary>Exact principal-to-group membership-path request.</summary>
public sealed record DirectoryMembershipPathRequest(
    string? Account,
    string? TargetGroup,
    string? Purpose = null,
    bool Refresh = false);

/// <summary>One group with explicit membership semantics.</summary>
public sealed record DirectoryMembershipGroupDto(
    DirectoryGroupSummaryDto Group,
    int MinimumDepth,
    bool AlsoTransitivelyReachable);

/// <summary>Safe graph traversal bounds and outcome metadata.</summary>
public sealed record DirectoryTraversalMetadataDto(
    int NodesVisited,
    int EdgesVisited,
    int MaximumDepthReached,
    bool CycleDetected,
    bool DepthLimitReached,
    bool NodeLimitReached,
    bool EdgeLimitReached,
    bool ProviderResultLimitReached,
    bool IsTruncated);

/// <summary>Direct and transitive memberships remain separate.</summary>
public sealed record DirectoryPrincipalMembershipsResponse(
    IReadOnlyList<DirectoryMembershipGroupDto> DirectGroups,
    IReadOnlyList<DirectoryMembershipGroupDto> TransitiveGroups,
    DirectoryTraversalMetadataDto Traversal);

/// <summary>One proven group chain beginning with a direct group and ending at the target.</summary>
public sealed record DirectoryMembershipPathDto(IReadOnlyList<DirectoryGroupSummaryDto> Groups);

/// <summary>Bounded exact principal-to-target membership proof.</summary>
public sealed record DirectoryMembershipPathResponse(
    bool IsMember,
    bool IsDirect,
    IReadOnlyList<DirectoryMembershipPathDto> Paths,
    bool PathsTruncated,
    DirectoryTraversalMetadataDto Traversal);

/// <summary>Safe operational account-health evidence.</summary>
public sealed record DirectoryAccountHealthResponse(
    bool? Enabled,
    bool? Locked,
    DateTimeOffset? PasswordLastSetUtc,
    int? PasswordAgeDays,
    bool? PasswordNeverExpires,
    DateTimeOffset? AccountExpiresUtc,
    bool? MustChangePassword,
    DateTimeOffset? LastLogonTimestampUtc,
    bool LastLogonTimestampIsApproximate);

/// <summary>Bounded SPN and directory account-type evidence.</summary>
public sealed record DirectoryServiceEvidenceResponse(
    IReadOnlyList<string> ServicePrincipalNames,
    int ServicePrincipalNameCount,
    bool ServicePrincipalNamesTruncated,
    string? ManagedBy,
    DateTimeOffset? AccountExpiresUtc,
    DateTimeOffset? PasswordLastSetUtc,
    int? PasswordAgeDays,
    string AccountTypeEvidence,
    int? DirectGroupCount,
    int? TransitiveGroupCount,
    DirectoryTraversalMetadataDto? Traversal,
    bool MembershipEvidenceAvailable);

/// <summary>Evidence for one exact server-configured privileged group.</summary>
public sealed record DirectoryPrivilegedMembershipDto(
    string ConfiguredIdentifier,
    bool GroupFound,
    DirectoryGroupSummaryDto? Group,
    bool Direct,
    bool Transitive,
    IReadOnlyList<DirectoryMembershipPathDto> Paths,
    bool PathsTruncated);

/// <summary>Separately authorized privileged-membership evidence.</summary>
public sealed record DirectoryPrivilegedMembershipResponse(
    IReadOnlyList<DirectoryPrivilegedMembershipDto> Groups,
    DirectoryTraversalMetadataDto Traversal);
