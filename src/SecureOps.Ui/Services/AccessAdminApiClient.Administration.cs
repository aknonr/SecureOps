using System.Net.Http.Json;
using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

public sealed partial class AccessAdminApiClient
{
    /// <inheritdoc />
    public Task<AccessPage<AccessUserResponse>> PageUsersAsync(AccessPageQuery query, CancellationToken cancellationToken) =>
        GetAsync<AccessPage<AccessUserResponse>>(PageRoute("users", query), cancellationToken);
    /// <inheritdoc />
    public Task<AccessPage<AccessRequestResponse>> PageRequestsAsync(AccessPageQuery query, CancellationToken cancellationToken) =>
        GetAsync<AccessPage<AccessRequestResponse>>(PageRoute("requests", query), cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<AccessRoleDefinition>> RolesAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AccessRoleDefinition>>("api/v1/access/roles", cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<AccessActionDefinition>> ActionsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AccessActionDefinition>>("api/v1/access/roles/actions", cancellationToken);
    /// <inheritdoc />
    public Task<AccessRoleImpact> PreviewRoleAsync(AccessRoleChange change, CancellationToken cancellationToken) => RoleAsync(change, false, cancellationToken);
    /// <inheritdoc />
    public Task<AccessRoleImpact> SaveRoleAsync(AccessRoleChange change, CancellationToken cancellationToken) => RoleAsync(change, true, cancellationToken);

    private async Task<AccessRoleImpact> RoleAsync(AccessRoleChange change, bool apply, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(() => apply
            ? _httpClient.PutAsJsonAsync("api/v1/access/roles", change, cancellationToken)
            : _httpClient.PostAsJsonAsync("api/v1/access/roles/preview", change, cancellationToken), cancellationToken);
        return await ApiResponseReader.ReadOrThrowAsync<AccessRoleImpact>(response, cancellationToken);
    }

    private static string PageRoute(string resource, AccessPageQuery query) =>
        $"api/v1/access/{resource}/page?page={query.Page}&pageSize={query.PageSize}"
        + (string.IsNullOrEmpty(query.Search) ? "" : $"&search={Uri.EscapeDataString(query.Search)}")
        + (string.IsNullOrEmpty(query.Status) ? "" : $"&status={Uri.EscapeDataString(query.Status)}")
        + (string.IsNullOrEmpty(query.Role) ? "" : $"&role={Uri.EscapeDataString(query.Role)}");
}
