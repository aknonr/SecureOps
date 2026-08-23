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
        string purpose,
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
        string purpose,
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
        string purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryMembershipPathRequest, DirectoryMembershipPathResponse>(
            "api/v1/directory/principals/membership-paths",
            new DirectoryMembershipPathRequest(account, targetGroup, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryAccountHealthResponse> GetAccountHealthAsync(
        string account,
        string purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalEnrichmentRequest, DirectoryAccountHealthResponse>(
            "api/v1/directory/principals/account-health",
            new DirectoryPrincipalEnrichmentRequest(account, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryServiceEvidenceResponse> GetServiceEvidenceAsync(
        string account,
        string purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalEnrichmentRequest, DirectoryServiceEvidenceResponse>(
            "api/v1/directory/principals/service-evidence",
            new DirectoryPrincipalEnrichmentRequest(account, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryPrivilegedMembershipResponse> GetPrivilegedMembershipsAsync(
        string account,
        string purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryPrincipalEnrichmentRequest, DirectoryPrivilegedMembershipResponse>(
            "api/v1/directory/principals/privileged-memberships",
            new DirectoryPrincipalEnrichmentRequest(account, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryGroupDetailResponse> GetGroupAsync(
        string group,
        string purpose,
        bool refresh,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryGroupLookupRequest, DirectoryGroupDetailResponse>(
            "api/v1/directory/groups/lookup",
            new DirectoryGroupLookupRequest(group, purpose, refresh),
            cancellationToken);

    /// <inheritdoc />
    public Task<DirectoryMemberPageResponse> GetGroupMembersAsync(
        string group,
        string purpose,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken) =>
        PostAsync<DirectoryGroupMembersRequest, DirectoryMemberPageResponse>(
            "api/v1/directory/groups/members",
            new DirectoryGroupMembersRequest(group, purpose, pageSize, continuationToken),
            cancellationToken);

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
