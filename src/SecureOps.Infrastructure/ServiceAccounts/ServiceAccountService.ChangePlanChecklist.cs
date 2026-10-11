using System.Security.Claims;
using System.Security.Cryptography;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    // Checklist, plan evidence and completion (CHANGE-PLAN-DESIGN.md PR 2). The service checks input shape; every command decides
    // scope, authority, state and staleness again inside its write transaction with the plan row locked. Nothing here connects
    // to a server; "Done" is the person's statement and completion never verifies an account or closes a request.

    /// <summary>Checklist marks of one row of the plan's current preview, newest first; a row outside it is NotFound.</summary>
    public Task<SaResult<IReadOnlyList<ChangePlanItemCheckView>>> ChangePlanItemChecksAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid itemId, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
            await VisiblePlanAsync(caller, id, cancellationToken) is { } plan && plan.Items.Any(i => i.ItemId == itemId)
                ? new SaResult<IReadOnlyList<ChangePlanItemCheckView>>(await repository!.ChangePlanItemChecksAsync(itemId, cancellationToken))
                : SaResult<IReadOnlyList<ChangePlanItemCheckView>>.Fail(SaErrors.NotFound), cancellationToken);

    /// <summary>Records the person's own statement for one row of the approved preview.</summary>
    public Task<SaResult<ChangePlanView>> CheckChangePlanItemAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, Guid itemId,
        CheckChangePlanItemRequest request, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
            ChangePlanRules.CheckInputError(request.State, request.Note) is { } field
                ? SaResult<ChangePlanView>.Fail(SaErrors.Invalid, field)
                : await AfterCommandAsync(caller, id, await repository!.CheckChangePlanItemAsync(id, itemId, Enum.Parse<ChangePlanCheckState>(request.State),
                    ServiceAccountText.Clean(request.Note), request.ExpectedCheckId, PlanCaller(caller), clock.GetUtcNow(), cancellationToken), cancellationToken),
            cancellationToken);

    /// <summary>Attaches evidence to an approved plan with the same file checks as account evidence.</summary>
    public Task<SaResult<ChangePlanView>> AddChangePlanEvidenceAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, string fileName,
        string contentType, byte[] content, string? label, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (!EvidenceFileAcceptable(contentType, content, label))
            {
                return SaResult<ChangePlanView>.Fail(SaErrors.Invalid, "file");
            }

            ChangePlanEvidenceFile file = new(SafeName(fileName), contentType.ToLowerInvariant(), content,
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), ServiceAccountText.Clean(label));
            return await AfterCommandAsync(caller, id, await repository!.AddChangePlanEvidenceAsync(id, file, PlanCaller(caller), cancellationToken), cancellationToken);
        }, cancellationToken);

    /// <summary>Closes an InProgress plan whose every row is marked.</summary>
    public Task<SaResult<ChangePlanView>> CompleteChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        CompleteChangePlanRequest request, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
            ServiceAccountText.Clean(request.Note) is { Length: > ChangePlanRules.MaxReason }
                ? SaResult<ChangePlanView>.Fail(SaErrors.Invalid, "note")
                : await AfterCommandAsync(caller, id, await repository!.CompleteChangePlanAsync(id, request.ExpectedVersion ?? string.Empty,
                    ServiceAccountText.Clean(request.Note), PlanCaller(caller), cancellationToken), cancellationToken), cancellationToken);

    /// <summary>Checklist progress from each row's latest mark (null before approval).</summary>
    private static ChangePlanChecklistView? Checklist(ChangePlanSnapshot plan)
    {
        if (plan.Approval is null)
        {
            return null;
        }

        int Count(ChangePlanCheckState state) => plan.Items.Count(i => i.State == state);
        return new ChangePlanChecklistView(plan.Items.Count, Count(ChangePlanCheckState.Done), Count(ChangePlanCheckState.Skipped), Count(ChangePlanCheckState.Failed),
            Count(ChangePlanCheckState.RolledBack), plan.Items.Count(i => i.State is null), plan.Items.Count(i => i.State is not null && i.OutsideWindow));
    }
}
