using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;

namespace SecureOps.Tests.Integration.Api;

public sealed partial class AnnouncementTests
{
    [PreparationSqlFact]
    public async Task RoleReplacement_AuditFailureRollsBackRolesAndVersion_WithoutHumanReason()
    {
        string connection = Environment.GetEnvironmentVariable("SECUREOPS_PREPARATION_SQL_CONNECTION")!;
        var guard = new SqlConnectionStringBuilder(connection);
        guard.DataSource.Should().Be("(localdb)\\SecureOpsResourcesV1");
        guard.InitialCatalog.Should().StartWith("SecureOps_ResourcesV1_OcoPreparation");
        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = connection }).Build();
        var repository = new SqlAccessRepository(config);
        EnsureAccessUserResult pending = await repository.EnsureUserAsync(new("synthetic-role:" + Guid.NewGuid(), "test"), true, TimeSpan.Zero, default);
        AccessMutationResult approved = await repository.DecideRequestAsync(pending.PendingRequest!.Id, AccessRequestStatus.Approved,
            pending.PendingRequest.Version, "synthetic-admin", ["Operator"], "Original approval retained", default);
        ApplicationUser original = approved.User!;
        await using var sql = new SqlConnection(connection);
        string trigger = "TR_RoleAuditTest_" + Guid.NewGuid().ToString("N");
        await sql.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Action='AccessRolesChanged') THROW 51179,'Synthetic audit failure.',1; END;");
        try
        {
            await FluentActions.Awaiting(() => repository.ReplaceRolesAsync(original.Id, ["ReadOnly"], original.Version, "synthetic-admin", default))
                .Should().ThrowAsync<SqlException>();
            ApplicationUser unchanged = (await repository.GetUserAsync(original.Id, default))!;
            unchanged.Version.Should().Be(original.Version);
            unchanged.Roles.Should().Equal(original.Roles);
        }
        finally { await sql.ExecuteAsync($"DROP TRIGGER audit.[{trigger}]"); }
        AccessMutationResult result = await repository.ReplaceRolesAsync(original.Id, ["ReadOnly"], original.Version, "synthetic-admin", default);
        result.User!.Roles.Should().Equal("ReadOnly");
        (await repository.ReplaceRolesAsync(original.Id, ["Lead"], original.Version, "synthetic-admin", default)).Disposition.Should().Be(AccessMutationDisposition.ConcurrencyConflict);
        string json = (await sql.QuerySingleAsync<string>("SELECT DetailsJson FROM audit.AuditLog WHERE Action='AccessRolesChanged' AND JSON_VALUE(DetailsJson,'$.targetUserId')=@id", new { id = original.Id.ToString() }));
        System.Text.Json.JsonElement audit = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        audit.GetProperty("descriptionSource").GetString().Should().Be("SystemGenerated");
        audit.GetProperty("oldRoles")[0].GetString().Should().Be("Operator");
        audit.GetProperty("newRoles")[0].GetString().Should().Be("ReadOnly");
        audit.GetProperty("version").GetInt64().Should().Be(original.Version + 1);
        (await repository.ListRequestsForUserAsync(original.Id, default)).Single().DecisionReason.Should().Be("Original approval retained");
        AccessMutationResult[] race = await Task.WhenAll(
            repository.ReplaceRolesAsync(original.Id, ["Operator"], result.User.Version, "synthetic-admin", default),
            repository.ReplaceRolesAsync(original.Id, ["Lead"], result.User.Version, "synthetic-admin", default));
        race.Count(r => r.Disposition == AccessMutationDisposition.Applied).Should().Be(1);
        race.Count(r => r.Disposition == AccessMutationDisposition.ConcurrencyConflict).Should().Be(1);
        race.Single(r => r.Disposition == AccessMutationDisposition.Applied).User!.Version.Should().Be(result.User.Version + 1);
    }
}
