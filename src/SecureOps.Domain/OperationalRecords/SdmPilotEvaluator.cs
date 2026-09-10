using System.Security.Cryptography;
using System.Text.Json;

namespace SecureOps.Domain.OperationalRecords;

/// <summary>Evaluable single-record policy proposal. Trusted adapters must establish every attestation.</summary>
public static class SdmPilotEvaluator
{
    /// <summary>Independent immutable rule version; v1 evidence remains fail-closed.</summary>
    public const string RuleSetVersion = "WASAS-SDM-PILOT-2026.09-v1";
    /// <summary>Positive only with explicit policy, supported mapping and all existing safety facts.</summary>
    public static SdmEvaluationResult Evaluate(SdmEvaluationInput input)
    {
        SdmEvaluationResult baseline = SdmEvaluator.Evaluate(input);
        var blockers = new SortedSet<string>(baseline.BlockingConditions, StringComparer.Ordinal);
        var reasons = new SortedSet<string>(baseline.ReasonCodes, StringComparer.Ordinal);
        if (input.PolicyApproved)
        { blockers.Remove("CategoryPolicyPending"); reasons.Remove("CategoryPolicyPending"); reasons.Add("SingleRecordPolicyApproved"); }
        if (!input.MappingComplete || input.Category != OperationalRecordClassification.ServerRequest)
        { blockers.Add("TypeMappingPending"); reasons.Add("TypeMappingPending"); }
        bool eligible = blockers.All(c => c == "ExternalWritesDisabled");
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new object[] { RuleSetVersion, input }))).ToLowerInvariant();
        return new(RuleSetVersion, hash, eligible ? OperationalRecordClassification.ServerRequest : baseline.RecommendedClassification,
            reasons.ToArray(), blockers.ToArray(), baseline.SourceChanged, baseline.EvaluationStale);
    }
}
