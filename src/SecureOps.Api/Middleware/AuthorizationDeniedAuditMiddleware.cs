using System.Diagnostics;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;

namespace SecureOps.Api.Middleware;

/// <summary>
/// Writes an audit entry when API authorization denies a request.
/// </summary>
public sealed class AuthorizationDeniedAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthorizationDeniedAuditMiddleware> _logger;

    /// <summary>
    /// Initializes the middleware.
    /// </summary>
    /// <param name="next">Next middleware.</param>
    /// <param name="logger">Logger.</param>
    public AuthorizationDeniedAuditMiddleware(RequestDelegate next, ILogger<AuthorizationDeniedAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    /// <param name="auditWriter">Audit writer.</param>
    /// <returns>A task that completes when the request has finished.</returns>
    public async Task InvokeAsync(HttpContext context, IAuditWriter auditWriter)
    {
        await _next(context);

        if (!context.Items.ContainsKey(ApiCsrfMiddleware.RejectionItem)
            && context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            string correlationId = Activity.Current?.Id ?? context.TraceIdentifier;

            try
            {
                await auditWriter.WriteAsync(
                    new AuditEvent
                    {
                        Actor = context.User.Identity?.Name ?? "anonymous",
                        Action = DeniedAction(context),
                        CorrelationId = correlationId,
                        SourceIp = context.Connection.RemoteIpAddress?.ToString(),
                        Details = new
                        {
                            endpoint = context.Request.Path.Value,
                            method = context.Request.Method,
                            statusCode = context.Response.StatusCode,
                            resultStatus = IsPrivilegedDirectoryEndpoint(context) ? "Forbidden" : "Denied"
                        }
                    },
                    context.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Failed to write authorization-denied audit event. CorrelationId: {CorrelationId}",
                    correlationId);
            }
        }
    }

    private static bool IsIdentityLookupEndpoint(HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method)
            && (context.Request.Path.Equals("/api/v1/identity/lookup", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.Equals("/api/v1/identity/bulk-lookup", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDirectoryExplorerEndpoint(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.StartsWithSegments("/api/v1/directory", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivilegedDirectoryEndpoint(HttpContext context) =>
        IsIdentityLookupEndpoint(context) || IsDirectoryExplorerEndpoint(context);

    private static string DeniedAction(HttpContext context) => IsDirectoryExplorerEndpoint(context)
        ? AuditActions.DirectoryGroupQueryForbidden
        : IsIdentityLookupEndpoint(context) ? AuditActions.IdentityLookupForbidden : AuditActions.AuthorizationDenied;
}
