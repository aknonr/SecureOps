namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>
/// Service Accounts capabilities. They gate the kind of action only; every data access is
/// additionally limited by the caller's persisted module scope grants.
/// </summary>
public static class ServiceAccountCapabilities
{
    /// <summary>Read accounts and work inside granted scope.</summary>
    public const string View = "ServiceAccounts.View";
    /// <summary>Create requests/plans, report actions, record mail, findings and evidence.</summary>
    public const string Work = "ServiceAccounts.Work";
    /// <summary>Ownership assignment and handover acceptance decisions.</summary>
    public const string Assign = "ServiceAccounts.Assign";
    /// <summary>Verify reported actions and closures.</summary>
    public const string Verify = "ServiceAccounts.Verify";
    /// <summary>Stage, preview, decide and commit imports.</summary>
    public const string Import = "ServiceAccounts.Import";
    /// <summary>Reports, snapshots and exports.</summary>
    public const string Report = "ServiceAccounts.Report";
    /// <summary>Scope grants and module dictionaries; cannot change business results.</summary>
    public const string Administer = "ServiceAccounts.Administer";

    /// <summary>All module capabilities.</summary>
    public static IReadOnlyList<string> All { get; } = [View, Work, Assign, Verify, Import, Report, Administer];
}

/// <summary>Module authorization policy names.</summary>
public static class ServiceAccountPolicies
{
    /// <summary>View policy.</summary>
    public const string View = "CanViewServiceAccounts";
    /// <summary>Work policy.</summary>
    public const string Work = "CanWorkServiceAccounts";
    /// <summary>Assign policy.</summary>
    public const string Assign = "CanAssignServiceAccounts";
    /// <summary>Verify policy.</summary>
    public const string Verify = "CanVerifyServiceAccounts";
    /// <summary>Import policy.</summary>
    public const string Import = "CanImportServiceAccounts";
    /// <summary>Report policy.</summary>
    public const string Report = "CanReportServiceAccounts";
    /// <summary>Administration policy.</summary>
    public const string Administer = "CanAdministerServiceAccounts";

    /// <summary>Policy-to-capability pairs registered by the module.</summary>
    public static IReadOnlyList<(string Policy, string Capability)> Map { get; } =
    [
        (View, ServiceAccountCapabilities.View),
        (Work, ServiceAccountCapabilities.Work),
        (Assign, ServiceAccountCapabilities.Assign),
        (Verify, ServiceAccountCapabilities.Verify),
        (Import, ServiceAccountCapabilities.Import),
        (Report, ServiceAccountCapabilities.Report),
        (Administer, ServiceAccountCapabilities.Administer)
    ];
}
