using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Infrastructure.Resources;

/// <summary>Safe business result; failures never contain submitted content.</summary>
public sealed record ResourceResult<T>(T? Value, string? ErrorCode = null)
{
    /// <summary>Whether a value is available.</summary>
    public bool IsSuccess => ErrorCode is null;
    /// <summary>Creates a safe failure.</summary>
    public static ResourceResult<T> Fail(string code) => new(default, code);
}

/// <summary>Stable catalogue business error identifiers.</summary>
public static class ResourceErrors
{
    /// <summary>Input failed the documented bounds or policy.</summary>
    public const string Invalid = "ResourceValidationFailed";
    /// <summary>Not found or not visible; intentionally indistinguishable.</summary>
    public const string NotFound = "ResourceNotFound";
    /// <summary>The submitted version is stale.</summary>
    public const string Conflict = "ResourceConcurrencyConflict";
    /// <summary>An aggregate capacity was reached.</summary>
    public const string Limit = "ResourceLimitExceeded";
}

/// <summary>Internal audit context contains opaque user ID, never a profile or submitted content.</summary>
public sealed record ResourceActor(Guid UserId, string CorrelationId);

/// <summary>Durable resource persistence with atomic compare-and-write and audit.</summary>
public interface IResourceRepository
{
    /// <summary>Lists bounded categories under inherited visibility rules.</summary>
    public Task<IReadOnlyList<ResourceCategory>> CategoriesAsync(bool manager, bool includeArchived, CancellationToken cancellationToken);
    /// <summary>Queries permitted links with stable order and bounded pagination.</summary>
    public Task<ResourcePage> QueryAsync(ResourceQuery query, bool manager, CancellationToken cancellationToken);
    /// <summary>Resolves only currently active visible references; missing IDs are omitted.</summary>
    public Task<IReadOnlyList<ResourceLink>> ResolveAsync(IReadOnlyCollection<Guid> ids, bool manager, CancellationToken cancellationToken);
    /// <summary>Reads a detail under the same visibility rules.</summary>
    public Task<ResourceLink?> GetAsync(Guid id, bool manager, bool includeArchived, CancellationToken cancellationToken);
    /// <summary>Persists a category and shared-change audit in one transaction.</summary>
    public Task<ResourceResult<ResourceCategory>> SaveCategoryAsync(Guid id, SaveResourceCategoryRequest request, ResourceActor actor, CancellationToken cancellationToken);
    /// <summary>Persists a link and shared-change audit in one transaction.</summary>
    public Task<ResourceResult<ResourceLink>> SaveLinkAsync(Guid id, SaveResourceLinkRequest request, ResourceActor actor, CancellationToken cancellationToken);
    /// <summary>Reads only the specified internal owner's personal aggregate.</summary>
    public Task<ResourcePreferences> PreferencesAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Atomically replaces only the specified owner's personal aggregate and audits safe metadata.</summary>
    public Task<ResourceResult<ResourcePreferences>> SavePreferencesAsync(ResourcePreferences preferences, long expectedVersion, ResourceActor actor, CancellationToken cancellationToken);
}
