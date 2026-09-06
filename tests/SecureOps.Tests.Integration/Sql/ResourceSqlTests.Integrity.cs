using System.Security.Claims;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Domain.Access;
using SecureOps.Domain.Resources;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Resources;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task ServiceMerge_UsesSqlAndRetainsHiddenReferencesAcrossEveryPersonalEdit()
    {
        IConfiguration configuration = Configuration();
        var repository = new SqlResourceRepository(configuration);
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        ResourceActor other = await CreateActorAsync(connection);
        ResourceCatalogueService service = OwnerService(repository, actor);
        var principal = new ClaimsPrincipal();
        var context = new AccessOperationContext("synthetic", actor.CorrelationId, null);
        Guid categoryId = Guid.NewGuid(), a = Guid.NewGuid(), hidden = Guid.NewGuid(), b = Guid.NewGuid(), added = Guid.NewGuid();
        await repository.SaveCategoryAsync(categoryId, new("Synthetic integrity"), actor, _token);
        SaveResourceLinkRequest Link(string environment) => new(categoryId, "Synthetic SQL link", "https://example.invalid/integrity", "Synthetic purpose", Environment: environment);
        await repository.SaveLinkAsync(a, Link("Lab"), actor, _token);
        await repository.SaveLinkAsync(hidden, Link("UnavailableOnly"), actor, _token);
        await repository.SaveLinkAsync(b, Link("Pilot"), actor, _token);
        await repository.SaveLinkAsync(added, Link("Lab"), actor, _token);
        ResourcePreferencesResponse personal = (await service.SaveSetAsync(principal, context, Guid.Empty,
            new("Original", [a, hidden, b]), true, _token)).Value!;
        Guid setId = personal.Sets.Single().Id;
        await repository.SaveLinkAsync(hidden, Link("UnavailableOnly") with { Archived = true, ExpectedVersion = 1 }, actor, _token);

        personal = (await service.SaveSetAsync(principal, context, setId,
            new("Renamed and reordered", [b, a, added], true, personal.Version), false, _token)).Value!;
        personal.Sets.Single().Links.Select(l => l.Id).Should().Equal(b, a, added);
        (await repository.PreferencesAsync(actor.UserId, _token)).Sets.Single().LinkIds.Should().Equal(b, hidden, a, added);
        personal = (await service.SaveSetAsync(principal, context, setId,
            new("Visible removal", [b, added], false, personal.Version, [a]), false, _token)).Value!;
        personal.DefaultSetId.Should().BeNull();
        (await repository.PreferencesAsync(actor.UserId, _token)).Sets.Single().LinkIds.Should().Equal(b, hidden, added);
        (await repository.PreferencesAsync(other.UserId, _token)).Sets.Should().BeEmpty();
        (await service.SaveSetAsync(principal, context, setId, new("Stale", [], ExpectedVersion: 1), false, _token))
            .ErrorCode.Should().Be(ResourceErrors.Conflict);

        ResourceEnvironmentOptions options = await repository.EnvironmentsAsync(new(CategoryId: categoryId), false, _token);
        options.Values.Should().Equal("Lab", "Pilot");
        (await repository.EnvironmentsAsync(new(Search: "pilot", CategoryId: categoryId), false, _token)).Values.Should().Equal("Pilot");
        await repository.SaveLinkAsync(hidden, Link("UnavailableOnly") with { ExpectedVersion = 2 }, actor, _token);
        // A projection read while archived is still safe after restoration, even on legacy PUT.
        personal = (await service.SaveSetAsync(principal, context, setId,
            new("Restored before save", [b, added], true, personal.Version), false, _token)).Value!;
        personal.Sets.Single().Links.Select(l => l.Id).Should().Equal(b, hidden, added);
        personal = (await service.DismissGuideAsync(principal, context, new(personal.Version), _token)).Value!;
        (await repository.PreferencesAsync(actor.UserId, _token)).GuideDismissed.Should().BeTrue();

        string trigger = "TR_ResourceTest_" + Guid.NewGuid().ToString("N");
        await connection.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Actor = '{actor.UserId:D}') THROW 51090, 'Synthetic audit failure.', 1; END;");
        try
        {
            ResourceResult<ResourcePreferencesResponse> result = await service.SaveSetAsync(principal, context, setId,
                new("Must roll back", [], ExpectedVersion: personal.Version, RemoveLinkIds: [b]), false, _token);
            result.ErrorCode.Should().Be("PersistenceUnavailable");
            ResourcePreferences persisted = await repository.PreferencesAsync(actor.UserId, _token);
            persisted.Version.Should().Be(personal.Version);
            persisted.Sets.Single().LinkIds.Should().Equal(b, hidden, added);
        }
        finally
        {
            await connection.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];");
        }
        string[] details = [.. await connection.QueryAsync<string>("SELECT DetailsJson FROM audit.AuditLog WHERE Actor=@Actor AND Action='ResourcePreferencesSaved'", new { Actor = actor.UserId.ToString("D") })];
        details.Should().OnlyContain(d => !d.Contains("example.invalid") && !d.Contains("Restored before save") && !d.Contains(hidden.ToString()));
    }

    [LocalResourceSqlFact]
    public async Task EnvironmentLookup_SqlBoundsAndAuthorizationAreIndependentOfCataloguePage()
    {
        var repository = new SqlResourceRepository(Configuration());
        await using SqlConnection connection = new(Configuration().GetConnectionString("SecureOpsDb"));
        ResourceActor actor = await CreateActorAsync(connection);
        var category = Guid.NewGuid();
        await repository.SaveCategoryAsync(category, new("Synthetic facets"), actor, _token);
        for (int i = 0; i < 102; i++)
        {
            await repository.SaveLinkAsync(Guid.NewGuid(), new(category, "Synthetic facet", "https://example.invalid/facet", "Synthetic", Environment: $"Env-{i:D3}"), actor, _token);
        }
        ResourceEnvironmentOptions options = await repository.EnvironmentsAsync(new(CategoryId: category), false, _token);
        options.Values.Should().HaveCount(100);
        options.HasMore.Should().BeTrue();
        (await repository.EnvironmentsAsync(new(Search: "101", CategoryId: category), false, _token)).Values.Should().Equal("Env-101");
        await repository.SaveCategoryAsync(category, new("Synthetic facets", ManagersOnly: true, ExpectedVersion: 1), actor, _token);
        (await repository.EnvironmentsAsync(new(CategoryId: category), false, _token)).Values.Should().BeEmpty();
        (await repository.EnvironmentsAsync(new(CategoryId: category), true, _token)).Values.Should().HaveCount(100);
        await repository.SaveCategoryAsync(category, new("Synthetic facets", Archived: true, ExpectedVersion: 2), actor, _token);
        (await repository.EnvironmentsAsync(new(CategoryId: category), true, _token)).Values.Should().BeEmpty();
    }

    private static ResourceCatalogueService OwnerService(IResourceRepository repository, ResourceActor actor)
    {
        IApplicationAccessService access = Substitute.For<IApplicationAccessService>();
        var user = new ApplicationUser(actor.UserId, "synthetic", "test", AccessStatus.Approved, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, null, 1, ["Operator"], AccessRoleCatalog.GetCapabilities(["Operator"]));
        access.GetCurrentAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<AccessOperationContext>(), Arg.Any<CancellationToken>())
            .Returns(AccessServiceResult<EnsureAccessUserResult>.Success(new(user, null, false, false)));
        return new(repository, access, NullLogger<ResourceCatalogueService>.Instance);
    }
}
