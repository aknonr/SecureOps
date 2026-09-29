using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

public sealed class ServiceAccountPersistenceSqlTests
{
    /// <summary>
    /// SqlClient pools by connection string and a pooled session keeps the isolation level of its last transaction.
    /// The module's SERIALIZABLE transactions must therefore never hand a SERIALIZABLE session to platform code that
    /// shares <c>ConnectionStrings:SecureOpsDb</c>. A test-unique application name isolates this pool from parallel tests.
    /// </summary>
    [ServiceAccountSqlFact]
    public async Task ModuleSerializableWork_DoesNotLeakIsolationIntoPlatformPooledConnections()
    {
        ServiceAccountSqlFixture fx = new();
        SynUser admin = await fx.UserAsync(ServiceAccountCapabilities.View, ServiceAccountCapabilities.Administer);
        string platform = new SqlConnectionStringBuilder(fx.Configuration.GetConnectionString("SecureOpsDb"))
        {
            ApplicationName = "sa-iso-" + Guid.NewGuid().ToString("N")[..12]
        }.ConnectionString;
        SqlServiceAccountRepository repository = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SecureOpsDb"] = platform
        }).Build());

        // A module write that runs in a SERIALIZABLE transaction.
        (await repository.SaveOrganizationAsync(null, new SaveOrganizationRequest("SYN ISO " + fx.Suffix, "Department", null),
            new SaActor(admin.User.Id, fx.Context.CorrelationId), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await using SqlConnection next = new(platform);
        await next.OpenAsync();
        (await next.ExecuteScalarAsync<short>("SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID;"))
            .Should().Be(2, "the next platform connection from the pool must be READ COMMITTED");
        SqlServiceAccountRepository.ModuleConnectionString(platform).Should().NotBe(platform, "the module uses its own pool");
        
    }
}
