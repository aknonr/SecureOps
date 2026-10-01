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
/// only from versioned role bundles an administrator creates, and data scope only from module grants. Nothing wraps or
/// substitutes access. Not covered here: the HTTP pipeline and Integrated Security, which the API host enforces at startup.
/// </summary>
public sealed class ServiceAccountPersistedAccessSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;

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
