using System.Security.Cryptography;
using System.Text.Json;

namespace SecureOps.Domain.OperationalRecords;

/// <summary>Identity-free facts; null attestation means unproven, never permission.</summary>
public sealed record SdmEvaluationInput(
    string SourceFingerprint,
    bool Synthetic = false, bool Contradictory = false, bool ProviderSupported = false,
    bool Active = false, bool? GroupInScope = null, bool? DccAllowed = null,
    bool ValidId = false, bool ValidCode = false, bool ValidTitle = false, bool ValidDescription = false,
    OperationalRecordClassification? Category = null, bool ServerPresent = false, bool IpPresent = false,
    bool RequesterPresent = false, bool RequesterResolved = false, bool RequesterAmbiguous = false,
    bool ReporterResolved = false, bool ApprovalGranted = false, bool WritesDisabled = true,
    bool AlreadyTransferred = false, bool ReconciliationRequired = false,
    bool SourceChanged = false, bool EvaluationStale = false,
    bool PolicyApproved = false, bool MappingComplete = false);

/// <summary>Pure versioned decision, without evaluation time or source content.</summary>
public sealed record SdmEvaluationResult(
    string RuleSetVersion, string InputHash, OperationalRecordClassification RecommendedClassification,
    IReadOnlyList<string> ReasonCodes, IReadOnlyList<string> BlockingConditions,
    bool SourceChanged, bool EvaluationStale)
{
    /// <summary>Only the independently versioned, fully attested pilot can recommend publication.</summary>
    public bool SdmCandidateRecommended => JiraEligible;
    /// <summary>Business eligibility is separate from deployment-owned external-write fences.</summary>
    public bool JiraEligible => RuleSetVersion == SdmPilotEvaluator.RuleSetVersion
        && !SourceChanged && !EvaluationStale && BlockingConditions.All(c => c == "ExternalWritesDisabled");
    /// <summary>Evaluation alone never authorizes an external write.</summary>
    public bool ExternalWriteEligible => JiraEligible && !BlockingConditions.Contains("ExternalWritesDisabled");
}

/// <summary>Durable safe evidence and metadata recorded outside the pure evaluator.</summary>
public sealed record SdmEvaluationSnapshot(
    string SourceFingerprint, SdmEvaluationResult Result, DateTimeOffset EvaluatedAt);

/// <summary>Pure fail-closed SDM v1 decision and canonical framework SHA-256 hashing.</summary>
public static class SdmEvaluator
{
    /// <summary>Immutable policy identifier; semantic changes require a new version.</summary>
    public const string RuleSetVersion = "WASAS-SDM-2026.09-v1";

    /// <summary>Evaluates only explicit facts, without I/O, identities, prose, or time.</summary>
    public static SdmEvaluationResult Evaluate(SdmEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        SortedSet<string> reasons = new(StringComparer.Ordinal);
        SortedSet<string> blockers = new(StringComparer.Ordinal);
        void Add(string code, bool blocking = true)
        {
            reasons.Add(code);
            if (blocking)
            {
                blockers.Add(code);
            }
        }

        void BlockWhen(bool condition, string code)
        {
            if (condition)
            {
                Add(code);
            }
        }

        bool fingerprintValid = IsFingerprint(input.SourceFingerprint);
        bool contradictory = input.Contradictory || !fingerprintValid
            || (input.RequesterResolved && (!input.RequesterPresent || input.RequesterAmbiguous))
            || (input.Category.HasValue && !Enum.IsDefined(input.Category.Value));
        BlockWhen(input.Synthetic, "SyntheticIdentifier");
        BlockWhen(contradictory, "ContradictoryEvidence");
        BlockWhen(input.AlreadyTransferred, "AlreadyTransferred");
        BlockWhen(input.ReconciliationRequired, "ReconciliationRequired");
        BlockWhen(input.SourceChanged, "SourceChanged");
        BlockWhen(!input.ProviderSupported, "UnsupportedProvider");
        BlockWhen(!input.Active, "InactiveSource");
        BlockWhen(input.GroupInScope != true, input.GroupInScope == false ? "GroupOutOfScope" : "GroupUnproven");
        BlockWhen(input.DccAllowed != true, input.DccAllowed == false ? "ExcludedDcc" : "DccUnproven");
        BlockWhen(!input.ValidId, "InvalidSourceId");
        BlockWhen(!input.ValidCode, "InvalidOrCode");
        BlockWhen(!input.ValidTitle, "InvalidTitle");
        BlockWhen(!input.ValidDescription, "InvalidDescription");

        bool recognized = input.Category is >= OperationalRecordClassification.ServerRequest
            and <= OperationalRecordClassification.OperationalSupport;
        Add(recognized ? "CategorySupported" : input.Category == OperationalRecordClassification.NotJiraEligible
            ? "CategoryUnsupported" : "CategoryUnknown", !recognized);
        Add("CategoryPolicyPending");
        Add(input.ServerPresent || input.IpPresent ? "InfrastructureReferencePresent" : "InfrastructureReferenceMissing", false);
        BlockWhen(!input.RequesterPresent, "RequesterMissing");
        BlockWhen(input.RequesterAmbiguous, "RequesterAmbiguous");
        BlockWhen(!input.RequesterResolved, "RequesterUnresolved");
        BlockWhen(!input.ReporterResolved, "ReporterUnresolved");

        Add(input.ApprovalGranted ? "ApprovalGranted" : "ApprovalRequired", !input.ApprovalGranted);
        BlockWhen(input.WritesDisabled, "ExternalWritesDisabled");

        bool stale = input.EvaluationStale || input.SourceChanged;
        Add(stale ? "EvaluationStale" : "EvaluationCurrent", stale);
        bool excluded = input.Synthetic || !input.ProviderSupported || !input.Active
            || input.GroupInScope == false || input.DccAllowed == false
            || input.Category == OperationalRecordClassification.NotJiraEligible;
        bool malformed = !input.ValidId || !input.ValidCode || !input.ValidTitle || !input.ValidDescription;
        return new(RuleSetVersion, Hash(input), excluded && !contradictory && (input.Synthetic || !malformed)
            ? OperationalRecordClassification.NotJiraEligible : OperationalRecordClassification.NeedsManualReview,
            Array.AsReadOnly(reasons.ToArray()), Array.AsReadOnly(blockers.ToArray()), input.SourceChanged, stale);
    }

    /// <summary>Hashes fixed-order UTF-8 JSON facts; see ADR-0018 for the canonicalization contract.</summary>
    public static string Hash(SdmEvaluationInput input)
    {
        // A fixed-position JSON array prevents property ordering or delimiter ambiguity.
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(new object?[]
        {
            RuleSetVersion, IsFingerprint(input.SourceFingerprint) ? input.SourceFingerprint : null,
            input.Synthetic, input.Contradictory || !IsFingerprint(input.SourceFingerprint), input.ProviderSupported,
            input.Active, input.GroupInScope, input.DccAllowed, input.ValidId, input.ValidCode,
            input.ValidTitle, input.ValidDescription, input.Category, input.ServerPresent, input.IpPresent,
            input.RequesterPresent, input.RequesterResolved, input.RequesterAmbiguous, input.ReporterResolved,
            input.ApprovalGranted, input.WritesDisabled, input.AlreadyTransferred, input.ReconciliationRequired,
            input.SourceChanged, input.EvaluationStale
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static bool IsFingerprint(string? value) => value?.Length == 64
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
