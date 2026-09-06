using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Api.Controllers;

/// <summary>Authorized operational-record query and Jira workflow endpoints.</summary>
[ApiController]
[Route("api/v1/operational-records")]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public sealed class OperationalRecordsController : ControllerBase
{
    private readonly IOperationalRecordService _recordService;
    private readonly IJiraTransferService _transferService;
    private readonly CommandIdempotencyOptions _commandOptions;
    private readonly TimeProvider _timeProvider;
    private readonly bool _simulationMode;
    private readonly bool _readOnlyIntegrationMode;

    /// <summary>Initializes the controller.</summary>
    public OperationalRecordsController(
        IOperationalRecordService recordService,
        IJiraTransferService transferService,
        Microsoft.Extensions.Options.IOptions<CommandIdempotencyOptions> commandOptions,
        Microsoft.Extensions.Options.IOptions<OperationalRecordsOptions> operationalOptions,
        Microsoft.Extensions.Options.IOptions<JiraIntegrationOptions> jiraOptions,
        TimeProvider timeProvider)
    {
        _recordService = recordService;
        _transferService = transferService;
        _commandOptions = commandOptions.Value;
        _timeProvider = timeProvider;
        _readOnlyIntegrationMode = operationalOptions.Value.ReadOnlyIntegrationMode;
        _simulationMode = string.Equals(
                operationalOptions.Value.SourceProvider,
                "Simulation",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(jiraOptions.Value.Provider, "Simulation", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Refreshes and returns a bounded list of active operational records.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.CanViewOperationalRecords)]
    [EnableRateLimiting(ApiRateLimits.OperationalRecordRefresh)]
    [ProducesResponseType(typeof(IReadOnlyList<OperationalRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<OperationalRecordResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        OperationalRecordResult<IReadOnlyList<OperationalRecord>> result = await _recordService.ListAsync(Context(), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!.Select(ToResponse).ToArray())
            : Failure<IReadOnlyList<OperationalRecordResponse>>(result.Failure!);
    }

    /// <summary>Returns one persisted operational record.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewOperationalRecords)]
    [ProducesResponseType(typeof(OperationalRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperationalRecordResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        OperationalRecordResult<OperationalRecord> result = await _recordService.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : Failure<OperationalRecordResponse>(result.Failure!);
    }

    /// <summary>Generates a read-only Jira field preview without modifying external systems.</summary>
    [HttpPost("{id:guid}/jira-preview")]
    [Authorize(Policy = Policies.CanPreviewJira)]
    [EnableRateLimiting(ApiRateLimits.JiraPreview)]
    [ProducesResponseType(typeof(JiraPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<JiraPreviewResponse>> PreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        OperationalRecordResult<JiraIssueDraft> result = await _transferService.PreviewAsync(id, Context(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure<JiraPreviewResponse>(result.Failure!);
        }

        JiraIssueDraft draft = result.Value!;
        JiraIssueFieldMapping mapping = draft.FieldMapping;
        return Ok(new JiraPreviewResponse(
            draft.OperationalRecordId,
            draft.OrCode,
            draft.ProjectKey,
            draft.IssueType,
            draft.Summary,
            draft.Description,
            draft.RequesterAccountId,
            draft.MappingVersion,
            draft.IdempotencyKey,
            draft.Warnings,
            draft.AssigneeUsername,
            _simulationMode,
            SimulationNotice(),
            _readOnlyIntegrationMode,
            ReadOnlyNotice(),
            draft.ReporterUsername,
            mapping.IssueTypeId,
            mapping.TeamCustomField,
            mapping.TeamValue,
            mapping.RequesterWatcherCustomField,
            mapping.Labels));
    }

    /// <summary>Explicitly creates Jira and then closes/updates the source record.</summary>
    [HttpPost("{id:guid}/jira")]
    [Authorize(Policy = Policies.CanCreateJira)]
    [EnableRateLimiting(ApiRateLimits.JiraCreate)]
    [ProducesResponseType(typeof(JiraTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<JiraTransferResponse>> CreateAsync(
        Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (_readOnlyIntegrationMode)
        {
            return Failure<JiraTransferResponse>(ExternalWritesDisabled());
        }

        if (!IsValidIdempotencyKey(idempotencyKey))
        {
            return Failure<JiraTransferResponse>(new OperationalRecordFailure(OperationalErrorCodes.InvalidIdempotencyKey, "idempotency", false));
        }

        OperationalRecordCommandContext context = Context(idempotencyKey);
        OperationalRecordResult<OperationalRecord> result = await _transferService.CreateAsync(id, context, cancellationToken);
        return result.IsSuccess ? Ok(ToTransferResponse(result.Value!, context.CorrelationId)) : Failure<JiraTransferResponse>(result.Failure!);
    }

    /// <summary>Resumes only the safe failed stage of a durable workflow.</summary>
    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = Policies.CanRetryJira)]
    [EnableRateLimiting(ApiRateLimits.WorkflowRetry)]
    [ProducesResponseType(typeof(JiraTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<JiraTransferResponse>> RetryAsync(
        Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (_readOnlyIntegrationMode)
        {
            return Failure<JiraTransferResponse>(ExternalWritesDisabled());
        }

        if (!IsValidIdempotencyKey(idempotencyKey))
        {
            return Failure<JiraTransferResponse>(new OperationalRecordFailure(OperationalErrorCodes.InvalidIdempotencyKey, "idempotency", false));
        }

        OperationalRecordCommandContext context = Context(idempotencyKey);
        OperationalRecordResult<OperationalRecord> result = await _transferService.RetryAsync(id, context, cancellationToken);
        return result.IsSuccess ? Ok(ToTransferResponse(result.Value!, context.CorrelationId)) : Failure<JiraTransferResponse>(result.Failure!);
    }

    private OperationalRecordCommandContext Context(string? idempotencyKey = null)
    {
        string correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return new OperationalRecordCommandContext(
            User.Identity?.Name ?? "unknown",
            correlationId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            idempotencyKey);
    }

    private ActionResult<T> Failure<T>(OperationalRecordFailure failure)
    {
        int status = failure.Code switch
        {
            OperationalErrorCodes.OperationalRecordNotFound => StatusCodes.Status404NotFound,
            OperationalErrorCodes.OperationalRecordInvalidState or
            OperationalErrorCodes.RequesterResolutionAmbiguous or
            OperationalErrorCodes.JiraAlreadyCreated or
            OperationalErrorCodes.WorkflowConflict or
            OperationalErrorCodes.WorkflowAlreadyCompleted or
            OperationalErrorCodes.OperationalRecordAlreadyClaimed or
            OperationalErrorCodes.OperationalRecordChanged or
            OperationalErrorCodes.OperationalRecordNoLongerOpen or
            OperationalErrorCodes.WorkflowAlreadyInProgress or
            OperationalErrorCodes.ExternalWritesDisabled => StatusCodes.Status409Conflict,
            OperationalErrorCodes.InvalidIdempotencyKey => StatusCodes.Status400BadRequest,
            OperationalErrorCodes.JiraValidationFailed or
            OperationalErrorCodes.JiraReporterRejected or
            OperationalErrorCodes.RequesterResolutionFailed or
            OperationalErrorCodes.OperatorReporterResolutionFailed => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return OperationalProblemDetails.Create(status, failure.Code, SafeTitle(failure.Code), Context().CorrelationId, failure.Stage, failure.Retryable);
    }

    private static string SafeTitle(string code) => code switch
    {
        OperationalErrorCodes.OperationalRecordNotFound => "Operational record was not found.",
        OperationalErrorCodes.OperationalRecordInvalidState => "Operational record is not eligible for this operation.",
        OperationalErrorCodes.RequesterResolutionAmbiguous => "Requester resolution requires review.",
        OperationalErrorCodes.RequesterResolutionFailed => "Requester could not be resolved safely.",
        OperationalErrorCodes.OperatorReporterResolutionFailed => "The authenticated operator could not be resolved safely as Jira reporter.",
        OperationalErrorCodes.JiraReporterRejected => "Jira rejected the authenticated operator as reporter.",
        OperationalErrorCodes.JiraAlreadyCreated => "A Jira issue already exists for this workflow.",
        OperationalErrorCodes.WorkflowAlreadyCompleted => "The workflow is already complete.",
        OperationalErrorCodes.OperationalRecordAlreadyClaimed => "Another actor owns the active workflow claim.",
        OperationalErrorCodes.OperationalRecordChanged => "The source record changed and must be refreshed.",
        OperationalErrorCodes.OperationalRecordNoLongerOpen => "The source record is no longer open.",
        OperationalErrorCodes.WorkflowAlreadyInProgress => "The workflow is already in progress.",
        OperationalErrorCodes.InvalidIdempotencyKey => "The idempotency key is invalid.",
        OperationalErrorCodes.WorkflowConflict => "The workflow cannot proceed automatically.",
        OperationalErrorCodes.ExternalWritesDisabled => "External writes are disabled for this integration mode.",
        _ => "The operational workflow could not be completed."
    };

    private OperationalRecordResponse ToResponse(OperationalRecord record) => new(
        record.Id,
        record.SourceRecordId,
        record.OrCode,
        record.Title,
        record.Description,
        record.Requester,
        record.CreatedAt,
        record.Environment,
        record.ServerReference,
        record.ApplicationReference,
        record.Classification,
        record.JiraEligible,
        record.EligibilityReason,
        record.WorkflowState,
        record.JiraIssueKey,
        record.LastErrorCode,
        record.CorrelationId,
        record.RetryCount,
        record.UpdatedAt,
        record.ClaimExpiresAt > _timeProvider.GetUtcNow(),
        record.ClaimedBy,
        record.ClaimedAt,
        record.ClaimExpiresAt,
        record.LastSourceValidationAt,
        record.Version,
        record.ReconciliationRequired,
        IsRetryEligible(record),
        !string.IsNullOrWhiteSpace(record.JiraIssueKey),
        PresentationState(record),
        _simulationMode,
        SimulationNotice(),
        _readOnlyIntegrationMode,
        ReadOnlyNotice())
    {
        RecommendedClassification = record.SdmEvaluation?.Result.RecommendedClassification,
        SdmCandidateRecommended = false,
        RuleSetVersion = record.SdmEvaluation?.Result.RuleSetVersion,
        ReasonCodes = record.SdmEvaluation?.Result.ReasonCodes ?? [],
        EvaluatedAt = record.SdmEvaluation?.EvaluatedAt,
        EvaluationStale = record.SdmEvaluation?.Result.EvaluationStale ?? true,
        SourceChanged = record.SdmEvaluation?.Result.SourceChanged ?? false,
        BlockingConditions = record.SdmEvaluation?.Result.BlockingConditions ?? [],
        ExternalWriteEligible = false
    };

    private static bool IsRetryEligible(OperationalRecord record) =>
        !record.ReconciliationRequired
        && (record.WorkflowState == OperationalRecordWorkflowState.JiraCreateFailed
            || (!string.IsNullOrWhiteSpace(record.JiraIssueKey)
                && record.WorkflowState is OperationalRecordWorkflowState.JiraCreated
                    or OperationalRecordWorkflowState.ClosingOperationalRecord
                    or OperationalRecordWorkflowState.OperationalRecordCloseFailed));

    private bool IsValidIdempotencyKey(string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey)
        || CommandIdempotency.IsValid(idempotencyKey, _commandOptions.MaxKeyLength);

    private JiraTransferResponse ToTransferResponse(OperationalRecord record, string correlationId) => new(
        record.Id,
        record.OrCode,
        record.WorkflowState,
        record.JiraIssueKey,
        record.MappingVersion ?? string.Empty,
        record.IdempotencyKey ?? string.Empty,
        record.RetryCount,
        correlationId,
        _simulationMode,
        SimulationNotice());

    private string? SimulationNotice() =>
        _simulationMode ? SimulationOperationalRecordClient.OperatorNotice : null;

    private string? ReadOnlyNotice() =>
        _readOnlyIntegrationMode ? ExternalIntegrationNotices.RealDataReadOnly : null;

    private static OperationalRecordFailure ExternalWritesDisabled() =>
        new(OperationalErrorCodes.ExternalWritesDisabled, "external-write-fence", false);

    private static string PresentationState(OperationalRecord record) => record.WorkflowState switch
    {
        OperationalRecordWorkflowState.Completed => OperationalRecordPresentationStates.Completed,
        OperationalRecordWorkflowState.Eligible or OperationalRecordWorkflowState.Previewed =>
            OperationalRecordPresentationStates.Actionable,
        OperationalRecordWorkflowState.CreateRequested or
        OperationalRecordWorkflowState.CreatingJira or
        OperationalRecordWorkflowState.JiraCreated or
        OperationalRecordWorkflowState.ClosingOperationalRecord => OperationalRecordPresentationStates.InProgress,
        _ => OperationalRecordPresentationStates.NeedsAttention
    };
}
