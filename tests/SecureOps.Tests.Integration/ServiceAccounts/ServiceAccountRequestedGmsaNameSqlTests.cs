using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Runs only against a disposable database left at migration 030 (harness <c>-ThroughMigration 30</c>).</summary>
public sealed class ServiceAccountSql030FactAttribute : FactAttribute
{
    /// <summary>Environment variable holding the isolated 030 test connection.</summary>
    public const string Variable = "SECUREOPS_SA_SQL_TEST_CONNECTION_030";

    public ServiceAccountSql030FactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = "NOT RUN: isolated Service Accounts SQL database at migration 030 is unavailable.";
        }
    }
}

/// <summary>
/// Requested gMSA name (migration 031) on the real svcacct schema: recorded on gMSA requests and transitions, refused by the
/// server above 15 counted characters or on other work, kept in history and listed in the report; before 031 everything
/// else keeps working and a name is refused honestly with nothing written. Synthetic data only.
/// </summary>
public sealed class ServiceAccountRequestedGmsaNameSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];

    [ServiceAccountSqlFact]
    public async Task Request_RecordsTheName_ServerRefusesLongOrMisplacedNames_AndHistoryKeepsIt()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync(null);
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "REQ");
        Guid id = account.Summary.Id;
        string name = $"SYN\\gmsa_{fx.Suffix[..6]}$";
        account.RequestedGmsaNameAvailable.Should().BeTrue();

        AccountDetail created = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, id,
            new CreateWorkRequest("GmsaConversion", RequestedGmsaName: "  " + name + " "), _token));
        RequestView request = created.Requests.Should().ContainSingle().Subject;
        request.RequestedGmsaName.Should().Be(name);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id AND Action = 'RequestCreated' AND ChangesJson LIKE @n",
            new { id, n = "%gmsa_" + fx.Suffix[..6] + "%" })).Should().Be(1);

        foreach ((string type, string value) in new[] { ("GmsaConversion", "SYN\\gmsa_synapp_reports$"), ("Review", "gmsa_syn$"), ("GmsaHandover", "SYN\\$") })
        {
            SaResult<AccountDetail> refused = await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, id,
                new CreateWorkRequest(type, RequestedGmsaName: value), _token);
            refused.ErrorCode.Should().Be(SaErrors.Invalid);
            refused.Field.Should().Be("requestedGmsaName");
        }

        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @id", new { id })).Should().Be(1, "a refused name writes nothing");

        string renamed = $"gmsa_r{fx.Suffix[..6]}$";
        AccountDetail updated = Ok(await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id,
            new UpdateWorkRequest(request.Version, RequestedGmsaName: renamed), _token));
        updated.Requests.Single().RequestedGmsaName.Should().Be(renamed);
        (await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id,
            new UpdateWorkRequest(updated.Requests.Single().Version, RequestedGmsaName: "gmsa_sixteen_chr$"), _token)).Field.Should().Be("requestedGmsaName");

        RequestView current = updated.Requests.Single();
        (await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id,
            new UpdateWorkRequest(current.Version, ClearFields: ["requestedGmsaName"]), _token)).Field.Should().Be("reason");
        AccountDetail cleared = Ok(await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id,
            new UpdateWorkRequest(current.Version, ClearFields: ["requestedGmsaName"], Reason: "Sentetik: ad henüz belli değil"), _token));
        cleared.Requests.Single().RequestedGmsaName.Should().BeNull();
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id AND Action = 'RequestUpdated' AND ChangesJson LIKE @n",
            new { id, n = "%gmsa_r" + fx.Suffix[..6] + "%" })).Should().Be(1);

        // A recorded name is never dropped silently by a type change: refuse it, keep the name, and allow it once the name is cleared with a reason.
        AccountDetail second = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, id,
            new CreateWorkRequest("GmsaHandover", RequestedGmsaName: "gmsa_keep$"), _token));
        RequestView named = second.Requests.Single(r => r.RequestedGmsaName == "gmsa_keep$");
        SaResult<AccountDetail> blocked = await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, named.Id,
            new UpdateWorkRequest(named.Version, ActionType: "Review"), _token);
        blocked.ErrorCode.Should().Be(SaErrors.Invalid);
        blocked.Field.Should().Be("requestedGmsaNameTypeConflict");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.WorkRequests WHERE Id = @id AND ActionType = 'GmsaHandover' AND RequestedGmsaName = 'gmsa_keep$'",
            new { id = named.Id })).Should().Be(1, "a refused type change writes nothing");
        (await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, named.Id,
            new UpdateWorkRequest(named.Version, ActionType: "GmsaConversion"), _token)).IsSuccess.Should().BeTrue("a move between gMSA work types keeps the name");
        RequestView moved = Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, id, _token)).Requests.Single(r => r.Id == named.Id);
        AccountDetail changed = Ok(await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, named.Id,
            new UpdateWorkRequest(moved.Version, ActionType: "Review", ClearFields: ["requestedGmsaName"], Reason: "Sentetik: tür değişiyor"), _token));
        changed.Requests.Single(r => r.Id == named.Id).Should().Match<RequestView>(r => r.ActionType == "Review" && r.RequestedGmsaName == null);
    }

    [ServiceAccountSqlFact]
    public async Task Transition_RecordsTheName_BlankKeepsIt_AndTheReportListsRequestAndTransition()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync(null);
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "TRN");
        Guid id = account.Summary.Id;
        Guid team = await fx.TeamAsync("SYN GMSA EKIP " + fx.Suffix, org);
        AccountDetail handed = Ok(await fx.Service.CreateHandoverAsync(coordinator.Principal, fx.Context, id, new CreateHandoverRequest(team, TrackGmsa: true), _token));
        TransitionView transition = handed.Transitions.Should().ContainSingle().Subject;
        transition.RequestedGmsaName.Should().BeNull("existing and new tracking rows start without a name");

        string name = $"gmsa_t{fx.Suffix[..6]}$";
        (await fx.Service.UpdateTransitionAsync(coordinator.Principal, fx.Context, transition.Id,
            new TransitionUpdateRequest(transition.Version, "Review", RequestedGmsaName: "SYN\\gmsa_synapp_reports$"), _token)).Field.Should().Be("requestedGmsaName");
        AccountDetail named = Ok(await fx.Service.UpdateTransitionAsync(coordinator.Principal, fx.Context, transition.Id,
            new TransitionUpdateRequest(transition.Version, "Review", RequestedGmsaName: name), _token));
        TransitionView after = named.Transitions.Single();
        after.RequestedGmsaName.Should().Be(name);
        AccountDetail kept = Ok(await fx.Service.UpdateTransitionAsync(coordinator.Principal, fx.Context, transition.Id,
            new TransitionUpdateRequest(after.Version, "Review", PlannedOn: new DateOnly(2026, 11, 2)), _token));
        kept.Transitions.Single().RequestedGmsaName.Should().Be(name, "a blank name never erases the stored one");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @id AND Action = 'TransitionUpdated' AND ChangesJson LIKE @n",
            new { id, n = "%gmsa_t" + fx.Suffix[..6] + "%" })).Should().Be(1);

        string requested = $"gmsa_q{fx.Suffix[..6]}$";
        Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, id, new CreateWorkRequest("GmsaHandover", team, RequestedGmsaName: requested), _token));
        ServiceAccountReport report = Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context,
            new WeeklyReportQuery(new(2026, 10, 5), OrganizationId: org), _token));
        report.GmsaNames.Should().NotBeNull();
        report.GmsaNames!.Select(l => (l.Source, l.RequestedName, l.Length)).Should().BeEquivalentTo(new[]
        {
            ("gMSA geçiş izlemesi", name, 12),
            ("gMSA ile devir talebi", requested, 12)
        });
    }

    [ServiceAccountSql030Fact]
    public async Task Before031_TheModuleKeepsWorking_AndANameIsRefusedWithNothingWritten()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync(Environment.GetEnvironmentVariable(ServiceAccountSql030FactAttribute.Variable));
        (await fx.CountAsync("SELECT CASE WHEN COL_LENGTH(N'svcacct.WorkRequests', N'RequestedGmsaName') IS NULL THEN 0 ELSE 1 END")).Should().Be(0,
            "this test needs a database left at migration 030");
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "OLD");
        Guid id = account.Summary.Id;
        account.RequestedGmsaNameAvailable.Should().BeFalse();

        AccountDetail created = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, id, new CreateWorkRequest("GmsaConversion"), _token));
        created.Requests.Should().ContainSingle().Which.RequestedGmsaName.Should().BeNull();
        SaResult<AccountDetail> refused = await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, id,
            new CreateWorkRequest("GmsaConversion", RequestedGmsaName: "gmsa_syn$"), _token);
        refused.ErrorCode.Should().Be(SaErrors.Invalid);
        refused.Field.Should().Be("gmsaNameColumnsMissing");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @id", new { id })).Should().Be(1);

        RequestView request = created.Requests.Single();
        Ok(await fx.Service.UpdateRequestAsync(coordinator.Principal, fx.Context, request.Id, new UpdateWorkRequest(request.Version, Notes: "Sentetik not"), _token))
            .Requests.Single().Notes.Should().Be("Sentetik not");
        Guid team = await fx.TeamAsync("SYN GMSA ESKI " + fx.Suffix, org);
        TransitionView transition = Ok(await fx.Service.CreateHandoverAsync(coordinator.Principal, fx.Context, id, new CreateHandoverRequest(team, TrackGmsa: true), _token))
            .Transitions.Single();
        (await fx.Service.UpdateTransitionAsync(coordinator.Principal, fx.Context, transition.Id,
            new TransitionUpdateRequest(transition.Version, "Review", RequestedGmsaName: "gmsa_syn$"), _token)).Field.Should().Be("gmsaNameColumnsMissing");
        Ok(await fx.Service.UpdateTransitionAsync(coordinator.Principal, fx.Context, transition.Id, new TransitionUpdateRequest(transition.Version, "Review"), _token))
            .Transitions.Single().Suitability.Should().Be("Review");

        Ok(await fx.Service.WeeklyReportAsync(coordinator.Principal, fx.Context, new WeeklyReportQuery(new(2026, 10, 5), OrganizationId: org), _token))
            .GmsaNames.Should().BeNull("before 031 the list is unknown, not empty");
    }

    private static async Task<(ServiceAccountSqlFixture, SynUser, Guid)> SetupAsync(string? connection)
    {
        ServiceAccountSqlFixture fx = new(connection);
        Guid org = await fx.OrganizationAsync("SYN GMSA ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        return (fx, coordinator, org);
    }

    private static async Task<AccountDetail> CreateAccountAsync(ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, string label) =>
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN_GMSA_{label}_{fx.Suffix}", "SYN", org, "Sentetik gMSA adı testi hesabı"), _token));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
