using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SecureOps.Api.Middleware;

/// <summary>
/// Applies caller-provided or generated correlation IDs to requests.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    /// <summary>
    /// Correlation ID header name.
    /// </summary>
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes the middleware.
    /// </summary>
    /// <param name="next">Next middleware.</param>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    /// <returns>A task that completes when the request finishes.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("secureops.correlation_id", correlationId);
        context.Response.Headers[HeaderName] = correlationId;

        await _next(context);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        string? supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(supplied)
            && supplied.Length <= 100
            && CorrelationIdPattern().IsMatch(supplied))
        {
            return supplied;
        }

        return context.TraceIdentifier;
    }

    [GeneratedRegex("^[a-zA-Z0-9_.:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
