using SecureOps.Domain.Access;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Infrastructure.Access;

/// <summary>
/// Projects the registered action catalog and the role definitions into a per-module view. Pure and
/// read-only: it explains existing authority and never decides or grants any.
/// </summary>
public static class AccessModuleView
{
    /// <summary>Module name for persisted capabilities that the action catalog does not describe.</summary>
    public const string UncataloguedModule = "Diğer";

    /// <summary>Every module with its actions and the roles that grant each action.</summary>
    public static AccessModuleOverviewResponse Overview(
        IReadOnlyList<AccessActionDefinition> actions,
        IReadOnlyList<AccessRoleDefinition> roles)
    {
        IReadOnlyList<AccessRoleDefinition> ordered = [.. roles.OrderBy(role => role.Code, StringComparer.OrdinalIgnoreCase)];
        return new AccessModuleOverviewResponse(
            [
                .. Group(actions).Select(module => new AccessModuleResponse(
                    module.Key,
                    [.. module.Select(action => new AccessModuleActionResponse(
                        action.Code, action.Name, action.Description, action.Assignable, GrantingRoles(action.Code, ordered)))]))
            ],
            ordered);
    }

    /// <summary>
    /// Explains one user's effective actions. Capabilities are taken from the persisted user record, which is
    /// the authority; roles only explain them. A user who is not Approved has nothing granted.
    /// </summary>
    public static AccessEffectiveResponse Explain(
        ApplicationUser user,
        IReadOnlyList<AccessActionDefinition> actions,
        IReadOnlyList<AccessRoleDefinition> roles)
    {
        bool approved = user.Status == AccessStatus.Approved;
        HashSet<string> granted = approved ? new(user.Capabilities, StringComparer.Ordinal) : [];
        IReadOnlyList<AccessRoleDefinition> assigned =
        [
            .. roles.Where(role => user.Roles.Contains(role.Code, StringComparer.OrdinalIgnoreCase))
                .OrderBy(role => role.Code, StringComparer.OrdinalIgnoreCase)
        ];

        // A persisted capability without a catalog entry is still shown, so the explanation never hides authority.
        IEnumerable<AccessActionDefinition> uncatalogued = granted
            .Where(code => actions.All(action => action.Code != code))
            .Order(StringComparer.Ordinal)
            .Select(code => new AccessActionDefinition(code, UncataloguedModule, code, "Katalogda açıklaması olmayan kayıtlı yetki.", false));

        AccessEffectiveModuleResponse[] modules =
        [
            .. Group([.. actions, .. uncatalogued]).Select(module => new AccessEffectiveModuleResponse(
                module.Key,
                [.. module.Select(action => new AccessEffectiveActionResponse(
                    action.Code,
                    action.Name,
                    action.Description,
                    granted.Contains(action.Code),
                    granted.Contains(action.Code) ? GrantingRoles(action.Code, assigned) : []))]))
        ];

        return new AccessEffectiveResponse(
            user.Id,
            user.Status.ToString(),
            [.. user.Roles.Order(StringComparer.OrdinalIgnoreCase)],
            modules.Sum(module => module.Actions.LongCount(action => action.Granted)),
            modules);
    }

    // Keeps the catalog's own module and action order, which is the order operators see in the product.
    private static IEnumerable<IGrouping<string, AccessActionDefinition>> Group(IEnumerable<AccessActionDefinition> actions) =>
        actions.GroupBy(action => action.Module, StringComparer.Ordinal);

    private static IReadOnlyList<string> GrantingRoles(string capability, IReadOnlyList<AccessRoleDefinition> roles) =>
        [.. roles.Where(role => role.Capabilities.Contains(capability, StringComparer.Ordinal)).Select(role => role.Code)];
}
