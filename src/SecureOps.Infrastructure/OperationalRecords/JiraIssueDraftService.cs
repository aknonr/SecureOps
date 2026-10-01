using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Builds safe Jira drafts from reviewed configuration and exact Jira user resolution.</summary>
public sealed class JiraIssueDraftService : IJiraIssueDraftService
{
    private readonly IJiraUserResolver _jiraUserResolver;
    private readonly IIdentityAccountNormalizer _identityNormalizer;
    private readonly JiraIntegrationOptions _options;
    private readonly OperationalRecordsOptions _operationalOptions;
    private readonly TuruncuHatOptions _sourceOptions;

    /// <summary>Initializes the draft service.</summary>
    public JiraIssueDraftService(
        IJiraUserResolver jiraUserResolver,
        IIdentityAccountNormalizer identityNormalizer,
        IOptions<JiraIntegrationOptions> options,
        IOptions<OperationalRecordsOptions>? operationalOptions = null,
        IOptions<TuruncuHatOptions>? sourceOptions = null)
    {
        _jiraUserResolver = jiraUserResolver;
        _identityNormalizer = identityNormalizer;
        _options = options.Value;
        _operationalOptions = operationalOptions?.Value ?? new();
        _sourceOptions = sourceOptions?.Value ?? new();
    }

    /// <inheritdoc />
    public async Task<OperationalRecordResult<JiraIssueDraft>> BuildAsync(
        OperationalRecord record,
        string actor,
        CancellationToken cancellationToken)
    {
        if (string.Equals(_operationalOptions.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase))
        {
            if (SdmPilotPolicy.Blockers(record, _operationalOptions, _options, _sourceOptions, DateTimeOffset.UtcNow).Count > 0)
            { return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.JiraValidationFailed, "pilot-policy", false); }
            record = record with
            {
                Classification = _operationalOptions.Pilot.RequestType!.Value,
                JiraEligible = true,
                WorkflowState = record.WorkflowState == OperationalRecordWorkflowState.NeedsManualReview ? OperationalRecordWorkflowState.Eligible : record.WorkflowState
            };
        }
        if (record.Classification is OperationalRecordClassification.SoftwareInstallation or OperationalRecordClassification.ServerRetirement)
        {
            return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.JiraValidationFailed, "application-mapping", false);
        }

        if (!record.JiraEligible || record.WorkflowState is OperationalRecordWorkflowState.NeedsManualReview or OperationalRecordWorkflowState.Imported or OperationalRecordWorkflowState.Classified)
        {
            return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.OperationalRecordInvalidState, "classification", false);
        }

        List<string> warnings = [];
        string? requesterAccountId = null;
        if (!string.IsNullOrWhiteSpace(record.Requester))
        {
            RequesterResolutionResult resolution = await _jiraUserResolver.ResolveExactAsync(record.Requester, cancellationToken);
            if (resolution.Status == RequesterResolutionStatus.Ambiguous)
            {
                return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.RequesterResolutionAmbiguous, "requester-resolution", false);
            }

            if (resolution.Status == RequesterResolutionStatus.Found && !string.IsNullOrWhiteSpace(resolution.JiraAccountId))
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

        if (string.IsNullOrWhiteSpace(record.Requester))
        {
            if (string.Equals(_options.UnresolvedRequesterPolicy, "Block", StringComparison.OrdinalIgnoreCase))
            {
                return OperationalRecordResult<JiraIssueDraft>.Fail(OperationalErrorCodes.RequesterResolutionFailed, "requester-resolution", false);
            }

            warnings.Add("Requester was not assigned because no unique exact Jira account was resolved.");
        }

        string? reporterUsername = null;
        if (string.Equals(_options.ReporterMode, "AuthenticatedOperator", StringComparison.OrdinalIgnoreCase))
        {
            IdentityAccountNormalizationResult normalized = _identityNormalizer.Normalize(actor);
            if (!normalized.IsValid)
            {
                return OperationalRecordResult<JiraIssueDraft>.Fail(
                    OperationalErrorCodes.OperatorReporterResolutionFailed,
                    "operator-reporter-resolution",
                    false);
            }

            RequesterResolutionResult resolution = await _jiraUserResolver.ResolveExactAsync(
                normalized.NormalizedAccount!,
                cancellationToken);
            if (resolution.Status != RequesterResolutionStatus.Found
                || string.IsNullOrWhiteSpace(resolution.JiraAccountId))
            {
                return OperationalRecordResult<JiraIssueDraft>.Fail(
                    OperationalErrorCodes.OperatorReporterResolutionFailed,
                    "operator-reporter-resolution",
                    resolution.Status == RequesterResolutionStatus.Failed);
            }

            reporterUsername = resolution.JiraAccountId;
        }

        (string summary, string description) = Content(record);

        string? assigneeUsername = ResolveAssignee(actor, warnings);
        JiraIssueFieldMapping fieldMapping = new(
            _options.IssueTypeId,
            _options.TeamCustomField,
            _options.TeamValue,
            _options.RequesterWatcherCustomField,
            Array.AsReadOnly((string[])_options.Labels.Clone()));
        string idempotencyMapping = JsonSerializer.Serialize(new
        {
            FingerprintVersion = "reviewed-draft-v3",
            Pilot = string.Equals(_operationalOptions.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase) ? _operationalOptions.Pilot : null,
            SourceCloseRequested = _operationalOptions.SourceCloseEnabled && !_operationalOptions.ReadOnlyIntegrationMode,
            record.SourceConcurrencyToken,
            Summary = summary,
            Description = description.ToString(),
            _options.MappingVersion,
            _options.ProjectKey,
            _options.IssueType,
            _options.SummarySeparator,
            _options.SummaryMaxLength,
            FieldMapping = fieldMapping,
            RequesterAccountId = requesterAccountId,
            AssigneeUsername = assigneeUsername,
            ReporterUsername = reporterUsername
        });
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
            fieldMapping,
            assigneeUsername,
            reporterUsername,
            _operationalOptions.SourceCloseEnabled && !_operationalOptions.ReadOnlyIntegrationMode)
        { RequestType = record.Classification, RecordVersion = record.Version });
    }

    /// <inheritdoc />
    public JiraIssueDraft BuildReview(OperationalRecord record, OperationalRecordClassification requestType)
    {
        if (requestType is not (OperationalRecordClassification.ServerRequest or OperationalRecordClassification.SoftwareInstallation or OperationalRecordClassification.ServerRetirement))
        {
            throw new ArgumentOutOfRangeException(nameof(requestType));
        }

        SortedSet<string> blockers = new(record.SdmEvaluation?.Result.BlockingConditions ?? [], StringComparer.Ordinal)
        { "CategoryPolicyPending", "OperatorDeclarationOnly", "RequesterUnresolved", "ReporterUnresolved" };
        bool installation = requestType == OperationalRecordClassification.SoftwareInstallation;
        bool retirement = requestType == OperationalRecordClassification.ServerRetirement;
        if (retirement)
        {
            blockers.Add("RetirementMappingPending");
        }

        if (string.Equals(_operationalOptions.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase))
        {
            blockers.UnionWith(SdmPilotPolicy.Blockers(record, _operationalOptions, _options, _sourceOptions, DateTimeOffset.UtcNow));
        }

        if (string.IsNullOrWhiteSpace(_options.IssueTypeId) || string.IsNullOrWhiteSpace(_options.TeamCustomField)
            || string.IsNullOrWhiteSpace(_options.TeamValue) || string.IsNullOrWhiteSpace(_options.RequesterWatcherCustomField)
            || (!installation && _options.Labels.Length == 0))
        {
            blockers.Add("JiraMappingPending");
        }
        if (installation)
        {
            blockers.Add("ApplicationMappingPending");
        }
        (string summary, string description) = Content(record);
        bool close = _operationalOptions.SourceCloseEnabled && !_operationalOptions.ReadOnlyIntegrationMode;
        JiraIssueFieldMapping mapping = new(_options.IssueTypeId, _options.TeamCustomField, _options.TeamValue,
            _options.RequesterWatcherCustomField,
            Array.AsReadOnly(installation || retirement ? Array.Empty<string>() : (string[])_options.Labels.Clone()));
        string fingerprint = OperationalRecordIdempotency.Create(record.SourceRecordId,
            JsonSerializer.Serialize(new { Kind = "review-only-v1", record.SourceConcurrencyToken, record.Version, requestType, summary, description, mapping, close }));
        return new(record.Id, record.OrCode, _options.ProjectKey, _options.IssueType, summary, description,
            null, _options.MappingVersion, fingerprint, [], mapping, SourceCloseRequested: close)
        { RequestType = requestType, ReviewOnly = true, BlockingConditions = blockers.ToArray(), RecordVersion = record.Version };
    }

    private (string Summary, string Description) Content(OperationalRecord record)
    {
        string summary = $"{record.OrCode}{_options.SummarySeparator}{record.Title}";
        if (summary.Length > _options.SummaryMaxLength)
        {
            summary = summary[.._options.SummaryMaxLength];
        }
        StringBuilder description = new(record.Description);
        if (string.Equals(_operationalOptions.SourceProvider, "TuruncuHat", StringComparison.OrdinalIgnoreCase)
            && SdmPilotPolicy.Blockers(record, _operationalOptions, _options, _sourceOptions, DateTimeOffset.UtcNow).Count == 0)
        { AppendReference(description, "SDM tracking reason (approved policy)", _operationalOptions.Pilot.TrackingReason); }
        AppendReference(description, "Environment", record.Environment);
        AppendReference(description, "Server", record.ServerReference);
        AppendReference(description, "Application", record.ApplicationReference);
        return (summary, description.ToString());
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
