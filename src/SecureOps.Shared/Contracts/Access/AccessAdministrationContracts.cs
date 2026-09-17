namespace SecureOps.Shared.Contracts.Access;

/// <summary>Bounded, stable server-side administrative query.</summary>
public sealed record AccessPageQuery(string? Search = null, string? Status = null, string? Role = null, int Page = 1, int PageSize = 25);

/// <summary>Count and rows from the same database snapshot.</summary>
public sealed record AccessPage<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

/// <summary>Registered implemented action, not a client-defined permission.</summary>
public sealed record AccessActionDefinition(string Code, string Module, string Name, string Description, bool Assignable = true);

/// <summary>Persisted versioned business role.</summary>
public sealed record AccessRoleDefinition(string Code, string Name, string Purpose, long Version, bool Protected, IReadOnlyList<string> Capabilities);

/// <summary>Proposed role definition; zero version creates a new business role.</summary>
public sealed record AccessRoleChange(string Code, string Name, string Purpose, long ExpectedVersion, IReadOnlyList<string> Capabilities, string? PreviewToken = null);

/// <summary>Reviewed effective access impact bound to definitions and current assignments.</summary>
public sealed record AccessRoleImpact(AccessRoleDefinition Proposed, long AffectedUsers, long UsersGaining, long UsersLosing, IReadOnlyList<string> AddedCapabilities, IReadOnlyList<string> RemovedCapabilities, string PreviewToken);
