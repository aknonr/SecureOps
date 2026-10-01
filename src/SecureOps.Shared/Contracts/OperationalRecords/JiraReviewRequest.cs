using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Operator declaration for a review-only draft; never source attestation or eligibility.</summary>
public sealed record JiraReviewRequest(OperationalRecordClassification RequestType, long ExpectedVersion);
