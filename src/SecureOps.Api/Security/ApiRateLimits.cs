using System.Threading.RateLimiting;
using SecureOps.Shared.Configuration;

namespace SecureOps.Api.Security;

/// <summary>Named actor-and-operation API rate-limit policies.</summary>
public static class ApiRateLimits
{
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
    /// <summary>Operational-record source refresh.</summary>
    public const string OperationalRecordRefresh = "OperationalRecordRefresh";
    /// <summary>Read-only Jira preview.</summary>
    public const string JiraPreview = "JiraPreview";
    /// <summary>External Jira creation command.</summary>
    public const string JiraCreate = "JiraCreate";
    /// <summary>Failed workflow retry command.</summary>
    public const string WorkflowRetry = "WorkflowRetry";

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
