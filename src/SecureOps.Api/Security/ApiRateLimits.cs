using System.Threading.RateLimiting;
using SecureOps.Shared.Configuration;

namespace SecureOps.Api.Security;

/// <summary>Named actor-and-operation API rate-limit policies.</summary>
public static class ApiRateLimits
{
    /// <summary>Bounded materialization of durable workflow report snapshots.</summary>
    public const string WorkflowReport = "WorkflowReport";
    /// <summary>Bounded transient announcement rendering.</summary>
    public const string AnnouncementPreview = "AnnouncementPreview";
    /// <summary>Single exact identity lookup.</summary>
    public const string IdentityLookup = "IdentityLookup";
    /// <summary>Bounded bulk exact identity lookup.</summary>
    public const string BulkIdentityLookup = "BulkIdentityLookup";
    /// <summary>Exact principal groups and exact group metadata.</summary>
    public const string DirectoryGroupQuery = "DirectoryGroupQuery";
    /// <summary>Exact group direct-member enumeration.</summary>
    public const string DirectoryGroupMembers = "DirectoryGroupMembers";
    /// <summary>Recursive membership and account/service enrichment.</summary>
    public const string DirectoryEnrichment = "DirectoryEnrichment";
    /// <summary>Separately authorized privileged-group analysis.</summary>
    public const string DirectoryPrivilegedGroups = "DirectoryPrivilegedGroups";
    /// <summary>Bounded nested and parent group analysis.</summary>
    public const string DirectoryGroupAnalysis = "DirectoryGroupAnalysis";
    /// <summary>Authorized bounded group membership export.</summary>
    public const string DirectoryGroupExport = "DirectoryGroupExport";
    /// <summary>Operational-record source refresh.</summary>
    public const string OperationalRecordRefresh = "OperationalRecordRefresh";
    /// <summary>Read-only Jira preview.</summary>
    public const string JiraPreview = "JiraPreview";
    /// <summary>External Jira creation command.</summary>
    public const string JiraCreate = "JiraCreate";
    /// <summary>Failed workflow retry command.</summary>
    public const string WorkflowRetry = "WorkflowRetry";
    /// <summary>Session revocation and access decisions.</summary>
    public const string AccessAdministration = "AccessAdministration";

    /// <summary>Rejects missing or non-positive limits at startup instead of failing per request.</summary>
    public static void Validate(RateLimitingOptions options)
    {
        foreach (System.Reflection.PropertyInfo property in typeof(RateLimitingOptions).GetProperties())
        {
            if (property.GetValue(options) is OperationRateLimitOptions limit
                && (limit.PermitLimit < 1 || limit.WindowSeconds < 1))
            {
                throw new InvalidOperationException($"RateLimiting:{property.Name} needs a positive PermitLimit and WindowSeconds.");
            }
        }
    }

    /// <summary>Per-actor partition for the global limiter; anonymous callers are partitioned by remote address.</summary>
    public static RateLimitPartition<string> GlobalPartition(HttpContext context, OperationRateLimitOptions options)
    {
        string key = context.User.Identity?.IsAuthenticated == true
            ? "actor|" + (context.User.Identity.Name ?? "authenticated-unknown").ToLowerInvariant()
            : "anonymous|" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = TimeSpan.FromSeconds(options.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    /// <summary>Builds an authenticated actor and operation partition.</summary>
    public static RateLimitPartition<string> Partition(HttpContext context, string operation, OperationRateLimitOptions options)
    {
        string actor = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? "authenticated-unknown"
            : "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{actor.ToLowerInvariant()}|{operation}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = TimeSpan.FromSeconds(options.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }
}
