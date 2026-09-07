using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Ui.Services;

/// <summary>
/// Reads and writes the shared link catalogue and the caller's own shift-start preferences.
/// </summary>
/// <remarks>
/// Personal routes are owner-scoped by the server from the authenticated session; the UI never
/// submits an owner identifier and cannot request another operator's favourites or sets. Management
/// routes are additionally gated on <c>Resources.Manage</c>, which the API re-checks on every call —
/// hiding a button here is a courtesy, never the control.
/// <para>
/// Every personal mutation carries the single personal aggregate version and returns refreshed
/// state, so callers replace what they hold rather than patching it locally.
/// </para>
/// </remarks>
public interface IResourceApiClient
{
    /// <summary>Saves only caller-owned layout with optimistic concurrency; no automatic retry.</summary>
    public Task<ResourcePreferencesResponse> SaveLayoutAsync(SaveResourceLayoutRequest request, CancellationToken cancellationToken);

    /// <summary>Freshly resolves selected references before a separate native browser activation.</summary>
    public Task<IReadOnlyList<ResourceLink>> ResolveLinksAsync(ResolveResourceLinksRequest request, CancellationToken cancellationToken);

    /// <summary>Lists categories under inherited visibility rules.</summary>
    /// <param name="includeArchived">Manager-only administrative read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Permitted categories in display order.</returns>
    public Task<IReadOnlyList<ResourceCategory>> GetCategoriesAsync(
        bool includeArchived,
        CancellationToken cancellationToken);

    /// <summary>Creates a category. Requires <c>expectedVersion=0</c>.</summary>
    public Task<ResourceCategory> CreateCategoryAsync(
        SaveResourceCategoryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Replaces a category in full at its current version.</summary>
    public Task<ResourceCategory> SaveCategoryAsync(
        Guid id,
        SaveResourceCategoryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Queries permitted links with bounded pagination.</summary>
    public Task<ResourcePage> QueryLinksAsync(ResourceQuery query, CancellationToken cancellationToken);

    /// <summary>Finds bounded permitted environment values independently of catalogue pagination.</summary>
    public Task<ResourceEnvironmentOptions> GetEnvironmentsAsync(ResourceEnvironmentQuery query, CancellationToken cancellationToken);

    /// <summary>Dismisses the owner-only first-use guide invitation at the current aggregate version.</summary>
    public Task<ResourcePreferencesResponse> DismissGuideAsync(long expectedVersion, CancellationToken cancellationToken);

    /// <summary>Reads one link under the same visibility rules.</summary>
    public Task<ResourceLink> GetLinkAsync(Guid id, bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Creates a link. Requires <c>expectedVersion=0</c>.</summary>
    public Task<ResourceLink> CreateLinkAsync(SaveResourceLinkRequest request, CancellationToken cancellationToken);

    /// <summary>Replaces a link in full at its current version.</summary>
    public Task<ResourceLink> SaveLinkAsync(
        Guid id,
        SaveResourceLinkRequest request,
        CancellationToken cancellationToken);

    /// <summary>Reads the caller's own favourites, sets and default selection.</summary>
    public Task<ResourcePreferencesResponse> GetPreferencesAsync(CancellationToken cancellationToken);

    /// <summary>Sets or clears a favourite, guarded by the personal aggregate version.</summary>
    public Task<ResourcePreferencesResponse> SetFavouriteAsync(
        Guid linkId,
        bool favourite,
        long expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Creates a personal set.</summary>
    public Task<ResourcePreferencesResponse> CreateSetAsync(
        SaveShiftSetRequest request,
        CancellationToken cancellationToken);

    /// <summary>Merges ordered membership and explicit removals, preserving omitted saved references.</summary>
    public Task<ResourcePreferencesResponse> SaveSetAsync(
        Guid id,
        SaveShiftSetRequest request,
        CancellationToken cancellationToken);

    /// <summary>Deletes a personal set at the given aggregate version.</summary>
    public Task<ResourcePreferencesResponse> DeleteSetAsync(
        Guid id,
        long expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a set to the references that are openable right now.
    /// </summary>
    /// <remarks>
    /// Called immediately before a user-initiated opening rather than reusing a stored list:
    /// catalogue state and permissions can change after a set was saved, and the response is
    /// deliberately the current permitted links only — it reveals no archived, restricted or
    /// removed reference, and no count of what was excluded.
    /// </remarks>
    public Task<ShiftSetResponse> ResolveSetAsync(Guid id, CancellationToken cancellationToken);
}
