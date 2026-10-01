using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Access;

/// <summary>Fail-fast validation for access, session, rate-limit, and command settings.</summary>
public static class PlatformSecurityConfigurationValidator
{
    /// <summary>Validates bounded secure settings.</summary>
    public static void Validate(IConfiguration configuration, string? environmentName = null)
    {
        AccessOptions access = configuration.GetSection(AccessOptions.SectionName).Get<AccessOptions>() ?? new();
        BootstrapAdminOptions bootstrap = configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>() ?? new();
        OidcOptions oidc = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new();
        AuditOptions audit = configuration.GetSection(AuditOptions.SectionName).Get<AuditOptions>() ?? new();
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

        if (configuration.GetSection("Access:BootstrapAdministrators").GetChildren().Any())
        {
            throw new InvalidOperationException("Access:BootstrapAdministrators is retired; use the SQL-only BootstrapAdmin OIDC gate.");
        }

        if (bootstrap.Enabled)
        {
            ValidateBootstrapAdmin(bootstrap, oidc, access, audit);
        }

        if (session.IdleTimeoutMinutes is < 1 or > 1440 || session.AbsoluteLifetimeHours is < 1 or > 168
            || session.AbsoluteLifetimeHours * 60 < session.IdleTimeoutMinutes
            || session.ActivityPersistenceIntervalMinutes is < 1 or > 60
            || session.ActivityPersistenceIntervalMinutes >= session.IdleTimeoutMinutes)
        {
            throw new InvalidOperationException("SessionSecurity idle, absolute, or activity-persistence settings are outside safe bounds.");
        }

        if (!string.Equals(session.RepositoryProvider, "InMemory", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(session.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SessionSecurity:RepositoryProvider must be InMemory or SqlServer.");
        }

        if (string.Equals(session.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuration.GetConnectionString(Audit.AuditConnectionStrings.SecureOpsDb)))
        {
            throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required when SessionSecurity:RepositoryProvider is SqlServer.");
        }

        if (!string.Equals(session.RepositoryProvider, access.RepositoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SessionSecurity and Access repository providers must match.");
        }

        if ((string.Equals(environmentName, "Pilot", StringComparison.OrdinalIgnoreCase)
                || string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
            && !string.Equals(session.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Pilot and Production require SessionSecurity:RepositoryProvider SqlServer.");
        }

        if (!string.Equals(session.CookieName, "__Host-SecureOps.ApplicationSession", StringComparison.Ordinal)
            || session.MaxAdminPageSize is < 1 or > 500)
        {
            throw new InvalidOperationException("SessionSecurity cookie name or administrative page size is outside safe bounds.");
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

        foreach (OperationRateLimitOptions policy in new[] { rateLimits.IdentityLookup, rateLimits.BulkIdentityLookup, rateLimits.DirectoryGroupQuery, rateLimits.DirectoryGroupMembers, rateLimits.DirectoryEnrichment, rateLimits.DirectoryPrivilegedGroups, rateLimits.DirectoryGroupAnalysis, rateLimits.DirectoryGroupExport, rateLimits.OperationalRecordRefresh, rateLimits.JiraPreview, rateLimits.JiraCreate, rateLimits.WorkflowRetry })
        {
            if (policy.PermitLimit is < 1 or > 1000 || policy.WindowSeconds is < 1 or > 3600)
            {
                throw new InvalidOperationException("RateLimiting policy settings are outside safe bounds.");
            }
        }
    }

    private static void ValidateBootstrapAdmin(
        BootstrapAdminOptions bootstrap,
        OidcOptions oidc,
        AccessOptions access,
        AuditOptions audit)
    {
        if (string.IsNullOrWhiteSpace(bootstrap.LoginName)
            || bootstrap.LoginName.Length > 256
            || !string.Equals(bootstrap.LoginName, bootstrap.LoginName.Trim(), StringComparison.Ordinal)
            || bootstrap.LoginName.Any(char.IsControl))
        {
            throw new InvalidOperationException("BootstrapAdmin:LoginName is required and must be one bounded exact login name.");
        }

        if (!Uri.TryCreate(bootstrap.AllowedIssuer, UriKind.Absolute, out Uri? issuer)
            || !string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(issuer.UserInfo)
            || !string.IsNullOrEmpty(issuer.Query)
            || !string.IsNullOrEmpty(issuer.Fragment)
            || bootstrap.AllowedIssuer.Contains('*', StringComparison.Ordinal)
            || !string.Equals(bootstrap.AllowedIssuer, bootstrap.AllowedIssuer.TrimEnd('/'), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("BootstrapAdmin:AllowedIssuer must be one exact absolute HTTPS issuer without wildcard, credentials, query, fragment, or trailing slash.");
        }

        if (!oidc.Enabled
            || !string.Equals(bootstrap.AllowedIssuer, oidc.Authority.TrimEnd('/'), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("BootstrapAdmin requires enabled OIDC and an AllowedIssuer that exactly matches Oidc:Authority.");
        }

        if (!access.AutoCreateRequest
            || !string.Equals(access.RepositoryProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("BootstrapAdmin requires Access:AutoCreateRequest=true and Access:RepositoryProvider=SqlServer.");
        }

        if (access.DemoCompatibilityEnabled)
        {
            throw new InvalidOperationException("BootstrapAdmin cannot be enabled with Access:DemoCompatibilityEnabled.");
        }

        if (!string.Equals(audit.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase) || !audit.FailClosed)
        {
            throw new InvalidOperationException("BootstrapAdmin requires Audit:Provider=SqlServer and Audit:FailClosed=true for atomic audit persistence.");
        }
    }
}
