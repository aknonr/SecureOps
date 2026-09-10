using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Maps validated source facts and durable workflow context without resolving external identities.</summary>
public static class SdmEvaluationEvidence
{
    /// <summary>Stable audit action for an atomically recorded evaluation.</summary>
    public const string AuditAction = "OperationalRecordSdmEvaluated";

    /// <summary>Reduces source content to validity/presence facts and the existing freshness digest.</summary>
    public static SdmEvaluationInput FromSource(OperationalRecordSourceItem source, bool providerSupported, bool writesDisabled) => new(
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperationalRecordSourceConcurrency.Create(source)))).ToLowerInvariant(),
        Synthetic: IsSynthetic(source.SourceRecordId) || IsSynthetic(source.OrCode),
        ProviderSupported: providerSupported, Active: source.IsOpen,
        ValidId: source.SourceRecordId.All(char.IsAsciiDigit)
            && long.TryParse(source.SourceRecordId, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0,
        ValidCode: ValidText(source.OrCode, 64) && source.OrCode.StartsWith("OR-", StringComparison.Ordinal)
            && source.OrCode.Length > 3 && source.OrCode.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0,
        ValidTitle: ValidText(source.Title, 500), ValidDescription: ValidText(source.Description, 8000),
        ServerPresent: ValidText(source.ServerReference, 255), RequesterPresent: ValidText(source.Requester, 256),
        WritesDisabled: writesDisabled);

    /// <summary>Applies locked workflow context and latches stale source evidence.</summary>
    public static OperationalRecord Apply(OperationalRecord current, SdmEvaluationInput input, DateTimeOffset now)
    {
        if (input.SourceFingerprint?.Length != 64 || !input.SourceFingerprint.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new InvalidOperationException("Invalid SDM source fingerprint.");
        }
        bool changed = current.SdmEvaluation?.Result.SourceChanged == true
            || (current.SdmEvaluation is not null && current.SdmEvaluation.SourceFingerprint != input.SourceFingerprint)
            || input.SourceFingerprint != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(current.SourceConcurrencyToken))).ToLowerInvariant();
        if (input.PolicyApproved && input.ApprovalGranted)
        { changed = input.SourceFingerprint != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(current.SourceConcurrencyToken))).ToLowerInvariant(); }
        SdmEvaluationInput checkedInput = input with
        {
            AlreadyTransferred = !string.IsNullOrWhiteSpace(current.JiraIssueKey)
                || current.WorkflowState is OperationalRecordWorkflowState.JiraCreated
                    or OperationalRecordWorkflowState.ClosingOperationalRecord
                    or OperationalRecordWorkflowState.OperationalRecordCloseFailed or OperationalRecordWorkflowState.Completed,
            ReconciliationRequired = current.ReconciliationRequired
                || current.WorkflowState == OperationalRecordWorkflowState.CreatingJira,
            SourceChanged = changed,
            EvaluationStale = changed || (!input.PolicyApproved && current.SdmEvaluation?.Result.EvaluationStale == true)
        };
        SdmEvaluationResult result = input.PolicyApproved ? SdmPilotEvaluator.Evaluate(checkedInput) : SdmEvaluator.Evaluate(checkedInput);
        if (current.SdmEvaluation?.Result.InputHash == result.InputHash)
        {
            return current;
        }

        bool initial = !current.ReconciliationRequired && string.IsNullOrWhiteSpace(current.JiraIssueKey)
            && current.WorkflowState is OperationalRecordWorkflowState.Imported or OperationalRecordWorkflowState.Classified
                or OperationalRecordWorkflowState.NeedsManualReview or OperationalRecordWorkflowState.Eligible;
        return current with
        {
            SdmEvaluation = new(input.SourceFingerprint, result, now),
            JiraEligible = result.JiraEligible,
            Classification = initial ? result.RecommendedClassification : current.Classification,
            EligibilityReason = initial ? result.JiraEligible ? "Approved single-record policy; exact preview and write gates still required." : "SDM evaluation requires review; publication is blocked." : current.EligibilityReason,
            WorkflowState = initial ? result.JiraEligible ? OperationalRecordWorkflowState.Eligible : OperationalRecordWorkflowState.NeedsManualReview : current.WorkflowState,
            UpdatedAt = now,
            Version = current.Version + 1
        };
    }

    /// <summary>Contains only safe result evidence and internal record identity.</summary>
    public static AuditEvent Audit(OperationalRecord record, OperationalRecordCommandContext context) => new()
    {
        Actor = record.SdmEvaluation?.Result.RuleSetVersion == SdmPilotEvaluator.RuleSetVersion ? context.Actor : "system:sdm-evaluator",
        Action = AuditAction,
        CorrelationId = context.CorrelationId,
        Details = new { operationalRecordId = record.Id, record.Classification, record.JiraEligible, evaluation = record.SdmEvaluation }
    };

    /// <summary>Bounded persistence serialization; contains no source payload or canonical input.</summary>
    public static string Serialize(SdmEvaluationSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(snapshot);
        return json.Length <= 4000 ? json : throw new InvalidOperationException("SDM evidence exceeds its storage contract.");
    }

    /// <summary>Marks prior evidence stale if source refresh outran evaluation persistence; retains the prior hash/time.</summary>
    public static OperationalRecord Project(OperationalRecord record)
    {
        SdmEvaluationSnapshot? snapshot = record.SdmEvaluation;
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.SourceConcurrencyToken))).ToLowerInvariant();
        if (snapshot is null || snapshot.SourceFingerprint == fingerprint)
        {
            return record;
        }
        string[] reasons = snapshot.Result.ReasonCodes.Where(code => code != "EvaluationCurrent")
            .Concat(["EvaluationStale", "SourceChanged"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] blockers = snapshot.Result.BlockingConditions.Concat(["EvaluationStale", "SourceChanged"])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return record with
        {
            JiraEligible = false,
            SdmEvaluation = snapshot with
            {
                Result = snapshot.Result with { EvaluationStale = true, SourceChanged = true, ReasonCodes = reasons, BlockingConditions = blockers }
            }
        };
    }

    private static bool ValidText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && !value.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t');

    /// <summary>Known synthetic source and code namespaces; never corporate classification evidence.</summary>
    public static bool IsSynthetic(string value) => new[] { "synthetic-", "SYN-", "SIM-", "simulation-", "FAKE-" }
        .Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
