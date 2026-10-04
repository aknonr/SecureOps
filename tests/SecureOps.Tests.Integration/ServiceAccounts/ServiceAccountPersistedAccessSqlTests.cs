using System.Security.Claims;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// Persisted-access composition: the production infrastructure registrations (SQL access repository, SQL audit, the real
/// <see cref="ApplicationAccessService"/>) and the module registration, against the disposable database. Capabilities come
/// only from versioned role bundles (including amended protected Admin), and data scope only from module grants. Nothing wraps or
/// substitutes access. Not covered here: the HTTP pipeline and Integrated Security, which the API host enforces at startup.
/// </summary>
public sealed class ServiceAccountPersistedAccessSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

    [ServiceAccountSqlFact]
    public async Task ProtectedAdmin_HasModuleOperations_ButNeedsExplicitIndependentScope()
    {
        string connectionString = Environment.GetEnvironmentVariable(ServiceAccountSqlFactAttribute.Variable)!;
        new SqlConnectionStringBuilder(connectionString).InitialCatalog.Should().StartWith("SecureOps_Sa");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SecureOpsDb"] = connectionString,
            ["Access:RepositoryProvider"] = "SqlServer",
            ["Audit:Provider"] = "SqlServer",
            ["Audit:Queue:Enabled"] = "false",
            ["OperationalRecords:SourceProvider"] = "Disabled",
            ["Jira:Provider"] = "Disabled",
            ["ServiceAccounts:Provider"] = "SqlServer"
        }).Build();
        ServiceCollection registrations = new();
        registrations.AddSingleton(configuration);
        registrations.AddLogging();
        registrations.AddSecureOpsInfrastructure(configuration);
        registrations.AddServiceAccounts(configuration);
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var users = (SqlAccessRepository)scope.ServiceProvider.GetRequiredService<IAccessRepository>();
        ServiceAccountService module = scope.ServiceProvider.GetRequiredService<ServiceAccountService>();
        string prefix = "sa-admin-" + Guid.NewGuid().ToString("N")[..10];
        AccessOperationContext context = new("synthetic", prefix, null);
        string admin = await PlatformAdminAsync(connectionString, prefix);
        string secondAdmin = await PlatformAdminAsync(connectionString, prefix + "-second");
        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, admin)], "test"));
        ClaimsPrincipal second = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, secondAdmin)], "test"));
        ApplicationUser persisted = (await users.GetUserAsync(admin, _token))!;
        persisted.Capabilities.Intersect(ServiceAccountCapabilities.All).Should()
            .BeEquivalentTo(ServiceAccountCapabilities.All);
        ServiceAccountMe me = Ok(await module.MeAsync(principal, context, _token));
        (me.ScopeKind, me.HasScope).Should().Be(("None", false));
        Ok(await module.GrantsAsync(principal, context, _token));
        (await module.CreateGrantAsync(principal, context, new CreateScopeGrantRequest(admin, "All", null, null, "Self grant"), users, _token))
            .Field.Should().Be("selfGrant");
        (await module.CreateAccountAsync(principal, context, new CreateAccountRequest("SYN-NO-ACTION-" + prefix, null, null, "Synthetic"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        (await module.ImportsAsync(principal, context, _token)).Field.Should().Be("scope");

        string creatorRole = await BundleAsync(users, admin, prefix + "-creator",
            [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Assign, ServiceAccountCapabilities.Work]);
        IReadOnlyList<AccessRoleDefinition> definitions = await users.GetRoleDefinitionsAsync(_token);
        (string identity, ClaimsPrincipal creator) = await ApprovedAsync(users, admin, prefix + "-creator-user", creatorRole, definitions);
        Guid org = Ok(await module.SaveOrganizationAsync(principal, context, null, new SaveOrganizationRequest("SYN ADMIN " + prefix, "Department", null), _token));
        Ok(await module.CreateGrantAsync(principal, context, new CreateScopeGrantRequest(identity, "Organization", org, null, "Synthetic scope"), users, _token));
        AccountDetail account = Ok(await module.CreateAccountAsync(creator, context, new CreateAccountRequest("SYN-" + prefix, null, org, "Synthetic"), _token));
        (await module.AccountAsync(principal, context, account.Summary.Id, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        Ok(await module.CreateGrantAsync(second, context, new CreateScopeGrantRequest(admin, "Organization", org, null, "Reviewed synthetic scope"), users, _token));
        Ok(await module.MeAsync(principal, context, _token)).ScopeKind.Should().Be("Organization");
        Ok(await module.AccountAsync(principal, context, account.Summary.Id, _token));
        Ok(await module.CreateAccountAsync(principal, context,
            new CreateAccountRequest("SYN-ADMIN-ACTION-" + prefix, null, org, "Synthetic"), _token));
        Ok(await module.ImportsAsync(principal, context, _token));
        SyntheticLegacy legacy = new(prefix, "SYN ADMIN " + prefix);
        ImportBatchView preview = Ok(await module.StageImportAsync(principal, context,
            new StageImportRequest(ServiceAccountImportProfiles.LegacyWorkbook, new DateOnly(2026, 10, 3), "Synthetic source date"),
            "synthetic-admin.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", legacy.Workbook, _token));
        preview.Status.Should().Be("Previewed");
        Ok(await module.ImportRowsAsync(principal, context, preview.Id, null, null, false, 1, 200, _token))
            .Items.Should().NotBeEmpty();
        Guid outside = Ok(await module.SaveOrganizationAsync(principal, context, null,
            new SaveOrganizationRequest("SYN OUTSIDE " + prefix, "Department", null), _token));
        (await module.CreateAccountAsync(principal, context,
            new CreateAccountRequest("SYN-OUTSIDE-" + prefix, null, outside, "Synthetic"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);

        (string _, ClaimsPrincipal ordinary) = await ApprovedAsync(users, admin, prefix + "-ordinary", "ReadOnly", definitions);
        ((ClaimsIdentity)ordinary.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        ((ClaimsIdentity)ordinary.Identity!).AddClaim(new Claim("display_name", "System administrator"));
        (await module.MeAsync(ordinary, context, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        (await module.GrantsAsync(ordinary, context, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
        await using SqlConnection sql = new(connectionString);
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM svcacct.ScopeGrants WHERE UserId=@id AND GrantedBy=@id", new { id = persisted.Id }))
            .Should().Be(0);
        (await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId=@prefix AND Action='ServiceAccount.ScopeGranted'", new { prefix }))
            .Should().Be(2);
    }

    [ServiceAccountSqlFact]
    public async Task PersistedRoleBundlesAndScopeGrants_DriveTheModule_WithoutAnyAccessWrapper()
    {
        string connectionString = Environment.GetEnvironmentVariable(ServiceAccountSqlFactAttribute.Variable)!;
        new SqlConnectionStringBuilder(connectionString).InitialCatalog.Should().StartWith("SecureOps_Sa", "synthetic disposable databases only");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SecureOpsDb"] = connectionString,
            ["Access:RepositoryProvider"] = "SqlServer",
            ["Audit:Provider"] = "SqlServer",
            ["Audit:Queue:Enabled"] = "false",
            ["OperationalRecords:SourceProvider"] = "Disabled",
            ["Jira:Provider"] = "Disabled",
            ["ServiceAccounts:Provider"] = "SqlServer"
        }).Build();
        ServiceCollection registrations = new();
        registrations.AddSingleton(configuration);
        registrations.AddLogging();
        registrations.AddSecureOpsInfrastructure(configuration);
        registrations.AddServiceAccounts(configuration);
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAccessRepository users = scope.ServiceProvider.GetRequiredService<IAccessRepository>();
        users.Should().BeOfType<SqlAccessRepository>();
        scope.ServiceProvider.GetRequiredService<IApplicationAccessService>().Should().BeOfType<ApplicationAccessService>();
        ServiceAccountService module = scope.ServiceProvider.GetRequiredService<ServiceAccountService>();
        var access = (SqlAccessRepository)users;
        string prefix = "sa-comp-" + Guid.NewGuid().ToString("N")[..10];
        AccessOperationContext context = new("synthetic", prefix, null);

        string platformAdmin = await PlatformAdminAsync(connectionString, prefix);
        string adminRole = await BundleAsync(access, platformAdmin, prefix + "-admin", [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer]);
        string coordinatorRole = await BundleAsync(access, platformAdmin, prefix + "-coord", [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work,
            ServiceAccountCapabilities.Assign, ServiceAccountCapabilities.Verify, ServiceAccountCapabilities.Report]);
        string memberRole = await BundleAsync(access, platformAdmin, prefix + "-member", [ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work]);
        IReadOnlyList<AccessRoleDefinition> reviewed = await access.GetRoleDefinitionsAsync(_token);
        (string Identity, ClaimsPrincipal Principal) moduleAdmin = await ApprovedAsync(users, platformAdmin, prefix + "-u-admin", adminRole, reviewed);
        (string Identity, ClaimsPrincipal Principal) coordinator = await ApprovedAsync(users, platformAdmin, prefix + "-u-coord", coordinatorRole, reviewed);
        (string Identity, ClaimsPrincipal Principal) member = await ApprovedAsync(users, platformAdmin, prefix + "-u-member", memberRole, reviewed);
        (string Identity, ClaimsPrincipal Principal) readOnly = await ApprovedAsync(users, platformAdmin, prefix + "-u-read", "ReadOnly", reviewed);

        (await module.MeAsync(readOnly.Principal, context, _token)).ErrorCode.Should().Be(SaErrors.Forbidden, "no existing platform role carries module actions");
        Guid org = Ok(await module.SaveOrganizationAsync(moduleAdmin.Principal, context, null, new SaveOrganizationRequest("SYN COMP ORG " + prefix, "Department", null), _token));
        Guid team = Ok(await module.SaveTeamAsync(moduleAdmin.Principal, context, null, new SaveTeamRequest("SYN COMP TEAM " + prefix, null), _token));
        (await module.CreateGrantAsync(moduleAdmin.Principal, context, new CreateScopeGrantRequest(coordinator.Identity, "Organization", org, null, "Sentetik kapsam"), users, _token))
            .ErrorCode.Should().BeNull();
        (await module.CreateGrantAsync(moduleAdmin.Principal, context, new CreateScopeGrantRequest(member.Identity, "Team", null, team, "Sentetik ekip"), users, _token))
            .ErrorCode.Should().BeNull();
        (await module.CreateGrantAsync(member.Principal, context, new CreateScopeGrantRequest(member.Identity, "All", null, null, "kendine yetki"), users, _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden, "only the module administrator capability manages scope");

        Ok(await module.MeAsync(coordinator.Principal, context, _token)).ScopeKind.Should().Be("Organization");
        AccountDetail account = Ok(await module.CreateAccountAsync(coordinator.Principal, context,
            new CreateAccountRequest("SYN" + prefix.Replace("-", string.Empty, StringComparison.Ordinal), null, org, "Sentetik"), _token));
        account = Ok(await module.CreateRequestAsync(coordinator.Principal, context, account.Summary.Id, new CreateWorkRequest("Review", TargetTeamId: team), _token));
        Guid request = account.Requests.Single().Id;

        ServiceAccountWorkSummary summary = Ok(await module.WorkSummaryAsync(member.Principal, context, _token));
        summary.TeamOpenRequests.Should().Be(1);
        AccountDetail seen = Ok(await module.AccountAsync(member.Principal, context, account.Summary.Id, _token));
        seen.Permissions.Basis.Should().Be(ServiceAccountAccessBasis.Participant);
        seen = Ok(await module.UpdateRequestAsync(member.Principal, context, request,
            new UpdateWorkRequest(seen.Requests.Single().Version, Notes: "ekip notu"), _token));

        // Removing Work from the member's bundle takes effect on the next call: authority is persisted, not cached in claims.
        AccessRoleDefinition bundle = (await access.GetRoleDefinitionsAsync(_token)).Single(r => r.Code == memberRole);
        AccessRoleChange narrowed = new(memberRole, bundle.Name, "Sentetik daraltma", bundle.Version, [ServiceAccountCapabilities.View]);
        AccessRoleImpact impact = (await access.ChangeRoleAsync(narrowed, platformAdmin, false, _token)).Value!;
        (await access.ChangeRoleAsync(narrowed with { PreviewToken = impact.PreviewToken }, platformAdmin, true, _token)).IsSuccess.Should().BeTrue();
        (await module.UpdateRequestAsync(member.Principal, context, request, new UpdateWorkRequest(seen.Requests.Single().Version, Notes: "yetkisiz"), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        (await module.AccountAsync(member.Principal, context, account.Summary.Id, _token)).IsSuccess.Should().BeTrue("View remains");

        await using SqlConnection connection = new(connectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @prefix AND Action LIKE 'ServiceAccount.%'", new { prefix }))
            .Should().BeGreaterThan(0, "module changes are audited in the platform audit log");
    }

    internal static async Task<string> PlatformAdminAsync(string connectionString, string prefix)
    {
        // Same seeding approach as the platform's own SQL access tests: one synthetic user with the protected Admin role.
        string identity = "synthetic-admin:" + prefix;
        await using SqlConnection connection = new(connectionString);
        await connection.ExecuteAsync("""
            DECLARE @id uniqueidentifier = NEWID();
            INSERT INTO security.Users(UserId, CorporateIdentity, AuthenticationSource, AccessStatus, DisplayName, Mail)
            VALUES(@id, @identity, 'test', 'Approved', 'Synthetic administrator', 'admin@example.invalid');
            INSERT INTO security.RoleAssignments(RoleAssignmentId, UserId, RoleId, GrantedByCorporateIdentity)
            SELECT NEWID(), @id, RoleId, 'synthetic-fixture' FROM security.Roles WHERE RoleCode = 'Admin';
            """, new { identity });
        return identity;
    }

    internal static async Task<string> BundleAsync(SqlAccessRepository access, string admin, string code, IReadOnlyList<string> capabilities)
    {
        AccessRoleChange change = new(code, "Sentetik " + code, "Sentetik test paketi", 0, capabilities);
        AccessRoleImpact preview = (await access.ChangeRoleAsync(change, admin, false, _token)).Value!;
        (await access.ChangeRoleAsync(change with { PreviewToken = preview.PreviewToken }, admin, true, _token)).IsSuccess.Should().BeTrue();
        return code;
    }

    /// <summary>Approves a pending request with one role at its reviewed version (the platform refuses unreviewed custom roles).</summary>
    internal static async Task<(string, ClaimsPrincipal)> ApprovedAsync(IAccessRepository users, string admin, string identity, string role,
        IReadOnlyList<AccessRoleDefinition> reviewed)
    {
        EnsureAccessUserResult pending = await users.EnsureUserAsync(new CorporatePrincipal(identity, "test"), true, TimeSpan.Zero, _token);
        AccessMutationResult approved = await users.DecideRequestAsync(pending.PendingRequest!.Id, AccessRequestStatus.Approved, pending.PendingRequest.Version,
            admin, [role], "Sentetik onay", _token, new Dictionary<string, long> { [role] = reviewed.Single(r => r.Code == role).Version });
        approved.Disposition.Should().Be(AccessMutationDisposition.Applied);
        approved.User!.Status.Should().Be(AccessStatus.Approved);
        return (identity, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, identity)], "test")));
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
