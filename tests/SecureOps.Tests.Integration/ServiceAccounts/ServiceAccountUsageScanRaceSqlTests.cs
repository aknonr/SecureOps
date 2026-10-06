using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Tests that hold the module's import-commit gate exclusively; they stall every other module write, so they run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServiceAccountWriteGateCollection
{
    /// <summary>Collection name.</summary>
    public const string Name = "Service Accounts write gate";
}

/// <summary>
/// A usage scan is linked only if the caller's authority still holds inside the write transaction (ADR-0027): the scope grant
/// is revoked, or the account moves to another owner team, after the service's own check but before the link is written,
/// and nothing is linked. The write is held at the module's import-commit gate (its first statement) while the change
/// commits, so the order is deterministic. Synthetic data only.
/// </summary>
[Collection(ServiceAccountWriteGateCollection.Name)]
public sealed class ServiceAccountUsageScanRaceSqlTests
{
    private const string _statement = "Sentetik: kendi yönetici hesabımla SYN-JUMP01 üzerinden çalıştırdım";
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];

    [ServiceAccountSqlFact]
    public async Task ScopeRevokedAfterTheCheck_MultiAccountUpload_LinksNothing_AndNamesNothing()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, Guid ownerTeam) = await SetupAsync();
        AccountDetail account = await OwnedAccountAsync(fx, coordinator, org, ownerTeam, "Y1");
        SynUser member = await fx.UserAsync(_all);
        await fx.GrantAsync(member, ScopeKind.Team, team: ownerTeam);

        UsageScanBatchResult result = Ok(await WhileWriteGateHeldAsync(fx,
            () => fx.Service.AttachUsageScanToAccountsAsync(member.Principal, fx.Context, [account.Summary.Id], "scan.json", Discovery(account),
                _statement, _token),
            "UPDATE svcacct.ScopeGrants SET RevokedAt = SYSDATETIMEOFFSET(), RevokedBy = @by, RevokeReason = N'Sentetik yarış' WHERE UserId = @user AND RevokedAt IS NULL;",
            new { by = coordinator.User.Id, user = member.User.Id }));

        result.ScanId.Should().BeNull("nothing from the file is stored when no account is linked");
        UsageScanBatchAccountResult row = result.Results.Single();
        (row.Outcome, row.AccountName, row.Domain, row.MatchedAccount).Should().Be(("Unavailable", (string?)null, (string?)null, (string?)null),
            "after the revocation the account is outside the caller's scope");
        await NothingLinkedAsync(fx, account.Summary.Id, member);
    }

    [ServiceAccountSqlFact]
    public async Task OwnerTeamChangesAfterTheCheck_SingleAccountUpload_IsRefused_AndLinksNothing()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, Guid ownerTeam) = await SetupAsync();
        Guid otherTeam = await fx.TeamAsync("SYN YARIS DIGER " + fx.Suffix, null);
        AccountDetail account = await OwnedAccountAsync(fx, coordinator, org, ownerTeam, "Y2");
        SynUser member = await fx.UserAsync(_all);
        await fx.GrantAsync(member, ScopeKind.Team, team: ownerTeam);

        SaResult<AccountDetail> refused = await WhileWriteGateHeldAsync(fx,
            () => fx.Service.AttachUsageScanAsync(member.Principal, fx.Context, account.Summary.Id, "scan.json", Discovery(account), _statement, null, _token),
            "UPDATE svcacct.Accounts SET CurrentOwnerTeamId = @otherTeam WHERE Id = @id;", new { otherTeam, id = account.Summary.Id });

        await NothingLinkedAsync(fx, account.Summary.Id, member);
        refused.ErrorCode.Should().Be(SaErrors.NotFound, "the account left the member's scope before the link was written (same answer as missing)");

        // Still visible through a request to the member's team, but no longer the member's to work on as a whole.
        Guid participantTeam = await fx.TeamAsync("SYN YARIS KATILIMCI " + fx.Suffix, null);
        AccountDetail shared = await OwnedAccountAsync(fx, coordinator, org, participantTeam, "Y3");
        shared = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, shared.Summary.Id,
            new CreateWorkRequest("GmsaHandover", TargetTeamId: participantTeam), _token));
        SynUser participant = await fx.UserAsync(_all);
        await fx.GrantAsync(participant, ScopeKind.Team, team: participantTeam);
        SaResult<AccountDetail> lost = await WhileWriteGateHeldAsync(fx,
            () => fx.Service.AttachUsageScanAsync(participant.Principal, fx.Context, shared.Summary.Id, "scan.json", Discovery(shared), _statement, null, _token),
            "UPDATE svcacct.Accounts SET CurrentOwnerTeamId = @otherTeam WHERE Id = @id;", new { otherTeam, id = shared.Summary.Id });
        await NothingLinkedAsync(fx, shared.Summary.Id, participant);
        (lost.ErrorCode, lost.Field).Should().Be((SaErrors.Forbidden, "requestId"), "a participant attaches only through its own request");
    }

    /// <summary>
    /// Holds the import-commit gate exclusively, starts <paramref name="call"/>, waits until its write transaction waits on the
    /// gate (its checks are done), commits <paramref name="change"/> in the same transaction and releases the gate.
    /// </summary>
    private static async Task<T> WhileWriteGateHeldAsync<T>(ServiceAccountSqlFixture fx, Func<Task<T>> call, string change, object parameters)
    {
        await using SqlConnection holder = fx.Connection();
        await holder.OpenAsync(_token);
        await using SqlTransaction hold = holder.BeginTransaction();
        (await holder.ExecuteScalarAsync<int>("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = N'svcacct:import-commit', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @result;
            """, null, hold)).Should().BeGreaterThanOrEqualTo(0);
        Task<T> running = call();
        for (int attempt = 0; ; attempt++)
        {
            int waiting = await holder.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT' AND resource_database_id = DB_ID();
                """, null, hold);
            if (waiting > 0)
            {
                break;
            }

            running.IsCompleted.Should().BeFalse("the call must reach the write gate after its own checks");
            attempt.Should().BeLessThan(400, "the call reached the write gate within 20 seconds");
            await Task.Delay(50, _token);
        }

        await holder.ExecuteAsync(change, parameters, hold);
        await hold.CommitAsync(_token);
        return await running;
    }

    private static async Task NothingLinkedAsync(ServiceAccountSqlFixture fx, Guid accountId, SynUser member)
    {
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE AccountId = @accountId", new { accountId })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScans WHERE UploadedBy = @u", new { u = member.User.Id })).Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE Action = 'UsageScanAttached' AND AccountId = @accountId", new { accountId }))
            .Should().Be(0);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action = 'ServiceAccount.UsageScanAttached' AND DetailsJson LIKE @id",
            new { id = "%" + accountId.ToString("D") + "%" })).Should().Be(0);
    }

    private static async Task<(ServiceAccountSqlFixture, SynUser, Guid, Guid)> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN YARIS ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        return (fx, coordinator, org, await fx.TeamAsync("SYN YARIS SAHIP " + fx.Suffix, org));
    }

    private static async Task<AccountDetail> OwnedAccountAsync(ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, Guid ownerTeam, string label)
    {
        AccountDetail created = Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN_YARIS_{label}_{fx.Suffix}", "SYN", org, "Sentetik yarış testi hesabı"), _token));
        return Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, created.Summary.Id,
            new OwnershipChangeRequest(created.Summary.Version, ownerTeam, null, "Confirm", "Sentetik sahiplik kararı"), _token));
    }

    private static byte[] Discovery(AccountDetail account) =>
        ServiceAccountUsageScanBatchSqlTests.Discovery([$"SYN\\{account.Summary.AccountName}"],
            ("SYN-APP01", "WindowsService", "SynSvc", $"SYN\\{account.Summary.AccountName}"));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
