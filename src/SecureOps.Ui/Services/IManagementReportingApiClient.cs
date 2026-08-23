using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the management reporting endpoints in
/// <c>docs/contracts/secureops-api-v1-ui-integration.md</c>.
/// </summary>
/// <remarks>
/// Both routes require <c>Reporting.ManagementView</c> and audit the privileged read on the server.
/// Every figure the UI shows comes from these two responses; the UI computes no metric of its own,
/// because a browser-side number would have no audit trail and could not be explained from persisted
/// evidence.
/// </remarks>
public interface IManagementReportingApiClient
{
    /// <summary>
    /// Reads the aggregate management summary for one window.
    /// </summary>
    /// <param name="window">Validated window request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Identity, workflow, adoption, security, and duration aggregates.</returns>
    public Task<ManagementReportResponse> GetSummaryAsync(
        ReportingWindowRequest window,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one page of per-operator activity for the same window.
    /// </summary>
    /// <param name="window">Validated window request.</param>
    /// <param name="page">One-based page number.</param>
    /// <param name="pageSize">Page size; the API caps this at 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One server-paginated page of persisted actor aggregates.</returns>
    /// <remarks>
    /// Pagination is server-side. The endpoint returns no directory enrichment, so the actor value
    /// is the persisted corporate principal and nothing more.
    /// </remarks>
    public Task<OperatorActivityPageResponse> GetOperatorsAsync(
        ReportingWindowRequest window,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
