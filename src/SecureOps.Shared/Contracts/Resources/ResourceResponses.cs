using SecureOps.Domain.Resources;

namespace SecureOps.Shared.Contracts.Resources;

/// <summary>Stable bounded page; total counts only entries permitted by this request.</summary>
public sealed record ResourcePage(IReadOnlyList<ResourceLink> Items, int Page, int PageSize, int Total);

/// <summary>Personal set projection with only currently openable references.</summary>
public sealed record ShiftSetResponse(Guid Id, string Name, IReadOnlyList<ResourceLink> Links, bool IsDefault);

/// <summary>Current owner-only preferences. Version guards all favourite/set mutations.</summary>
public sealed record ResourcePreferencesResponse(long Version, IReadOnlyList<ResourceLink> Favourites,
    IReadOnlyList<ShiftSetResponse> Sets, Guid? DefaultSetId, bool GuideDismissed = false,
    ResourceWorkspaceLayout? WorkspaceLayout = null, bool AnnouncementGuideDismissed = false);

/// <summary>At most 100 permitted environment values; refine search when HasMore is true.</summary>
public sealed record ResourceEnvironmentOptions(IReadOnlyList<string> Values, bool HasMore);
