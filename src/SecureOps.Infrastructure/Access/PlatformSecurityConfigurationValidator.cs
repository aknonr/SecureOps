using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Access;

/// <summary>Fail-fast validation for access, session, rate-limit, and command settings.</summary>
public static class PlatformSecurityConfigurationValidator
{
    /// <summary>Validates bounded secure settings.</summary>
    public static void Validate(IConfiguration configuration)
    {
        AccessOptions access = configuration.GetSection(AccessOptions.SectionName).Get<AccessOptions>() ?? new();
        SessionSecurityOptions session = configuration.GetSection(SessionSecurityOptions.SectionName).Get<SessionSecurityOptions>() ?? new();
        CommandIdempotencyOptions command = configuration.GetSection(CommandIdempotencyOptions.SectionName).Get<CommandIdempotencyOptions>() ?? new();
        RateLimitingOptions rateLimits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new();
        if (!string.Equals(access.RepositoryProvider, "InMemory", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(access.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Access:RepositoryProvider must be InMemory or SqlServer.");
        }

        if (string.Equals(access.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuration.GetConnectionString(Audit.AuditConnectionStrings.SecureOpsDb)))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required when Access:RepositoryProvider is SqlServer.");
        }

        if (access.BootstrapAdministrators.Any(string.IsNullOrWhiteSpace)
            || access.BootstrapAdministrators.Any(identifier => identifier.Trim().Length > 256)
            || access.BootstrapAdministrators.Distinct(StringComparer.OrdinalIgnoreCase).Count() != access.BootstrapAdministrators.Length)
        {
            throw new InvalidOperationException("Access:BootstrapAdministrators must contain unique, non-empty identifiers no longer than 256 characters.");
        }

        if (access.BootstrapAdministrators.Length > 0 && !access.AutoCreateRequest)
        {
            throw new InvalidOperationException("Access:AutoCreateRequest must be true while configured bootstrap administrators are enabled.");
        }

        if (session.IdleTimeoutMinutes is < 1 or > 1440 || session.AbsoluteLifetimeHours is < 1 or > 168
            || session.AbsoluteLifetimeHours * 60 < session.IdleTimeoutMinutes)
        {
            throw new InvalidOperationException("SessionSecurity idle and absolute lifetimes are outside safe bounds.");
        }

        if (!session.SecureCookie || !session.HttpOnly || !session.RevalidateAccessOnEveryRequest)
        {
            throw new InvalidOperationException("SessionSecurity must keep Secure, HttpOnly, and per-request access revalidation enabled.");
        }

        if (!new[] { "Lax", "Strict", "None" }.Contains(session.SameSite, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SessionSecurity:SameSite must be Lax, Strict, or None.");
        }

        if (command.ExecutionLeaseSeconds is < 30 or > 900 || command.MaxKeyLength is < 32 or > 256)
        {
            throw new InvalidOperationException("CommandIdempotency settings are outside safe bounds.");
        }

        foreach (OperationRateLimitOptions policy in new[] { rateLimits.IdentityLookup, rateLimits.BulkIdentityLookup, rateLimits.OperationalRecordRefresh, rateLimits.JiraPreview, rateLimits.JiraCreate, rateLimits.WorkflowRetry })
        {
            if (policy.PermitLimit is < 1 or > 1000 || policy.WindowSeconds is < 1 or > 3600)
            {
                throw new InvalidOperationException("RateLimiting policy settings are outside safe bounds.");
            }
        }
    }
}
