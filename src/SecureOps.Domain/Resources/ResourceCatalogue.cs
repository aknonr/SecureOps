namespace SecureOps.Domain.Resources;

/// <summary>A shared category; manager-only visibility is inherited by its links.</summary>
public sealed record ResourceCategory(Guid Id, string Name, int DisplayOrder, bool ManagersOnly,
    bool Archived, long Version, DateTimeOffset UpdatedAt);

/// <summary>A shared HTTPS reference, never fetched by WASAS.</summary>
public sealed record ResourceLink(Guid Id, Guid CategoryId, string Name, string Url, string Purpose,
    string? Notes, string? Environment, string? Location, IReadOnlyList<string> Tags, int DisplayOrder,
    bool Active, bool Archived, long Version, DateTimeOffset UpdatedAt);

/// <summary>A private named ordered list of references, not a snapshot of target details.</summary>
public sealed record ShiftStartSet(Guid Id, string Name, IReadOnlyList<Guid> LinkIds);

/// <summary>Bounded owner-scoped aggregate; its version guards every personal mutation.</summary>
public sealed record ResourcePreferences(long Version, IReadOnlyList<Guid> FavouriteIds,
    IReadOnlyList<ShiftStartSet> Sets, Guid? DefaultSetId, bool GuideDismissed = false,
    ResourceWorkspaceLayout? WorkspaceLayout = null)
{
    /// <summary>Unevaluated personal state, with no database write on read.</summary>
    public static ResourcePreferences Empty => new(0, [], [], null);
}
