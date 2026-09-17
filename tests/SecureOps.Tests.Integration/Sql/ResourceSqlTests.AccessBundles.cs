using System.Text.Json;
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

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task AccessBundles_PreserveRights_PageDecisions_ImpactAndConcurrentGuards()
    {
        var repository = new SqlAccessRepository(Configuration());
        await using var connection = new SqlConnection(Configuration().GetConnectionString("SecureOpsDb"));
        IReadOnlyList<AccessRoleDefinition> roles = await repository.GetRoleDefinitionsAsync(_token);
        foreach (string code in AccessRoleCatalog.RoleCodes)
        {
            roles.Single(role => role.Code == code).Capabilities.Should().BeEquivalentTo(AccessRoleCatalog.GetCapabilities([code]));
        }
        string prefix = "bundle-" + Guid.NewGuid().ToString("N");
        ApplicationUser admin = (await repository.GetUserAsync(await SqlAccessTestActors.AdminAsync(Configuration()), _token))!;
        EnsureAccessUserResult userPending = await repository.EnsureUserAsync(new(prefix + "-user", "oidc", LoginName: prefix, DisplayName: "Synthetic operator", Mail: "operator@example.invalid"), true, TimeSpan.Zero, _token);
        ApplicationUser user = (await repository.DecideRequestAsync(userPending.PendingRequest!.Id, AccessRequestStatus.Approved,
            userPending.PendingRequest.Version, admin.CorporateIdentity, ["ReadOnly"], "Synthetic fixture", _token)).User!;
        var change = new AccessRoleChange(prefix, "Synthetic review", "Local role test", 0, [Capabilities.InUseView, Capabilities.InUseReview]);
        AccessRoleImpact preview = (await repository.ChangeRoleAsync(change, admin.CorporateIdentity, false, _token)).Value!;
        preview.AffectedUsers.Should().Be(0);
        (await repository.ChangeRoleAsync(change, admin.CorporateIdentity, true, _token)).ErrorCode.Should().Be(OperationalErrorCodes.AccessConcurrencyConflict);
        (await repository.ChangeRoleAsync(change with { PreviewToken = preview.PreviewToken }, admin.CorporateIdentity, true, _token)).IsSuccess.Should().BeTrue();
        user = (await repository.ReplaceRolesAsync(user.Id, [prefix], user.Version, admin.CorporateIdentity, _token, new Dictionary<string, long> { [prefix] = 1 })).User!;
        user.Capabilities.Should().BeEquivalentTo(change.Capabilities);
        (await repository.ReplaceRolesAsync(user.Id, ["Admin"], user.Version, user.CorporateIdentity, _token)).Disposition.Should().Be(AccessMutationDisposition.AdministrativeGuard);
        AccessRoleChange edit = change with { ExpectedVersion = 1, Capabilities = [Capabilities.InUseView] };
        AccessRoleImpact impact = (await repository.ChangeRoleAsync(edit, admin.CorporateIdentity, false, _token)).Value!;
        impact.AffectedUsers.Should().Be(1);
        impact.UsersLosing.Should().Be(1);
        AccessServiceResult<AccessRoleImpact>[] race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            repository.ChangeRoleAsync(edit with { PreviewToken = impact.PreviewToken }, admin.CorporateIdentity, true, _token)));
        race.Count(result => result.IsSuccess).Should().Be(1);
        race.Count(result => result.ErrorCode == OperationalErrorCodes.AccessConcurrencyConflict).Should().Be(1);
        ApplicationUser changed = (await repository.GetUserAsync(user.Id, _token))!;
        changed.Version.Should().Be(user.Version + 1);
        changed.Capabilities.Should().Equal(Capabilities.InUseView);
        (await repository.ChangeRoleAsync(new("Admin", "Changed", "Denied", roles.Single(role => role.Code == "Admin").Version, []), admin.CorporateIdentity, false, _token)).ErrorCode.Should().Be(OperationalErrorCodes.AccessDenied);
        (await repository.ChangeRoleAsync(edit, user.CorporateIdentity, false, _token)).ErrorCode.Should().Be(OperationalErrorCodes.AccessDenied);
        for (int index = 0; index < 31; index++)
        {
            await repository.EnsureUserAsync(new(prefix + "-page-" + index, "test"), true, TimeSpan.Zero, _token);
        }
        AccessPage<AccessUserResponse> page = await repository.PageUsersAsync(new(Search: prefix + "-page-", Status: "Pending", PageSize: 20), _token);
        page.Total.Should().Be(31);
        page.Items.Should().HaveCount(20);
        AccessPage<AccessUserResponse> second = await repository.PageUsersAsync(new(Search: prefix + "-page-", Status: "Pending", Page: 2, PageSize: 20), _token);
        second.Items.Should().HaveCount(11);
        page.Items.Select(item => item.UserId).Intersect(second.Items.Select(item => item.UserId)).Should().BeEmpty();
        AccessRequestResponse request = page.Items[0].LatestRequest!;
        await repository.DecideRequestAsync(request.Id, AccessRequestStatus.Rejected, request.Version, admin.CorporateIdentity, [], "Synthetic rejection", _token);
        (await repository.PageUsersAsync(new(Search: prefix + "-page-", Status: "Pending"), _token)).Total.Should().Be(30);
        (await repository.PageUsersAsync(new(Search: prefix + "-page-", Status: "Rejected"), _token)).Total.Should().Be(1);
        (await repository.PageRequestsAsync(new(Search: prefix + "-page-", Status: "Rejected"), _token)).Total.Should().Be(1);
        (await repository.PageUsersAsync(new(Search: "%" + prefix), _token)).Total.Should().Be(0);
        await FluentActions.Awaiting(() => repository.PageUsersAsync(new(PageSize: 101), _token)).Should().ThrowAsync<ArgumentException>();
        string audit = await connection.QuerySingleAsync<string>("SELECT DetailsJson FROM audit.AuditLog WHERE CorrelationId=@correlation", new { correlation = $"role:{prefix}:2" });
        JsonSerializer.Deserialize<JsonElement>(audit).GetProperty("actorId").GetGuid().Should().Be(admin.Id);
    }
}
