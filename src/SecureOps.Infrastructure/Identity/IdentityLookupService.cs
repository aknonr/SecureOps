using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Default identity lookup service.
/// </summary>
public sealed class IdentityLookupService : IIdentityLookupService
{
    private readonly IIdentityAccountNormalizer _normalizer;
    private readonly IPamAccountResolver _pamResolver;
    private readonly IIdentityDirectoryProvider _directoryProvider;
    private readonly IIdentityReadThroughCache _readCache;
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<IdentityLookupService> _logger;

    /// <summary>
    /// Initializes a new identity lookup service.
    /// </summary>
    /// <param name="normalizer">Account normalizer.</param>
    /// <param name="pamResolver">PAM resolver hook.</param>
    /// <param name="directoryProvider">Directory provider.</param>
    /// <param name="readCache">Short-lived exact-account cache.</param>
    /// <param name="auditWriter">Audit writer.</param>
    /// <param name="logger">Logger.</param>
    public IdentityLookupService(
        IIdentityAccountNormalizer normalizer,
        IPamAccountResolver pamResolver,
        IIdentityDirectoryProvider directoryProvider,
        IIdentityReadThroughCache readCache,
        IAuditWriter auditWriter,
        ILogger<IdentityLookupService> logger)
    {
        _normalizer = normalizer;
        _pamResolver = pamResolver;
        _directoryProvider = directoryProvider;
        _readCache = readCache;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool SupportsUpnLookup => _directoryProvider.SupportsUpnLookup;

    /// <inheritdoc />
    public async Task<IdentityLookupResult> LookupAsync(
        IdentityLookupRequest request,
        IdentityLookupExecutionContext context,
        CancellationToken cancellationToken)
    {
        IdentityAccountNormalizationResult normalized = _normalizer.Normalize(request.Account);
        if (!normalized.IsValid || normalized.NormalizedAccount is null)
        {
            bool rejectedAuditWritten = await TryWriteAuditAsync(
                AuditActions.IdentityLookupRejected,
                context,
                request,
                normalized.NormalizedAccount,
                null,
                normalized.ErrorCode,
                cancellationToken);

            if (!rejectedAuditWritten)
            {
                return AuditUnavailable();
            }

            return new IdentityLookupResult(
                IdentityLookupResultStatus.Invalid,
                null,
                normalized.ErrorCode,
                "The identity lookup request was rejected.");
        }

        bool requestAuditWritten = await TryWriteAuditAsync(
            AuditActions.IdentityLookupRequested,
            context,
            request,
            normalized.NormalizedAccount,
            null,
            null,
            cancellationToken);

        if (!requestAuditWritten)
        {
            return AuditUnavailable();
        }

        try
        {
            PamAccountResolution pamResolution = await _pamResolver.ResolveAsync(normalized.NormalizedAccount, cancellationToken);
            IdentityReadCacheResult cached = await _readCache.GetOrCreateAsync(
                normalized.NormalizedAccount,
                async providerCancellationToken =>
                {
                    bool providerAuditWritten = await TryWriteAuditAsync(
                        AuditActions.IdentityLookupProviderCall,
                        context,
                        request,
                        normalized.NormalizedAccount,
                        null,
                        null,
                        providerCancellationToken);
                    if (!providerAuditWritten)
                    {
                        throw new AuditWriteUnavailableException("Identity lookup provider-call audit is unavailable.");
                    }

                    return await _directoryProvider.FindUserAsync(pamResolution.DirectoryAccount, providerCancellationToken);
                },
                cancellationToken);
            if (cached.Disposition is IdentityReadCacheDisposition.CacheHit or IdentityReadCacheDisposition.Coalesced
                && !await TryWriteAuditAsync(AuditActions.IdentityLookupCacheHit, context, request, normalized.NormalizedAccount, null, null, cancellationToken))
            {
                return AuditUnavailable();
            }

            DirectoryUserRecord? user = cached.User;

            if (user is null)
            {
                IdentityLookupResponse notFoundResponse = new(
                    "NotFound",
                    normalized.NormalizedAccount,
                    pamResolution.Source,
                    null);

                bool notFoundAuditWritten = await TryWriteAuditAsync(
                    AuditActions.IdentityLookupNotFound,
                    context,
                    request,
                    normalized.NormalizedAccount,
                    null,
                    null,
                    cancellationToken);

                if (!notFoundAuditWritten)
                {
                    return AuditUnavailable();
                }

                return new IdentityLookupResult(IdentityLookupResultStatus.NotFound, notFoundResponse, null, null);
            }

            IdentityLookupResponse response = new(
                "Found",
                normalized.NormalizedAccount,
                user.Source,
                new IdentityLookupUserDto(
                    user.DisplayName,
                    user.SamAccountName,
                    user.UserPrincipalName,
                    user.Mail,
                    user.Department,
                    user.Title,
                    user.ManagerDisplayName,
                    user.Enabled,
                    user.Locked));

            bool successAuditWritten = await TryWriteAuditAsync(
                AuditActions.IdentityLookupSucceeded,
                context,
                request,
                normalized.NormalizedAccount,
                user.SamAccountName,
                null,
                cancellationToken);

            if (!successAuditWritten)
            {
                return AuditUnavailable();
            }

            return new IdentityLookupResult(IdentityLookupResultStatus.Found, response, null, null);
        }
        catch (AuditWriteUnavailableException)
        {
            return AuditUnavailable();
        }
        catch (IdentityProviderInputRejectedException ex)
        {
            _logger.LogWarning(
                ex,
                "Identity provider rejected normalized input. CorrelationId: {CorrelationId}",
                context.CorrelationId);

            bool failedAuditWritten = await TryWriteAuditAsync(
                AuditActions.IdentityLookupRejected,
                context,
                request,
                normalized.NormalizedAccount,
                null,
                ex.Code,
                cancellationToken);

            return failedAuditWritten
                ? new IdentityLookupResult(
                    IdentityLookupResultStatus.Invalid,
                    null,
                    ex.Code,
                    "The identity lookup request was rejected.")
                : AuditUnavailable();
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(
                ex,
                "Identity lookup directory provider timed out. CorrelationId: {CorrelationId}",
                context.CorrelationId);

            bool failedAuditWritten = await TryWriteAuditAsync(
                AuditActions.IdentityLookupProviderTimeout,
                context,
                request,
                normalized.NormalizedAccount,
                null,
                "DirectoryProviderTimeout",
                cancellationToken);

            if (!failedAuditWritten)
            {
                return AuditUnavailable();
            }

            return new IdentityLookupResult(
                IdentityLookupResultStatus.Failed,
                null,
                "DirectoryProviderTimeout",
                "Identity provider lookup timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Identity lookup failed. CorrelationId: {CorrelationId}",
                context.CorrelationId);

            bool failedAuditWritten = await TryWriteAuditAsync(
                AuditActions.IdentityLookupFailed,
                context,
                request,
                normalized.NormalizedAccount,
                null,
                "ProviderUnavailable",
                cancellationToken);

            if (!failedAuditWritten)
            {
                return AuditUnavailable();
            }

            return new IdentityLookupResult(
                IdentityLookupResultStatus.Failed,
                null,
                "ProviderUnavailable",
                "Identity provider lookup failed.");
        }
    }

    private async Task<bool> TryWriteAuditAsync(
        string action,
        IdentityLookupExecutionContext context,
        IdentityLookupRequest request,
        string? normalizedAccount,
        string? matchedAccount,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditWriter.WriteAsync(
                new AuditEvent
                {
                    Actor = context.Actor,
                    Action = action,
                    AlertId = request.AlertId,
                    CorrelationId = context.CorrelationId,
                    SourceIp = context.SourceIp,
                    Details = new
                    {
                        accountInputHash = AuditAccountHasher.HashAccountInput(request.Account),
                        accountLength = request.Account?.Trim().Length,
                        request.Purpose,
                        request.TuruncuhatEvtId,
                        resultStatus = ToResultStatus(action),
                        errorCode
                    }
                },
                cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Identity lookup audit write failed for action {AuditAction}. CorrelationId: {CorrelationId}",
                action,
                context.CorrelationId);
            return false;
        }
    }

    private static IdentityLookupResult AuditUnavailable()
    {
        return new IdentityLookupResult(
            IdentityLookupResultStatus.Failed,
            null,
            "AuditUnavailable",
            "Identity lookup audit is unavailable.");
    }

    private static string ToResultStatus(string action)
    {
        return action switch
        {
            AuditActions.IdentityLookupRequested => "Requested",
            AuditActions.IdentityLookupSucceeded => "Succeeded",
            AuditActions.IdentityLookupNotFound => "NotFound",
            AuditActions.IdentityLookupRejected => "Rejected",
            AuditActions.IdentityLookupProviderTimeout => "ProviderTimeout",
            AuditActions.IdentityLookupCacheHit => "CacheHit",
            AuditActions.IdentityLookupProviderCall => "ProviderCall",
            _ => "Failed"
        };
    }
}
