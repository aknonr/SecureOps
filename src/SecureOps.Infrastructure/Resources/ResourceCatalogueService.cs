using System.Data.Common;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.Access;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Infrastructure.Resources;

/// <summary>Authorization, validation and owner-scoped catalogue orchestration; never contacts targets.</summary>
public sealed class ResourceCatalogueService(IResourceRepository repository, IApplicationAccessService access,
    ILogger<ResourceCatalogueService> logger)
{
    /// <summary>Lists categories visible to the approved caller.</summary>
    public Task<ResourceResult<IReadOnlyList<ResourceCategory>>> CategoriesAsync(ClaimsPrincipal principal, AccessOperationContext context,
        bool includeArchived, CancellationToken cancellationToken) => ExecuteAsync<IReadOnlyList<ResourceCategory>>(principal, context, includeArchived,
        async user => new(await repository.CategoriesAsync(Manager(user), includeArchived, cancellationToken)), cancellationToken);

    /// <summary>Returns a bounded page after checking read and optional management authority.</summary>
    public Task<ResourceResult<ResourcePage>> QueryAsync(ClaimsPrincipal principal, AccessOperationContext context, ResourceQuery query,
        CancellationToken cancellationToken) => ExecuteAsync(principal, context, query.IncludeArchived, async user =>
            ResourceValidation.Query(query) ? new(await repository.QueryAsync(query, Manager(user), cancellationToken))
                : ResourceResult<ResourcePage>.Fail(ResourceErrors.Invalid), cancellationToken);

    /// <summary>Returns searchable bounded filter values from the caller's permitted catalogue.</summary>
    public Task<ResourceResult<ResourceEnvironmentOptions>> EnvironmentsAsync(ClaimsPrincipal principal, AccessOperationContext context,
        ResourceEnvironmentQuery query, CancellationToken cancellationToken) => ExecuteAsync(principal, context, query.IncludeArchived, async user =>
            ResourceValidation.Text(query.Search, 40) && query.CategoryId != Guid.Empty
                ? new(await repository.EnvironmentsAsync(query, Manager(user), cancellationToken))
                : ResourceResult<ResourceEnvironmentOptions>.Fail(ResourceErrors.Invalid), cancellationToken);

    /// <summary>Reads a direct ID with the same visibility predicate as search and sets.</summary>
    public Task<ResourceResult<ResourceLink>> GetAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        bool includeArchived, CancellationToken cancellationToken) => ExecuteAsync(principal, context, includeArchived, async user =>
        {
            ResourceLink? link = await repository.GetAsync(id, Manager(user), includeArchived, cancellationToken);
            return link is null ? ResourceResult<ResourceLink>.Fail(ResourceErrors.NotFound) : new(link);
        }, cancellationToken);

    /// <summary>Creates or replaces a category under explicit catalogue management authority.</summary>
    public Task<ResourceResult<ResourceCategory>> SaveCategoryAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        SaveResourceCategoryRequest request, bool create, CancellationToken cancellationToken) => ExecuteAsync(principal, context, true, async user =>
        {
            if (!ResourceValidation.Category(request) || !VersionValid(request.ExpectedVersion, create))
            {
                return ResourceResult<ResourceCategory>.Fail(ResourceErrors.Invalid);
            }

            return await repository.SaveCategoryAsync(create ? Guid.NewGuid() : id, request, Actor(user, context), cancellationToken);
        }, cancellationToken);

    /// <summary>Creates or replaces a link without fetching its URL.</summary>
    public Task<ResourceResult<ResourceLink>> SaveLinkAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        SaveResourceLinkRequest request, bool create, CancellationToken cancellationToken) => ExecuteAsync(principal, context, true, async user =>
        {
            if (!ResourceValidation.Link(request) || !VersionValid(request.ExpectedVersion, create))
            {
                return ResourceResult<ResourceLink>.Fail(ResourceErrors.Invalid);
            }

            return await repository.SaveLinkAsync(create ? Guid.NewGuid() : id, request with { Url = new Uri(request.Url).AbsoluteUri }, Actor(user, context), cancellationToken);
        }, cancellationToken);

    /// <summary>Returns the caller's current preferences with inaccessible references removed.</summary>
    public Task<ResourceResult<ResourcePreferencesResponse>> PreferencesAsync(ClaimsPrincipal principal, AccessOperationContext context,
        CancellationToken cancellationToken) => ExecuteAsync<ResourcePreferencesResponse>(principal, context, false, async user =>
            new(await ProjectAsync(await repository.PreferencesAsync(user.Id, cancellationToken), user, cancellationToken)), cancellationToken);

    /// <summary>Resolves a personal set. Unknown and another user's IDs both return not found.</summary>
    public Task<ResourceResult<ShiftSetResponse>> ResolveSetAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        CancellationToken cancellationToken) => ExecuteAsync(principal, context, false, async user =>
        {
            ResourcePreferencesResponse personal = await ProjectAsync(await repository.PreferencesAsync(user.Id, cancellationToken), user, cancellationToken);
            ShiftSetResponse? set = personal.Sets.FirstOrDefault(s => s.Id == id);
            return set is null ? ResourceResult<ShiftSetResponse>.Fail(ResourceErrors.NotFound) : new(set);
        }, cancellationToken);

    /// <summary>Changes only the caller's favourite membership, guarded by aggregate version.</summary>
    public Task<ResourceResult<ResourcePreferencesResponse>> FavouriteAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid linkId, SaveFavouriteRequest request, CancellationToken cancellationToken) => ExecuteAsync(principal, context, false, async user =>
        {
            if (request.ExpectedVersion is < 0 or long.MaxValue || linkId == Guid.Empty)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Invalid);
            }

            ResourcePreferences current = await repository.PreferencesAsync(user.Id, cancellationToken);
            if (request.Favourite && await repository.GetAsync(linkId, Manager(user), false, cancellationToken) is null)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.NotFound);
            }

            Guid[] ids = [.. current.FavouriteIds.Where(id => id != linkId).Concat(request.Favourite ? [linkId] : Array.Empty<Guid>()).OrderBy(id => id.ToString("D"), StringComparer.Ordinal)];
            if (ids.Length > 200)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Limit);
            }

            return await SavePersonalAsync(current with { FavouriteIds = ids }, request.ExpectedVersion, user, context, cancellationToken);
        }, cancellationToken);

    /// <summary>Dismisses only the caller's guide invitation with the same personal concurrency and audit guarantees.</summary>
    public Task<ResourceResult<ResourcePreferencesResponse>> DismissGuideAsync(ClaimsPrincipal principal, AccessOperationContext context,
        DismissResourceGuideRequest request, CancellationToken cancellationToken) => ExecuteAsync(principal, context, false, async user =>
        {
            if (request.ExpectedVersion is < 0 or long.MaxValue || request.Guide is not ("resources" or "announcements"))
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Invalid);
            }
            ResourcePreferences current = await repository.PreferencesAsync(user.Id, cancellationToken);
            if (request.Guide == "announcements" && !user.Capabilities.Contains(Capabilities.AnnouncementDrafts))
            { return ResourceResult<ResourcePreferencesResponse>.Fail("AccessDenied"); }
            return await SavePersonalAsync(request.Guide == "announcements"
                ? current with { AnnouncementGuideDismissed = true } : current with { GuideDismissed = true },
                request.ExpectedVersion, user, context, cancellationToken);
        }, cancellationToken);

    /// <summary>Saves only caller-owned layout, preserving groups, hidden membership and favourites.</summary>
    public Task<ResourceResult<ResourcePreferencesResponse>> SaveLayoutAsync(ClaimsPrincipal principal, AccessOperationContext context,
        SaveResourceLayoutRequest request, CancellationToken cancellationToken) => ExecuteAsync(principal, context, false, async user =>
        {
            if (!ResourceWorkspacePolicy.Valid(request.Layout) || request.ExpectedVersion is < 0 or long.MaxValue)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Invalid);
            }
            if (request.Layout.Shortcuts.Any(key => !ResourceWorkspacePolicy.Permitted(key, user.Capabilities)))
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail("AccessDenied");
            }
            ResourcePreferences current = await repository.PreferencesAsync(user.Id, cancellationToken);
            return await SavePersonalAsync(current with { WorkspaceLayout = request.Layout }, request.ExpectedVersion, user, context, cancellationToken);
        }, cancellationToken);

    /// <summary>Freshly resolves bounded selections; unavailable IDs reveal neither details nor reasons.</summary>
    public Task<ResourceResult<IReadOnlyList<ResourceLink>>> ResolveLinksAsync(ClaimsPrincipal principal, AccessOperationContext context,
        ResolveResourceLinksRequest request, CancellationToken cancellationToken) => ExecuteAsync<IReadOnlyList<ResourceLink>>(principal, context, false, async user =>
        {
            if (request.LinkIds is not { Count: >= 1 and <= 100 } || request.LinkIds.Contains(Guid.Empty)
                || request.LinkIds.Distinct().Count() != request.LinkIds.Count)
            {
                return ResourceResult<IReadOnlyList<ResourceLink>>.Fail(ResourceErrors.Invalid);
            }
            IReadOnlyList<ResourceLink> resolved = await repository.ResolveAsync(request.LinkIds, Manager(user), cancellationToken);
            var byId = resolved.ToDictionary(link => link.Id);
            return new(request.LinkIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray());
        }, cancellationToken);

    /// <summary>Atomically merges ordered membership and explicit removals; omissions never delete saved references.</summary>
    public Task<ResourceResult<ResourcePreferencesResponse>> SaveSetAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        SaveShiftSetRequest request, bool create, CancellationToken cancellationToken) => ExecuteAsync(principal, context, false, async user =>
        {
            if (!ResourceValidation.Set(request) || create && request.RemoveLinkIds is { Count: > 0 })
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Invalid);
            }

            ResourcePreferences current = await repository.PreferencesAsync(user.Id, cancellationToken);
            if (!create && !current.Sets.Any(s => s.Id == id))
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.NotFound);
            }

            if (create && current.Sets.Count >= 20)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Limit);
            }

            if (current.Sets.Any(s => (create || s.Id != id) && string.Equals(s.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Invalid);
            }

            if (current.Version != request.ExpectedVersion)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Conflict);
            }

            IReadOnlyList<Guid> removed = request.RemoveLinkIds ?? [];
            IReadOnlyList<Guid> savedIds = create ? [] : current.Sets.Single(s => s.Id == id).LinkIds;
            Guid[] requestedIds = [.. request.LinkIds.Concat(removed)];
            IReadOnlyList<ResourceLink> visible = await repository.ResolveAsync(requestedIds, Manager(user), cancellationToken);
            if (visible.Count != requestedIds.Length || removed.Any(linkId => !savedIds.Contains(linkId)))
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.NotFound);
            }

            Guid[] merged = ResourceSetMembership.Merge(savedIds, request.LinkIds, removed);
            if (merged.Length > 100)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Limit);
            }
            Guid setId = create ? Guid.NewGuid() : id;
            ShiftStartSet next = new(setId, request.Name.Trim(), merged);
            ShiftStartSet[] sets = [.. current.Sets.Where(s => s.Id != setId).Append(next).OrderBy(s => s.Id.ToString("D"), StringComparer.Ordinal)];
            Guid? defaultId = request.IsDefault ? setId : current.DefaultSetId == setId ? null : current.DefaultSetId;
            return await SavePersonalAsync(current with { Sets = sets, DefaultSetId = defaultId }, request.ExpectedVersion, user, context, cancellationToken);
        }, cancellationToken);

    /// <summary>Deletes only an owned set; catalogue entries and audit history are never deleted.</summary>
    public Task<ResourceResult<ResourcePreferencesResponse>> DeleteSetAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        long expectedVersion, CancellationToken cancellationToken) => ExecuteAsync(principal, context, false, async user =>
        {
            ResourcePreferences current = await repository.PreferencesAsync(user.Id, cancellationToken);
            if (expectedVersion is < 0 or long.MaxValue)
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Invalid);
            }

            if (!current.Sets.Any(s => s.Id == id))
            {
                return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.NotFound);
            }

            return await SavePersonalAsync(current with
            {
                Sets = [.. current.Sets.Where(s => s.Id != id)],
                DefaultSetId = current.DefaultSetId == id ? null : current.DefaultSetId
            }, expectedVersion, user, context, cancellationToken);
        }, cancellationToken);

    private async Task<ResourceResult<ResourcePreferencesResponse>> SavePersonalAsync(ResourcePreferences next, long expectedVersion,
        ApplicationUser user, AccessOperationContext context, CancellationToken cancellationToken)
    {
        if (next.Version != expectedVersion)
        {
            return ResourceResult<ResourcePreferencesResponse>.Fail(ResourceErrors.Conflict);
        }
        ResourceResult<ResourcePreferences> saved = await repository.SavePreferencesAsync(next, expectedVersion, Actor(user, context), cancellationToken);
        return saved.IsSuccess ? new(await ProjectAsync(saved.Value!, user, cancellationToken)) : ResourceResult<ResourcePreferencesResponse>.Fail(saved.ErrorCode!);
    }

    private async Task<ResourcePreferencesResponse> ProjectAsync(ResourcePreferences value, ApplicationUser user, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. value.FavouriteIds.Concat(value.Sets.SelectMany(s => s.LinkIds)).Distinct()];
        IReadOnlyList<ResourceLink> links = await repository.ResolveAsync(ids, Manager(user), cancellationToken);
        var byId = links.ToDictionary(l => l.Id);
        ResourceLink[] Resolve(IEnumerable<Guid> references) => [.. references.Where(byId.ContainsKey).Select(id => byId[id])];
        return new(value.Version, Resolve(value.FavouriteIds), [.. value.Sets.Select(s => new ShiftSetResponse(s.Id, s.Name,
            Resolve(s.LinkIds), s.Id == value.DefaultSetId))], value.DefaultSetId, value.GuideDismissed,
            ResourceWorkspacePolicy.Project(value.WorkspaceLayout, user.Capabilities), value.AnnouncementGuideDismissed);
    }

    private async Task<ResourceResult<T>> ExecuteAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, bool manage,
        Func<ApplicationUser, Task<ResourceResult<T>>> operation, CancellationToken cancellationToken)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, cancellationToken);
            if (!current.IsSuccess)
            {
                return ResourceResult<T>.Fail(current.ErrorCode!);
            }

            ApplicationUser user = current.Value!.User;
            if (user.Id == Guid.Empty || user.Status != AccessStatus.Approved || !user.Capabilities.Contains(Capabilities.ResourcesView)
                || manage && !Manager(user))
            {
                return ResourceResult<T>.Fail("AccessDenied");
            }

            return await operation(user);
        }
        catch (Exception exception) when (exception is DbException or IOException or InvalidOperationException)
        {
            // Exception messages can contain SQL parameters or submitted content; log only the type.
            logger.LogError("Resource persistence failed. FailureType: {FailureType}", exception.GetType().Name);
            return ResourceResult<T>.Fail("PersistenceUnavailable");
        }
    }

    private static bool Manager(ApplicationUser user) => user.Capabilities.Contains(Capabilities.ResourcesManage, StringComparer.Ordinal);
    private static bool VersionValid(long version, bool create) => create ? version == 0 : version > 0;
    private static ResourceActor Actor(ApplicationUser user, AccessOperationContext context) => new(user.Id, context.CorrelationId);
}
