namespace SecureOps.Domain.OperationalRecords;

/// <summary>Classification assigned to an imported operational record. Numeric values are frozen by the v1 wire contract.</summary>
public enum OperationalRecordClassification
{
    /// <summary>A server-related request.</summary>
    ServerRequest = 0,
    /// <summary>An environment-related request.</summary>
    EnvironmentRequest = 1,
    /// <summary>A software installation request.</summary>
    SoftwareInstallation = 2,
    /// <summary>A configuration request.</summary>
    ConfigurationRequest = 3,
    /// <summary>An operational support request.</summary>
    OperationalSupport = 4,
    /// <summary>A record explicitly excluded from Jira.</summary>
    NotJiraEligible = 5,
    /// <summary>No approved deterministic rule classified the record.</summary>
    NeedsManualReview = 6,
    /// <summary>Retirement request tracking only; never infrastructure decommissioning.</summary>
    ServerRetirement = 7
}
