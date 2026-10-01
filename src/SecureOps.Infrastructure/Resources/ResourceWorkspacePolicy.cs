using SecureOps.Domain.Resources;
using SecureOps.Shared.Auth;

namespace SecureOps.Infrastructure.Resources;

/// <summary>Closed layout vocabulary with capability-filtered shortcut projection.</summary>
public static class ResourceWorkspacePolicy
{
    /// <summary>Rejects arbitrary routes, excessive lists, duplicate keys and invalid presentation values.</summary>
    public static bool Valid(ResourceWorkspaceLayout? layout) => layout is not null
        && layout.View is "cards" or "list" && layout.Density is "comfortable" or "compact"
        && layout.PageSize is 10 or 25 or 50 or 100 && layout.Shortcuts is { Count: <= 4 }
        && layout.Shortcuts.All(key => key is "links" or "groups" or "requests" or "catalogue")
        && layout.Shortcuts.Distinct(StringComparer.Ordinal).Count() == layout.Shortcuts.Count;

    /// <summary>Every route remains subject to its existing independent API capability.</summary>
    public static bool Permitted(string key, IReadOnlyList<string> capabilities) => key switch
    {
        "links" or "groups" => capabilities.Contains(Capabilities.ResourcesView),
        "requests" => capabilities.Contains(Capabilities.OperationalRecordsView),
        "catalogue" => capabilities.Contains(Capabilities.ResourcesManage),
        _ => false
    };

    /// <summary>Legacy/invalid settings use defaults; revoked shortcuts are not returned and reads never write.</summary>
    public static ResourceWorkspaceLayout Project(ResourceWorkspaceLayout? layout, IReadOnlyList<string> capabilities)
    {
        ResourceWorkspaceLayout safe = Valid(layout) ? layout! : ResourceWorkspaceLayout.Default;
        return safe with { Shortcuts = safe.Shortcuts.Where(key => Permitted(key, capabilities)).ToArray() };
    }
}
