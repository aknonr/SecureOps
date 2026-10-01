using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public sealed partial class InUseService
{
    /// <summary>Searches the SQL catalogue, never the archive filesystem or current identity directory.</summary>
    public Task<InUseResult<InUseReportPage>> ReportsAsync(ClaimsPrincipal principal, AccessOperationContext context,
        InUseReportQuery query, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            if (query.Page is < 1 or > 10000 || query.PageSize is < 1 or > 100 || query.Search?.Length > 100
                || query.Version is < 1 || query.Status is not (null or "Current" or "Superseded" or "Discarded")
                || (query.From is not null && query.To is not null && query.From >= query.To))
            { return InUseResult<InUseReportPage>.Fail("InUseInvalid"); }
            if (reports is null || repository is not SqlInUseRepository)
            { return InUseResult<InUseReportPage>.Fail("PersistenceUnavailable"); }
            InUseReportPage? page = await reports.CatalogueAsync(user, query, context.CorrelationId, token);
            return page is null ? InUseResult<InUseReportPage>.Fail("AccessDenied") : new(page);
        }, token);

    /// <summary>Indexes selected historical envelopes with normal archive integrity, version and audit guards.</summary>
    public Task<InUseResult<IndexInUseReportsResult>> IndexReportsAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, IndexInUseReportsRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async _ =>
        {
            if (request.ExpectedVersion < 1 || request.Versions is null || request.Versions.Count is < 1 or > 25
                || request.Versions.Any(v => v < 1) || request.Versions.Distinct().Count() != request.Versions.Count)
            { return InUseResult<IndexInUseReportsResult>.Fail("InUseInvalid"); }
            if (reports is null || repository is not SqlInUseRepository)
            { return InUseResult<IndexInUseReportsResult>.Fail("PersistenceUnavailable"); }
            List<long> indexed = [];
            foreach (long version in request.Versions)
            {
                InUseResult<InUseReport> result = await ExportAsync(principal, context, id,
                    new(request.ExpectedVersion, false) { ArchivedVersion = version }, token);
                if (result.Error is not null)
                { return new(null, result.Error, "Already indexed versions are retained. Retry the same selection after resolving the reported conflict."); }
                indexed.Add(version);
            }
            return new(new(indexed));
        }, token);
}
