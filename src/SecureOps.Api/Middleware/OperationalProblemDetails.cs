using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SecureOps.Api.Middleware;

/// <summary>Creates the versioned safe operational error representation.</summary>
public static class OperationalProblemDetails
{
    /// <summary>Creates a safe RFC ProblemDetails result.</summary>
    public static ObjectResult Create(int status, string code, string title, string correlationId, string stage, bool retryable)
    {
        ProblemDetails details = new()
        {
            Status = status,
            Title = title,
            Type = $"https://secureops.internal/errors/{code}",
            Instance = correlationId
        };
        details.Extensions["code"] = code;
        details.Extensions["correlationId"] = correlationId;
        details.Extensions["traceId"] = correlationId;
        details.Extensions["stage"] = stage;
        details.Extensions["retryable"] = retryable;
        return new ObjectResult(details) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    /// <summary>Writes a safe RFC ProblemDetails response.</summary>
    public static Task WriteAsync(HttpContext context, int status, string code, string title, string stage, bool retryable, CancellationToken cancellationToken)
    {
        string correlationId = Activity.Current?.Id ?? context.TraceIdentifier;
        ObjectResult result = Create(status, code, title, correlationId, stage, retryable);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync((ProblemDetails)result.Value!, cancellationToken);
    }
}
