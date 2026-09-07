using SecureOps.Shared.Auth;

namespace SecureOps.Ui.Services;

/// <summary>Closed presentation vocabulary for capability-checked personal workspace shortcuts.</summary>
public static class ResourceWorkspaceView
{
    /// <summary>Supported shortcut keys, never arbitrary URLs.</summary>
    public static IReadOnlyList<string> Keys => ["links", "groups", "requests", "catalogue"];
    /// <summary>Checks the current API-authoritative access projection.</summary>
    public static bool Permitted(AccessSnapshot? access, string key) => access?.Can(key switch
    {
        "links" or "groups" => Capabilities.ResourcesView,
        "requests" => Capabilities.OperationalRecordsView,
        "catalogue" => Capabilities.ResourcesManage,
        _ => string.Empty
    }) == true;
    /// <summary>Approved Turkish shortcut labels.</summary>
    public static string Label(string key) => key switch
    {
        "links" => "Uygulama Bağlantıları",
        "groups" => "Bağlantı Gruplarım",
        "requests" => "Operasyonel Kayıtlar",
        "catalogue" => "Bağlantı Yönetimi",
        _ => string.Empty
    };
    /// <summary>Fixed local navigation targets; unknown keys have no destination.</summary>
    public static string? Href(string key) => key switch
    {
        "links" => "resources",
        "groups" => "resources/sets",
        "requests" => "operational-records",
        "catalogue" => "admin/resources",
        _ => null
    };
}
