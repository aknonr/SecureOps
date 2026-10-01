using System.Security.Cryptography;
using System.Text;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Checks deployment-owned one-record approval without inferring category, users or inventory.</summary>
public static class SdmPilotPolicy
{
    /// <summary>Safe digest for the source owner's exact-version approval record.</summary>
    public static string Fingerprint(OperationalRecord record) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.SourceConcurrencyToken))).ToLowerInvariant();

    /// <summary>Evaluates explicit policy and current reviewed mapping; missing configuration stays blocked.</summary>
    public static IReadOnlyList<string> Blockers(OperationalRecord record, OperationalRecordsOptions options,
        JiraIntegrationOptions jira, TuruncuHatOptions source, DateTimeOffset now)
    {
        SdmPilotOptions policy = options.Pilot;
        var reasons = new List<string>();
        if (policy.RuleSetVersion != SdmPilotEvaluator.RuleSetVersion || string.IsNullOrWhiteSpace(policy.ApprovalReference)
            || policy.ApprovalReference.Length > 128)
        {
            reasons.Add("CategoryPolicyPending");
        }

        if (policy.ExpiresAt is null || policy.ExpiresAt <= now)
        {
            reasons.Add("PilotPolicyExpired");
        }

        if (string.IsNullOrWhiteSpace(policy.TrackingReason) || policy.TrackingReason.Length > 500)
        {
            reasons.Add("TrackingReasonMissing");
        }

        if (policy.SourceRecordId != record.SourceRecordId || policy.SourceFingerprint != Fingerprint(record))
        {
            reasons.Add("PilotRecordMismatch");
        }

        if (source.SourceBaseObject != "SMSS_oRFF" || !source.ExcludedDccIds.Contains(4241)
            || policy.SourceScope != $"{source.SourceBaseObject}:{source.RelatedGroupId}:{string.Join(',', source.ExcludedDccIds.Order())}")
        {
            reasons.Add("PilotScopeUnproven");
        }

        if (policy.RequestType != OperationalRecordClassification.ServerRequest)
        {
            reasons.Add("TypeMappingPending");
        }

        if (options.SourceCloseEnabled)
        {
            reasons.Add("PilotMustRemainSourceOpen");
        }

        if (string.IsNullOrWhiteSpace(jira.MappingVersion) || policy.MappingVersion != jira.MappingVersion
            || string.IsNullOrWhiteSpace(jira.ProjectKey) || string.IsNullOrWhiteSpace(jira.IssueTypeId)
            || string.IsNullOrWhiteSpace(jira.TeamCustomField) || string.IsNullOrWhiteSpace(jira.TeamValue)
            || string.IsNullOrWhiteSpace(jira.RequesterWatcherCustomField) || jira.Labels.Length == 0
            || jira.ReporterMode != "AuthenticatedOperator" || jira.UnresolvedRequesterPolicy != "Block")
        {
            reasons.Add("JiraMappingPending");
        }

        if (!long.TryParse(record.SourceRecordId, out long id) || id <= 0 || !record.SourceRecordId.All(char.IsAsciiDigit)
            || !record.OrCode.StartsWith("OR-", StringComparison.Ordinal) || record.OrCode.Length <= 3
            || !record.OrCode.AsSpan(3).ToArray().All(char.IsAsciiDigit)
            || string.IsNullOrWhiteSpace(record.Title) || string.IsNullOrWhiteSpace(record.Description))
        {
            reasons.Add("InvalidSourceId");
        }

        if (string.IsNullOrWhiteSpace(record.Requester))
        {
            reasons.Add("RequesterMissing");
        }

        if (record.JiraIssueKey is not null || record.ReconciliationRequired
            || record.WorkflowState == OperationalRecordWorkflowState.CreatingJira)
        {
            reasons.Add("ReconciliationRequired");
        }

        return reasons;
    }

    /// <summary>Called only after exact source revalidation and successful exact identity/mapping resolution.</summary>
    public static SdmEvaluationInput ConfirmedInput(OperationalRecord record, JiraIssueDraft draft, OperationalRecordsOptions options) =>
        new(Fingerprint(record), ProviderSupported: true, Active: true, GroupInScope: true, DccAllowed: true,
            ValidId: true, ValidCode: true, ValidTitle: true, ValidDescription: true, Category: draft.RequestType,
            ServerPresent: !string.IsNullOrWhiteSpace(record.ServerReference), RequesterPresent: !string.IsNullOrWhiteSpace(record.Requester),
            RequesterResolved: !string.IsNullOrWhiteSpace(draft.RequesterAccountId), ReporterResolved: !string.IsNullOrWhiteSpace(draft.ReporterUsername),
            ApprovalGranted: true, WritesDisabled: options.ReadOnlyIntegrationMode || !options.ControlledTestWritesEnabled,
            PolicyApproved: true, MappingComplete: true);
}
