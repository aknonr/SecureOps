using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public sealed partial class InUseService
{
    /// <summary>Explicit local attestation by the authenticated, currently authorized operator.</summary>
    public Task<InUseResult<InUseExecution>> ConfirmClosureAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, ConfirmInUseClosureRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseComplete, async user =>
        {
            if (!Reviewer(user))
            { return InUseResult<InUseExecution>.Fail("AccessDenied"); }
            return completion is null ? InUseResult<InUseExecution>.Fail("InUseCompletionUnavailable")
                : await completion.ConfirmClosureAsync(id, user.Id, ActorLabel(user), request, token);
        }, token);

    /// <summary>Readiness does not mutate or activate a historical blocked intent.</summary>
    public Task<InUseResult<InUseExecutionStatus>> ExecutionStatusAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            InUseRecord? record = await repository.GetAsync(id, token);
            if (record is null)
            { return InUseResult<InUseExecutionStatus>.Fail("InUseNotFound"); }
            return completion is null ? new(new(new(false, "Disabled", "Kaynak tamamlama kapalı; yerel arşiv kullanılabilir."), null))
                : new(await completion.StatusAsync(record, user.Capabilities.Contains(Capabilities.InUseComplete), token));
        }, token);

    /// <summary>Fresh explicit confirmation using current narrow authority and exact immutable archive.</summary>
    public Task<InUseResult<InUseExecution>> StartExecutionAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, StartInUseExecutionRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseComplete, async user =>
        {
            if (!Reviewer(user))
            { return InUseResult<InUseExecution>.Fail("AccessDenied"); }
            InUseRecord? record = await repository.GetAsync(id, token);
            if (record is null)
            { return InUseResult<InUseExecution>.Fail("InUseNotFound"); }
            if (completion is null)
            { return InUseResult<InUseExecution>.Fail("InUseCompletionUnavailable"); }
            return await completion.StartAsync(record, user.Id, ActorLabel(user), request, token);
        }, token);
}
