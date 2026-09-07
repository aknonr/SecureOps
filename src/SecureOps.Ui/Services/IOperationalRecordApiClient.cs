using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the operational-record and Jira transfer endpoints in
/// <c>docs/contracts/secureops-api-v1-ui-integration.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No idempotency key is sent.</b> The contract makes <c>Idempotency-Key</c> optional, and when it
/// is absent the API derives a deterministic key from actor, command, and record id. That is already
/// the behaviour the UI wants — repeating a create after a refresh or a navigation collapses onto the
/// same command instead of producing a second Jira issue — and it belongs to the backend. Generating
/// a key here would be a second, competing idempotency policy that could disagree with the server's,
/// which is exactly what the contract warns against.
/// </para>
/// <para>
/// Every method returns authoritative server state. Nothing in this layer infers claim ownership,
/// retry safety, Jira success, or source-close success.
/// </para>
/// </remarks>
public interface IOperationalRecordApiClient
{
    /// <summary>Builds a non-publishable, version-bound operator declaration.</summary>
    public Task<JiraPreviewResponse> ReviewAsync(Guid id, JiraReviewRequest request, CancellationToken cancellationToken);
    /// <summary>
    /// Lists operational records.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Records with workflow, claim, freshness, and version state.</returns>
    /// <remarks>
    /// This is a <b>source refresh, not a passive read</b>: it imports and classifies from the
    /// bounded source response and is rate-limited. Callers must not treat it as free, and must not
    /// poll it.
    /// </remarks>
    public Task<IReadOnlyList<OperationalRecordResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one operational record.
    /// </summary>
    /// <param name="id">Record identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record including claim, freshness, retry, and version state.</returns>
    public Task<OperationalRecordResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Generates the proposed Jira mapping for operator review.
    /// </summary>
    /// <param name="id">Record identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The backend-authored draft, including any warnings.</returns>
    /// <remarks>
    /// Safe by contract: never creates a Jira issue and never closes the source. The returned mapping
    /// is the server's, and the UI renders it verbatim rather than reproducing any mapping rule.
    /// </remarks>
    public Task<JiraPreviewResponse> PreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the Jira issue and then closes or updates the source record.
    /// </summary>
    /// <param name="id">Record identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The durable transfer result, including the workflow state reached.</returns>
    /// <remarks>
    /// Two stages behind one call. A success response may still report
    /// <c>OperationalRecordCloseFailed</c>: the Jira issue exists and the source close did not
    /// complete. Callers must render the returned <c>WorkflowState</c> rather than assuming the
    /// whole transfer succeeded.
    /// </remarks>
    public Task<JiraTransferResponse> CreateJiraAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Resumes the failed stage of a durable workflow.
    /// </summary>
    /// <param name="id">Record identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resumed transfer result.</returns>
    /// <remarks>
    /// Only the safe failed stage is resumed, and the server decides what that is. The UI must not
    /// offer this because a call failed; it offers it because authoritative record state says the
    /// workflow is in a retryable stage.
    /// </remarks>
    public Task<JiraTransferResponse> RetryAsync(Guid id, CancellationToken cancellationToken);
}
