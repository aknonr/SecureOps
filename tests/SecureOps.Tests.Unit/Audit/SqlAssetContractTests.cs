using System.Text.RegularExpressions;
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
    public void OperationalRecordMigration_UsesSqlServerValidUnicodeDescriptionAndEmptyOpsSchemaRecovery()
    {
        string root = FindRepositoryRoot();
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "002-operational-record-jira-workflow.sql"));
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "002-operational-record-jira-workflow.sql"));

        schema.Should().Contain("Description nvarchar(max) NOT NULL")
            .And.NotContain("Description nvarchar(8000)")
            .And.Contain("IF SCHEMA_ID(N'ops') IS NULL")
            .And.Contain("EXEC(N'CREATE SCHEMA ops');")
            .And.Contain("CREATE TABLE ops.OperationalRecords")
            .And.Contain("CREATE TABLE ops.JiraTransfers")
            .And.Contain("CREATE TABLE ops.OperationalRecordWorkflowHistory");
        migration.Should().Contain(":r ..\\schema\\002-operational-record-jira-workflow.sql");

        string[] schemaNames = Directory.EnumerateFiles(Path.Combine(root, "sql", "schema"), "*.sql")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;
        string[] migrationNames = Directory.EnumerateFiles(Path.Combine(root, "sql", "migrations"), "*.sql")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;
        migrationNames.Should().Equal(schemaNames)
            .And.HaveCount(17)
            .And.ContainSingle(name => name == "017-announcement-source-recovery.sql")
            .And.ContainSingle(name => name == "016-announcement-source-jobs.sql")
            .And.ContainSingle(name => name == "015-announcement-owner-index.sql")
            .And.ContainSingle(name => name == "014-announcement-drafts.sql")
            .And.ContainSingle(name => name!.StartsWith("009-", StringComparison.Ordinal))
            .And.ContainSingle(name => name!.StartsWith("010-", StringComparison.Ordinal))
            .And.ContainSingle(name => name!.StartsWith("011-", StringComparison.Ordinal));

        IReadOnlyList<int> declaredLengths = Directory
            .EnumerateFiles(Path.Combine(root, "sql", "schema"), "*.sql")
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\bnvarchar\((\d+)\)", RegexOptions.IgnoreCase)
                .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

        declaredLengths.Should().OnlyContain(length => length <= 4000);
    }

    [Fact]
    public void OidcUserProfileMigration_UpgradesInstalledUsersWithoutChangingStableIdentity()
    {
        string root = FindRepositoryRoot();
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "008-oidc-user-profile.sql"));
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "008-oidc-user-profile.sql"));

        migration.Should().Contain(":r ..\\schema\\008-oidc-user-profile.sql");
        schema.Should().Contain("COL_LENGTH(N'security.Users', N'LoginName') IS NULL")
            .And.Contain("LoginName nvarchar(256) NULL")
            .And.Contain("DisplayName nvarchar(256) NULL")
            .And.Contain("Mail nvarchar(320) NULL")
            .And.Contain("Uid nvarchar(256) NULL")
            .And.Contain("ProfileUpdatedAt datetimeoffset(7) NULL")
            .And.NotContain("CorporateIdentity =")
            .And.NotContain("AuthenticationSource =")
            .And.NotContain("AccessStatus =")
            .And.NotContain("AccessVersion =")
            .And.NotContain("Password")
            .And.NotContain("Token");
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

    [Fact]
    public void AccessReadModelSqlAsset_AddsExplicitMutationVersionsOnly()
    {
        string root = FindRepositoryRoot();
        string sql = File.ReadAllText(Path.Combine(root, "sql", "schema", "004-access-read-model-and-versioning.sql"));

        sql.Should().Contain("security.Users")
            .And.Contain("AccessVersion bigint NOT NULL")
            .And.Contain("security.AccessRequests")
            .And.Contain("Version bigint NOT NULL")
            .And.NotContain("Password")
            .And.NotContain("ConnectionString");
    }

    [Fact]
    public void ManagementReportingSqlAsset_UsesLimitedViewsAndSupportingIndexes()
    {
        string root = FindRepositoryRoot();
        string sql = File.ReadAllText(Path.Combine(root, "sql", "schema", "005-management-reporting-read-model.sql"));

        sql.Should().Contain("CREATE OR ALTER VIEW reporting.ManagementAuditEvents")
            .And.Contain("CREATE OR ALTER VIEW reporting.ManagementWorkflowEvents")
            .And.Contain("CREATE OR ALTER VIEW reporting.ManagementOperationalStatus")
            .And.Contain("IX_AuditLog_ActionOccurredAt")
            .And.Contain("IX_OperationalRecordWorkflowHistory_StateOccurredAt")
            .And.Contain("IX_JiraTransfers_ReconciliationUpdatedAt");
        sql.Should().NotContain("SourceIp")
            .And.NotContain("Requester")
            .And.NotContain("Password")
            .And.NotContain("ConnectionString");
    }

    [Fact]
    public void ManagementReportingMigrationAndRepository_UseBoundedServerSideQueries()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "005-management-reporting-read-model.sql"));
        string repository = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "Reporting", "SqlManagementReportingRepository.cs"));

        migration.Should().Contain(":r ..\\schema\\005-management-reporting-read-model.sql");
        repository.Should().Contain("FROM reporting.ManagementAuditEvents")
            .And.Contain("FROM reporting.ManagementWorkflowEvents")
            .And.Contain("FROM reporting.ManagementOperationalStatus")
            .And.Contain("OccurredAt >= @FromInclusive AND OccurredAt < @ToExclusive")
            .And.Contain("COUNT_BIG(DISTINCT Actor)")
            .And.Contain("MIN(EarliestAt) AS CoverageFromUtc")
            .And.Contain("WHERE Action IN @CoverageActions")
            .And.Contain("@ImportToPreviewKey AS [Key]")
            .And.Contain("MIN(CONVERT(float, DurationMilliseconds)) / 1000.0 AS MinimumSeconds")
            .And.Contain("AVG(CONVERT(float, DurationMilliseconds)) / 1000.0 AS AverageSeconds")
            .And.Contain("MAX(CONVERT(float, DurationMilliseconds)) / 1000.0 AS MaximumSeconds")
            .And.Contain("ManagementReportingDurationKeys.ClaimToJiraCreation")
            .And.Contain("ManagementReportingDurationKeys.ClaimToCompletion")
            .And.Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY")
            .And.Contain("commandTimeout: CommandTimeoutSeconds")
            .And.Contain("cancellationToken: cancellationToken");
    }

    [Fact]
    public void ApplicationSessionMigration_UsesAuthoritativeLifecycleWithoutDeleteOrSecrets()
    {
        string root = FindRepositoryRoot();
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "007-application-session-governance.sql"));
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "007-application-session-governance.sql"));
        string repository = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "Sessions", "SqlApplicationSessionRepository.cs"));
        string accessRepository = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "Access", "SqlAccessRepository.cs"));

        schema.Should().Contain("CREATE TABLE security.ApplicationSessions")
            .And.Contain("SessionId uniqueidentifier")
            .And.Contain("LastSeenAtUtc datetimeoffset(7)")
            .And.Contain("AbsoluteExpiresAtUtc datetimeoffset(7)")
            .And.Contain("AuthenticationMethod nvarchar(64)")
            .And.Contain("AccessVersion bigint")
            .And.Contain("CREATE OR ALTER VIEW reporting.ManagementSessionStatus")
            .And.NotContain("Password")
            .And.NotContain("Token")
            .And.NotContain("DELETE ");
        migration.Should().Contain(":r ..\\schema\\007-application-session-governance.sql");
        repository.Should().Contain("INSERT INTO security.ApplicationSessions")
            .And.Contain("UPDATE security.ApplicationSessions")
            .And.Contain("CommandTimeoutSeconds = 15")
            .And.Contain("LastSeenAtUtc <= @PersistBeforeUtc")
            .And.Contain("cancellationToken: cancellationToken")
            .And.NotContain("DELETE FROM")
            .And.NotContain("Retry");
        accessRepository.Should().Contain("LastAuthenticatedAt <= DATEADD(MINUTE, -@ActivityPersistenceIntervalMinutes")
            .And.NotContain("UPDATE security.Users SET LastAuthenticatedAt = SYSUTCDATETIME() WHERE UserId = @UserId;");
    }

    [Fact]
    public void SqlRepositories_MatchInstalledSchemaAndConcurrencyContracts()
    {
        string root = FindRepositoryRoot();
        string access = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "Access", "SqlAccessRepository.cs"));
        string sessions = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "Sessions", "SqlApplicationSessionRepository.cs"));
        string operational = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "OperationalRecords", "SqlOperationalRecordRepository.cs"));
        string audit = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "Audit", "SqlAuditWriter.cs"));
        string firstAdmin = File.ReadAllText(Path.Combine(root, "src", "SecureOps.Infrastructure", "Access", "SqlFirstAdminBootstrapStore.cs"));

        access.Should().Contain("IsolationLevel.Serializable")
            .And.Contain("WITH (UPDLOCK, HOLDLOCK)")
            .And.Contain("Version = Version + 1")
            .And.Contain("AccessVersion = AccessVersion + 1")
            .And.Contain("LoginName, DisplayName, Mail, Uid, ProfileUpdatedAt")
            .And.Contain("LoginName = COALESCE(@LoginName, LoginName)")
            .And.Contain("ProfileUpdatedAt = CASE WHEN")
            .And.Contain("CONVERT(varbinary(max), LoginName)")
            .And.Contain("ra.RevokedAt IS NULL")
            .And.Contain("transaction.CommitAsync(cancellationToken)")
            .And.NotContain("DELETE FROM");
        sessions.Should().Contain("WHERE SessionId = @SessionId AND EndedAtUtc IS NULL")
            .And.Contain("AbsoluteExpiresAtUtc <= @NowUtc OR LastSeenAtUtc <= @IdleCutoffUtc")
            .And.Contain("OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY")
            .And.NotContain("DELETE FROM");
        operational.Should().Contain("SourceCreatedAt AS CreatedAt")
            .And.Contain("CONVERT(bigint, r.RowVersion) AS Version")
            .And.Contain("OperationalRecordWorkflowHistory")
            .And.Contain("IsolationLevel.Serializable")
            .And.Contain("transaction.CommitAsync(cancellationToken)")
            .And.NotContain("DELETE FROM");
        audit.Should().Contain("INSERT INTO audit.AuditLog")
            .And.Contain("commandTimeout: CommandTimeoutSeconds")
            .And.NotContain("UPDATE audit.AuditLog")
            .And.NotContain("DELETE FROM audit.AuditLog");
        firstAdmin.Should().Contain("IsolationLevel.Serializable")
            .And.Contain("SET XACT_ABORT ON")
            .And.Contain("security.Roles WITH (UPDLOCK, HOLDLOCK)")
            .And.Contain("security.RoleAssignments WITH (HOLDLOCK)")
            .And.Contain("WHERE RoleId = @RoleId")
            .And.NotContain("RevokedAt IS NULL")
            .And.Contain("INSERT INTO audit.AuditLog")
            .And.Contain("transaction.CommitAsync(cancellationToken)")
            .And.Contain("transaction.RollbackAsync(cancellationToken)")
            .And.NotContain("DELETE FROM")
            .And.NotContain("UPDATE audit.AuditLog");
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
