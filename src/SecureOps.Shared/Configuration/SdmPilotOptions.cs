using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Shared.Configuration;

/// <summary>Server-owned approval evidence for one bounded Jira-only candidate; no secrets.</summary>
public sealed class SdmPilotOptions
{
    /// <summary>Exact approved policy version; empty blocks publication.</summary>
    public string RuleSetVersion { get; set; } = "";
    /// <summary>Business decision reference; not supplied by browser text.</summary>
    public string ApprovalReference { get; set; } = "";
    /// <summary>Approved reason for SDM tracking, separate from the source's requested work.</summary>
    public string TrackingReason { get; set; } = "";
    /// <summary>One numeric source identity, never a wildcard or list.</summary>
    public string SourceRecordId { get; set; } = "";
    /// <summary>Lowercase SHA256 of the exact source concurrency token.</summary>
    public string SourceFingerprint { get; set; } = "";
    /// <summary>Approved base-object:group:sorted-exclusions tuple.</summary>
    public string SourceScope { get; set; } = "";
    /// <summary>Approved existing Jira mapping version.</summary>
    public string MappingVersion { get; set; } = "";
    /// <summary>Policy expiry, not a business SLA.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>Approved request classification; null is not approved.</summary>
    public OperationalRecordClassification? RequestType { get; set; }
}
