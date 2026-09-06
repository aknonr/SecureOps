using System.Globalization;
using System.Net.Http.Json;
using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Ui.Services;

/// <summary>
/// HTTP implementation of the resource catalogue and personal shift-set client.
/// </summary>
public sealed class ResourceApiClient : IResourceApiClient
{
    private const string Root = "api/v1/resources";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new resource API client.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    /// <param name="sessionContext">Browser session whose API cookies these calls belong to.</param>
    public ResourceApiClient(HttpClient httpClient, IApiSessionContext sessionContext)
    {
        _httpClient = httpClient;
        ApiSessionHeaders.Attach(httpClient, sessionContext);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ResourceCategory>> GetCategoriesAsync(
        bool includeArchived,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<ResourceCategory>>(
            () => _httpClient.GetAsync(
                $"{Root}/categories?includeArchived={Bool(includeArchived)}",
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourceCategory> CreateCategoryAsync(
        SaveResourceCategoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<ResourceCategory>(
            () => _httpClient.PostAsJsonAsync($"{Root}/categories", request, JsonOptions, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourceCategory> SaveCategoryAsync(
        Guid id,
        SaveResourceCategoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<ResourceCategory>(
            () => _httpClient.PutAsJsonAsync($"{Root}/categories/{id}", request, JsonOptions, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePage> QueryLinksAsync(ResourceQuery query, CancellationToken cancellationToken) =>
        SendAsync<ResourcePage>(
            () => _httpClient.GetAsync($"{Root}/links{ToQueryString(query)}", cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourceLink> GetLinkAsync(Guid id, bool includeArchived, CancellationToken cancellationToken) =>
        SendAsync<ResourceLink>(
            () => _httpClient.GetAsync(
                $"{Root}/links/{id}?includeArchived={Bool(includeArchived)}",
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourceLink> CreateLinkAsync(
        SaveResourceLinkRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<ResourceLink>(
            () => _httpClient.PostAsJsonAsync($"{Root}/links", request, JsonOptions, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourceLink> SaveLinkAsync(
        Guid id,
        SaveResourceLinkRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<ResourceLink>(
            () => _httpClient.PutAsJsonAsync($"{Root}/links/{id}", request, JsonOptions, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePreferencesResponse> GetPreferencesAsync(CancellationToken cancellationToken) =>
        SendAsync<ResourcePreferencesResponse>(
            () => _httpClient.GetAsync($"{Root}/me", cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePreferencesResponse> SetFavouriteAsync(
        Guid linkId,
        bool favourite,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        SendAsync<ResourcePreferencesResponse>(
            () => _httpClient.PutAsJsonAsync(
                $"{Root}/me/favourites/{linkId}",
                new SaveFavouriteRequest(favourite, expectedVersion),
                JsonOptions,
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePreferencesResponse> CreateSetAsync(
        SaveShiftSetRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<ResourcePreferencesResponse>(
            () => _httpClient.PostAsJsonAsync($"{Root}/me/sets", request, JsonOptions, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePreferencesResponse> SaveSetAsync(
        Guid id,
        SaveShiftSetRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<ResourcePreferencesResponse>(
            () => _httpClient.PutAsJsonAsync($"{Root}/me/sets/{id}", request, JsonOptions, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePreferencesResponse> DeleteSetAsync(
        Guid id,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        SendAsync<ResourcePreferencesResponse>(
            () => _httpClient.DeleteAsync(
                $"{Root}/me/sets/{id}?expectedVersion={expectedVersion.ToString(CultureInfo.InvariantCulture)}",
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<ShiftSetResponse> ResolveSetAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync<ShiftSetResponse>(
            () => _httpClient.GetAsync($"{Root}/me/sets/{id}/resolve", cancellationToken),
            cancellationToken);

    private static System.Text.Json.JsonSerializerOptions JsonOptions => ApiResponseReader.JsonOptions;

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// Builds the catalogue query string.
    /// </summary>
    /// <remarks>
    /// Empty filters are omitted rather than sent blank: the contract ignores empty values, and an
    /// omitted parameter keeps the URL honest about what was actually asked for. Values are escaped
    /// because search text is free-form operator input.
    /// </remarks>
    private static string ToQueryString(ResourceQuery query)
    {
        List<string> parts =
        [
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={query.PageSize.ToString(CultureInfo.InvariantCulture)}"
        ];

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parts.Add($"search={Uri.EscapeDataString(query.Search.Trim())}");
        }

        if (query.CategoryId is { } categoryId)
        {
            parts.Add($"categoryId={categoryId}");
        }

        if (!string.IsNullOrWhiteSpace(query.Environment))
        {
            parts.Add($"environment={Uri.EscapeDataString(query.Environment.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            parts.Add($"location={Uri.EscapeDataString(query.Location.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            parts.Add($"tag={Uri.EscapeDataString(query.Tag.Trim())}");
        }

        if (query.IncludeArchived)
        {
            parts.Add("includeArchived=true");
        }

        return $"?{string.Join('&', parts)}";
    }

    private static async Task<T> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await send();
            return await ApiResponseReader.ReadOrThrowAsync<T>(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw ApiResponseReader.ToTransportException(ex, cancellationToken);
        }
    }
}
