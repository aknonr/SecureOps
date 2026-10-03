using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// One-time first scope grant (ADR-0026) against the real repository and the SA-003 schema guard. The module database is
/// shared by the whole run, so the success path can only run while no grant exists yet (a fresh database with this test
/// first); the refusal paths are asserted on every run.
/// </summary>
public sealed class ServiceAccountScopeBootstrapSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [ServiceAccountSqlFact]
    public async Task Bootstrap_IsAvailableOnlyBeforeAnyGrant_IsAuditedOnce_AndThenSelfGrantsAreRefusedAgain()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        SynUser other = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);

        (await fx.Service.BootstrapScopeAsync(viewer.Principal, fx.Context, new ScopeBootstrapRequest("Sentetik"), _token)).ErrorCode
            .Should().Be(SaErrors.Forbidden, "only module administrators may bootstrap");
        (await fx.Service.BootstrapScopeAsync(admin.Principal, fx.Context, new ScopeBootstrapRequest(" "), _token)).Field.Should().Be("reason");

        ScopeBootstrapState before = Ok(await fx.Service.ScopeBootstrapStateAsync(admin.Principal, fx.Context, _token));
        before.SchemaReady.Should().BeTrue("the harness applies candidate SA-003");
        if (before.Available)
        {
            Guid id = Ok(await fx.Service.BootstrapScopeAsync(admin.Principal, fx.Context, new ScopeBootstrapRequest("İlk kurulum (sentetik)"), _token));
            await using SqlConnection connection = fx.Connection();
            (await connection.QuerySingleAsync<(string Kind, bool Bootstrap, Guid GrantedBy)>(
                "SELECT ScopeKind, IsBootstrap, GrantedBy FROM svcacct.ScopeGrants WHERE Id = @id", new { id }))
                .Should().Be(("All", true, admin.User.Id));
            (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @c AND Action = 'ServiceAccount.ScopeBootstrapped'",
                new { c = fx.Context.CorrelationId })).Should().Be(1);
            Ok(await fx.Service.MeAsync(admin.Principal, fx.Context, _token)).ScopeKind.Should().Be("All");
        }
        else
        {
            await fx.GrantAsync(other, ScopeKind.All);
        }

        ScopeBootstrapState after = Ok(await fx.Service.ScopeBootstrapStateAsync(other.Principal, fx.Context, _token));
        after.Available.Should().BeFalse("the bootstrap closes for good once any grant exists");
        (await fx.Service.BootstrapScopeAsync(other.Principal, fx.Context, new ScopeBootstrapRequest("İkinci deneme"), _token)).Field
            .Should().Be("bootstrapClosed");
        IAccessRepository users = Substitute.For<IAccessRepository>();
        users.GetUserAsync(other.User.CorporateIdentity, Arg.Any<CancellationToken>()).Returns(other.User);
        (await fx.Service.CreateGrantAsync(other.Principal, fx.Context, new CreateScopeGrantRequest(other.User.CorporateIdentity, "All", null, null, "Kendime"),
            users, _token)).Field.Should().Be("selfGrant");
    }

    [ServiceAccountSqlFact]
    public async Task Candidates_ListOnlyApprovedApplicationUsers_AndMarkTheCallerAndMissingModuleAccess()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);
        ApplicationUser outsider = viewer.User with { Id = Guid.NewGuid(), CorporateIdentity = "synthetic:sa:outsider", Capabilities = [], DisplayName = "Sentetik Dış" };
        ApplicationUser pending = outsider with { Id = Guid.NewGuid(), CorporateIdentity = "synthetic:sa:pending", Status = AccessStatus.Pending };
        IAccessRepository users = Substitute.For<IAccessRepository>();
        users.ListUsersAsync(Arg.Any<CancellationToken>()).Returns([admin.User, viewer.User, outsider, pending]);

        IReadOnlyList<ScopeGrantCandidate> list = Ok(await fx.Service.ScopeGrantCandidatesAsync(admin.Principal, fx.Context, users, _token));
        list.Select(c => c.CorporateIdentity).Should().BeEquivalentTo([admin.User.CorporateIdentity, viewer.User.CorporateIdentity, outsider.CorporateIdentity]);
        list.Single(c => c.CorporateIdentity == admin.User.CorporateIdentity).IsCaller.Should().BeTrue();
        list.Single(c => c.CorporateIdentity == outsider.CorporateIdentity).HasServiceAccountsAccess.Should().BeFalse();
        (await fx.Service.ScopeGrantCandidatesAsync(viewer.Principal, fx.Context, users, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    [ServiceAccountSqlFact]
    public async Task Schema_AllowsOneBootstrapRowEver_AndOnlyAsAnAllGrantToItsGrantor()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser user = await fx.UserAsync(ServiceAccountCapabilities.View);
        await using SqlConnection connection = fx.Connection();
        Func<Task> selfNormal = () => connection.ExecuteAsync("""
            INSERT INTO svcacct.ScopeGrants(Id, UserId, ScopeKind, Reason, GrantedBy, GrantedAt) VALUES(NEWID(), @u, 'All', 't', @u, SYSUTCDATETIME());
            """, new { u = user.User.Id });
        (await selfNormal.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
        Func<Task> notAll = () => connection.ExecuteAsync("""
            INSERT INTO svcacct.ScopeGrants(Id, UserId, ScopeKind, TeamId, Reason, GrantedBy, GrantedAt, IsBootstrap)
            VALUES(NEWID(), @u, 'Team', (SELECT TOP 1 Id FROM svcacct.Teams), 't', @u, SYSUTCDATETIME(), 1);
            """, new { u = user.User.Id });
        await notAll.Should().ThrowAsync<SqlException>();
        if (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM svcacct.ScopeGrants WHERE IsBootstrap = 1") > 0)
        {
            Func<Task> second = () => connection.ExecuteAsync("""
                INSERT INTO svcacct.ScopeGrants(Id, UserId, ScopeKind, Reason, GrantedBy, GrantedAt, IsBootstrap) VALUES(NEWID(), @u, 'All', 't', @u, SYSUTCDATETIME(), 1);
                """, new { u = user.User.Id });
            (await second.Should().ThrowAsync<SqlException>()).Which.Number.Should().BeOneOf(2601, 2627);
        }
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
