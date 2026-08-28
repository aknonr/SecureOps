using System.Globalization;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the management reporting API client.
/// </summary>
public sealed class ManagementReportingApiClient : IManagementReportingApiClient
{
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
