using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>Validates identity and future PAM provider settings before startup.</summary>
public static class IdentityLookupConfigurationValidator
{
    /// <summary>Validates the configured identity and PAM provider boundary.</summary>
    /// <param name="configuration">Application configuration.</param>
    public static void Validate(IConfiguration configuration)
    {
        IdentityLookupOptions identity = new();
        configuration.GetSection(IdentityLookupOptions.SectionName).Bind(identity);
        if (!new[] { "Mock", "ActiveDirectory" }.Contains(identity.Provider, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("IdentityLookup:Provider must be Mock or ActiveDirectory.");
        }

        if (string.Equals(identity.Provider, "ActiveDirectory", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(identity.DomainName))
        {
            throw new InvalidOperationException("IdentityLookup:DomainName is required when IdentityLookup:Provider is ActiveDirectory.");
        }

        if (identity.ProviderTimeoutSeconds <= 0 || identity.BulkMaxAccounts is < 1 or > 20)
        {
            throw new InvalidOperationException("IdentityLookup:ProviderTimeoutSeconds must be positive and BulkMaxAccounts must be between 1 and 20.");
        }

        PamProviderOptions pam = new();
        configuration.GetSection(PamProviderOptions.SectionName).Bind(pam);
        if (!string.Equals(pam.Provider, "Mock", StringComparison.OrdinalIgnoreCase) || pam.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("PamProvider currently supports only Mock with a positive timeout. An approved real PAM adapter is required before another provider can be selected.");
        }
    }
}
