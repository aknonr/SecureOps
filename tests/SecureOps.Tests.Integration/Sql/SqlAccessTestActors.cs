using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SecureOps.Tests.Integration.Sql;

internal static class SqlAccessTestActors
{
    internal static async Task<string> AdminAsync(IConfiguration configuration, string? identity = null)
    {
        string value = identity ?? "synthetic-admin:" + Guid.NewGuid().ToString("N");
        string connectionString = configuration.GetConnectionString("SecureOpsDb")!;
        var guard = new SqlConnectionStringBuilder(connectionString);
        if (guard.DataSource != "(localdb)\\SecureOpsResourcesV1" || !guard.InitialCatalog.StartsWith("SecureOps_ResourcesV1_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Synthetic fixture only.");
        }
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync("""
            IF NOT EXISTS(SELECT 1 FROM security.Users WHERE CorporateIdentity=@value)
            BEGIN
                DECLARE @id uniqueidentifier=NEWID();
                INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus,DisplayName,Mail)
                VALUES(@id,@value,'test','Approved','Synthetic administrator','admin@example.invalid');
                INSERT INTO security.RoleAssignments(RoleAssignmentId,UserId,RoleId,GrantedByCorporateIdentity)
                SELECT NEWID(),@id,RoleId,'synthetic-fixture' FROM security.Roles WHERE RoleCode='Admin';
            END
            """, new { value });
        return value;
    }
}
