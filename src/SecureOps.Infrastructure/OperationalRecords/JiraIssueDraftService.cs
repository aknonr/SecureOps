using System.Text;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Builds safe Jira drafts from reviewed configuration and exact requester resolution.</summary>
public sealed class JiraIssueDraftService : IJiraIssueDraftService
{
    private readonly IRequesterResolver _requesterResolver;
    private readonly JiraIntegrationOptions _options;

    /// <summary>Initializes the draft service.</summary>
    public JiraIssueDraftService(IRequesterResolver requesterResolver, IOptions<JiraIntegrationOptions> options)
    {
        _requesterResolver = requesterResolver;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<OperationalRecordResult<JiraIssueDraft>> BuildAsync(
        OperationalRecord record,
        string actor,
        CancellationToken cancellationToken)
    {
        if (!record.JiraEligible || record.WorkflowState is OperationalRecordWorkflowState.NeedsManualReview or OperationalRecordWorkflowState.Imported or OperationalRecordWorkflowState.Classified)
        {
            return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.OperationalRecordInvalidState, "classification", false);
        }

        List<string> warnings = [];
        string? requesterAccountId = null;
        if (!string.IsNullOrWhiteSpace(record.Requester))
        {
            RequesterResolutionResult resolution = await _requesterResolver.ResolveExactAsync(record.Requester, cancellationToken);
            if (resolution.Status == RequesterResolutionStatus.Ambiguous)
            {
                return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.RequesterResolutionAmbiguous, "requester-resolution", false);
            }

            if (resolution.Status == RequesterResolutionStatus.Found)
            {
                requesterAccountId = resolution.JiraAccountId;
            }
            else if (string.Equals(_options.UnresolvedRequesterPolicy, "Block", StringComparison.OrdinalIgnoreCase))
            {
                return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.RequesterResolutionFailed, "requester-resolution", resolution.Status == RequesterResolutionStatus.Failed);
            }
            else
            {
                warnings.Add("Requester was not assigned because no unique exact Jira account was resolved.");
            }
        }

        string summary = $"{record.OrCode}{_options.SummarySeparator}{record.Title}";
        if (summary.Length > _options.SummaryMaxLength)
        {
            summary = summary[.._options.SummaryMaxLength];
        }

        StringBuilder description = new(record.Description);
        AppendReference(description, "Environment", record.Environment);
        AppendReference(description, "Server", record.ServerReference);
        AppendReference(description, "Application", record.ApplicationReference);

        string? assigneeUsername = ResolveAssignee(actor, warnings);
        string idempotencyMapping = assigneeUsername is null
            ? _options.MappingVersion
            : $"{_options.MappingVersion}\nassignee:{assigneeUsername}";
        string idempotencyKey = OperationalRecordIdempotency.Create(record.SourceRecordId, idempotencyMapping);
        return OperationalRecordResult<JiraIssueDraft>.Success(new JiraIssueDraft(
            record.Id,
            record.OrCode,
            _options.ProjectKey,
            _options.IssueType,
            summary,
            description.ToString(),
            requesterAccountId,
            _options.MappingVersion,
            idempotencyKey,
            warnings,
            assigneeUsername));
    }

    private string? ResolveAssignee(string actor, ICollection<string> warnings)
    {
        if (!string.Equals(_options.AssignmentMode, "VerifiedOperatorMapping", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        JiraOperatorAssigneeMappingOptions? mapping = _options.OperatorAssigneeMappings.SingleOrDefault(candidate =>
            string.Equals(candidate.SecureOpsActor, actor, StringComparison.OrdinalIgnoreCase));
        if (mapping is null)
        {
            warnings.Add("Assignee uses the Jira project default because no verified exact operator mapping exists.");
            return null;
        }

        return mapping.JiraUsername;
    }

    private static void AppendReference(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine().Append(label).Append(": ").Append(value);
        }
    }
}
