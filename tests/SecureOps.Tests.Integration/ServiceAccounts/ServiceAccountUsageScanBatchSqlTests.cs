using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// One usage-scan file attached to several accounts in one upload (ADR-0027, no migration) on the real svcacct schema: the
/// file is checked once and a refused file stores nothing for anyone; each account is answered on its own (attached,
/// already attached, not in the file, ambiguous, unavailable) and one refusal never blocks another; an account outside the
/// caller's scope comes back without its name; the scan is stored once with one link, history row and audit row per
/// account. Synthetic data only.
/// </summary>
public sealed class ServiceAccountUsageScanBatchSqlTests
{
    private const string _statement = "Sentetik: kendi yönetici hesabımla SYN-JUMP01 üzerinden çalıştırdım";
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];

    [ServiceAccountSqlFact]
    public async Task OneFile_SeveralAccounts_EachAnsweredOnItsOwn_StoredOnce_AndReplayAddsNothing()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail first = await CreateAccountAsync(fx, coordinator, org, "B1");
        AccountDetail second = await CreateAccountAsync(fx, coordinator, org, "B2");
        AccountDetail notSearched = await CreateAccountAsync(fx, coordinator, org, "B3");
        AccountDetail noDomain = await CreateAccountAsync(fx, coordinator, org, "B4", domain: null);
        Guid otherOrg = await fx.OrganizationAsync("SYN SCAN BASKA ORG " + fx.Suffix);
        SynUser otherCoordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(otherCoordinator, ScopeKind.Organization, otherOrg);
        AccountDetail outside = await CreateAccountAsync(fx, otherCoordinator, otherOrg, "B5");
        var missing = Guid.NewGuid();
        string Searched(AccountDetail a) => $"SYN\\{a.Summary.AccountName}";
        byte[] file = Discovery([Searched(first), Searched(second), Searched(outside), Searched(noDomain), $"OTHER\\{noDomain.Summary.AccountName}"],
            ("SYN-APP01", "WindowsService", "SynSvc", Searched(first)), ("SYN-APP02", "ScheduledTask", "\\SynNightly", Searched(second)),
            ("SYN-APP01", "IisAppPool", "SynPool", Searched(outside)));
        Guid[] ids = [first.Summary.Id, outside.Summary.Id, second.Summary.Id, notSearched.Summary.Id, missing, noDomain.Summary.Id];

        UsageScanBatchResult result = Ok(await fx.Service.AttachUsageScanToAccountsAsync(coordinator.Principal, fx.Context, ids, "multi.json", file,
            _statement, _token));

        result.ScanId.Should().NotBeNull();
        (result.Purpose, result.PlannedServers, result.AnsweredServers).Should().Be(("Discovery", 2, 2));
        result.Results.Select(r => (r.AccountId, r.Outcome)).Should().Equal(
            (first.Summary.Id, "Attached"), (outside.Summary.Id, "Unavailable"), (second.Summary.Id, "Attached"),
            (notSearched.Summary.Id, "NotInScan"), (missing, "Unavailable"), (noDomain.Summary.Id, "Ambiguous"));
        UsageScanBatchAccountResult hidden = result.Results.Single(r => r.AccountId == outside.Summary.Id);
        (hidden.AccountName, hidden.Domain, hidden.MatchedAccount).Should().Be(((string?)null, (string?)null, (string?)null),
            "an account outside the caller's scope looks exactly like a missing one");
        result.Results.Single(r => r.AccountId == missing).Should().BeEquivalentTo(hidden with { AccountId = missing });
        result.Results.Single(r => r.AccountId == notSearched.Summary.Id).AccountName.Should().Be(notSearched.Summary.AccountName);
        result.Results.Single(r => r.AccountId == first.Summary.Id).MatchedAccount.Should().Be(Searched(first));
        result.Results.Single(r => r.AccountId == noDomain.Summary.Id).OutcomeLabel.Should().Contain("Belirsiz");

        UsageScanView firstScan = Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, first.Summary.Id, _token)).UsageScans!.Single();
        firstScan.MatchedAccount.Should().Be(Searched(first));
        firstScan.Items.Should().ContainSingle().Which.ComponentName.Should().Be("SynSvc", "each account sees only its own searched name");
        firstScan.Coverage.NotFound.Should().Be(1, "SYN-APP02 was fully scanned without this account");
        Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, second.Summary.Id, _token)).UsageScans!.Single().ScanId.Should().Be(firstScan.ScanId);
        Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, notSearched.Summary.Id, _token)).UsageScans.Should().BeEmpty();
        Ok(await fx.Service.AccountAsync(otherCoordinator.Principal, fx.Context, outside.Summary.Id, _token)).UsageScans.Should().BeEmpty(
            "the file searched it, but the uploader may not work on it");

        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScans WHERE UploadedBy = @u", new { u = coordinator.User.Id })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE ScanId = @s", new { s = result.ScanId })).Should().Be(2);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE Action = 'UsageScanAttached' AND AccountId IN @a",
            new { a = ids })).Should().Be(2);
        foreach (Guid id in new[] { first.Summary.Id, second.Summary.Id })
        {
            (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action = 'ServiceAccount.UsageScanAttached' AND DetailsJson LIKE @id",
                new { id = "%" + id.ToString("D") + "%" })).Should().Be(1);
        }

        string refusal = (await RefusalAuditsAsync(fx, coordinator)).Single();
        JsonNode counts = JsonNode.Parse(refusal)!;
        ((string?)counts["Reason"], (int?)counts["Requested"], (int?)counts["Attached"], (int?)counts["AlreadyAttached"], (int?)counts["NotInScan"],
            (int?)counts["Ambiguous"], (int?)counts["Unavailable"], (int?)counts["Failed"]).Should().Be(("accounts", 6, 2, 0, 1, 1, 2, 0));
        foreach (string withheld in new[] { outside.Summary.AccountName, outside.Summary.Id.ToString("D"), first.Summary.AccountName, "SYN\\\\" })
        {
            refusal.Should().NotContain(withheld, "the refusal record carries counts only: no account name or id, nothing from the file");
        }

        UsageScanBatchResult replay = Ok(await fx.Service.AttachUsageScanToAccountsAsync(coordinator.Principal, fx.Context,
            [second.Summary.Id, notSearched.Summary.Id, first.Summary.Id], "again.json", file, "Sentetik: aynı dosya ikinci kez", _token));
        replay.ScanId.Should().Be(result.ScanId);
        replay.Results.Select(r => r.Outcome).Should().Equal("AlreadyAttached", "NotInScan", "AlreadyAttached");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE ScanId = @s", new { s = result.ScanId })).Should().Be(2);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE Action = 'UsageScanAttached' AND AccountId IN @a",
            new { a = ids })).Should().Be(2, "a replay writes nothing");
    }

    [ServiceAccountSqlFact]
    public async Task RefusedFileOrRequest_StoresNothingForAnyAccount()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail first = await CreateAccountAsync(fx, coordinator, org, "R1");
        AccountDetail second = await CreateAccountAsync(fx, coordinator, org, "R2");
        Guid[] ids = [first.Summary.Id, second.Summary.Id];
        const string secret = "SYN-NEVER-STORED-77ab";
        JsonNode leaked = JsonNode.Parse(Encoding.UTF8.GetString(Discovery([$"SYN\\{first.Summary.AccountName}"],
            ("SYN-APP01", "WindowsService", "SynSvc", $"SYN\\{first.Summary.AccountName}"))))!;
        leaked["results"]![0]!["components"]![0]!["Password"] = secret;

        SaResult<UsageScanBatchResult> refused = await fx.Service.AttachUsageScanToAccountsAsync(coordinator.Principal, fx.Context, ids, "leaked.json",
            Encoding.UTF8.GetBytes(leaked.ToJsonString()), _statement, _token);

        (refused.ErrorCode, refused.Field).Should().Be((SaErrors.UsageScanFile, "scanSecretField"));
        refused.Current.Should().BeNull("the refusal carries no part of the file and no account");
        byte[] good = Discovery([$"SYN\\{first.Summary.AccountName}"], ("SYN-APP01", "WindowsService", "SynSvc", $"SYN\\{first.Summary.AccountName}"));
        foreach ((IReadOnlyList<Guid>? request, string? statement, string field) in new (IReadOnlyList<Guid>?, string?, string)[]
        {
            (null, _statement, "accountIds"), ([], _statement, "accountIds"), ([first.Summary.Id, first.Summary.Id], _statement, "accountIds"),
            ([first.Summary.Id, Guid.Empty], _statement, "accountIds"), ([.. Enumerable.Range(0, 21).Select(_ => Guid.NewGuid())], _statement, "accountIds"),
            (ids, " ", "runStatement"), (ids, "Sentetik\u0007beyan", "runStatement")
        })
        {
            SaResult<UsageScanBatchResult> invalid = await fx.Service.AttachUsageScanToAccountsAsync(coordinator.Principal, fx.Context, request, "scan.json",
                good, statement, _token);
            (invalid.ErrorCode, invalid.Field).Should().Be((SaErrors.Invalid, field));
        }

        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(viewer, ScopeKind.Organization, org);
        (await fx.Service.AttachUsageScanToAccountsAsync(viewer.Principal, fx.Context, ids, "scan.json", good, _statement, _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, "attaching evidence needs the Work capability");

        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScans WHERE UploadedBy IN @u", new { u = new[] { coordinator.User.Id, viewer.User.Id } }))
            .Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE EntityType = 'UsageScan' AND AccountId IN @ids", new { ids })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE DetailsJson LIKE @s", new { s = "%" + secret + "%" })).Should().Be(0);

        // Every refusal after the capability check is on record with its stable reason and the requested count only.
        string[] refusals = await RefusalAuditsAsync(fx, coordinator);
        refusals.Select(r => ((string?)JsonNode.Parse(r)!["Reason"], (int?)JsonNode.Parse(r)!["Requested"])).Should().Equal(
            ("scanSecretField", 2), ("accountIds", 0), ("accountIds", 0), ("accountIds", 2), ("accountIds", 2), ("accountIds", 21),
            ("runStatement", 2), ("runStatement", 2));
        refusals.Should().AllSatisfy(r => r.Should().NotContain(first.Summary.AccountName).And.NotContain(first.Summary.Id.ToString("D")));
        (await RefusalAuditsAsync(fx, viewer)).Should().BeEmpty("a caller without the Work capability is refused before the module looks at anything");
    }

    /// <summary>The caller's multi-account refusal records, oldest first.</summary>
    private static async Task<string[]> RefusalAuditsAsync(ServiceAccountSqlFixture fx, SynUser caller)
    {
        await using SqlConnection connection = fx.Connection();
        return [.. await connection.QueryAsync<string>("""
            SELECT DetailsJson FROM audit.AuditLog WHERE Action = 'ServiceAccount.UsageScanBatchRefused' AND Actor = @actor ORDER BY OccurredAt, AuditLogId;
            """, new { actor = caller.User.Id.ToString("D") })];
    }

    [ServiceAccountSqlFact]
    public async Task OnlyTheResponsibleBasis_Attaches_AParticipantSeesTheNameButNothingIsStored()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        Guid ownerTeam = await fx.TeamAsync("SYN TOPLU SAHIP " + fx.Suffix, org), executor = await fx.TeamAsync("SYN TOPLU GMSA " + fx.Suffix, null);
        AccountDetail owned = await CreateAccountAsync(fx, coordinator, org, "P1");
        owned = Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, owned.Summary.Id,
            new OwnershipChangeRequest(owned.Summary.Version, ownerTeam, null, "Confirm", "Sentetik sahiplik kararı"), _token));
        AccountDetail shared = await CreateAccountAsync(fx, coordinator, org, "P2");
        shared = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, shared.Summary.Id,
            new CreateWorkRequest("GmsaHandover", TargetTeamId: executor), _token));
        byte[] file = Discovery([$"SYN\\{owned.Summary.AccountName}", $"SYN\\{shared.Summary.AccountName}"],
            ("SYN-APP01", "WindowsService", "SynSvc", $"SYN\\{owned.Summary.AccountName}"),
            ("SYN-APP02", "WindowsService", "SynSvc2", $"SYN\\{shared.Summary.AccountName}"));

        SynUser participant = await fx.UserAsync(_all);
        await fx.GrantAsync(participant, ScopeKind.Team, team: executor);
        UsageScanBatchResult refused = Ok(await fx.Service.AttachUsageScanToAccountsAsync(participant.Principal, fx.Context,
            [shared.Summary.Id, owned.Summary.Id], "scan.json", file, _statement, _token));
        refused.ScanId.Should().BeNull("no account was linked, so nothing from the file is stored");
        refused.Results.Select(r => (r.Outcome, r.AccountName)).Should().Equal(("Unavailable", shared.Summary.AccountName), ("Unavailable", (string?)null));
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScans WHERE UploadedBy = @u", new { u = participant.User.Id })).Should().Be(0);

        SynUser member = await fx.UserAsync(_all);
        await fx.GrantAsync(member, ScopeKind.Team, team: ownerTeam);
        UsageScanBatchResult byOwner = Ok(await fx.Service.AttachUsageScanToAccountsAsync(member.Principal, fx.Context,
            [owned.Summary.Id, shared.Summary.Id], "scan.json", file, _statement, _token));
        byOwner.Results.Select(r => r.Outcome).Should().Equal("Attached", "Unavailable");
        Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, owned.Summary.Id, _token)).UsageScans.Should().ContainSingle()
            .Which.RequestId.Should().BeNull("a multi-account upload never goes through a request");
    }

    [ServiceAccountSqlFact]
    public async Task MiddleAccountFails_EarlierLinkStands_LaterAccountIsTried_AndTheAnswerIs200()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail a19 = await CreateAccountAsync(fx, coordinator, org, "A19");
        AccountDetail a20 = await CreateAccountAsync(fx, coordinator, org, "A20");
        AccountDetail a21 = await CreateAccountAsync(fx, coordinator, org, "A21");
        string Searched(AccountDetail a) => $"SYN\\{a.Summary.AccountName}";
        byte[] file = Discovery([Searched(a19), Searched(a20), Searched(a21)], ("SYN-APP01", "WindowsService", "SynSvc", Searched(a20)));
        Guid[] ids = [a19.Summary.Id, a20.Summary.Id, a21.Summary.Id];

        UsageScanBatchResult result;
        await using (SqlConnection holder = fx.Connection())
        {
            // Another session holds an uncommitted change on A20's account row longer than the module's command timeout. The
            // account row is read by primary key, so only A20 waits (a request row would block every account on a small,
            // scanned table and test the lock instead of the per-account boundary).
            await holder.OpenAsync(_token);
            await using SqlTransaction hold = holder.BeginTransaction();
            await holder.ExecuteAsync("UPDATE svcacct.Accounts SET Notes = CONCAT(Notes, N' ') WHERE Id = @id;", new { id = a20.Summary.Id }, hold);
            result = Ok(await fx.Service.AttachUsageScanToAccountsAsync(coordinator.Principal, fx.Context, ids, "multi.json", file, _statement, _token));
            await hold.RollbackAsync(_token);
        }

        result.Results.Select(r => (r.AccountId, r.Outcome)).Should().Equal(
            (a19.Summary.Id, "Attached"), (a20.Summary.Id, "Failed"), (a21.Summary.Id, "Attached"));
        result.ScanId.Should().NotBeNull();
        UsageScanBatchAccountResult failed = result.Results[1];
        failed.OutcomeLabel.Should().StartWith("Kaydedilemedi");
        failed.MatchedAccount.Should().BeNull("nothing was linked for the failed account");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE AccountId IN @ids", new { ids })).Should().Be(2);

        UsageScanBatchResult retry = Ok(await fx.Service.AttachUsageScanToAccountsAsync(coordinator.Principal, fx.Context, ids, "multi.json", file,
            _statement, _token));
        retry.Results.Select(r => r.Outcome).Should().Equal("AlreadyAttached", "Attached", "AlreadyAttached");
        retry.ScanId.Should().Be(result.ScanId);
    }

    private static async Task<(ServiceAccountSqlFixture, SynUser, Guid)> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN TOPLU ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        return (fx, coordinator, org);
    }

    private static async Task<AccountDetail> CreateAccountAsync(ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, string label,
        string? domain = "SYN") =>
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN_TOPLU_{label}_{fx.Suffix}", domain, org, "Sentetik toplu tarama testi hesabı"), _token));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }

    /// <summary>A complete discovery scan of SYN-APP01 and SYN-APP02 that searched <paramref name="searched"/>; each component runs as one name.</summary>
    internal static byte[] Discovery(string[] searched, params (string Server, string Type, string Component, string Identity)[] configured)
    {
        JsonArray Names() => [.. searched.Select(s => (JsonNode)JsonValue.Create(s)!)];
        JsonArray results = [];
        foreach (string server in new[] { "SYN-APP01", "SYN-APP02" })
        {
            JsonArray components = [.. configured.Where(c => c.Server == server).Select(c => (JsonNode)new JsonObject
            {
                ["ComponentType"] = c.Type,
                ["ComponentName"] = c.Component,
                ["Identity"] = c.Identity,
                ["MatchedAccount"] = c.Identity,
                ["State"] = "Running",
                ["Detail"] = null
            })];
            results.Add(new JsonObject
            {
                ["schema"] = "service-account-usage-v1",
                ["serverName"] = server,
                ["generatedAt"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("o"),
                ["durationMs"] = 700,
                ["accounts"] = Names(),
                ["scanResult"] = "Success",
                ["sources"] = new JsonObject { ["WindowsServices"] = "Success", ["ScheduledTasks"] = "Success", ["Iis"] = "NotInstalled" },
                ["components"] = components,
                ["verification"] = null,
                ["warnings"] = new JsonArray()
            });
        }

        return Encoding.UTF8.GetBytes(new JsonObject
        {
            ["schema"] = "service-account-usage-scan-v1",
            ["generatedAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"),
            ["tool"] = "Combined",
            ["accounts"] = Names(),
            ["expectedAccount"] = null,
            ["plannedServers"] = new JsonArray("SYN-APP01", "SYN-APP02"),
            ["results"] = results,
            ["notReached"] = new JsonArray()
        }.ToJsonString());
    }
}
