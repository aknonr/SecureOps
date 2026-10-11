using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    // gMSA conversion change plans (CHANGE-PLAN-DESIGN.md, migration 033). The service validates input shape and decides what
    // the caller may see; every command decides authority again inside its write transaction with the plan row locked.
    // Nothing here connects to a server, converts an account or carries a password.

    /// <summary>Plans whose every account is in the caller's scope, newest change first.</summary>
    public Task<SaResult<ChangePlanPage>> ChangePlansAsync(ClaimsPrincipal principal, AccessOperationContext context, ChangePlanListQuery query,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
            query.Page < 1 || query.PageSize is < 1 or > 100 || query.Status is { } status && !Enum.TryParse<ChangePlanStatus>(status, false, out _)
                ? SaResult<ChangePlanPage>.Fail(SaErrors.Invalid, query.Status is null ? "page" : "status")
                : new SaResult<ChangePlanPage>(await repository!.ChangePlansAsync(caller.Scope, query, cancellationToken)), cancellationToken);

    /// <summary>Plan detail; a plan with any account outside the caller's scope is NotFound, same as a missing one.</summary>
    public Task<SaResult<ChangePlanView>> ChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.View, caller => PlanViewAsync(caller, id, cancellationToken), cancellationToken);

    /// <summary>Rows of the plan's current preview (an empty page with version 0 while the plan has none).</summary>
    public Task<SaResult<ChangePlanItemPage>> ChangePlanItemsAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, int page, int pageSize,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            if (await VisiblePlanAsync(caller, id, cancellationToken) is not { } plan)
            {
                return SaResult<ChangePlanItemPage>.Fail(SaErrors.NotFound);
            }

            if (page < 1 || pageSize is < 1 or > 200)
            {
                return SaResult<ChangePlanItemPage>.Fail(SaErrors.Invalid, "page");
            }

            return plan.CurrentPreviewVersion is { } version
                ? await repository!.ChangePlanItemsAsync(id, version, page, pageSize, cancellationToken)
                : new ChangePlanItemPage(0, [], 0, page, pageSize);
        }, cancellationToken);

    /// <summary>Creates a Draft plan (1–20 accounts); any refused account refuses the request with per-account answers.</summary>
    public Task<SaResult<ChangePlanView>> CreateChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, CreateChangePlanRequest request,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (ServiceAccountText.Clean(request.Title) is not { Length: <= ChangePlanRules.MaxTitle } title)
            {
                return SaResult<ChangePlanView>.Fail(SaErrors.Invalid, "title");
            }

            if (AccountListError(request.Accounts) is { } field)
            {
                return SaResult<ChangePlanView>.Fail(SaErrors.Invalid, field);
            }

            SaResult<Guid> created = await repository!.CreateChangePlanAsync(title, request.Accounts, PlanCaller(caller), cancellationToken);
            return created.IsSuccess ? await PlanViewAsync(caller, created.Value, cancellationToken) : SaResult<ChangePlanView>.Fail(created.ErrorCode!, created.Field,
                created.Current);
        }, cancellationToken);

    /// <summary>Replaces the plan's account list at the expected version.</summary>
    public Task<SaResult<ChangePlanView>> UpdateChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, UpdateChangePlanRequest request,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
            AccountListError(request.Accounts) is { } field
                ? SaResult<ChangePlanView>.Fail(SaErrors.Invalid, field)
                : await AfterCommandAsync(caller, id, await repository!.UpdateChangePlanAsync(id, request.ExpectedVersion ?? string.Empty, request.Accounts,
                    PlanCaller(caller), cancellationToken), cancellationToken), cancellationToken);

    /// <summary>Builds a new preview version from each account's latest Discovery scan.</summary>
    public Task<SaResult<ChangePlanView>> PreviewChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, PreviewChangePlanRequest request,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
            await AfterCommandAsync(caller, id, await repository!.PreviewChangePlanAsync(id, request.ExpectedVersion ?? string.Empty, PlanCaller(caller),
                clock.GetUtcNow(), cancellationToken), cancellationToken), cancellationToken);

    /// <summary>Approves exactly the current preview (version + SHA-256); never the planner, the previewer or an editor.</summary>
    public Task<SaResult<ChangePlanView>> ApproveChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, ApproveChangePlanRequest request,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.Verify, async caller =>
        {
            string? field = request.PreviewVersion < 1 ? "previewVersion"
                : request.Sha256 is null || !Regex.IsMatch(request.Sha256, "^[0-9a-f]{64}$", RegexOptions.None, TimeSpan.FromMilliseconds(50)) ? "sha256"
                : ChangePlanRules.OcoNumber(request.OcoNumber) is null ? "ocoNumber"
                : request.WindowEnd <= request.WindowStart ? "window"
                : ServiceAccountText.Clean(request.Reason) is not { Length: <= ChangePlanRules.MaxReason } ? "reason"
                : null;
            if (field is not null)
            {
                return SaResult<ChangePlanView>.Fail(SaErrors.Invalid, field);
            }

            SaResult<Guid> result = await repository!.ApproveChangePlanAsync(id, new ChangePlanApproval(request.PreviewVersion, request.Sha256!,
                ChangePlanRules.OcoNumber(request.OcoNumber)!, request.WindowStart, request.WindowEnd, ServiceAccountText.Clean(request.Reason)!), PlanCaller(caller),
                cancellationToken);
            if (result.Field == SqlServiceAccountRepository.PreviewDigestMismatch)
            {
                // The stored rows no longer produce the stored digest: refused like a stale preview; nothing of the rows is logged.
                logger.LogError("Service Accounts change plan preview digest mismatch; approval refused. PlanId={PlanId} CorrelationId={CorrelationId}",
                    id, context.CorrelationId);
                result = SaResult<Guid>.Fail(SaErrors.ChangePlanState, "previewStale");
            }

            return await AfterCommandAsync(caller, id, result, cancellationToken);
        }, cancellationToken);

    /// <summary>Cancels an open plan: the planner (Work) or a verifier (Verify), with a reason.</summary>
    public Task<SaResult<ChangePlanView>> CancelChangePlanAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, CancelChangePlanRequest request,
        CancellationToken cancellationToken) =>
        PlansAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
            ServiceAccountText.Clean(request.Reason) is not { Length: <= ChangePlanRules.MaxReason } reason
                ? SaResult<ChangePlanView>.Fail(SaErrors.Invalid, "reason")
                : await AfterCommandAsync(caller, id, await repository!.CancelChangePlanAsync(id, request.ExpectedVersion ?? string.Empty, reason, PlanCaller(caller),
                    cancellationToken), cancellationToken), cancellationToken);

    /// <summary>Capability check (RunAsync), then "not installed" while migration 033 is missing.</summary>
    private Task<SaResult<T>> PlansAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, string capability,
        Func<SaCaller, Task<SaResult<T>>> operation, CancellationToken cancellationToken) =>
        RunAsync(principal, context, capability, async caller =>
            await repository!.ChangePlansInstalledAsync(cancellationToken) ? await operation(caller) : SaResult<T>.Fail(SaErrors.ChangePlansNotInstalled),
            cancellationToken);

    /// <summary>The caller's view after a command; a 409 carries the current view the caller may already read, nothing else.</summary>
    private async Task<SaResult<ChangePlanView>> AfterCommandAsync(SaCaller caller, Guid id, SaResult<Guid> result, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            return await PlanViewAsync(caller, id, cancellationToken);
        }

        object? current = result.ErrorCode is SaErrors.Conflict or SaErrors.ChangePlanState
            ? (await PlanViewAsync(caller, id, cancellationToken)).Value
            : result.ErrorCode == SaErrors.ChangePlanAccountsRefused ? result.Current : null;
        return SaResult<ChangePlanView>.Fail(result.ErrorCode!, result.Field, current);
    }

    private async Task<SaResult<ChangePlanView>> PlanViewAsync(SaCaller caller, Guid id, CancellationToken cancellationToken)
    {
        if (await VisiblePlanAsync(caller, id, cancellationToken) is not { } plan)
        {
            return SaResult<ChangePlanView>.Fail(SaErrors.NotFound);
        }

        bool responsible = plan.Accounts.All(a => SqlServiceAccountRepository.PlanResponsible(caller.Scope, a.Anchor));
        bool editable = plan.Status is ChangePlanStatus.Draft or ChangePlanStatus.Previewed;
        bool verifier = caller.Can(ServiceAccountCapabilities.Verify) && responsible && plan.Status == ChangePlanStatus.Previewed;
        string? separation = ChangePlanRules.ApproverRefusal(caller.User.Id, plan.CreatedById, plan.PreviewCreatedById ?? Guid.Empty, plan.Editors);
        bool work = caller.Can(ServiceAccountCapabilities.Work) && responsible;
        ChangePlanPermissions permissions = new(work && editable, work && editable, verifier && separation is null, verifier ? separation : null,
            responsible && ChangePlanRules.IsOpen(plan.Status) && SqlServiceAccountRepository.MayCancel(plan, PlanCaller(caller)));
        return new ChangePlanView(plan.Id, plan.Kind, plan.Title, plan.Status.ToString(), ChangePlanRules.StatusLabel(plan.Status), plan.CreatedBy, plan.CreatedAt,
            plan.UpdatedAt, plan.Version, [.. plan.Accounts.Select(a => a.View)], plan.Preview, plan.Approval, plan.Events, permissions);
    }

    /// <summary>The plan when every one of its accounts is in the caller's scope; otherwise null (same as missing).</summary>
    private async Task<ChangePlanSnapshot?> VisiblePlanAsync(SaCaller caller, Guid id, CancellationToken cancellationToken) =>
        await repository!.ChangePlanAsync(id, cancellationToken) is { Accounts.Count: > 0 } plan && plan.Accounts.All(a => caller.Scope.Covers(a.Anchor))
            ? plan
            : null;

    private static ChangePlanCaller PlanCaller(SaCaller caller) =>
        new(caller.Actor, caller.Can(ServiceAccountCapabilities.Work), caller.Can(ServiceAccountCapabilities.Verify));

    /// <summary>1–20 accounts with real identifiers; per-account rules are answered inside the transaction.</summary>
    private static string? AccountListError(IReadOnlyList<ChangePlanAccountInput>? accounts) =>
        accounts is null || accounts.Count is 0 or > ChangePlanRules.MaxAccounts || accounts.Any(a => a is null || a.AccountId == Guid.Empty) ? "accounts" : null;
}
