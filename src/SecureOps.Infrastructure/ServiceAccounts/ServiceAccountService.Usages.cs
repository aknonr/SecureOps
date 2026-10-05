using System.Security.Claims;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class ServiceAccountService
{
    /// <summary>Internal whole-module scope used only after the caller's own scope check passed for that account.</summary>
    private static readonly ServiceAccountScope _accountOnlyScope = ServiceAccountScope.Resolve([new ScopeGrant(ScopeKind.All, null, null)], [], []);

    private static readonly string[] _teamRoles = [ServiceAccountTeamRoles.SqlTeam, ServiceAccountTeamRoles.GmsaExecutor];

    /// <summary>Configured risk thresholds; a non-positive value is a configuration error (fails closed).</summary>
    private RiskThresholds Thresholds => _options.RiskLogonDays > 0 && _options.RiskPasswordDays > 0
        ? new RiskThresholds(_options.RiskLogonDays, _options.RiskPasswordDays)
        : throw new InvalidOperationException("ServiceAccounts risk thresholds must be positive.");

    /// <summary>
    /// Adds usages and the rule evaluation to an already scope-checked detail. The evaluation uses the report facts and
    /// rule implementation narrowed to this one account, so the detail and the reports never disagree.
    /// </summary>
    private async Task<SaResult<AccountDetail>> WithRulesAsync(AccountDetail detail, CancellationToken cancellationToken)
    {
        Guid id = detail.Summary.Id;
        IReadOnlyList<UsageView> usages = await repository!.UsagesAsync(id, cancellationToken);
        (IReadOnlyList<UsageScanView>? scans, int scanTotal) = await repository.UsageScansAsync(id, cancellationToken);
        detail = detail with { UsageScans = scans, UsageScanTotal = scanTotal };
        (ReportFacts facts, _) = await repository.ReportFactsAsync(_accountOnlyScope, null, null, Thresholds, id, cancellationToken);
        Dictionary<Guid, AccountRuleEvaluation> rules = ServiceAccountInsights.Evaluate(facts, facts.Insights!);
        if (!rules.TryGetValue(id, out AccountRuleEvaluation? rule))
        {
            return detail with { Usages = usages };
        }

        string? stage = ServiceAccountInsights.FunnelStages(facts, rules).GetValueOrDefault(id);
        return detail with
        {
            Usages = usages,
            Rule = new RuleEvaluationView(ServiceAccountUsageRules.RuleSetVersion, rule.Path?.ToString(),
                rule.Path is { } path ? ServiceAccountUsageRules.PathLabel(path) : null, rule.Conformance.ToString(),
                ServiceAccountUsageRules.ConformanceLabel(rule.Conformance),
                [.. rule.Items.Select(i => new RuleItemView(i.RuleCode, i.Path.ToString(), ServiceAccountUsageRules.PathLabel(i.Path), i.Reason, i.UsageId, i.Excepted))],
                stage is null ? null : ServiceAccountInsights.StageLabel(stage), facts.Insights!.ExecutorTeam, facts.Insights.SqlTeamAccounts.Contains(id))
        };
    }

    /// <summary>Records where the account is used (responsible scope with Work).</summary>
    public Task<SaResult<AccountDetail>> CreateUsageAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, CreateUsageRequest request,
        CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Work, p => p.Work, (caller, _) =>
        {
            (UsageKind kind, DatabaseEngine? engine, bool? needVerified, string? invalid) = UsageFields(request.Kind, request.DatabaseEngine, request.NeedVerified);
            invalid ??= !ValidText(request.Server, 256) ? "server"
                : !ValidText(request.Component, 256) ? "component"
                : !ValidText(request.Notes, 1000) ? "notes"
                : null;
            return invalid is not null
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, invalid))
                : repository!.CreateUsageAsync(accountId, kind, engine, needVerified, request, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// Kind and kind-specific fields of a usage, the same for a manual usage and one recorded from a scan: engine only for a
    /// database (default Unknown), the need flag only for a Windows service (default false).
    /// </summary>
    private static (UsageKind Kind, DatabaseEngine? Engine, bool? NeedVerified, string? Invalid) UsageFields(string kindText, string? engineText, bool? needVerified)
    {
        if (!Enum.TryParse(kindText, false, out UsageKind kind) || !Enum.IsDefined(kind))
        {
            return (default, null, null, "kind");
        }

        DatabaseEngine? engine = null;
        if (kind == UsageKind.Database)
        {
            if (!Enum.TryParse(engineText ?? nameof(DatabaseEngine.Unknown), false, out DatabaseEngine parsed) || !Enum.IsDefined(parsed))
            {
                return (kind, null, null, "databaseEngine");
            }

            engine = parsed;
        }

        string? invalid = kind != UsageKind.Database && engineText is not null ? "databaseEngine"
            : kind != UsageKind.WindowsService && needVerified is not null ? "needVerified"
            : null;
        return (kind, engine, kind == UsageKind.WindowsService ? needVerified ?? false : null, invalid);
    }

    /// <summary>Updates an active usage at the expected version.</summary>
    public Task<SaResult<AccountDetail>> UpdateUsageAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, UpdateUsageRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Usage", id, ServiceAccountCapabilities.Work, p => p.Work, (caller, detail, accountId) =>
        {
            UsageView? current = detail.Usages?.FirstOrDefault(u => u.Id == id && !u.Removed);
            DatabaseEngine? engine = Enum.TryParse(request.DatabaseEngine, false, out DatabaseEngine parsed) && Enum.IsDefined(parsed) ? parsed : null;
            string? invalid = current is null ? "id"
                : request.DatabaseEngine is not null && (current.Kind != nameof(UsageKind.Database) || engine is null) ? "databaseEngine"
                : request.NeedVerified is not null && current.Kind != nameof(UsageKind.WindowsService) ? "needVerified"
                : !ValidText(request.Server, 256) ? "server"
                : !ValidText(request.Component, 256) ? "component"
                : !ValidText(request.Notes, 1000) ? "notes"
                : null;
            return invalid is not null
                ? Task.FromResult(SaResult<Guid>.Fail(current is null ? SaErrors.NotFound : SaErrors.Invalid, invalid))
                : repository!.UpdateUsageAsync(accountId, id, engine, request, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Removes a usage with a reason (kept for history).</summary>
    public Task<SaResult<AccountDetail>> RemoveUsageAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, RemoveUsageRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Usage", id, ServiceAccountCapabilities.Work, p => p.Work, (caller, _, accountId) =>
            !ValidText(request.Reason, 1000, true)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "reason"))
                : repository!.RemoveUsageAsync(accountId, id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Records or clears a reasoned rule exception (Verify capability in responsible scope).</summary>
    public Task<SaResult<AccountDetail>> SetUsageExceptionAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, UsageExceptionRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Usage", id, ServiceAccountCapabilities.Verify, p => p.Verify, (caller, _, accountId) =>
            !ValidText(request.Reason, 1000, true)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "reason"))
                : repository!.SetUsageExceptionAsync(accountId, id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Active team roles (module dictionary).</summary>
    public Task<SaResult<IReadOnlyList<TeamRoleView>>> TeamRolesAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async _ => new SaResult<IReadOnlyList<TeamRoleView>>(
            await repository!.TeamRolesAsync(cancellationToken)), cancellationToken);

    /// <summary>Assigns a team role (administration); a role is configuration and never grants access.</summary>
    public Task<SaResult<Guid>> CreateTeamRoleAsync(ClaimsPrincipal principal, AccessOperationContext context, CreateTeamRoleRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, async caller =>
        {
            if (!_teamRoles.Contains(request.Role, StringComparer.Ordinal) || !ValidText(request.Reason, 1000, true))
            {
                return SaResult<Guid>.Fail(SaErrors.Invalid, _teamRoles.Contains(request.Role, StringComparer.Ordinal) ? "reason" : "role");
            }

            return (await repository!.TeamRefsAsync([request.TeamId], cancellationToken)).Count == 0
                ? SaResult<Guid>.Fail(SaErrors.Invalid, "teamId")
                : await repository.CreateTeamRoleAsync(request, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Revokes a team role with a reason (administration).</summary>
    public Task<SaResult<Guid>> RevokeTeamRoleAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, RevokeTeamRoleRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Administer, async caller => !ValidText(request.Reason, 1000, true)
            ? SaResult<Guid>.Fail(SaErrors.Invalid, "reason")
            : !(await repository!.TeamRolesAsync(cancellationToken)).Any(r => r.Id == id)
            ? SaResult<Guid>.Fail(SaErrors.NotFound)
            : await repository.RevokeTeamRoleAsync(id, request.Reason, caller.Actor, cancellationToken), cancellationToken);
}
