using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public sealed partial class InUseService
{
    /// <summary>Explicit authorized suggestion read; no assignment, provisioning or source query.</summary>
    public Task<InUseResult<InUseReporterSuggestion>> ReporterAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseAssign, async user =>
        {
            InUseRecord? record = await repository.GetAsync(id, token);
            if (record is null)
            { return InUseResult<InUseReporterSuggestion>.Fail("InUseNotFound"); }
            if (reporterResolver is null)
            { return InUseResult<InUseReporterSuggestion>.Fail("PersistenceUnavailable"); }
            InUseReporterSuggestion proposal = await reporterResolver.ResolveAsync(record, DateTimeOffset.UtcNow, token);
            return await repository.ExportAsync(id, record.Version, Audit(user, context, "ReporterSuggestionRead",
                new { id, proposal.State, proposal.Fingerprint }, id, record.Version), token)
                ? new(proposal) : InUseResult<InUseReporterSuggestion>.Fail("InUseConflict");
        }, token);
}
