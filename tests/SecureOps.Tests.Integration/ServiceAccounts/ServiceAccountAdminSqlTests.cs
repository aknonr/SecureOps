using FluentAssertions;
using NSubstitute;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

public sealed class ServiceAccountAdminSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [ServiceAccountSqlFact]
    public async Task ScopeGrant_ThroughTheService_IsAudited_RejectsDuplicatesAndSelfGrants_AndRevokesAtVersion()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        await fx.GrantAsync(admin, ScopeKind.All);
        SynUser lead = await fx.UserAsync(ServiceAccountCapabilities.View);
        Guid org = Ok(await fx.Service.SaveOrganizationAsync(admin.Principal, fx.Context, null, new SaveOrganizationRequest("SYN ADM " + fx.Suffix, "Department", null), _token));
        Guid team = Ok(await fx.Service.SaveTeamAsync(admin.Principal, fx.Context, null, new SaveTeamRequest("SYN ADM TEAM " + fx.Suffix, org), _token));
        IAccessRepository users = Substitute.For<IAccessRepository>();
        users.GetUserAsync(lead.User.CorporateIdentity, Arg.Any<CancellationToken>()).Returns(lead.User);
        users.GetUserAsync(admin.User.CorporateIdentity, Arg.Any<CancellationToken>()).Returns(admin.User);

        Guid grant = Ok(await fx.Service.CreateGrantAsync(admin.Principal, fx.Context,
            new CreateScopeGrantRequest(lead.User.CorporateIdentity, "Team", null, team, "Sentetik ekip kapsamı"), users, _token));
        Ok(await fx.Service.MeAsync(lead.Principal, fx.Context, _token)).Teams.Should().ContainSingle(t => t.Id == team);
        (await fx.Service.CreateGrantAsync(admin.Principal, fx.Context,
            new CreateScopeGrantRequest(lead.User.CorporateIdentity, "Team", null, team, "tekrar"), users, _token)).Field.Should().Be("duplicate");
        (await fx.Service.CreateGrantAsync(admin.Principal, fx.Context,
            new CreateScopeGrantRequest(admin.User.CorporateIdentity, "All", null, null, "kendine"), users, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a AND Action = 'ServiceAccount.ScopeGranted'",
            new { a = admin.User.Id.ToString("D") })).Should().Be(1);

        ScopeGrantView view = Ok(await fx.Service.GrantsAsync(admin.Principal, fx.Context, _token)).Single(g => g.Id == grant);
        Ok(await fx.Service.RevokeGrantAsync(admin.Principal, fx.Context, grant, new RevokeScopeGrantRequest(view.Version, "Sentetik geri alma"), _token));
        (await fx.Service.RevokeGrantAsync(admin.Principal, fx.Context, grant, new RevokeScopeGrantRequest(view.Version, "tekrar"), _token))
            .ErrorCode.Should().Be(SaErrors.Conflict);
        Ok(await fx.Service.MeAsync(lead.Principal, fx.Context, _token)).HasScope.Should().BeFalse();
    }

    [ServiceAccountSqlFact]
    public async Task Dictionaries_UseVersions_AndPersonVerificationIsEvidenced()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work, ServiceAccountCapabilities.Administer);
        await fx.GrantAsync(admin, ScopeKind.All);
        Guid org = Ok(await fx.Service.SaveOrganizationAsync(admin.Principal, fx.Context, null, new SaveOrganizationRequest("SYN DICT " + fx.Suffix, "Directorate", null), _token));
        OrganizationView saved = Ok(await fx.Service.OrganizationsAsync(admin.Principal, fx.Context, _token)).Single(o => o.Id == org);
        Ok(await fx.Service.SaveOrganizationAsync(admin.Principal, fx.Context, org, new SaveOrganizationRequest("SYN DICT2 " + fx.Suffix, "Directorate", null, saved.Version), _token));
        (await fx.Service.SaveOrganizationAsync(admin.Principal, fx.Context, org, new SaveOrganizationRequest("SYN DICT3 " + fx.Suffix, "Directorate", null, saved.Version), _token))
            .ErrorCode.Should().Be(SaErrors.Conflict);

        Guid person = Ok(await fx.Service.CreatePersonAsync(admin.Principal, fx.Context, new CreatePersonRequest("Sentetik Kişi " + fx.Suffix), _token));
        PersonView created = Ok(await fx.Service.PeopleAsync(admin.Principal, fx.Context, "Sentetik Kişi " + fx.Suffix, _token)).Single();
        created.VerificationState.Should().Be("Provisional");
        Ok(await fx.Service.VerifyPersonAsync(admin.Principal, fx.Context, person,
            new VerifyPersonRequest(created.Version, $"SYN.{fx.Suffix}@EXAMPLE.TEST", null, "Sentetik kanıt"), _token));
        Ok(await fx.Service.PeopleAsync(admin.Principal, fx.Context, "Sentetik Kişi " + fx.Suffix, _token)).Single().VerificationState.Should().Be("Verified");
        Ok(await fx.Service.MeAsync(admin.Principal, fx.Context, _token)).ScopeKind.Should().Be("All", "verifying a person changes no access");
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentScopeGrants_CommitDistinctUsers_AndRejectTheSameGrantWithoutPartialAudit()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        await fx.GrantAsync(admin, ScopeKind.All);
        var targets = new SynUser[8];
        foreach (int index in Enumerable.Range(0, targets.Length))
        {
            targets[index] = await fx.UserAsync(ServiceAccountCapabilities.View);
        }
        IAccessRepository users = Substitute.For<IAccessRepository>();
        foreach (SynUser user in targets)
        {
            users.GetUserAsync(user.User.CorporateIdentity, Arg.Any<CancellationToken>()).Returns(user.User);
        }
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SaResult<Guid>>[] writes = targets.Select(async user =>
        {
            await start.Task;
            return await fx.Service.CreateGrantAsync(admin.Principal, fx.Context,
                new CreateScopeGrantRequest(user.User.CorporateIdentity, "All", null, null, "Synthetic concurrent grant"), users, _token);
        }).ToArray();
        start.SetResult();
        foreach (SaResult<Guid> result in await Task.WhenAll(writes))
        {
            Ok(result).Should().NotBeEmpty();
        }

        SynUser duplicateTarget = await fx.UserAsync(ServiceAccountCapabilities.View);
        users.GetUserAsync(duplicateTarget.User.CorporateIdentity, Arg.Any<CancellationToken>()).Returns(duplicateTarget.User);
        TaskCompletionSource duplicateStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SaResult<Guid>>[] duplicateWrites = Enumerable.Range(0, 2).Select(async _ =>
        {
            await duplicateStart.Task;
            return await fx.Service.CreateGrantAsync(admin.Principal, fx.Context,
                new CreateScopeGrantRequest(duplicateTarget.User.CorporateIdentity, "All", null, null, "Synthetic duplicate grant"), users, _token);
        }).ToArray();
        duplicateStart.SetResult();
        SaResult<Guid>[] results = await Task.WhenAll(duplicateWrites);
        results.Count(result => result.ErrorCode is null).Should().Be(1);
        results.Count(result => result.ErrorCode == SaErrors.Invalid && result.Field == "duplicate").Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ScopeGrants WHERE UserId = @id AND RevokedAt IS NULL",
            new { id = duplicateTarget.User.Id })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @actor AND Action = 'ServiceAccount.ScopeGranted'",
            new { actor = admin.User.Id.ToString("D") })).Should().Be(9);
        fx.SqlDiagnostics.Should().BeEmpty("no deadlock, retry or swallowed persistence failure is allowed");
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
