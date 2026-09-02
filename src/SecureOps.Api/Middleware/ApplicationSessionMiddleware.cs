using System.Diagnostics;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Sessions;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Api.Middleware;

/// <summary>Enforces authoritative server-side application sessions for authenticated requests.</summary>
public sealed class ApplicationSessionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes the middleware.</summary>
    public ApplicationSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Validates or creates the current application session.</summary>
    public async Task InvokeAsync(
        HttpContext httpContext,
        ApplicationSessionCookie cookie,
        IApplicationSessionService sessionService,
        ApplicationSessionContext sessionContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true
            || IsPersistenceIndependentHealthPath(httpContext.Request.Path))
        {
            await _next(httpContext);
            return;
        }

        ApplicationSessionCookieReadResult cookieResult = cookie.Read(httpContext.Request);
        if (cookieResult.Status == ApplicationSessionCookieStatus.Invalid)
        {
            cookie.Delete(httpContext.Response);
            await OperationalProblemDetails.WriteAsync(
                httpContext,
                StatusCodes.Status403Forbidden,
                OperationalErrorCodes.SessionRevoked,
                "The application session is invalid.",
                "session",
                false,
                httpContext.RequestAborted);
            return;
        }

        AccessOperationContext operationContext = new(
            httpContext.User.Identity?.Name ?? "unknown",
            Activity.Current?.Id ?? httpContext.TraceIdentifier,
            httpContext.Connection.RemoteIpAddress?.ToString());
        ApplicationSessionResult result = await sessionService.ValidateOrStartAsync(
            httpContext.User,
            cookieResult.SessionId,
            operationContext,
            httpContext.RequestAborted);

        if (result.Disposition is ApplicationSessionDisposition.Active or ApplicationSessionDisposition.Started)
        {
            sessionContext.Set(result.Session!);
            if (result.Disposition == ApplicationSessionDisposition.Started)
            {
                cookie.Write(httpContext.Response, result.Session!.SessionId);
            }

            await _next(httpContext);
            return;
        }

        cookie.Delete(httpContext.Response);
        bool unavailable = result.Disposition is ApplicationSessionDisposition.StoreUnavailable or ApplicationSessionDisposition.AuditUnavailable;
        await OperationalProblemDetails.WriteAsync(
            httpContext,
            unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden,
            result.ErrorCode ?? OperationalErrorCodes.SessionRevoked,
            unavailable ? "Application-session validation is unavailable." : "The application session is no longer valid.",
            unavailable ? "session-store" : "session",
            unavailable,
            httpContext.RequestAborted);
    }

    private static bool IsPersistenceIndependentHealthPath(PathString path) =>
        path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/v1/health/persistence", StringComparison.OrdinalIgnoreCase);
}
