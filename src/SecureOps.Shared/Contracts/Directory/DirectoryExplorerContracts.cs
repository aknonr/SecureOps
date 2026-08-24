namespace SecureOps.Shared.Contracts.Directory;

/// <summary>Exact principal direct-group request.</summary>
public sealed record DirectoryPrincipalGroupsRequest(
    string? Account,
    string? Purpose = null,
    int? PageSize = null,
    string? ContinuationToken = null,
    bool Refresh = false);

/// <summary>Exact group metadata request.</summary>
public sealed record DirectoryGroupLookupRequest(string? Group, string? Purpose = null, bool Refresh = false);

/// <summary>Exact group direct-members request.</summary>
public sealed record DirectoryGroupMembersRequest(
    string? Group,
    string? Purpose = null,
    int? PageSize = null,
    string? ContinuationToken = null,
    bool Refresh = false);

/// <summary>Safe direct group summary.</summary>
public sealed record DirectoryGroupSummaryDto(
    string? StableIdentifier,
    string? Name,
    string? SamAccountName,
    string? DistinguishedName,
    string? Description,
    string Category,
    string Scope,
    string? MembershipKind = null);

/// <summary>Safe exact group metadata.</summary>
public sealed record DirectoryGroupDetailDto(
    string? StableIdentifier,
    string? Name,
    string? SamAccountName,
    string? DistinguishedName,
    string? Description,
    string Category,
    string Scope,
    string? ManagedBy,
    int? DirectMemberCount,
    string? ManagedByDisplayName = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? ChangedAtUtc = null);

/// <summary>Safe direct member projection.</summary>
public sealed record DirectoryMemberDto(
    string? StableIdentifier,
    string? Name,
    string? SamAccountName,
    string? DistinguishedName,
    string MemberType);

/// <summary>Bounded direct-group page.</summary>
public sealed record DirectoryGroupPageResponse(
    IReadOnlyList<DirectoryGroupSummaryDto> Items,
    int PageSize,
    string? ContinuationToken,
    bool IsComplete = true);

/// <summary>Exact group metadata response.</summary>
public sealed record DirectoryGroupDetailResponse(DirectoryGroupDetailDto Group);

/// <summary>Bounded direct-member page.</summary>
public sealed record DirectoryMemberPageResponse(
    IReadOnlyList<DirectoryMemberDto> Items,
    int PageSize,
    string? ContinuationToken,
    bool IncludesPrimaryGroupMembers = false,
    string MembershipSemantics = "ExplicitMemberLinks");
