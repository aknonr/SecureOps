using System.Security.Claims;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Infrastructure.ServiceAccounts.WhereUsed;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Directory;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    /// <summary>
    /// "Hangi sunucularda?": one row per server for one account from the stored sources (scans, Excel findings, manual usages)
    /// and, when the directory is configured and the caller may read it, the account's SPN hosts. Read only; same scope rule as
    /// the account detail (out of scope = missing). A source that cannot be read says so in <c>Sources</c> and never hides the
    /// others. The reverse direction (server to accounts) does not exist.
    /// </summary>
    public Task<SaResult<WhereUsedView>> WhereUsedAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, string? source, bool foundOnly,
        int page, int? pageSize, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            int size = pageSize ?? WhereUsedPaging.DefaultPageSize;
            string? invalid = source is not null && !WhereUsedBuilder.IsSource(source) ? "source"
                : page is < 1 or > _maxScanPage ? "page"
                : size is < 1 or > WhereUsedPaging.MaxPageSize ? "pageSize"
                : null;
            if (invalid is not null)
            {
                return SaResult<WhereUsedView>.Fail(SaErrors.Invalid, invalid);
            }

            if (await repository!.AnchorAsync(accountId, cancellationToken) is not { } anchor || !caller.Scope.Covers(anchor.Anchor))
            {
                return SaResult<WhereUsedView>.Fail(SaErrors.NotFound);
            }

            WhereUsedData data = await repository.WhereUsedDataAsync(accountId, cancellationToken);
            WhereUsedSpnInput spn = await SpnHostsAsync(principal, context, caller, anchor.Name, cancellationToken);
            (IReadOnlyList<WhereUsedServer> servers, WhereUsedSummary summary, IReadOnlyList<WhereUsedSourceStatus> sources) = WhereUsedBuilder.Build(data, spn);
            WhereUsedServer[] filtered = [.. WhereUsedBuilder.Filter(servers, source, foundOnly)];
            return new SaResult<WhereUsedView>(new WhereUsedView(accountId, [.. filtered.Skip((page - 1) * size).Take(size)], filtered.Length, page, size, summary, sources));
        }, cancellationToken);

    /// <summary>
    /// The account's SPN hosts through the existing directory enrichment service (module View plus <c>Identity.Groups.View</c>, the
    /// same capability that route needs). Any failure is "unknown" for this source only.
    /// </summary>
    private async Task<WhereUsedSpnInput> SpnHostsAsync(ClaimsPrincipal principal, AccessOperationContext context, SaCaller caller, string accountName,
        CancellationToken cancellationToken)
    {
        if (!caller.Can(Capabilities.DirectoryGroupsView))
        {
            return WhereUsedSpnInput.Unknown(WhereUsedSourceStates.Denied, "SPN bilgisi okunmadı: dizin görüntüleme yetkiniz yok. Diğer kaynaklar etkilenmez.");
        }

        if (serviceEvidence is null)
        {
            return WhereUsedSpnInput.Unknown(WhereUsedSourceStates.NotConfigured, "SPN bilgisi bilinmiyor: bu ortamda dizin yapılandırılmamış.");
        }

        try
        {
            DirectoryQueryResult<DirectoryServiceEvidenceResponse> result = await serviceEvidence.GetServiceEvidenceAsync(
                new DirectoryPrincipalEnrichmentRequest(accountName), new DirectoryQueryExecutionContext(principal.Identity?.Name ?? "unknown", context.SourceIp,
                    context.CorrelationId), cancellationToken);
            switch (result.Status)
            {
                case DirectoryQueryStatus.Success when result.Value is { } evidence:
                    (IReadOnlyList<SpnHost> hosts, int unreadable) = ServicePrincipalNameHosts.Parse(evidence.ServicePrincipalNames);
                    string note = (evidence.ServicePrincipalNamesTruncated ? $" Dizin yalnız ilk {evidence.ServicePrincipalNames.Count} SPN'i döndürdü ({evidence.ServicePrincipalNameCount} tanımlı)." : string.Empty)
                        + (unreadable > 0 ? $" {unreadable} SPN okunamadı." : string.Empty);
                    return hosts.Count == 0 && note.Length == 0
                        ? new WhereUsedSpnInput(WhereUsedSourceStates.Empty, "Dizinde bu hesap için SPN tanımlı değil.", [])
                        : new WhereUsedSpnInput(note.Length > 0 ? WhereUsedSourceStates.Partial : WhereUsedSourceStates.Available,
                            $"{hosts.Count} ana makine için SPN tanımlı; tanım kullanım kanıtı değildir.{note}", hosts);
                case DirectoryQueryStatus.NotFound:
                    return WhereUsedSpnInput.Unknown(WhereUsedSourceStates.Unknown, "SPN bilinmiyor: hesap dizinde bulunamadı.");
                default:
                    return WhereUsedSpnInput.Unknown(WhereUsedSourceStates.Unknown, "SPN bilinmiyor: dizine şu an ulaşılamadı. Diğer kaynaklar etkilenmez.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Provider messages may echo the account; only the failure type is logged.
            logger.LogWarning("Service Accounts where-used SPN read failed. FailureType: {FailureType} CorrelationId={CorrelationId}", exception.GetType().Name,
                context.CorrelationId);
            return WhereUsedSpnInput.Unknown(WhereUsedSourceStates.Unknown, "SPN bilinmiyor: dizine şu an ulaşılamadı. Diğer kaynaklar etkilenmez.");
        }
    }
}
