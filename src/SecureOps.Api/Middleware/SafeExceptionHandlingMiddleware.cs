using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Identity;

namespace SecureOps.Api.Middleware;

/// <summary>Converts unhandled operational failures to safe RFC ProblemDetails responses.</summary>
public sealed class SafeExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SafeExceptionHandlingMiddleware> _logger;

    /// <summary>Initializes the middleware.</summary>
    public SafeExceptionHandlingMiddleware(RequestDelegate next, ILogger<SafeExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Invokes the error boundary.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            (int status, string code, string title, string stage, bool retryable) = exception switch
            {
                IdentityProviderInputRejectedException => (StatusCodes.Status400BadRequest, "InvalidIdentityInput", "Identity input was rejected.", "validation", false),
                TimeoutException => (StatusCodes.Status503ServiceUnavailable, "IdentityProviderTimeout", "Identity provider timed out.", "provider", true),
                AuditWriteUnavailableException => (StatusCodes.Status503ServiceUnavailable, "AuditStoreUnavailable", "Audit storage is unavailable.", "audit", true),
                _ => (StatusCodes.Status503ServiceUnavailable, "IdentityProviderUnavailable", "The requested operation is currently unavailable.", "operation", true)
            };

            _logger.LogError(exception, "Unhandled operational failure. Code: {Code}. TraceId: {TraceId}", code, context.TraceIdentifier);
            await OperationalProblemDetails.WriteAsync(context, status, code, title, stage, retryable, context.RequestAborted);
        }
    }
}
