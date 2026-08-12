namespace SecureOps.Domain.OperationalRecords;

/// <summary>Classification assigned to an imported operational record.</summary>
public enum OperationalRecordClassification
{
    /// <summary>A server-related request.</summary>
    ServerRequest,
    /// <summary>An environment-related request.</summary>
    EnvironmentRequest,
    /// <summary>A software installation request.</summary>
    SoftwareInstallation,
    /// <summary>A configuration request.</summary>
    ConfigurationRequest,
    /// <summary>An operational support request.</summary>
    OperationalSupport,
    /// <summary>A record explicitly excluded from Jira.</summary>
    NotJiraEligible,
    /// <summary>No approved deterministic rule classified the record.</summary>
    NeedsManualReview
}
