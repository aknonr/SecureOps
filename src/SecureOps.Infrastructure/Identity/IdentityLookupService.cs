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
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<IdentityLookupService> _logger;

    /// <summary>
    /// Initializes a new identity lookup service.
    /// </summary>
    /// <param name="normalizer">Account normalizer.</param>
    /// <param name="pamResolver">PAM resolver hook.</param>
    /// <param name="directoryProvider">Directory provider.</param>
    /// <param name="auditWriter">Audit writer.</param>
    /// <param name="logger">Logger.</param>
    public IdentityLookupService(
        IIdentityAccountNormalizer normalizer,
        IPamAccountResolver pamResolver,
        IIdentityDirectoryProvider directoryProvider,
        IAuditWriter auditWriter,
        ILogger<IdentityLookupService> logger)
    {
        _normalizer = normalizer;
        _pamResolver = pamResolver;
        _directoryProvider = directoryProvider;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IdentityLookupResult> LookupAsync(
        IdentityLookupRequest request,
        IdentityLookupExecutionContext context,
        CancellationToken cancellationToken)
    {
        IdentityAccountNormalizationResult normalized = _normalizer.Normalize(request.Account);
        if (!normalized.IsValid || normalized.NormalizedAccount is null)
        {
            await WriteAuditAsync(
                AuditActions.IdentityLookupFailed,
                context,
                request,
                normalized.NormalizedAccount,
                null,
                normalized.ErrorCode,
                cancellationToken);

            return new IdentityLookupResult(
                IdentityLookupResultStatus.Invalid,
                null,
                normalized.ErrorCode,
                normalized.ErrorMessage);
        }

        await WriteAuditAsync(
            AuditActions.IdentityLookupRequested,
            context,
            request,
            normalized.NormalizedAccount,
            null,
            null,
            cancellationToken);

        try
        {
            PamAccountResolution pamResolution = await _pamResolver.ResolveAsync(normalized.NormalizedAccount, cancellationToken);
            DirectoryUserRecord? user = await _directoryProvider.FindUserAsync(pamResolution.DirectoryAccount, cancellationToken);

            if (user is null)
            {
                IdentityLookupResponse notFoundResponse = new(
                    "NotFound",
                    normalized.NormalizedAccount,
                    pamResolution.Source,
                    null);

                await WriteAuditAsync(
                    AuditActions.IdentityLookupNotFound,
                    context,
                    request,
                    normalized.NormalizedAccount,
                    null,
                    null,
                    cancellationToken);

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

            await WriteAuditAsync(
                AuditActions.IdentityLookupSucceeded,
                context,
                request,
                normalized.NormalizedAccount,
                user.SamAccountName,
                null,
                cancellationToken);

            return new IdentityLookupResult(IdentityLookupResultStatus.Found, response, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Identity lookup failed for normalized account {NormalizedAccount}", normalized.NormalizedAccount);

            await WriteAuditAsync(
                AuditActions.IdentityLookupFailed,
                context,
                request,
                normalized.NormalizedAccount,
                null,
                "ProviderUnavailable",
                cancellationToken);

            return new IdentityLookupResult(
                IdentityLookupResultStatus.Failed,
                null,
                "ProviderUnavailable",
                "Identity provider lookup failed.");
        }
    }

    private Task WriteAuditAsync(
        string action,
        IdentityLookupExecutionContext context,
        IdentityLookupRequest request,
        string? normalizedAccount,
        string? matchedAccount,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        return _auditWriter.WriteAsync(
            new AuditEvent
            {
                Actor = context.Actor,
                Action = action,
                AlertId = request.AlertId,
                CorrelationId = context.CorrelationId,
                SourceIp = context.SourceIp,
                Details = new
                {
                    normalizedAccount,
                    matchedAccount,
                    request.Purpose,
                    request.TuruncuhatEvtId,
                    errorCode
                }
            },
            cancellationToken);
    }
}
