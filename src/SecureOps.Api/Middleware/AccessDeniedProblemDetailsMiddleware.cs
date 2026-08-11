namespace SecureOps.Api.Middleware;

/// <summary>Normalizes authentication and authorization denials to safe operational errors.</summary>
public sealed class AccessDeniedProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes the middleware.</summary>
    public AccessDeniedProblemDetailsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Invokes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);
        if (!context.Response.HasStarted && context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            int status = context.Response.StatusCode;
            context.Response.Clear();
            await OperationalProblemDetails.WriteAsync(context, status, "AccessDenied", "Access is denied.", "authorization", false, context.RequestAborted);
        }
    }
}
