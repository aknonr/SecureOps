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

    [Fact]
    public void OperationalRecordSqlAsset_EnforcesDurableIdempotencyAndHistory()
    {
        string root = FindRepositoryRoot();
        string sql = File.ReadAllText(Path.Combine(root, "sql", "schema", "002-operational-record-jira-workflow.sql"));

        sql.Should().Contain("CREATE TABLE ops.OperationalRecords")
            .And.Contain("CREATE TABLE ops.JiraTransfers")
            .And.Contain("CREATE TABLE ops.OperationalRecordWorkflowHistory");
        sql.Should().Contain("UQ_OperationalRecords_SourceRecordId")
            .And.Contain("UQ_JiraTransfers_OperationalRecord")
            .And.Contain("UQ_JiraTransfers_IdempotencyKey")
            .And.Contain("UX_JiraTransfers_JiraIssueKey");
        sql.Should().Contain("TR_OperationalRecordWorkflowHistory_AppendOnly")
            .And.NotContain("Password")
            .And.NotContain("ApiToken")
            .And.NotContain("AuthorizationHeader");
    }

    [Fact]
    public void PlatformHardeningSqlAsset_AddsAccessClaimsAndCommandIdempotency()
    {
        string root = FindRepositoryRoot();
        string sql = File.ReadAllText(Path.Combine(root, "sql", "schema", "003-platform-access-concurrency-hardening.sql"));

        sql.Should().Contain("AuthenticationSource")
            .And.Contain("AccessStatus")
            .And.Contain("LastAuthenticatedAt")
            .And.Contain("SourceConcurrencyToken")
            .And.Contain("ClaimedBy")
            .And.Contain("ClaimExpiresAt")
            .And.Contain("CREATE TABLE ops.CommandExecutions")
            .And.Contain("ExecutionToken uniqueidentifier")
            .And.Contain("UQ_CommandExecutions_Scope");
        sql.Should().Contain("CK_OperationalRecords_Claim")
            .And.NotContain("Password")
            .And.NotContain("ApiToken")
            .And.NotContain("AuthorizationHeader");
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
