using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public sealed partial class InUseService
{
    /// <summary>Creates a fresh local revision without deleting evidence or reversing external effects.</summary>
    public Task<InUseResult<InUseRecord>> ChangeDraftAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, ChangeInUseDraftRequest request, CancellationToken token) =>
        RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            if (request.Action is not ("Reset" or "Discard" or "Restart") || !Text(request.Reason, 500, true))
            { return InUseResult<InUseRecord>.Fail("InUseInvalid"); }
            InUseRecord? old = await repository.GetAsync(id, token);
            if (old is null)
            { return InUseResult<InUseRecord>.Fail("InUseNotFound"); }
            if (old.Version != request.ExpectedVersion || old.Discarded != (request.Action == "Restart"))
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            InUseRecord next = old with
            {
                Version = old.Version + 1,
                Draft = null,
                PolicyProposal = null,
                Discarded = request.Action == "Discard",
                InvalidatedReviewsThrough = old.Version,
                DraftLifecycle = new(request.Action, user.Id, ActorLabel(user), DateTimeOffset.UtcNow, request.Reason.Trim())
            };
            InUseResult<InUseRecord> result = await SaveAsync(next, old.Version, Audit(user, context, "DraftLifecycle",
                new { id, request.Action, request.Reason, next.Version, next.InvalidatedReviewsThrough, PreviousDraft = old.Draft, PreviousLifecycle = old.DraftLifecycle }, id, old.Version), token);
            return result.Error == "InUseConflict"
                ? new(null, "InUseConflict", "Kayıt değişmiş veya sürmekte/uzlaştırılmayı bekleyen kaynak işlemi var. Kaydı ve işlem geçmişini yenileyin; uzak etki geri alınmadı.")
                : result;
        }, token);
}
