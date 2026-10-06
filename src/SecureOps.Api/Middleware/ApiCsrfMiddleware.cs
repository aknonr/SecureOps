using System.Diagnostics;
using Microsoft.AspNetCore.Routing;
using SecureOps.Api.Security;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Api.Middleware;

/// <summary>Rejects unsafe API requests after authorization and before body consumption.</summary>
public sealed class ApiCsrfMiddleware(RequestDelegate next, ApiCsrfPolicy policy, ILogger<ApiCsrfMiddleware> logger)
{
    /// <summary>Marks a denial already handled and audited by this guard.</summary>
    public const string RejectionItem = "SecureOps.ApiCsrfRejected";

    /// <summary>Checks request intent and writes privacy-safe denial evidence without reading the body.</summary>
    public async Task InvokeAsync(HttpContext context, IAuditWriter auditWriter)
    {
        string? reason = policy.RejectionReason(context.Request);
        if (reason is null)
        {
            await next(context);
            return;
        }

        context.Items[RejectionItem] = true;
        string correlationId = Activity.Current?.Id ?? context.TraceIdentifier;
        try
        {
            await auditWriter.WriteAsync(new AuditEvent
            {
                Actor = context.User.Identity?.Name ?? "anonymous",
                Action = AuditActions.ApiCsrfRejected,
                CorrelationId = correlationId,
                Details = new
                {
                    route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)",
                    method = context.Request.Method,
                    reason,
                    resultStatus = "Rejected"
                }
            }, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("CSRF denial audit unavailable. CorrelationId: {CorrelationId}", correlationId);
            await OperationalProblemDetails.WriteAsync(context, StatusCodes.Status503ServiceUnavailable,
                OperationalErrorCodes.AuditStoreUnavailable, "Audit storage is unavailable.", "audit", true, context.RequestAborted);
            return;
        }

        await OperationalProblemDetails.WriteAsync(context, StatusCodes.Status403Forbidden,
            OperationalErrorCodes.ApiCsrfRejected, "The request intent or source origin was rejected.", "csrf", false, context.RequestAborted);
    }
}
