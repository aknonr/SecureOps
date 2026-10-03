namespace SecureOps.Shared.Contracts.Access;

/// <summary>One registered action inside a module and the business roles that currently grant it.</summary>
public sealed record AccessModuleActionResponse(
    string Code,
    string Name,
    string Description,
    bool Assignable,
    IReadOnlyList<string> GrantedByRoles);

/// <summary>One product module with its registered actions, in catalog order.</summary>
public sealed record AccessModuleResponse(string Module, IReadOnlyList<AccessModuleActionResponse> Actions);

/// <summary>Read-only module view of the role catalog; it grants nothing by itself.</summary>
public sealed record AccessModuleOverviewResponse(
    IReadOnlyList<AccessModuleResponse> Modules,
    IReadOnlyList<AccessRoleDefinition> Roles);

/// <summary>Whether one user can perform one action, and through which assigned roles.</summary>
public sealed record AccessEffectiveActionResponse(
    string Code,
    string Name,
    string Description,
    bool Granted,
    IReadOnlyList<string> ViaRoles);

/// <summary>Effective actions for one module.</summary>
public sealed record AccessEffectiveModuleResponse(string Module, IReadOnlyList<AccessEffectiveActionResponse> Actions);

/// <summary>
/// Server-explained effective access for one user. Only an Approved user has any granted action; any other
/// status reports every action as not granted (fail closed), with <see cref="Status"/> saying why.
/// </summary>
public sealed record AccessEffectiveResponse(
    Guid UserId,
    string Status,
    IReadOnlyList<string> Roles,
    long GrantedCount,
    IReadOnlyList<AccessEffectiveModuleResponse> Modules);
