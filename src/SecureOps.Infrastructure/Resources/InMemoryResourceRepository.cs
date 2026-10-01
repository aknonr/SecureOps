using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Infrastructure.Resources;

/// <summary>Explicit local/test substitute. Serializes changes and fails closed before assignment on audit failure.</summary>
public sealed class InMemoryResourceRepository(IAuditWriter auditWriter) : IResourceRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, ResourceCategory> _categories = [];
    private readonly Dictionary<Guid, ResourceLink> _links = [];
    private readonly Dictionary<Guid, ResourcePreferences> _preferences = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<ResourceCategory>> CategoriesAsync(bool manager, bool includeArchived, CancellationToken cancellationToken) => LockedAsync<IReadOnlyList<ResourceCategory>>(() => Task.FromResult<IReadOnlyList<ResourceCategory>>(
        [.. _categories.Values.Where(c => (manager || !c.ManagersOnly) && (manager && includeArchived || !c.Archived))
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id.ToString("D"), StringComparer.Ordinal).Take(200)]), cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePage> QueryAsync(ResourceQuery query, bool manager, CancellationToken cancellationToken) => LockedAsync(() =>
    {
        ResourceLink[] matches = [.. Visible(manager, query.IncludeArchived)
            .Where(l => query.CategoryId is null || l.CategoryId == query.CategoryId)
            .Where(l => Match(l.Environment, query.Environment) && Match(l.Location, query.Location))
            .Where(l => string.IsNullOrWhiteSpace(query.Tag) || l.Tags.Contains(query.Tag.Trim(), StringComparer.OrdinalIgnoreCase))
            .Where(l => string.IsNullOrWhiteSpace(query.Search) || l.Name.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)
                || l.Purpose.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)
                || l.Tags.Any(tag => tag.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(l => _categories[l.CategoryId].DisplayOrder).ThenBy(l => l.DisplayOrder)
            .ThenBy(l => l.Id.ToString("D"), StringComparer.Ordinal)];
        return Task.FromResult(new ResourcePage([.. matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], query.Page, query.PageSize, matches.Length));
    }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResourceLink>> ResolveAsync(IReadOnlyCollection<Guid> ids, bool manager, CancellationToken cancellationToken) => LockedAsync<IReadOnlyList<ResourceLink>>(() =>
        Task.FromResult<IReadOnlyList<ResourceLink>>([.. Visible(manager, false).Where(l => ids.Contains(l.Id))]), cancellationToken);

    /// <inheritdoc />
    public Task<ResourceLink?> GetAsync(Guid id, bool manager, bool includeArchived, CancellationToken cancellationToken) => LockedAsync(() =>
        Task.FromResult(Visible(manager, includeArchived).FirstOrDefault(l => l.Id == id)), cancellationToken);

    /// <inheritdoc />
    public Task<ResourceResult<ResourceCategory>> SaveCategoryAsync(Guid id, SaveResourceCategoryRequest request, ResourceActor actor, CancellationToken cancellationToken) => LockedAsync(async () =>
    {
        _categories.TryGetValue(id, out ResourceCategory? current);
        if ((current?.Version ?? 0) != request.ExpectedVersion)
        {
            return ResourceResult<ResourceCategory>.Fail(ResourceErrors.Conflict);
        }

        if (current is null && _categories.Count >= 200)
        {
            return ResourceResult<ResourceCategory>.Fail(ResourceErrors.Limit);
        }

        ResourceCategory next = new(id, request.Name.Trim(), request.DisplayOrder, request.ManagersOnly, request.Archived, request.ExpectedVersion + 1, DateTimeOffset.UtcNow);
        await auditWriter.WriteAsync(ResourceAudit.Change("Category", id, next.Version, actor, next.UpdatedAt, next.Archived), cancellationToken);
        _categories[id] = next;
        return new ResourceResult<ResourceCategory>(next);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<ResourceResult<ResourceLink>> SaveLinkAsync(Guid id, SaveResourceLinkRequest request, ResourceActor actor, CancellationToken cancellationToken) => LockedAsync(async () =>
    {
        _links.TryGetValue(id, out ResourceLink? current);
        if ((current?.Version ?? 0) != request.ExpectedVersion)
        {
            return ResourceResult<ResourceLink>.Fail(ResourceErrors.Conflict);
        }

        if (!_categories.TryGetValue(request.CategoryId, out ResourceCategory? category) || category.Archived)
        {
            return ResourceResult<ResourceLink>.Fail(ResourceErrors.NotFound);
        }

        ResourceLink next = new(id, request.CategoryId, request.Name.Trim(), request.Url, request.Purpose.Trim(), request.Notes?.Trim(),
            request.Environment?.Trim(), request.Location?.Trim(), [.. (request.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Order(StringComparer.Ordinal)],
            request.DisplayOrder, request.Active, request.Archived, request.ExpectedVersion + 1, DateTimeOffset.UtcNow);
        await auditWriter.WriteAsync(ResourceAudit.Change("Link", id, next.Version, actor, next.UpdatedAt, next.Archived, next.Active), cancellationToken);
        _links[id] = next;
        return new ResourceResult<ResourceLink>(next);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<ResourcePreferences> PreferencesAsync(Guid userId, CancellationToken cancellationToken) => LockedAsync(() =>
        Task.FromResult(Copy(_preferences.GetValueOrDefault(userId, ResourcePreferences.Empty))), cancellationToken);

    /// <inheritdoc />
    public Task<ResourceResult<ResourcePreferences>> SavePreferencesAsync(ResourcePreferences preferences, long expectedVersion, ResourceActor actor, CancellationToken cancellationToken) => LockedAsync(async () =>
    {
        ResourcePreferences current = _preferences.GetValueOrDefault(actor.UserId, ResourcePreferences.Empty);
        if (current.Version != expectedVersion)
        {
            return ResourceResult<ResourcePreferences>.Fail(ResourceErrors.Conflict);
        }

        ResourcePreferences next = Copy(preferences with { Version = expectedVersion + 1 });
        await auditWriter.WriteAsync(ResourceAudit.Change("Preferences", actor.UserId, next.Version, actor, DateTimeOffset.UtcNow), cancellationToken);
        _preferences[actor.UserId] = next;
        return new ResourceResult<ResourcePreferences>(Copy(next));
    }, cancellationToken);

    private IEnumerable<ResourceLink> Visible(bool manager, bool archived) => _links.Values.Where(l =>
        _categories.TryGetValue(l.CategoryId, out ResourceCategory? category) && (manager || !category.ManagersOnly)
        && (manager && archived || l.Active && !l.Archived && !category.Archived));

    /// <inheritdoc />
    public Task<ResourceEnvironmentOptions> EnvironmentsAsync(ResourceEnvironmentQuery query, bool manager, CancellationToken cancellationToken) => LockedAsync(() =>
    {
        string[] values = [.. Visible(manager, query.IncludeArchived)
            .Where(l => query.CategoryId is null || l.CategoryId == query.CategoryId)
            .Select(l => l.Environment).OfType<string>()
            .Where(e => !string.IsNullOrWhiteSpace(e) && (string.IsNullOrWhiteSpace(query.Search)
                || e.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(101)];
        return Task.FromResult(new ResourceEnvironmentOptions([.. values.Take(100)], values.Length > 100));
    }, cancellationToken);

    private static bool Match(string? actual, string? filter) => string.IsNullOrWhiteSpace(filter)
        || string.Equals(actual, filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static ResourcePreferences Copy(ResourcePreferences value) => value with
    { FavouriteIds = [.. value.FavouriteIds], Sets = [.. value.Sets.Select(s => s with { LinkIds = [.. s.LinkIds] })] };

    private async Task<T> LockedAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        { return await operation(); }
        finally { _gate.Release(); }
    }
}
