namespace SecureOps.Shared.Auth;

/// <summary>
/// Authorization policy names used by the API and UI.
/// </summary>
public static class Policies
{
    /// <summary>
    /// Operator, TeamLead, Admin, or Auditor where explicitly allowed by endpoint logic.
    /// </summary>
    public const string OperatorOrAbove = "OperatorOrAbove";

    /// <summary>
    /// TeamLead or Admin.
    /// </summary>
    public const string TeamLeadOrAbove = "TeamLeadOrAbove";

    /// <summary>
    /// Admin only.
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    /// Auditor only.
    /// </summary>
    public const string AuditorOnly = "AuditorOnly";

    /// <summary>
    /// Auditor or Admin audit-query access.
    /// </summary>
    public const string CanViewAudit = "CanViewAudit";

    /// <summary>
    /// TeamLead or Admin manual diagnostic access.
    /// </summary>
    public const string CanTriggerDiagnostic = "CanTriggerDiagnostic";
}
