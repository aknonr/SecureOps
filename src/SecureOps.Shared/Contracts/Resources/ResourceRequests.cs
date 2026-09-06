using System.ComponentModel.DataAnnotations;

namespace SecureOps.Shared.Contracts.Resources;

/// <summary>Category replacement. Create requires version zero; updates require the current version.</summary>
public sealed record SaveResourceCategoryRequest(
    [Required, StringLength(80)] string Name,
    [Range(0, 100000)] int DisplayOrder = 0,
    bool ManagersOnly = false, bool Archived = false,
    [Range(0, long.MaxValue)] long ExpectedVersion = 0);

/// <summary>Full shared link replacement; metadata is always server-owned.</summary>
public sealed record SaveResourceLinkRequest(
    Guid CategoryId,
    [Required, StringLength(120)] string Name,
    [Required, StringLength(2048)] string Url,
    [Required, StringLength(300)] string Purpose,
    [StringLength(1000)] string? Notes = null,
    [StringLength(40)] string? Environment = null,
    [StringLength(40)] string? Location = null,
    [MaxLength(10)] IReadOnlyList<string>? Tags = null,
    [Range(0, 100000)] int DisplayOrder = 0,
    bool Active = true, bool Archived = false,
    [Range(0, long.MaxValue)] long ExpectedVersion = 0);

/// <summary>Bounded catalogue query. All filters are combined with AND.</summary>
public sealed record ResourceQuery(
    [StringLength(100)] string? Search = null,
    Guid? CategoryId = null,
    [StringLength(40)] string? Environment = null,
    [StringLength(40)] string? Location = null,
    [StringLength(30)] string? Tag = null,
    bool IncludeArchived = false,
    [Range(1, 10000)] int Page = 1,
    [Range(1, 100)] int PageSize = 50);

/// <summary>Idempotent favourite membership intent guarded by the personal aggregate version.</summary>
public sealed record SaveFavouriteRequest(bool Favourite, [Range(0, long.MaxValue)] long ExpectedVersion);

/// <summary>Whole personal-set replacement; duplicates are invalid and at most 100 links are accepted.</summary>
public sealed record SaveShiftSetRequest(
    [Required, StringLength(80)] string Name,
    [Required, MaxLength(100)] IReadOnlyList<Guid> LinkIds,
    bool IsDefault = false,
    [Range(0, long.MaxValue)] long ExpectedVersion = 0);
