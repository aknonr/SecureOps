using Microsoft.AspNetCore.Authorization;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Api.Security;

/// <summary>Evaluates application status and capability independently of authentication source.</summary>
public sealed class CapabilityAuthorizationHandler : AuthorizationHandler<CapabilityRequirement>
{
    /// <summary>HTTP item containing the safe application-access denial code.</summary>
    public const string DenialCodeItem = "SecureOps.AccessDenialCode";

    private readonly IApplicationAccessService _accessService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes the authorization handler.</summary>
    public CapabilityAuthorizationHandler(IApplicationAccessService accessService, IHttpContextAccessor httpContextAccessor)
    {
        _accessService = accessService;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CapabilityRequirement requirement)
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        string correlationId = System.Diagnostics.Activity.Current?.Id ?? httpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        AccessOperationContext operationContext = new(
            context.User.Identity?.Name ?? "unknown",
            correlationId,
            httpContext?.Connection.RemoteIpAddress?.ToString());
        AccessServiceResult<EnsureAccessUserResult> access = await _accessService.GetCurrentAsync(context.User, operationContext, httpContext?.RequestAborted ?? CancellationToken.None);
        if (!access.IsSuccess)
        {
            SetDenial(httpContext, access.ErrorCode ?? OperationalErrorCodes.AccessDenied);
            return;
        }

        ApplicationUser user = access.Value!.User;
        if (user.Status == AccessStatus.Pending)
        {
            SetDenial(httpContext, OperationalErrorCodes.AccessPending);
            return;
        }

        if (user.Status == AccessStatus.Disabled)
        {
            SetDenial(httpContext, OperationalErrorCodes.AccessDisabled);
            return;
        }

        if (user.Capabilities.Contains(requirement.Capability, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }
        else
        {
            SetDenial(httpContext, OperationalErrorCodes.AccessDenied);
        }
    }

    private static void SetDenial(HttpContext? context, string code)
    {
        if (context is not null)
        {
            context.Items[DenialCodeItem] = code;
        }
    }
}
