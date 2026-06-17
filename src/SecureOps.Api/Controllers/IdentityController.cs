using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SecureOps.Api.Security;
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
    [Authorize(Policy = Policies.TeamLeadOrAbove)]
    [ProducesResponseType(typeof(IdentityLookupCapabilitiesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IdentityLookupCapabilitiesResponse> Capabilities()
    {
        return Ok(new IdentityLookupCapabilitiesResponse(
            _options.MaxAccountLength,
            _options.EnableUpnLookup,
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
    [Authorize(Policy = Policies.TeamLeadOrAbove)]
    [EnableRateLimiting(IdentityLookupRateLimits.Lookup)]
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
                correlationId,
                cancellationToken);

            return auditWritten ? RejectedRequest() : AuditUnavailable();
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
                correlationId,
                cancellationToken);

            return auditWritten ? RejectedRequest() : AuditUnavailable();
        }

        IdentityLookupExecutionContext context = new(
            User.Identity?.Name ?? "unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            correlationId);

        IdentityLookupResult result = await _identityLookupService.LookupAsync(request, context, cancellationToken);

        return result.Status switch
        {
            IdentityLookupResultStatus.Found => Ok(result.Response),
            IdentityLookupResultStatus.NotFound => NotFound(result.Response),
            IdentityLookupResultStatus.Invalid => BadRequest(new ProblemDetails
            {
                Title = "Identity lookup request was rejected.",
                Detail = "The request could not be accepted."
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Identity lookup unavailable.",
                Detail = "The lookup could not be completed."
            })
        };
    }

    private async Task<bool> TryWriteValidationRejectedAuditAsync(
        IdentityLookupRequest? request,
        IReadOnlyCollection<string> rejectedFields,
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
                        purpose = request?.Purpose,
                        turuncuhatEvtId = request?.TuruncuhatEvtId,
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

    private static BadRequestObjectResult RejectedRequest()
    {
        return new BadRequestObjectResult(new ProblemDetails
        {
            Title = "Identity lookup request was rejected.",
            Detail = "The request could not be accepted."
        });
    }

    private static ObjectResult AuditUnavailable()
    {
        return new ObjectResult(new ProblemDetails
        {
            Title = "Identity lookup unavailable.",
            Detail = "The lookup could not be completed."
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
    }
}
