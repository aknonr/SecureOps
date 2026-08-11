namespace SecureOps.Shared.Auth;

/// <summary>Stable application capability identifiers.</summary>
public static class Capabilities
{
    /// <summary>Read one exact identity.</summary>
    public const string IdentityLookup = "IdentityLookup";
    /// <summary>Read a bounded set of exact identities.</summary>
    public const string BulkIdentityLookup = "BulkIdentityLookup";
    /// <summary>View approved team metadata.</summary>
    public const string TeamView = "TeamView";
    /// <summary>View operational audit evidence.</summary>
    public const string AuditView = "AuditView";
    /// <summary>Administer application access requests and roles.</summary>
    public const string AccessAdministration = "AccessAdministration";
    /// <summary>Run or view approved system diagnostics.</summary>
    public const string SystemDiagnostics = "SystemDiagnostics";
}
