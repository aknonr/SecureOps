using System.Security.Claims;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    private static readonly string[] _accountClearable = ["notes", "consumerTeam"];
    private static readonly string[] _requestClearable = ["targetTeam", "followupPerson", "contactPerson", "plan", "planAnnouncedOn", "nextFollowupOn", "notes", "requestedGmsaName"];

    /// <summary>Scoped, server-paged account list.</summary>
    public Task<SaResult<AccountPage>> AccountsAsync(ClaimsPrincipal principal, AccessOperationContext context, AccountListQuery query,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
            query.Page < 1 || query.PageSize is < 1 or > 100 || !ValidText(query.Search, 100) || !SqlServiceAccountRepository.ValidListQuery(query)
                ? SaResult<AccountPage>.Fail(SaErrors.Invalid, "query")
                : new SaResult<AccountPage>(await repository!.ListAccountsAsync(caller.Scope, query, Today, cancellationToken)), cancellationToken);

    /// <summary>Scoped account detail; out-of-scope and missing are indistinguishable.</summary>
    public Task<SaResult<AccountDetail>> AccountAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, caller => DetailAsync(caller, id, cancellationToken), cancellationToken);

    private async Task<SaResult<AccountDetail>> DetailAsync(SaCaller caller, Guid id, CancellationToken cancellationToken)
    {
        if (await repository!.AnchorAsync(id, cancellationToken) is not { } anchor || !caller.Scope.Covers(anchor.Anchor))
        {
            return SaResult<AccountDetail>.Fail(SaErrors.NotFound);
        }

        AccountDetail? detail = await repository.AccountDetailAsync(id, Today, cancellationToken);
        return detail is null ? SaResult<AccountDetail>.Fail(SaErrors.NotFound)
            : await WithRulesAsync(detail with { Permissions = Permissions(caller, anchor.Anchor, detail.Requests) }, cancellationToken);
    }

    /// <summary>
    /// Visibility is not authority. Organization-level scope or the confirmed owner team is responsible for the whole
    /// account; a team that sees the account only through a request targeted at it (or an incoming handover) is a
    /// participant limited to those open requests.
    /// </summary>
    private static AccountPermissions Permissions(SaCaller caller, AccountScopeAnchor anchor, IReadOnlyList<RequestView> requests)
    {
        bool orgLevel = caller.Scope.CoversAtOrganizationLevel(anchor);
        bool responsible = orgLevel || caller.Scope.CoversTeam(anchor.OwnerTeamId);
        Guid[] participant = responsible ? [] : [.. requests.Where(r => r.Status == "Open" && caller.Scope.CoversTeam(r.TargetTeam?.Id)).Select(r => r.Id)];
        bool incomingHandover = anchor.IncomingHandoverTeamIds.Any(t => caller.Scope.CoversTeam(t));
        bool work = caller.Can(ServiceAccountCapabilities.Work);
        return new AccountPermissions(
            work && responsible,
            caller.Can(ServiceAccountCapabilities.Assign) && responsible,
            caller.Can(ServiceAccountCapabilities.Assign) && orgLevel,
            caller.Can(ServiceAccountCapabilities.Verify) && responsible,
            caller.Can(ServiceAccountCapabilities.Assign) && (orgLevel || incomingHandover),
            work && (responsible || participant.Length > 0),
            responsible ? ServiceAccountAccessBasis.Responsible
                : participant.Length > 0 || incomingHandover ? ServiceAccountAccessBasis.Participant : ServiceAccountAccessBasis.Viewer,
            work ? participant : []);
    }

    /// <summary>Runs an account-scoped mutation after capability and permission checks; conflicts return the caller's current view.</summary>
    private Task<SaResult<AccountDetail>> MutateAccountAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, string capability,
        Func<AccountPermissions, bool> allowed, Func<SaCaller, AccountDetail, Task<SaResult<Guid>>> mutation, CancellationToken cancellationToken) =>
        RunAsync(principal, context, capability, async caller =>
        {
            SaResult<AccountDetail> before = await DetailAsync(caller, accountId, cancellationToken);
            if (!before.IsSuccess)
            {
                return before;
            }

            if (!allowed(before.Value!.Permissions))
            {
                return SaResult<AccountDetail>.Fail(SaErrors.Forbidden, "scope");
            }

            SaResult<Guid> result = await mutation(caller, before.Value);
            SaResult<AccountDetail> after = await DetailAsync(caller, accountId, cancellationToken);
            return result.IsSuccess ? after : SaResult<AccountDetail>.Fail(result.ErrorCode!, result.Field, result.ErrorCode == SaErrors.Conflict ? after.Value : result.Current);
        }, cancellationToken);

    /// <summary>Creates an account manually (coordinator scope).</summary>
    public Task<SaResult<AccountDetail>> CreateAccountAsync(ClaimsPrincipal principal, AccessOperationContext context, CreateAccountRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Assign, async caller =>
        {
            if (!ValidText(request.AccountName, 256, true) || !ValidText(request.Domain, 128) || !ValidText(request.Reason, 1000, true))
            {
                return SaResult<AccountDetail>.Fail(SaErrors.Invalid, "accountName");
            }

            if (!caller.Scope.All && (request.ReportOrganizationId is not { } org || !caller.Scope.Organizations.Contains(org)))
            {
                return SaResult<AccountDetail>.Fail(SaErrors.Forbidden, "reportOrganizationId");
            }

            SaResult<Guid> created = await repository!.CreateAccountAsync(request.AccountName, request.Domain, request.ReportOrganizationId, request.Reason, caller.Actor, cancellationToken);
            return created.IsSuccess ? await DetailAsync(caller, created.Value, cancellationToken) : SaResult<AccountDetail>.Fail(created.ErrorCode!, created.Field);
        }, cancellationToken);

    /// <summary>Updates account attributes (rule 11: blank never erases; clearing needs a reason).</summary>
    public Task<SaResult<AccountDetail>> UpdateAccountAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, UpdateAccountRequest request,
        CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, id, ServiceAccountCapabilities.Work, p => p.Work && (request.ReportOrganizationId is null || p.AssignTeam), (caller, _) =>
        {
            IReadOnlyList<string> clear = request.ClearFields ?? [];
            if (clear.Any(c => !_accountClearable.Contains(c, StringComparer.Ordinal)) || clear.Count > 0 && !ValidText(request.Reason, 1000, true)
                || !ValidText(request.Notes, 2000) || !ValidText(request.Domain, 128) || References(request.AddReferences) is not { } references)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, clear.Count > 0 ? "reason" : "notes"));
            }

            return repository!.UpdateAccountAsync(id, new AccountChange(request.ExpectedVersion, request.Notes, request.ConsumerTeamId, request.ReportOrganizationId,
                request.Domain, clear.Contains("notes"), clear.Contains("consumerTeam"), request.Reason, references), caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Proposes (Work) or confirms (Assign within scope) ownership; names never confirm automatically.</summary>
    public Task<SaResult<AccountDetail>> ChangeOwnershipAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, OwnershipChangeRequest request,
        CancellationToken cancellationToken)
    {
        bool confirm = request.Mode == "Confirm";
        return MutateAccountAsync(principal, context, id, confirm ? ServiceAccountCapabilities.Assign : ServiceAccountCapabilities.Work,
            p => confirm ? p.AssignPerson : p.Work, (caller, detail) =>
            {
                if (request.Mode is not ("Propose" or "Confirm") || request.TeamId is null && request.PersonId is null || !ValidText(request.Reason, 1000, true)
                    || !ValidText(request.EvidenceNote, 1000))
                {
                    return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "mode"));
                }

                // A team lead may confirm people within the current owner team; changing the owner team needs organization-level scope.
                bool teamChange = request.TeamId is { } team && team != detail.Summary.OwnerTeam?.Id;
                if (confirm && teamChange && !detail.Permissions.AssignTeam)
                {
                    return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Forbidden, "teamId"));
                }

                return repository!.ChangeOwnershipAsync(id, request.ExpectedVersion, request.TeamId ?? (confirm ? detail.Summary.OwnerTeam?.Id : null), request.PersonId,
                    confirm, request.Reason.Trim(), request.EffectiveFrom, request.EvidenceNote, Today, caller.Actor, cancellationToken);
            }, cancellationToken);
    }

    /// <summary>Confirms or rejects a proposed ownership (conflicts are audited decisions).</summary>
    public Task<SaResult<AccountDetail>> DecideOwnershipAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, Guid assignmentId,
        OwnershipDecisionRequest request, CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Assign, p => p.AssignPerson, (caller, detail) =>
        {
            OwnershipView? proposal = detail.Ownership.FirstOrDefault(o => o.Id == assignmentId && o.State == "Proposed");
            if (proposal is null || request.Decision is not ("Confirm" or "Reject") || !ValidText(request.Reason, 1000, true))
            {
                return Task.FromResult(SaResult<Guid>.Fail(proposal is null ? SaErrors.NotFound : SaErrors.Invalid, "decision"));
            }

            if (request.Decision == "Confirm" && proposal.Team is { } team && team.Id != detail.Summary.OwnerTeam?.Id && !detail.Permissions.AssignTeam)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Forbidden, "teamId"));
            }

            return repository!.DecideOwnershipAsync(accountId, assignmentId, request.ExpectedVersion, request.Decision == "Confirm", request.Reason.Trim(), Today,
                caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Creates a request; the controlled vocabulary drives the action, free text stays separate.</summary>
    public Task<SaResult<AccountDetail>> CreateRequestAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, CreateWorkRequest request,
        CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Work, p => p.Work, (caller, detail) =>
        {
            if (!Enum.TryParse(request.ActionType, false, out ServiceAccountActionType type))
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "actionType"));
            }

            if (ServiceAccountRules.ValidatePlan(request.PlanStart, request.PlanEnd) is { } planError)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, planError));
            }

            if (RequestedGmsaNameError(request.RequestedGmsaName, type) is { } nameError)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, nameError));
            }

            return !ValidText(request.Notes, 4000) || References(request.References) is null
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "notes"))
                : repository!.CreateRequestAsync(accountId, request, type, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Partial request update at the expected version.</summary>
    public Task<SaResult<AccountDetail>> UpdateRequestAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid requestId, UpdateWorkRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Request", requestId, ServiceAccountCapabilities.Work, p => p.CanWorkRequest(requestId), (caller, detail, accountId) =>
        {
            IReadOnlyList<string> clear = request.ClearFields ?? [];
            // A participant team works on its own request but cannot move it to another team or change what is expected.
            if (!detail.Permissions.Work && (request.TargetTeamId is not null || request.ActionType is not null || clear.Contains("targetTeam")))
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Forbidden, request.ActionType is not null ? "actionType" : "targetTeamId"));
            }

            ServiceAccountActionType? type = request.ActionType is null ? null : Enum.TryParse(request.ActionType, false, out ServiceAccountActionType parsed) ? parsed : (ServiceAccountActionType?)null;
            RequestView? current = detail.Requests.FirstOrDefault(r => r.Id == requestId);
            string? invalid = current is null ? "id"
                : request.ActionType is not null && type is null ? "actionType"
                : clear.Any(c => !_requestClearable.Contains(c, StringComparer.Ordinal)) ? "clearFields"
                : clear.Count > 0 && !ValidText(request.Reason, 1000, true) ? "reason"
                : !ValidText(request.Notes, 4000) ? "notes"
                : References(request.AddReferences) is null ? "addReferences"
                : RequestedGmsaNameError(request.RequestedGmsaName, type ?? Enum.Parse<ServiceAccountActionType>(current.ActionType)) is { } name ? name
                : StoredNameBlocksType(current.RequestedGmsaName, clear, type) ? "requestedGmsaNameTypeConflict"
                : !clear.Contains("plan") && ServiceAccountRules.ValidatePlan(request.PlanStart ?? current.PlanStart, request.PlanEnd ?? current.PlanEnd) is { } plan ? plan
                : null;
            if (invalid is not null)
            {
                return Task.FromResult(SaResult<Guid>.Fail(current is null ? SaErrors.NotFound : SaErrors.Invalid, invalid));
            }

            IReadOnlyList<SaExternalRef> references = References(request.AddReferences)!;

            return repository!.UpdateRequestAsync(accountId, requestId, new RequestChange(request.ExpectedVersion, type, request.TargetTeamId, request.FollowupPersonId,
                request.ContactPersonId, request.PlanStart, request.PlanEnd, request.PlanAnnouncedOn, request.NextFollowupOn, request.FirstSentOn, request.LastReplyOn,
                request.Notes, clear.ToHashSet(StringComparer.Ordinal), request.Reason, references, request.RequestedGmsaName), caller.Actor,
                cancellationToken);
        }, cancellationToken);

    /// <summary>Explicitly closes one request after checking its completion conditions (rule 9).</summary>
    public Task<SaResult<AccountDetail>> CloseRequestAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid requestId, CloseWorkRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Request", requestId, ServiceAccountCapabilities.Work, p => p.Work, async (caller, _, accountId) =>
        {
            if (!Enum.TryParse(request.Outcome, false, out ServiceAccountCloseOutcome outcome) || !ValidText(request.Reason, 1000))
            {
                return SaResult<Guid>.Fail(SaErrors.Invalid, "outcome");
            }

            if (await repository!.RequestCloseFactsAsync(requestId, cancellationToken) is not { } facts)
            {
                return SaResult<Guid>.Fail(SaErrors.NotFound);
            }

            string? rule = ServiceAccountRules.ValidateClose(facts.Status, facts.Type, outcome, request.Reason, facts.Actions, facts.OwnershipConfirmed);
            return rule is not null
                ? SaResult<Guid>.Fail(SaErrors.Invalid, rule)
                : await repository.CloseRequestAsync(accountId, requestId, request.ExpectedVersion, outcome, request.Reason?.Trim(), caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Resolves an entity's account, checks scope/permissions and runs the mutation.</summary>
    private Task<SaResult<AccountDetail>> EntityAsync(ClaimsPrincipal principal, AccessOperationContext context, string entityType, Guid entityId, string capability,
        Func<AccountPermissions, bool> allowed, Func<SaCaller, AccountDetail, Guid, Task<SaResult<Guid>>> mutation, CancellationToken cancellationToken) =>
        RunAsync(principal, context, capability, async caller =>
        {
            if (await repository!.AccountOfAsync(entityType, entityId, cancellationToken) is not { } accountId)
            {
                return SaResult<AccountDetail>.Fail(SaErrors.NotFound);
            }

            SaResult<AccountDetail> before = await DetailAsync(caller, accountId, cancellationToken);
            if (!before.IsSuccess)
            {
                return before;
            }

            if (!allowed(before.Value!.Permissions))
            {
                return SaResult<AccountDetail>.Fail(SaErrors.Forbidden, "scope");
            }

            SaResult<Guid> result = await mutation(caller, before.Value, accountId);
            SaResult<AccountDetail> after = await DetailAsync(caller, accountId, cancellationToken);
            return result.IsSuccess ? after : SaResult<AccountDetail>.Fail(result.ErrorCode!, result.Field, result.ErrorCode == SaErrors.Conflict ? after.Value : result.Current);
        }, cancellationToken);

    /// <summary>
    /// Requested gMSA name rule (the server decides; the UI only warns): blank means none, otherwise only on gMSA work, at most
    /// <see cref="ServiceAccountGmsaName.Limit"/> counted characters (no domain prefix, UPN suffix or trailing $) and at most
    /// <see cref="ServiceAccountGmsaName.MaxStoredLength"/> stored. Returns the invalid field or null.
    /// </summary>
    private static string? RequestedGmsaNameError(string? value, ServiceAccountActionType? type)
    {
        if (ServiceAccountText.Clean(value) is not { } name)
        {
            return null;
        }

        return name.Length > ServiceAccountGmsaName.MaxStoredLength || ServiceAccountGmsaName.Length(name) is 0 or > ServiceAccountGmsaName.Limit
            || type is not null and not (ServiceAccountActionType.GmsaHandover or ServiceAccountActionType.GmsaConversion)
            ? "requestedGmsaName"
            : null;
    }

    /// <summary>
    /// A request that already records a requested gMSA name cannot be moved to a work type that is not gMSA work; the name is never
    /// dropped silently. The caller clears the name first (clearFields requestedGmsaName with a reason), then changes the type.
    /// </summary>
    private static bool StoredNameBlocksType(string? storedName, IReadOnlyList<string> clear, ServiceAccountActionType? newType) =>
        newType is not null and not (ServiceAccountActionType.GmsaHandover or ServiceAccountActionType.GmsaConversion)
        && !clear.Contains("requestedGmsaName") && ServiceAccountText.Clean(storedName) is not null;

    /// <summary>Validates external references: known type, bounded number, Jira key format for JIRA, HTTPS links only.</summary>
    private static IReadOnlyList<SaExternalRef>? References(IReadOnlyList<SaExternalRef>? references)
    {
        IReadOnlyList<SaExternalRef> list = references ?? [];
        if (list.Count > 10)
        {
            return null;
        }

        foreach (SaExternalRef reference in list)
        {
            if (!Enum.TryParse(reference.Type, false, out ExternalRecordType type) || ServiceAccountText.RecordNumber(reference.Number) is not { Length: <= 64 } number
                || type == ExternalRecordType.JIRA && !System.Text.RegularExpressions.Regex.IsMatch(number, "^[A-Z][A-Z0-9_]{1,15}-[0-9]{1,9}$",
                    System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50))
                || reference.Url is { } url && (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || url.Length > 400))
            {
                return null;
            }
        }

        return list;
    }
}
