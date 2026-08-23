using Microsoft.AspNetCore.RateLimiting;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;

namespace SecureOps.Api.Security;

/// <summary>Writes privacy-safe evidence for rejected Directory Explorer requests.</summary>
internal static class DirectoryRateLimitAudit
{
    private static readonly HashSet<string> _policies =
    [
        ApiRateLimits.DirectoryGroupQuery,
        ApiRateLimits.DirectoryGroupMembers,
        ApiRateLimits.DirectoryEnrichment,
        ApiRateLimits.DirectoryPrivilegedGroups
    ];

    /// <summary>Writes a best-effort event after the request has already been safely rejected.</summary>
    public static async Task TryWriteAsync(HttpContext context, CancellationToken cancellationToken)
    {
        string? policy = context.GetEndpoint()?.Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy is null || !_policies.Contains(policy))
        {
            return;
        }

        try
        {
            IAuditWriter writer = context.RequestServices.GetRequiredService<IAuditWriter>();
            await writer.WriteAsync(new AuditEvent
            {
                Actor = context.User.Identity?.Name ?? "unknown",
                Action = AuditActions.DirectoryGroupQueryRateLimited,
                CorrelationId = context.TraceIdentifier,
                SourceIp = context.Connection.RemoteIpAddress?.ToString(),
                Details = new
                {
                    operation = policy,
                    outcome = "RateLimited",
                    durationMs = 0,
                    providerInvoked = false
                }
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("SecureOps.DirectoryRateLimitAudit");
            logger.LogError(exception, "Directory rate-limit audit failed. CorrelationId: {CorrelationId}", context.TraceIdentifier);
        }
    }
}
