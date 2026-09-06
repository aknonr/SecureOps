using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Api.Middleware;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Api.Controllers;

/// <summary>Shared catalogue and caller-owned favourites/shift-start sets. Never opens target URLs.</summary>
[ApiController]
[Route("api/v1/resources")]
[Authorize(Policy = Policies.CanViewResources)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ResourcesController(ResourceCatalogueService service) : ControllerBase
{
    /// <summary>Looks up at most 100 permitted environment values independently of link pagination.</summary>
    [HttpGet("environments")]
    [ProducesResponseType(typeof(ResourceEnvironmentOptions), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourceEnvironmentOptions>> EnvironmentsAsync([FromQuery] ResourceEnvironmentQuery query, CancellationToken cancellationToken) =>
        Reply(await service.EnvironmentsAsync(User, Context(), query, cancellationToken));

    /// <summary>Dismisses only the caller's first-use resource guide invitation.</summary>
    [HttpPut("me/guide")]
    [ProducesResponseType(typeof(ResourcePreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourcePreferencesResponse>> DismissGuideAsync(DismissResourceGuideRequest request, CancellationToken cancellationToken) =>
        Reply(await service.DismissGuideAsync(User, Context(), request, cancellationToken));

    /// <summary>Lists permitted categories; includeArchived is manager-only.</summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<ResourceCategory>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ResourceCategory>>> CategoriesAsync([FromQuery] bool includeArchived, CancellationToken cancellationToken) =>
        Reply(await service.CategoriesAsync(User, Context(), includeArchived, cancellationToken));

    /// <summary>Creates a category; no runtime deployment is needed.</summary>
    [HttpPost("categories")]
    [Authorize(Policy = Policies.CanManageResources)]
    [ProducesResponseType(typeof(ResourceCategory), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourceCategory>> CreateCategoryAsync(SaveResourceCategoryRequest request, CancellationToken cancellationToken) =>
        Reply(await service.SaveCategoryAsync(User, Context(), Guid.Empty, request, true, cancellationToken));

    /// <summary>Replaces category fields, ordering, visibility or archive state at an expected version.</summary>
    [HttpPut("categories/{id:guid}")]
    [Authorize(Policy = Policies.CanManageResources)]
    [ProducesResponseType(typeof(ResourceCategory), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourceCategory>> SaveCategoryAsync(Guid id, SaveResourceCategoryRequest request, CancellationToken cancellationToken) =>
        Reply(await service.SaveCategoryAsync(User, Context(), id, request, false, cancellationToken));

    /// <summary>Searches permitted links with stable ordering and bounded pagination.</summary>
    [HttpGet("links")]
    [ProducesResponseType(typeof(ResourcePage), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourcePage>> LinksAsync([FromQuery] ResourceQuery query, CancellationToken cancellationToken) =>
        Reply(await service.QueryAsync(User, Context(), query, cancellationToken));

    /// <summary>Reads a permitted link; absent and inaccessible IDs are indistinguishable.</summary>
    [HttpGet("links/{id:guid}")]
    [ProducesResponseType(typeof(ResourceLink), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourceLink>> LinkAsync(Guid id, [FromQuery] bool includeArchived, CancellationToken cancellationToken) =>
        Reply(await service.GetAsync(User, Context(), id, includeArchived, cancellationToken));

    /// <summary>Creates a validated HTTPS link without contacting its destination.</summary>
    [HttpPost("links")]
    [Authorize(Policy = Policies.CanManageResources)]
    [ProducesResponseType(typeof(ResourceLink), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourceLink>> CreateLinkAsync(SaveResourceLinkRequest request, CancellationToken cancellationToken) =>
        Reply(await service.SaveLinkAsync(User, Context(), Guid.Empty, request, true, cancellationToken));

    /// <summary>Replaces a link or archives it; audit history is retained.</summary>
    [HttpPut("links/{id:guid}")]
    [Authorize(Policy = Policies.CanManageResources)]
    [ProducesResponseType(typeof(ResourceLink), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourceLink>> SaveLinkAsync(Guid id, SaveResourceLinkRequest request, CancellationToken cancellationToken) =>
        Reply(await service.SaveLinkAsync(User, Context(), id, request, false, cancellationToken));

    /// <summary>Reads only the current user's favourites, sets and personal concurrency version.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ResourcePreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourcePreferencesResponse>> PreferencesAsync(CancellationToken cancellationToken) =>
        Reply(await service.PreferencesAsync(User, Context(), cancellationToken));

    /// <summary>Adds or removes an owned favourite using the personal aggregate version.</summary>
    [HttpPut("me/favourites/{id:guid}")]
    [ProducesResponseType(typeof(ResourcePreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourcePreferencesResponse>> FavouriteAsync(Guid id, SaveFavouriteRequest request, CancellationToken cancellationToken) =>
        Reply(await service.FavouriteAsync(User, Context(), id, request, cancellationToken));

    /// <summary>Creates a private ordered set with optional default selection.</summary>
    [HttpPost("me/sets")]
    [ProducesResponseType(typeof(ResourcePreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourcePreferencesResponse>> CreateSetAsync(SaveShiftSetRequest request, CancellationToken cancellationToken) =>
        Reply(await service.SaveSetAsync(User, Context(), Guid.Empty, request, true, cancellationToken));

    /// <summary>Merges an owned set atomically, retaining omitted references; cross-user IDs return not found.</summary>
    [HttpPut("me/sets/{id:guid}")]
    [ProducesResponseType(typeof(ResourcePreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourcePreferencesResponse>> SaveSetAsync(Guid id, SaveShiftSetRequest request, CancellationToken cancellationToken) =>
        Reply(await service.SaveSetAsync(User, Context(), id, request, false, cancellationToken));

    /// <summary>Removes an owned set and clears its default selection if necessary.</summary>
    [HttpDelete("me/sets/{id:guid}")]
    [ProducesResponseType(typeof(ResourcePreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourcePreferencesResponse>> DeleteSetAsync(Guid id, [FromQuery] long expectedVersion, CancellationToken cancellationToken) =>
        Reply(await service.DeleteSetAsync(User, Context(), id, expectedVersion, cancellationToken));

    /// <summary>Resolves a private set to currently permitted active links in saved order.</summary>
    [HttpGet("me/sets/{id:guid}/resolve")]
    [ProducesResponseType(typeof(ShiftSetResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShiftSetResponse>> ResolveSetAsync(Guid id, CancellationToken cancellationToken) =>
        Reply(await service.ResolveSetAsync(User, Context(), id, cancellationToken));

    private AccessOperationContext Context() => new("resource-caller", HttpContext.TraceIdentifier, null);

    private ActionResult<T> Reply<T>(ResourceResult<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        (int status, string stage, bool retryable) = result.ErrorCode switch
        {
            ResourceErrors.Invalid or ResourceErrors.Limit => (400, "validation", false),
            ResourceErrors.NotFound => (404, "resources", false),
            ResourceErrors.Conflict => (409, "concurrency", true),
            "AccessDenied" or "AccessPending" or "AccessDisabled" => (403, "authorization", false),
            "AuditStoreUnavailable" => (503, "audit", true),
            _ => (503, "persistence", true)
        };
        return OperationalProblemDetails.Create(status, result.ErrorCode!, "The resource operation could not be completed.",
            HttpContext.TraceIdentifier, stage, retryable);
    }
}
