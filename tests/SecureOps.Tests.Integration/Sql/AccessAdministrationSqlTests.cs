using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Sql;

public sealed class AccessAdministrationSqlTests
{
    [AccessGuardSqlFact]
    public async Task AdministrativeGuards_ConcurrentLastAdmin_ForgedActor_StaleDefinition_AuditRollback()
    {
        string connectionString = Environment.GetEnvironmentVariable("SECUREOPS_ACCESS_GUARD_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(connectionString);
        guard.DataSource.Should().Be("(localdb)\\SecureOpsResourcesV1");
        guard.InitialCatalog.Should().StartWith("SecureOps_ResourcesV1_OcoAccessGuards");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = connectionString }).Build();
        await using var sql = new SqlConnection(connectionString);
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId WHERE r.RoleCode='Admin'")).Should().Be(0, "this test owns a fresh database, never an existing task database");
        var repository = new SqlAccessRepository(configuration);
        ApplicationUser first = (await repository.GetUserAsync(await SqlAccessTestActors.AdminAsync(configuration), default))!;
        ApplicationUser second = (await repository.GetUserAsync(await SqlAccessTestActors.AdminAsync(configuration), default))!;
        AccessMutationResult[] concurrent = await Task.WhenAll(
            repository.DisableUserAsync(first.Id, first.Version, second.CorporateIdentity, "Synthetic race", default),
            repository.DisableUserAsync(second.Id, second.Version, first.CorporateIdentity, "Synthetic race", default));
        concurrent.Count(result => result.Disposition == AccessMutationDisposition.Applied).Should().Be(1);
        concurrent.Count(result => result.Disposition == AccessMutationDisposition.AdministrativeGuard).Should().Be(1);
        ApplicationUser survivor = (await repository.GetUserAsync(concurrent[0].Disposition == AccessMutationDisposition.Applied ? second.Id : first.Id, default))!;
        (await repository.DisableUserAsync(survivor.Id, survivor.Version, survivor.CorporateIdentity, "Synthetic last admin", default)).Disposition.Should().Be(AccessMutationDisposition.AdministrativeGuard);
        (await repository.ReplaceRolesAsync(survivor.Id, ["ReadOnly"], survivor.Version, survivor.CorporateIdentity, default)).Disposition.Should().Be(AccessMutationDisposition.AdministrativeGuard);
        (await repository.ReplaceRolesAsync(survivor.Id, ["Admin"], survivor.Version, "forged:actor", default)).Disposition.Should().Be(AccessMutationDisposition.AdministrativeGuard);
        EnsureAccessUserResult pending = await repository.EnsureUserAsync(new("synthetic:role-target", "test"), true, TimeSpan.Zero, default);
        ApplicationUser target = (await repository.DecideRequestAsync(pending.PendingRequest!.Id, AccessRequestStatus.Approved, pending.PendingRequest.Version,
            survivor.CorporateIdentity, ["ReadOnly"], "Synthetic approval", default)).User!;
        AccessRoleChange change = new("GuardBusiness", "Synthetic business", "Guard validation", 0, [Capabilities.InUseView]);
        AccessRoleImpact preview = (await repository.ChangeRoleAsync(change, survivor.CorporateIdentity, false, default)).Value!;
        (await repository.ChangeRoleAsync(change with { PreviewToken = preview.PreviewToken }, survivor.CorporateIdentity, true, default)).IsSuccess.Should().BeTrue();
        (await repository.ReplaceRolesAsync(target.Id, [change.Code], target.Version, survivor.CorporateIdentity, default)).Disposition.Should().Be(AccessMutationDisposition.ConcurrencyConflict);
        target = (await repository.ReplaceRolesAsync(target.Id, [change.Code], target.Version, survivor.CorporateIdentity, default,
            new Dictionary<string, long> { [change.Code] = 1 })).User!;
        AccessRoleChange edit = change with { ExpectedVersion = 1, Capabilities = [Capabilities.InUseView, Capabilities.InUseReview] };
        AccessRoleImpact impact = (await repository.ChangeRoleAsync(edit, survivor.CorporateIdentity, false, default)).Value!;
        string trigger = "TR_SyntheticRoleGuardAudit";
        await sql.ExecuteAsync($"CREATE TRIGGER audit.{trigger} ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Action='AccessRoleDefinitionChanged') THROW 51181,'Synthetic audit failure',1; END;");
        try
        {
            await FluentActions.Awaiting(() => repository.ChangeRoleAsync(edit with { PreviewToken = impact.PreviewToken }, survivor.CorporateIdentity, true, default)).Should().ThrowAsync<SqlException>();
            (await repository.GetUserAsync(target.Id, default))!.Version.Should().Be(target.Version);
            (await repository.GetRoleDefinitionsAsync(default)).Single(role => role.Code == change.Code).Version.Should().Be(1);
        }
        finally { await sql.ExecuteAsync($"DROP TRIGGER audit.{trigger}"); }
        (await repository.ChangeRoleAsync(edit with { PreviewToken = impact.PreviewToken }, survivor.CorporateIdentity, true, default)).IsSuccess.Should().BeTrue();
        target = (await repository.GetUserAsync(target.Id, default))!;
        (await repository.ReplaceRolesAsync(target.Id, [change.Code], target.Version, survivor.CorporateIdentity, default,
            new Dictionary<string, long> { [change.Code] = 1 })).Disposition.Should().Be(AccessMutationDisposition.ConcurrencyConflict);
        await sql.ExecuteAsync("UPDATE security.Users SET DisplayName='Changed later' WHERE UserId=@Id", new { survivor.Id });
        string audit = await sql.QuerySingleAsync<string>("SELECT DetailsJson FROM audit.AuditLog WHERE CorrelationId='role:GuardBusiness:2'");
        audit.Should().Contain("Synthetic administrator").And.NotContain("Changed later");
    }
}

public sealed class AccessGuardSqlFactAttribute : FactAttribute
{
    public AccessGuardSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECUREOPS_ACCESS_GUARD_CONNECTION")))
        {
            Skip = "Requires a fresh isolated OcoAccessGuards database through 019 and SECUREOPS_ACCESS_GUARD_CONNECTION.";
        }
    }
}
