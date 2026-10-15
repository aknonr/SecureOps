using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>OLAY-2a: <c>GET requests/{id}/close-readiness</c> against real SQL — same rule as the close command, scope-checked, read only.</summary>
public sealed class ServiceAccountCloseReadinessSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];

    [ServiceAccountSqlFact]
    public async Task DeletionRequest_ListsEveryMissingCondition_UntilTheVerifiedClosure_ThenAlreadyClosed()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN CR ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_CLOSEREADY", null, org, "Sentetik test hesabı"), _token));
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("Deletion"), _token));
        Guid request = account.Requests.Single().Id;

        CloseReadiness empty = Ok(await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, null, _token));
        empty.Should().Match<CloseReadiness>(r => !r.CanClose && r.Status == "Open" && r.Outcome == "Completed");
        empty.Missing.Select(m => m.Code).Should().Equal(ServiceAccountRules.Errors.NoMatchingAction, ServiceAccountRules.Errors.VerifiedClosureRequired);
        empty.Missing[0].Should().Be(new CloseMissing(ServiceAccountRules.Errors.NoMatchingAction, "ReportAction", null, "Deletion"));
        await CloseRefusedWithFirstMissingAsync(fx, coordinator, account.Summary.Id, request, empty);

        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new ReportActionRequest("Deletion", "Performed", "Closure", request, ActualOn: new(2026, 9, 12), EvidenceNote: "silme bildirimi"), _token));
        ActionView deletion = account.Actions.Single();
        CloseReadiness reported = Ok(await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, "Completed", _token));
        reported.Missing.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Code = ServiceAccountRules.Errors.VerifiedClosureRequired,
            Remedy = "VerifyAction",
            SubCodes = new[] { ServiceAccountRules.Errors.NotVerified, ServiceAccountRules.Errors.VerifierRequired, ServiceAccountRules.Errors.OrRequiredForDeletion },
            RelatedActionId = (Guid?)deletion.Id
        });
        await CloseRefusedWithFirstMissingAsync(fx, coordinator, account.Summary.Id, request, reported);

        account = Ok(await fx.Service.UpdateActionAsync(coordinator.Principal, fx.Context, deletion.Id,
            new UpdateActionRequest(deletion.Version, AddReferences: [new SaExternalRef("OR", "OR-SYN-" + fx.Suffix)]), _token));
        deletion = account.Actions.Single();
        Ok(await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, null, _token)).Missing.Single().SubCodes
            .Should().Equal(ServiceAccountRules.Errors.NotVerified, ServiceAccountRules.Errors.VerifierRequired);

        account = Ok(await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, deletion.Id,
            new VerifyActionRequest(deletion.Version, new(2026, 9, 13), VerificationNote: "AD'de yok, OR kapalı"), _token));
        CloseReadiness ready = Ok(await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, null, _token));
        ready.CanClose.Should().BeTrue();
        ready.Missing.Should().BeEmpty();
        Ok(await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, "NotNeeded", _token)).Missing.Should().ContainSingle()
            .Which.Should().Be(new CloseMissing(ServiceAccountRules.Errors.ReasonRequired, "GiveReason"), "a reason is typed at close time");

        Ok(await fx.Service.CloseRequestAsync(coordinator.Principal, fx.Context, request,
            new CloseWorkRequest(account.Requests.Single().Version, "Completed"), _token));
        CloseReadiness closed = Ok(await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, null, _token));
        closed.Status.Should().Be("Closed");
        closed.Missing.Should().ContainSingle().Which.Should().Be(new CloseMissing(ServiceAccountRules.Errors.AlreadyClosed, "None"));
    }

    [ServiceAccountSqlFact]
    public async Task Readiness_IsScopeChecked_ValidatesOutcome_AndWritesNothing()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN CR SCOPE " + fx.Suffix);
        Guid ownerTeam = await fx.TeamAsync("SYN CR SAHIP " + fx.Suffix, org), otherTeam = await fx.TeamAsync("SYN CR DIGER " + fx.Suffix, null);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_CRSCOPE", null, org, "Sentetik test hesabı"), _token));
        account = Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new OwnershipChangeRequest(account.Summary.Version, ownerTeam, null, "Confirm", "Sentetik sahiplik kararı"), _token));
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("PasswordChange"), _token));
        Guid request = account.Requests.Single().Id;

        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(viewer, ScopeKind.Team, team: ownerTeam);
        CloseReadiness seen = Ok(await fx.Service.CloseReadinessAsync(viewer.Principal, fx.Context, request, null, _token));
        seen.Missing.Single().Code.Should().Be(ServiceAccountRules.Errors.NoMatchingAction, "View is enough to read why a visible request cannot close");

        SynUser outsider = await fx.UserAsync(_all);
        await fx.GrantAsync(outsider, ScopeKind.Team, team: otherTeam);
        (await fx.Service.CloseReadinessAsync(outsider.Principal, fx.Context, request, null, _token)).ErrorCode
            .Should().Be(SaErrors.NotFound, "out of scope and missing are indistinguishable");
        (await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, Guid.NewGuid(), null, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        SynUser noView = await fx.UserAsync(ServiceAccountCapabilities.Report);
        await fx.GrantAsync(noView, ScopeKind.Organization, org);
        (await fx.Service.CloseReadinessAsync(noView.Principal, fx.Context, request, null, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);

        foreach (string bad in new[] { "completed", "1", "Completed,NotNeeded", "Bilinmiyor", "" })
        {
            SaResult<CloseReadiness> invalid = await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, bad, _token);
            invalid.ErrorCode.Should().Be(SaErrors.Invalid, bad);
            invalid.Field.Should().Be("outcome");
        }

        object accountId = new { accountId = account.Summary.Id };
        int history = await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @accountId", accountId);
        object actor = new { a = coordinator.User.Id.ToString("D") };
        int audit = await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a", actor);
        Ok(await fx.Service.CloseReadinessAsync(coordinator.Principal, fx.Context, request, "Cancelled", _token));
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @accountId", accountId)).Should().Be(history);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a", actor)).Should().Be(audit, "reading readiness is not an audited action");
        Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, account.Summary.Id, _token)).Requests.Single().Version
            .Should().Be(account.Requests.Single().Version);
    }

    [ServiceAccountSqlFact]
    public async Task AccountDetail_CarriesEachOpenRequestsReadiness_EqualToTheRoute_AndUnverifiedActionsVerificationReadiness()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN CR DETAY " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN{fx.Suffix}_CRDETAY", null, org, "Sentetik test hesabı"), _token));
        Guid accountId = account.Summary.Id;
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, accountId, new CreateWorkRequest("Deletion"), _token));
        Guid deletionRequest = account.Requests.Single().Id;
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, accountId, new CreateWorkRequest("PasswordChange"), _token));
        Guid passwordRequest = account.Requests.Single(r => r.Id != deletionRequest).Id;
        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, accountId,
            new ReportActionRequest("Deletion", "Performed", "Closure", deletionRequest, ActualOn: new(2026, 9, 12)), _token));
        account = Ok(await fx.Service.ReportActionAsync(coordinator.Principal, fx.Context, accountId,
            new ReportActionRequest("PasswordChange", "Performed", "Intermediate", passwordRequest, ActualOn: new(2026, 9, 12), EvidenceNote: "parola değişti"), _token));

        account = Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, accountId, _token));
        await DetailMatchesRouteAsync(fx, coordinator, account);
        account.Requests.Single(r => r.Id == passwordRequest).CloseReadiness!.CanClose.Should().BeTrue();
        account.Requests.Single(r => r.Id == deletionRequest).CloseReadiness!.Missing.Single().Code.Should().Be(ServiceAccountRules.Errors.VerifiedClosureRequired);

        ActionView deletion = account.Actions.Single(a => a.ActionType == "Deletion");
        deletion.VerificationReadiness.Should().BeEquivalentTo(new VerificationReadiness(false,
            [ServiceAccountRules.Errors.EvidenceRequired, ServiceAccountRules.Errors.OrRequiredForDeletion]));
        account.Actions.Single(a => a.ActionType == "PasswordChange").VerificationReadiness.Should().BeEquivalentTo(new VerificationReadiness(true, []));
        SaResult<AccountDetail> refused = await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, deletion.Id,
            new VerifyActionRequest(deletion.Version, new(2026, 9, 13)), _token);
        refused.Field.Should().Be(deletion.VerificationReadiness!.Missing[0], "readiness and the verify command use the same rule");

        Ok(await fx.Service.CloseRequestAsync(coordinator.Principal, fx.Context, passwordRequest,
            new CloseWorkRequest(account.Requests.Single(r => r.Id == passwordRequest).Version, "Completed"), _token));
        account = Ok(await fx.Service.VerifyActionAsync(coordinator.Principal, fx.Context, account.Actions.Single(a => a.ActionType == "PasswordChange").Id,
            new VerifyActionRequest(account.Actions.Single(a => a.ActionType == "PasswordChange").Version, new(2026, 9, 13)), _token));
        account.Requests.Single(r => r.Id == passwordRequest).CloseReadiness.Should().BeNull("a closed request carries no readiness");
        account.Actions.Single(a => a.ActionType == "PasswordChange").VerificationReadiness.Should().BeNull("a verified action carries no readiness");
        await DetailMatchesRouteAsync(fx, coordinator, account);
    }

    private static async Task DetailMatchesRouteAsync(ServiceAccountSqlFixture fx, SynUser user, AccountDetail account)
    {
        foreach (RequestView request in account.Requests.Where(r => r.Status == "Open"))
        {
            CloseReadiness route = Ok(await fx.Service.CloseReadinessAsync(user.Principal, fx.Context, request.Id, null, _token));
            request.CloseReadiness.Should().BeEquivalentTo(route, o => o.WithStrictOrdering(), "the detail and the route use the same facts and rule");
        }
    }

    private static async Task CloseRefusedWithFirstMissingAsync(ServiceAccountSqlFixture fx, SynUser user, Guid accountId, Guid request, CloseReadiness readiness)
    {
        AccountDetail current = Ok(await fx.Service.AccountAsync(user.Principal, fx.Context, accountId, _token));
        SaResult<AccountDetail> refused = await fx.Service.CloseRequestAsync(user.Principal, fx.Context, request,
            new CloseWorkRequest(current.Requests.Single(r => r.Id == request).Version, readiness.Outcome), _token);
        refused.ErrorCode.Should().Be(SaErrors.Invalid);
        refused.Field.Should().Be(readiness.Missing[0].Code, "readiness and the close command use the same rule");
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
