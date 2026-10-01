using FluentAssertions;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class SdmSqlContractTests
{
    [Fact]
    public void Migration009_IsAdditiveBoundedAndPreservesAppendOnlyProtection()
    {
        string root = Root();
        string schema = File.ReadAllText(Path.Combine(root, "sql/schema/009-sdm-evaluation-foundation.sql"));
        string migration = File.ReadAllText(Path.Combine(root, "sql/migrations/009-sdm-evaluation-foundation.sql"));
        migration.Should().Contain(":r ..\\schema\\009-sdm-evaluation-foundation.sql");
        schema.Should().Contain("nvarchar(4000) NULL").And.Contain("WITH CHECK ADD CONSTRAINT")
            .And.Contain("ISJSON").And.Contain("TRY_CONVERT(datetimeoffset(7)")
            .And.Contain("$.Result.InputHash").And.Contain("$.Result.RuleSetVersion")
            .And.Contain("$.Result.ReasonCodes").And.Contain("$.Result.BlockingConditions")
            .And.Contain("$.Result.ExternalWriteEligible");
        schema.Should().NotContain("DROP ").And.NotContain("DISABLE TRIGGER").And.NotContain("DELETE FROM")
            .And.NotContain("UPDATE ops.OperationalRecordWorkflowHistory");
        string repository = File.ReadAllText(Path.Combine(root, "src/SecureOps.Infrastructure/OperationalRecords/SqlOperationalRecordRepository.Evaluation.cs"));
        repository.Should().Contain("IsolationLevel.Serializable").And.Contain("GetForUpdateAsync")
            .And.Contain("INSERT INTO ops.OperationalRecordWorkflowHistory").And.Contain("INSERT INTO audit.AuditLog")
            .And.Contain("ReferenceEquals(current, evaluated)").And.Contain("cancellationToken, transaction");
        string existing = File.ReadAllText(Path.Combine(root, "sql/schema/002-operational-record-jira-workflow.sql"));
        existing.Should().Contain("AFTER UPDATE, DELETE").And.Contain("THROW 51011");
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecureOps.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
