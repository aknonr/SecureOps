using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Resources;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task Layout_UpgradesLegacyJsonRoundTripsAcrossRepositoriesAndRollsBackOnAuditFailure()
    {
        IConfiguration configuration = Configuration();
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        var repository = new SqlResourceRepository(configuration);
        await connection.ExecuteAsync("""
            INSERT INTO resources.PersonalPreferences(UserId,Version,PreferencesJson,UpdatedAt)
            VALUES(@UserId,1,'{"Version":1,"FavouriteIds":[],"Sets":[],"DefaultSetId":null}',SYSUTCDATETIME());
            """, new { actor.UserId });
        ResourcePreferences legacy = await repository.PreferencesAsync(actor.UserId, _token);
        legacy.WorkspaceLayout.Should().BeNull();
        var layout = new ResourceWorkspaceLayout("list", "compact", 50, ["groups", "links"]);
        ResourcePreferences saved = (await repository.SavePreferencesAsync(legacy with { WorkspaceLayout = layout }, 1, actor, _token)).Value!;
        (await new SqlResourceRepository(configuration).PreferencesAsync(actor.UserId, _token)).Should().BeEquivalentTo(saved);
        (await repository.SavePreferencesAsync(legacy, 1, actor, _token)).ErrorCode.Should().Be(ResourceErrors.Conflict);
        (await repository.PreferencesAsync(Guid.NewGuid(), _token)).Should().BeEquivalentTo(ResourcePreferences.Empty);
        string trigger = "TR_WorkspaceTest_" + Guid.NewGuid().ToString("N");
        await connection.ExecuteAsync($"CREATE TRIGGER audit.{trigger} ON audit.AuditLog AFTER INSERT AS BEGIN THROW 51099, 'Synthetic layout audit failure', 1; END;");
        try
        {
            Func<Task> fail = () => repository.SavePreferencesAsync(saved with { WorkspaceLayout = ResourceWorkspaceLayout.Default }, 2, actor, _token);
            await fail.Should().ThrowAsync<SqlException>();
        }
        finally { await connection.ExecuteAsync($"DROP TRIGGER audit.{trigger};"); }
        (await repository.PreferencesAsync(actor.UserId, _token)).Should().BeEquivalentTo(saved);
    }
}
