using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SecureOps.Api.Security;
using SecureOps.Api.Middleware;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Api.Controllers;

/// <summary>
/// Privileged identity lookup endpoints.
/// </summary>
[ApiController]
[Route("api/v1/identity")]
[Authorize]
public sealed class IdentityController : ControllerBase
{
    private static readonly string[] _returnedFields =
    [
        nameof(IdentityLookupUserDto.DisplayName),
        nameof(IdentityLookupUserDto.SamAccountName),
        nameof(IdentityLookupUserDto.UserPrincipalName),
        nameof(IdentityLookupUserDto.Mail),
        nameof(IdentityLookupUserDto.Department),
        nameof(IdentityLookupUserDto.Title),
        nameof(IdentityLookupUserDto.ManagerDisplayName),
        nameof(IdentityLookupUserDto.Enabled),
        nameof(IdentityLookupUserDto.Locked)
    ];

    private static readonly string[] _rejectedInputClasses =
    [
        "empty-account",
        "bulk-input",
        "wildcard-input",
        "ldap-filter-input",
        "over-max-length-input",
        "outside-allow-list-input"
    ];

    private readonly IIdentityLookupService _identityLookupService;
    private readonly IValidator<IdentityLookupRequest> _validator;
    private readonly IAuditWriter _auditWriter;
    private readonly IdentityLookupOptions _options;
    private readonly ILogger<IdentityController> _logger;

    /// <summary>
    /// Initializes a new identity controller.
    /// </summary>
    /// <param name="identityLookupService">Identity lookup service.</param>
    /// <param name="validator">Request validator.</param>
    /// <param name="auditWriter">Audit writer.</param>
    /// <param name="options">Identity lookup options.</param>
    /// <param name="logger">Logger.</param>
    public IdentityController(
        IIdentityLookupService identityLookupService,
        IValidator<IdentityLookupRequest> validator,
        IAuditWriter auditWriter,
        IOptions<IdentityLookupOptions> options,
        ILogger<IdentityController> logger)
    {
        _identityLookupService = identityLookupService;
        _validator = validator;
        _auditWriter = auditWriter;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns safe metadata about the current API caller.
    /// </summary>
    /// <param name="authorizationService">Authorization service.</param>
    /// <returns>Current caller metadata.</returns>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentIdentityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentIdentityResponse>> MeAsync(
        [FromServices] IAuthorizationService authorizationService)
    {
        AuthorizationResult authorization = await authorizationService.AuthorizeAsync(User, Policies.TeamLeadOrAbove);

        return Ok(new CurrentIdentityResponse(
            User.Identity?.Name,
            User.Identity?.IsAuthenticated == true,
            authorization.Succeeded));
    }

    /// <summary>
    /// Returns safe metadata about privileged identity lookup behavior.
    /// </summary>
    /// <returns>Lookup capabilities.</returns>
    [HttpGet("lookup/capabilities")]
    [Authorize(Policy = Policies.CanIdentityLookup)]
    [ProducesResponseType(typeof(IdentityLookupCapabilitiesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IdentityLookupCapabilitiesResponse> Capabilities()
    {
        return Ok(new IdentityLookupCapabilitiesResponse(
            _options.MaxAccountLength,
            _identityLookupService.SupportsUpnLookup,
            _returnedFields,
            _rejectedInputClasses,
            IdentityLookupRateLimits.Lookup));
    }

    /// <summary>
    /// Looks up one exact PAM account or AD username.
    /// </summary>
    /// <param name="request">Lookup request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lookup response.</returns>
    [HttpPost("lookup")]
    [Authorize(Policy = Policies.CanIdentityLookup)]
    [EnableRateLimiting(ApiRateLimits.IdentityLookup)]
    [ProducesResponseType(typeof(IdentityLookupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IdentityLookupResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IdentityLookupResponse>> LookupAsync(
        [FromBody] IdentityLookupRequest? request,
        CancellationToken cancellationToken)
    {
        string correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (request is null)
        {
            bool auditWritten = await TryWriteValidationRejectedAuditAsync(
                null,
                ["Body"],
                "InvalidRequestBody",
                correlationId,
                cancellationToken);

            return auditWritten
                ? OperationalProblemDetails.Create(StatusCodes.Status400BadRequest, "InvalidIdentityInput", "The request body is required.", correlationId, "validation", false)
                : AuditUnavailable(correlationId);
        }

        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            string[] rejectedFields = validation.Errors
                .Select(error => error.PropertyName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            bool auditWritten = await TryWriteValidationRejectedAuditAsync(
                request,
                rejectedFields,
                ResolveValidationErrorCode(rejectedFields),
                correlationId,
                cancellationToken);

            return auditWritten
                ? OperationalProblemDetails.Create(
                    StatusCodes.Status400BadRequest,
                    "InvalidIdentityInput",
                    "The identity lookup request was rejected.",
                    correlationId,
                    "validation",
                    false)
                : AuditUnavailable(correlationId);
        }

        IdentityLookupExecutionContext context = new(
            User.Identity?.Name ?? "unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            correlationId);

        IdentityLookupResult result = await _identityLookupService.LookupAsync(request, context, cancellationToken);

        return result.Status switch
        {
            IdentityLookupResultStatus.Found => Ok(result.Response),
            IdentityLookupResultStatus.NotFound => OperationalProblemDetails.Create(
                StatusCodes.Status404NotFound, "IdentityNotFound", "Identity was not found.", correlationId, "provider", false),
            IdentityLookupResultStatus.Invalid => OperationalProblemDetails.Create(
                StatusCodes.Status400BadRequest, "InvalidIdentityInput", "Identity input was rejected.", correlationId, "validation", false),
            _ => OperationalProblemDetails.Create(
                StatusCodes.Status503ServiceUnavailable,
                string.Equals(result.ErrorCode, "DirectoryProviderTimeout", StringComparison.Ordinal) ? "IdentityProviderTimeout" :
                string.Equals(result.ErrorCode, "AuditUnavailable", StringComparison.Ordinal) ? "AuditStoreUnavailable" : "IdentityProviderUnavailable",
                "The identity lookup could not be completed.", correlationId,
                string.Equals(result.ErrorCode, "AuditUnavailable", StringComparison.Ordinal) ? "audit" : "provider", true)
        };
    }

    /// <summary>Returns aggregate cache metrics without account labels or identity data.</summary>
    [HttpGet("lookup/cache-diagnostics")]
    [Authorize(Policy = Policies.CanSystemDiagnostics)]
    [ProducesResponseType(typeof(IdentityLookupCacheDiagnosticsResponse), StatusCodes.Status200OK)]
    public ActionResult<IdentityLookupCacheDiagnosticsResponse> CacheDiagnostics(
        [FromServices] IdentityReadThroughCache cache)
    {
        IdentityLookupCacheMetricsSnapshot snapshot = cache.GetSnapshot();
        return Ok(new IdentityLookupCacheDiagnosticsResponse(
            snapshot.CacheHits,
            snapshot.CacheMisses,
            snapshot.ProviderCalls,
            snapshot.CoalescedRequests,
            snapshot.CachedEntries,
            snapshot.InflightRequests,
            _options.Cache.Enabled,
            _options.Cache.TtlSeconds));
    }

    /// <summary>Looks up a bounded, ordered set of exact identity accounts.</summary>
    [HttpPost("bulk-lookup")]
    [Authorize(Policy = Policies.CanBulkIdentityLookup)]
    [EnableRateLimiting(ApiRateLimits.BulkIdentityLookup)]
    [ProducesResponseType(typeof(BulkIdentityLookupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BulkIdentityLookupResponse>> BulkLookupAsync(
        [FromBody] BulkIdentityLookupRequest? request,
        [FromServices] IIdentityAccountNormalizer normalizer,
        CancellationToken cancellationToken)
    {
        string correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        if (request?.Accounts is null || request.Accounts.Count == 0 || request.Accounts.Count > _options.BulkMaxAccounts || string.IsNullOrWhiteSpace(request.Purpose))
        {
            return OperationalProblemDetails.Create(StatusCodes.Status400BadRequest, "InvalidIdentityInput", "The bulk identity lookup request was rejected.", correlationId, "validation", false);
        }

        List<(string Input, string? Normalized, string? Error)> accounts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string account in request.Accounts)
        {
            IdentityAccountNormalizationResult normalized = normalizer.Normalize(account);
            if (!normalized.IsValid || normalized.NormalizedAccount is null)
            {
                accounts.Add((account, null, normalized.ErrorCode));
            }
            else if (seen.Add(normalized.NormalizedAccount))
            {
                accounts.Add((account, normalized.NormalizedAccount, null));
            }
        }

        if (!await TryWriteBulkAuditAsync(AuditActions.BulkIdentityLookupRequested, request, correlationId, accounts.Count, null, cancellationToken))
        {
            return AuditUnavailable(correlationId);
        }

        IdentityLookupExecutionContext context = new(User.Identity?.Name ?? "unknown", HttpContext.Connection.RemoteIpAddress?.ToString(), correlationId);
        List<BulkIdentityLookupItemResponse> results = [];
        foreach ((string input, string? normalized, string? error) in accounts)
        {
            if (normalized is null)
            {
                results.Add(new BulkIdentityLookupItemResponse(input, "Rejected", null, "InvalidIdentityInput"));
                continue;
            }

            IdentityLookupResult result = await _identityLookupService.LookupAsync(
                new IdentityLookupRequest(normalized, request.Purpose, request.AlertId, request.TuruncuhatEvtId), context, cancellationToken);
            results.Add(result.Status switch
            {
                IdentityLookupResultStatus.Found => new BulkIdentityLookupItemResponse(normalized, "Found", result.Response),
                IdentityLookupResultStatus.NotFound => new BulkIdentityLookupItemResponse(normalized, "NotFound", null),
                IdentityLookupResultStatus.Invalid => new BulkIdentityLookupItemResponse(normalized, "Rejected", null, "InvalidIdentityInput"),
                _ => new BulkIdentityLookupItemResponse(normalized, "ProviderError", null,
                    string.Equals(result.ErrorCode, "DirectoryProviderTimeout", StringComparison.Ordinal) ? "IdentityProviderTimeout" : "IdentityProviderUnavailable")
            });
        }

        _ = await TryWriteBulkAuditAsync(AuditActions.BulkIdentityLookupCompleted, request, correlationId, results.Count,
            new { found = results.Count(result => result.Status == "Found"), notFound = results.Count(result => result.Status == "NotFound"), rejected = results.Count(result => result.Status == "Rejected"), providerError = results.Count(result => result.Status == "ProviderError") }, cancellationToken);
        return Ok(new BulkIdentityLookupResponse(results));
    }

    private async Task<bool> TryWriteValidationRejectedAuditAsync(
        IdentityLookupRequest? request,
        IReadOnlyCollection<string> rejectedFields,
        string errorCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditWriter.WriteAsync(
                new AuditEvent
                {
                    Actor = User.Identity?.Name ?? "unknown",
                    Action = AuditActions.IdentityLookupRejected,
                    AlertId = request?.AlertId,
                    CorrelationId = correlationId,
                    SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Details = new
                    {
                        normalizedAccount = (string?)null,
                        accountProvided = !string.IsNullOrWhiteSpace(request?.Account),
                        accountLength = request?.Account?.Trim().Length,
                        accountInputHash = AuditAccountHasher.HashAccountInput(request?.Account),
                        purpose = request?.Purpose,
                        turuncuhatEvtId = request?.TuruncuhatEvtId,
                        resultStatus = "Rejected",
                        errorCode,
                        rejectedFields
                    }
                },
                cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Identity lookup validation audit failed. CorrelationId: {CorrelationId}",
                correlationId);
            return false;
        }
    }

    private async Task<bool> TryWriteBulkAuditAsync(string action, BulkIdentityLookupRequest request, string correlationId, int accountCount, object? resultSummary, CancellationToken cancellationToken)
    {
        try
        {
            await _auditWriter.WriteAsync(new AuditEvent
            {
                Actor = User.Identity?.Name ?? "unknown",
                Action = action,
                AlertId = request.AlertId,
                CorrelationId = correlationId,
                SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Details = new { accountCount, request.Purpose, request.TuruncuhatEvtId, resultSummary }
            }, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Bulk identity lookup audit failed. CorrelationId: {CorrelationId}", correlationId);
            return false;
        }
    }

    private static ObjectResult AuditUnavailable(string correlationId)
    {
        return OperationalProblemDetails.Create(StatusCodes.Status503ServiceUnavailable, "AuditStoreUnavailable", "Audit storage is unavailable.", correlationId, "audit", true);
    }

    private static string ResolveValidationErrorCode(IReadOnlyCollection<string> rejectedFields)
    {
        if (rejectedFields.Contains(nameof(IdentityLookupRequest.Purpose), StringComparer.Ordinal))
        {
            return "PurposeRequired";
        }

        if (rejectedFields.Contains(nameof(IdentityLookupRequest.Account), StringComparer.Ordinal))
        {
            return "EmptyAccount";
        }

        return "InvalidIdentityLookupRequest";
    }
}
