using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure.Identity;

namespace SecureOps.Infrastructure.Access;

/// <summary>Provider-neutral exact-match profile enrichment for access projections.</summary>
public sealed class AccessIdentityProfileResolver : IAccessIdentityProfileResolver
{
    private readonly IIdentityAccountNormalizer _normalizer;
    private readonly IIdentityDirectoryProvider _provider;
    private readonly ILogger<AccessIdentityProfileResolver> _logger;

    /// <summary>Initializes the resolver.</summary>
    public AccessIdentityProfileResolver(
        IIdentityAccountNormalizer normalizer,
        IIdentityDirectoryProvider provider,
        ILogger<AccessIdentityProfileResolver> logger)
    {
        _normalizer = normalizer;
        _provider = provider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccessIdentityProfile?> ResolveAsync(string corporateIdentity, CancellationToken cancellationToken)
    {
        IdentityAccountNormalizationResult normalized;
        try
        {
            normalized = _normalizer.Normalize(corporateIdentity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Access profile normalization failed.");
            return null;
        }

        if (!normalized.IsValid || string.IsNullOrWhiteSpace(normalized.NormalizedAccount))
        {
            return null;
        }

        try
        {
            DirectoryUserRecord? record = await _provider.FindUserAsync(normalized.NormalizedAccount, cancellationToken);
            return record is null
                ? null
                : new AccessIdentityProfile(record.DisplayName, record.SamAccountName, record.Mail, record.Department, record.Title);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Access profile enrichment failed for an exact normalized principal.");
            return null;
        }
    }
}
