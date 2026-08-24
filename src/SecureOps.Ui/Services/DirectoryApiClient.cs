using System.Net.Http.Json;
using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the Directory Explorer API client.
/// </summary>
/// <remarks>
/// Requests are POST bodies rather than query strings throughout, which is the contract's shape and
/// also keeps the queried account out of the URL, browser history, and proxy logs.
/// </remarks>
public sealed class DirectoryApiClient : IDirectoryApiClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new Directory Explorer API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    public DirectoryApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public Task<DirectoryGroupPageResponse> GetPrincipalGroupsAsync(
        string account,
        string? purpose,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalGroupsRequest, DirectoryGroupPageResponse>(
            "api/v1/directory/principals/groups",
            new DirectoryPrincipalGroupsRequest(account, purpose, pageSize, continuationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryPrincipalMembershipsResponse> GetMembershipsAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalEnrichmentRequest, DirectoryPrincipalMembershipsResponse>(
            "api/v1/directory/principals/memberships",
            new DirectoryPrincipalEnrichmentRequest(account, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryMembershipPathResponse> GetMembershipPathsAsync(
        string account,
        string targetGroup,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryMembershipPathRequest, DirectoryMembershipPathResponse>(
            "api/v1/directory/principals/membership-paths",
            new DirectoryMembershipPathRequest(account, targetGroup, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryAccountHealthResponse> GetAccountHealthAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalEnrichmentRequest, DirectoryAccountHealthResponse>(
            "api/v1/directory/principals/account-health",
            new DirectoryPrincipalEnrichmentRequest(account, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryServiceEvidenceResponse> GetServiceEvidenceAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalEnrichmentRequest, DirectoryServiceEvidenceResponse>(
            "api/v1/directory/principals/service-evidence",
            new DirectoryPrincipalEnrichmentRequest(account, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryPrivilegedMembershipResponse> GetPrivilegedMembershipsAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalEnrichmentRequest, DirectoryPrivilegedMembershipResponse>(
            "api/v1/directory/principals/privileged-memberships",
            new DirectoryPrincipalEnrichmentRequest(account, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryGroupDetailResponse> GetGroupAsync(
        string group,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryGroupLookupRequest, DirectoryGroupDetailResponse>(
            "api/v1/directory/groups/lookup",
            new DirectoryGroupLookupRequest(group, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryMemberPageResponse> GetGroupMembersAsync(
        string group,
        string? purpose,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryGroupMembersRequest, DirectoryMemberPageResponse>(
            "api/v1/directory/groups/members",
            new DirectoryGroupMembersRequest(group, purpose, pageSize, continuationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryGroupAnalysisResponse> AnalyzeGroupAsync(
        string group,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryGroupAnalysisRequest, DirectoryGroupAnalysisResponse>(
            "api/v1/directory/groups/analysis",
            new DirectoryGroupAnalysisRequest(group, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public async Task<DirectoryExportFile> ExportGroupAsync(
        string group,
        string mode,
        string? purpose,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            // Format is pinned to Csv: the contract offers nothing else, and passing through a
            // caller-chosen value would invite a request the server would only reject.
            response = await _httpClient.PostAsJsonAsync(
                "api/v1/directory/groups/export",
                new DirectoryGroupExportRequest(group, mode, "Csv", purpose),
                ApiResponseReader.JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ApiResponseReader.ToExceptionAsync(response, cancellationToken);
            }

            byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // The server names the file; echoing its name keeps the export traceable to the audited
            // operation rather than to something the browser invented.
            string fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? "directory-group-export.csv";

            return new DirectoryExportFile(
                fileName,
                response.Content.Headers.ContentType?.MediaType ?? "text/csv",
                content);
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string route,
        TRequest body,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync(route, body, ApiResponseReader.JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }

        using (response)
        {
            return await ApiResponseReader.ReadOrThrowAsync<TResponse>(response, cancellationToken);
        }
    }
}
