using System.Globalization;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the management reporting API client.
/// </summary>
public sealed class ManagementReportingApiClient : IManagementReportingApiClient
{
    /// <inheritdoc />
    public async Task<WorkflowReport> CaptureWorkflowsAsync(WorkflowReportRequest request, CancellationToken token)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/v1/reporting/management/workflows", request, token);
        return await ApiResponseReader.ReadOrThrowAsync<WorkflowReport>(response, token);
    }
    /// <inheritdoc />
    public Task<WorkflowReport> ReadWorkflowsAsync(Guid id, WorkflowReportFilter filter, CancellationToken token) =>
        GetAsync<WorkflowReport>($"api/v1/reporting/management/workflows/{id:D}{WorkflowQuery(filter)}", token);
    /// <inheritdoc />
    public async Task<byte[]> ExportWorkflowsAsync(Guid id, WorkflowReportFilter filter, CancellationToken token)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync($"api/v1/reporting/management/workflows/{id:D}/export{WorkflowQuery(filter)}", token);
        if (!response.IsSuccessStatusCode)
        { _ = await ApiResponseReader.ReadOrThrowAsync<WorkflowReport>(response, token); }
        return await response.Content.ReadAsByteArrayAsync(token);
    }
    private static string WorkflowQuery(WorkflowReportFilter filter) => "?page=" + filter.Page.ToString(CultureInfo.InvariantCulture)
        + "&pageSize=" + filter.PageSize.ToString(CultureInfo.InvariantCulture)
        + "&module=" + Uri.EscapeDataString(filter.Module ?? "") + "&status=" + Uri.EscapeDataString(filter.Status ?? "")
        + "&recordType=" + Uri.EscapeDataString(filter.RecordType ?? "") + "&metric=" + Uri.EscapeDataString(filter.Metric ?? "");
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new management reporting API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    /// <param name="sessionContext">Browser session whose API cookies these calls belong to.</param>
    public ManagementReportingApiClient(HttpClient httpClient, IApiSessionContext sessionContext)
    {
        _httpClient = httpClient;
        ApiSessionHeaders.Attach(httpClient, sessionContext);
    }

    /// <inheritdoc />
    public Task<ManagementReportResponse> GetSummaryAsync(
        ReportingWindowRequest window,
        CancellationToken cancellationToken) =>
        GetAsync<ManagementReportResponse>(
            $"api/v1/reporting/management/summary{Query(window)}",
            cancellationToken);

    /// <inheritdoc />
    public Task<OperatorActivityPageResponse> GetOperatorsAsync(
        ReportingWindowRequest window,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        GetAsync<OperatorActivityPageResponse>(
            $"api/v1/reporting/management/operators{Query(window)}"
                + $"&page={page.ToString(CultureInfo.InvariantCulture)}"
                + $"&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);

    /// <summary>
    /// Builds the window query string, always leading with <c>?window=</c>.
    /// </summary>
    /// <remarks>
    /// Bounds are sent only for a custom window and are formatted round-trip in UTC. A preset that
    /// carried leftover bounds would be rejected by the server as an invalid window, so they are
    /// omitted rather than defaulted.
    /// </remarks>
    private static string Query(ReportingWindowRequest window)
    {
        string query = $"?window={Uri.EscapeDataString(window.Selection)}";

        if (window.FromInclusiveUtc is { } from && window.ToExclusiveUtc is { } to)
        {
            query += $"&from={Uri.EscapeDataString(from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
            query += $"&to={Uri.EscapeDataString(to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
        }

        return query;
    }

    private async Task<T> GetAsync<T>(string route, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync(route, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<T>(response, cancellationToken);
        }
    }
}
