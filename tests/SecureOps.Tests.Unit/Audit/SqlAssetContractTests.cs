using FluentAssertions;

namespace SecureOps.Tests.Unit.Audit;

public sealed class SqlAssetContractTests
{
    [Fact]
    public void ReviewedSqlAsset_PreservesAuditAndAccessControlContracts()
    {
        string root = FindRepositoryRoot();
        string sql = File.ReadAllText(Path.Combine(root, "sql", "schema", "001-audit-and-access-control.sql"));

        sql.Should().Contain("CREATE TABLE audit.AuditLog");
        sql.Should().Contain("OccurredAt datetimeoffset(7)").And.Contain("Actor nvarchar(256)").And.Contain("DetailsJson nvarchar(max)");
        sql.Should().Contain("TR_AuditLog_AppendOnly");
        sql.Should().Contain("security.Users").And.Contain("security.Roles").And.Contain("security.RoleAssignments").And.Contain("security.AccessRequests");
        sql.Should().Contain("UX_SecurityAccessRequests_Pending").And.Contain("TR_AccessRequests_NoSelfApproval");
        sql.Should().NotContain("Password").And.NotContain("ConnectionString");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
