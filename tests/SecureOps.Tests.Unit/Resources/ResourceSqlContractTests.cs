using FluentAssertions;

namespace SecureOps.Tests.Unit.Resources;

public sealed class ResourceSqlContractTests
{
    [Fact]
    public void Migration010_IsAdditiveBoundedAndReferencesUnchanged009()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SecureOps.sln")))
        {
            root = root.Parent;
        }
        root.Should().NotBeNull();
        string sql = File.ReadAllText(Path.Combine(root!.FullName, "sql/schema/010-resource-catalogue.sql"));
        sql.Should().Contain("Migration 009 is required").And.Contain("BEGIN TRANSACTION").And.Contain("COMMIT TRANSACTION")
            .And.Contain("nvarchar(2048)").And.Contain("DATALENGTH(PreferencesJson) <= 240000")
            .And.Contain("REFERENCES security.Users(UserId)").And.Contain("ResourceCurator");
        sql.Should().NotContain("DROP ").And.NotContain("DISABLE TRIGGER").And.NotContain("DELETE FROM")
            .And.NotContain("INSERT INTO security.RoleAssignments");
        string source = File.ReadAllText(Path.Combine(root.FullName, "src/SecureOps.Infrastructure/Resources/SqlResourceRepository.Writes.cs"));
        source.Should().Contain("IsolationLevel.Serializable").And.Contain("UPDLOCK, HOLDLOCK")
            .And.Contain("INSERT INTO audit.AuditLog").And.Contain("Version = @ExpectedVersion");
    }
}
