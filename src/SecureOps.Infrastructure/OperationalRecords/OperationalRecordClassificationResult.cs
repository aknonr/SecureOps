using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Deterministic classification and eligibility outcome.</summary>
public sealed record OperationalRecordClassificationResult(
    OperationalRecordClassification Classification,
    bool JiraEligible,
    string EligibilityReason);
