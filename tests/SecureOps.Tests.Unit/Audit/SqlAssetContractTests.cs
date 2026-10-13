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
            .And.HaveCount(36)
            .And.ContainSingle(name => name == "036-service-account-events.sql")
            .And.ContainSingle(name => name == "035-service-account-plan-operational-records.sql")
            .And.ContainSingle(name => name == "034-service-account-plan-evidence.sql")
            .And.ContainSingle(name => name == "033-service-account-change-plans.sql")
            .And.ContainSingle(name => name == "032-access-request-user-index.sql")
            .And.ContainSingle(name => name == "031-service-account-requested-gmsa-name.sql")
            .And.ContainSingle(name => name == "030-service-account-usage-scans.sql")
            .And.ContainSingle(name => name == "029-service-account-scope-bootstrap.sql")
            .And.ContainSingle(name => name == "028-admin-service-account-operations.sql")
            .And.ContainSingle(name => name == "027-admin-service-account-navigation.sql")
            .And.ContainSingle(name => name == "026-service-account-usage-rules.sql")
            .And.ContainSingle(name => name == "025-service-accounts.sql")
            .And.ContainSingle(name => name == "024-in-use-report-catalogue.sql")
            .And.ContainSingle(name => name == "022-in-use-review-and-execution.sql")
            .And.ContainSingle(name => name == "019-access-role-bundles.sql")
            .And.ContainSingle(name => name == "020-operational-mail-commands.sql")
            .And.ContainSingle(name => name == "021-workflow-actor-and-closure-evidence.sql")
            .And.ContainSingle(name => name == "018-announcement-preparations.sql")
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
            .And.Contain("commandTimeout: _commandTimeoutSeconds")
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
            .And.Contain("_commandTimeoutSeconds = 15")
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
            .And.Contain("commandTimeout: _commandTimeoutSeconds")
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

    [Fact]
    public void AdminOperationAmendment_PreservesScopeAndRequiresAtomicVersionedAudit()
    {
        string sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "sql", "schema", "028-admin-service-account-operations.sql"));
        sql.Should().Contain("IsProtected=1 AND IsSeeded=1")
            .And.Contain("027 Admin navigation baseline required")
            .And.Contain("028 already satisfied")
            .And.Contain("ServiceAccounts.Import")
            .And.Contain("ServiceAccounts.Verify")
            .And.Contain("Version=Version+1")
            .And.Contain("AccessVersion=AccessVersion+1")
            .And.Contain("a.RevokedAt IS NULL")
            .And.Contain("INSERT INTO audit.AuditLog")
            .And.Contain("ROLLBACK TRANSACTION")
            .And.NotContain("INSERT INTO svcacct.ScopeGrants")
            .And.NotContain("INSERT INTO security.RoleAssignments")
            .And.NotContain("ALTER ROLE")
            .And.NotContain("UPDATE audit.AuditLog");
    }

    [Fact]
    public void ScopeBootstrapMigration_IncludesReviewedCandidateOnceAndKeepsSelfGrantGuard()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "029-service-account-scope-bootstrap.sql"));
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "029-service-account-scope-bootstrap.sql"));
        string candidate = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-003-scope-bootstrap.sql"));

        migration.Should().Contain(":r ../schema/029-service-account-scope-bootstrap.sql");
        schema.Should().Contain("svcacct.ScopeGrants")
            .And.Contain("requires reviewed 025/026")
            .And.Contain(":r ../pending/service-accounts/SA-003-scope-bootstrap.sql");
        candidate.Should().Contain("already applied; compare definitions, do not replay")
            .And.Contain("(IsBootstrap = 0 AND UserId <> GrantedBy)")
            .And.Contain("IsBootstrap = 1 AND UserId = GrantedBy AND ScopeKind = 'All'")
            .And.Contain("CREATE UNIQUE INDEX UX_SaScopeGrants_OneBootstrap ON svcacct.ScopeGrants(IsBootstrap) WHERE IsBootstrap = 1")
            .And.NotContain("INSERT INTO svcacct.ScopeGrants")
            .And.NotContain("DELETE FROM")
            .And.NotContain("ALTER ROLE");
    }

    [Fact]
    public void UsageScanMigration_IncludesReviewedCandidateOnce_IsAppendOnly_AndHasNoSecretColumn()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "030-service-account-usage-scans.sql"));
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "030-service-account-usage-scans.sql"));
        string candidate = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-004-usage-scans.sql"));
        string grants = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-004-API-permissions.sql"));

        migration.Should().Contain(":r ../schema/030-service-account-usage-scans.sql");
        schema.Should().Contain("requires reviewed 025/026").And.Contain(":r ../pending/service-accounts/SA-004-usage-scans.sql");
        candidate.Should().Contain("already applied; compare definitions, do not replay");
        foreach (string table in new[] { "UsageScans", "UsageScanServers", "UsageScanItems", "UsageScanLinks", "UsageScanDecisions" })
        {
            candidate.Should().Contain($"CREATE TABLE svcacct.{table}(");
            candidate.Should().MatchRegex($@"CREATE TRIGGER svcacct\.TR_Sa\w+_Immutable ON svcacct\.{table} AFTER UPDATE, DELETE");
            grants.Should().Contain($"GRANT SELECT, INSERT ON OBJECT::svcacct.{table} TO svcacct_api_runtime;");
        }

        candidate.Should().NotContainEquivalentOf("password").And.NotContain("ALTER TABLE").And.NotContain("DROP ").And.NotContain("ALTER ROLE");
        grants.Should().NotContain("UPDATE").And.NotContain("DELETE").And.NotContain("ALTER ROLE");
    }

    [Fact]
    public void ChangePlanMigration_IsAdditive_AppendOnly_RefusesReplay_AndGrantsNoDelete()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "033-service-account-change-plans.sql"));
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "033-service-account-change-plans.sql"));
        string candidate = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-006-change-plans.sql"));
        string grants = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-006-API-permissions.sql"));

        migration.Should().Contain(":r ../schema/033-service-account-change-plans.sql");
        schema.Should().Contain("requires reviewed 025, 030 and 031").And.Contain(":r ../pending/service-accounts/SA-006-change-plans.sql");
        candidate.Should().Contain("already applied; compare definitions, do not replay");
        foreach (string table in new[] { "ChangePlanAccounts", "ChangePlanPreviews", "ChangePlanItems", "ChangePlanApprovals", "ChangeItemChecks", "ChangePlanEvents" })
        {
            candidate.Should().Contain($"CREATE TABLE svcacct.{table}(");
            candidate.Should().MatchRegex($@"CREATE TRIGGER svcacct\.TR_Sa\w+_AppendOnly ON svcacct\.{table} AFTER UPDATE, DELETE");
            grants.Should().Contain($"GRANT SELECT, INSERT ON OBJECT::svcacct.{table} TO svcacct_api_runtime;");
        }

        candidate.Should().Contain("CREATE TRIGGER svcacct.TR_SaChangePlans_Fixed ON svcacct.ChangePlans AFTER UPDATE, DELETE")
            .And.Contain("CREATE TRIGGER svcacct.TR_SaChangePlanApprovals_Separation ON svcacct.ChangePlanApprovals AFTER INSERT")
            .And.Contain("CONSTRAINT UQ_SaChangePlanApprovals_Preview UNIQUE (PreviewId)");
        // T8: UPDATE only on the status columns; the trigger compares fixed columns exactly (the database collation is CI_AS).
        grants.Should().Contain("GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlans TO svcacct_api_runtime;")
            .And.Contain("GRANT UPDATE ON OBJECT::svcacct.ChangePlans (Status, CurrentPreviewVersion, UpdatedBy, UpdatedAt) TO svcacct_api_runtime;")
            .And.NotContain("INSERT, UPDATE ON");
        candidate.Should().Contain("i.Title COLLATE Latin1_General_BIN2 <> d.Title COLLATE Latin1_General_BIN2 OR DATALENGTH(i.Title) <> DATALENGTH(d.Title)")
            .And.Contain("i.Kind COLLATE Latin1_General_BIN2 <> d.Kind COLLATE Latin1_General_BIN2 OR DATALENGTH(i.Kind) <> DATALENGTH(d.Kind)")
            .And.Contain("DATEPART(TZOFFSET, i.CreatedAt) <> DATEPART(TZOFFSET, d.CreatedAt)");
        foreach (string column in new[] { "Kind", "Status", "Action", "ComponentType", "Flag", "State", "Event" })
        {
            candidate.Should().MatchRegex($@"CHECK \({column} COLLATE Latin1_General_BIN2 IN \([^)]*\)\s+AND DATALENGTH\({column}\) = LEN\({column}\)\)");
        }
        // Additive only: no existing object is altered or dropped, no secret column, no role or delete grant.
        candidate.Should().NotContainEquivalentOf("password").And.NotContain("ALTER TABLE").And.NotContain("DROP ").And.NotContain("ALTER ROLE");
        grants.Should().NotContain("DELETE").And.NotContain("ALTER ROLE").And.NotContain("svcacct_worker");
    }

    [Fact]
    public void PlanEvidenceMigration_OnlyWidensOwnerCheck_RefusesReplay()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "034-service-account-plan-evidence.sql"));
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "034-service-account-plan-evidence.sql"));
        string candidate = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-007-plan-evidence.sql"));
        string original = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-001-service-accounts.sql"));

        migration.Should().Contain(":r ../schema/034-service-account-plan-evidence.sql");
        schema.Should().Contain("requires reviewed 033").And.Contain(":r ../pending/service-accounts/SA-007-plan-evidence.sql");
        candidate.Should().Contain("already applied; compare definitions, do not replay").And.Contain("THROW 51396");
        // The same constraint name comes back with exactly one more owner type, validated against existing rows.
        string before = Regex.Match(original, @"CONSTRAINT CK_SaEvidence_Owner CHECK \(OwnerEntityType IN \(([^)]*)\)\)").Groups[1].Value;
        before.Should().NotBeEmpty().And.NotContain("ChangePlan");
        candidate.Should().Contain("ALTER TABLE svcacct.Evidence DROP CONSTRAINT CK_SaEvidence_Owner;")
            .And.Contain($"ALTER TABLE svcacct.Evidence WITH CHECK ADD CONSTRAINT CK_SaEvidence_Owner CHECK (OwnerEntityType IN ({before},'ChangePlan'));");
        Regex.Matches(candidate, @"ALTER TABLE").Should().HaveCount(2, "only the owner check is replaced");
        candidate.Should().NotContain("ChangePlanItem").And.NotContain("GRANT").And.NotContain("DROP TABLE").And.NotContain("ALTER ROLE")
            .And.NotContain("INSERT").And.NotContain("UPDATE ").And.NotContain("DELETE ").And.NotContainEquivalentOf("password");
    }
    [Fact]
    public void AccountEventMigration_IsAdditive_AppendOnly_HasNoTextColumn_AndGrantsOnlySelectInsert()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "036-service-account-events.sql"));
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "036-service-account-events.sql"));
        string candidate = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-009-account-events.sql"));
        string grants = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-009-API-permissions.sql"));

        migration.Should().Contain(":r ../schema/036-service-account-events.sql");
        schema.Should().Contain("requires reviewed 025").And.Contain(":r ../pending/service-accounts/SA-009-account-events.sql");
        candidate.Should().Contain("already applied; compare definitions, do not replay").And.Contain("THROW 51410");
        foreach (string table in new[] { "AccountEvents", "AccountEventItems" })
        {
            candidate.Should().Contain($"CREATE TABLE svcacct.{table}(");
            candidate.Should().MatchRegex($@"CREATE TRIGGER svcacct\.TR_Sa{table}_AppendOnly ON svcacct\.{table} AFTER UPDATE, DELETE AS\s+BEGIN THROW 51411");
            grants.Should().Contain($"GRANT SELECT, INSERT ON OBJECT::svcacct.{table} TO svcacct_api_runtime;");
        }

        // E5: one key per account. Design §5.6: no mail body, sender, name, address or other free-text column (no nvarchar at all).
        candidate.Should().Contain("CONSTRAINT UQ_SaAccountEvents_Key UNIQUE (AccountId, IdempotencyKey)");
        string ddl = string.Join('\n', candidate.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal))).Split("*/", 2)[1];
        // "PasswordChanged" is an event kind value; no column may hold a password, a body or any other text.
        ddl.Should().NotContainEquivalentOf("nvarchar").And.NotContainEquivalentOf("varbinary").And.NotContain("ALTER TABLE").And.NotContain("DROP ").And.NotContain("GRANT");
        Regex.Matches(ddl, @"^\s+(\w+) [a-z]+", RegexOptions.Multiline).Select(m => m.Groups[1].Value)
            .Should().NotContain(c => c.Contains("Password", StringComparison.OrdinalIgnoreCase) || c.Contains("Body", StringComparison.OrdinalIgnoreCase)
                || c.Contains("Subject", StringComparison.OrdinalIgnoreCase) || c.Contains("Sender", StringComparison.OrdinalIgnoreCase));
        Regex.Matches(ddl, @"CREATE TABLE").Should().HaveCount(2, "036 adds only the event header and item tables");
        grants.Should().NotContain("UPDATE").And.NotContain("DELETE").And.NotContain("ALTER ROLE").And.NotContain("svcacct_worker");
    }

    [Fact]
    public void PlanOperationalRecordMigration_IsAdditive_GuardsContentAndTransitions_AndGrantsNoDelete()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "035-service-account-plan-operational-records.sql"));
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "035-service-account-plan-operational-records.sql"));
        string candidate = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-008-plan-operational-records.sql"));
        string grants = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-008-API-permissions.sql"));

        migration.Should().Contain(":r ../schema/035-service-account-plan-operational-records.sql");
        schema.Should().Contain("requires reviewed 033 and 034").And.Contain(":r ../pending/service-accounts/SA-008-plan-operational-records.sql");
        candidate.Should().Contain("already applied; compare definitions, do not replay").And.Contain("THROW 51400")
            .And.Contain("CREATE TABLE svcacct.ChangePlanRecordSubmissions(").And.Contain("CREATE TABLE svcacct.ChangePlanRecordSubmissionEvents(")
            .And.MatchRegex(@"CREATE TRIGGER svcacct\.TR_SaRecordSubmissionEvents_AppendOnly ON svcacct\.ChangePlanRecordSubmissionEvents AFTER UPDATE, DELETE AS\s+BEGIN THROW 51402")
            .And.Contain("CREATE TRIGGER svcacct.TR_SaRecordSubmissions_Guard ON svcacct.ChangePlanRecordSubmissions AFTER UPDATE, DELETE AS")
            .And.Contain("THROW 51403").And.Contain("THROW 51404")
            .And.Contain("CONSTRAINT UQ_SaRecordSubmissions_Key UNIQUE (AccountId, IdempotencyKey)")
            .And.Contain("CREATE UNIQUE INDEX UX_SaRecordSubmissions_Active ON svcacct.ChangePlanRecordSubmissions(PlanId, AccountId)")
            .And.Contain("WHERE State IN ('Sending','Created','PartiallyCreated','OutcomeUnknown','Linked');");

        // Additive: no existing object changes (OcoNumber stays required, ADR-0030 §10 is open); no secret or remote-body column.
        string ddl = string.Join('\n', candidate.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal))).Split("*/", 2)[1];
        ddl.Should().NotContain("ALTER TABLE").And.NotContain("DROP ").And.NotContain("GRANT").And.NotContain("ALTER ROLE").And.NotContain("OcoNumber");
        Regex.Matches(ddl, @"CREATE TABLE").Should().HaveCount(2, "035 adds only the submission and its event table");
        Regex.Matches(ddl, @"^\s+(\w+) [a-z]+", RegexOptions.Multiline).Select(m => m.Groups[1].Value)
            .Should().NotContain(c => c.Contains("Password", StringComparison.OrdinalIgnoreCase) || c.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || c.Contains("Session", StringComparison.OrdinalIgnoreCase) || c.Contains("Url", StringComparison.OrdinalIgnoreCase)
                || c.Contains("Body", StringComparison.OrdinalIgnoreCase) || c.Contains("Exception", StringComparison.OrdinalIgnoreCase));

        grants.Should().Contain("GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanRecordSubmissions TO svcacct_api_runtime;")
            .And.Contain("GRANT UPDATE ON OBJECT::svcacct.ChangePlanRecordSubmissions (State, ExternalId, ExternalNumber, LinkVerification, OutcomeReason, UpdatedBy, UpdatedAt)")
            .And.Contain("GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanRecordSubmissionEvents TO svcacct_api_runtime;")
            .And.NotContain("DELETE").And.NotContain("ALTER ROLE").And.NotContain("svcacct_worker");
    }

    [Fact]
    public void RequestedGmsaNameMigration_AddsTwoNullableColumns_RefusesReplay_AndGrantsNothing()
    {
        string root = FindRepositoryRoot();
        string migration = File.ReadAllText(Path.Combine(root, "sql", "migrations", "031-service-account-requested-gmsa-name.sql"));
        string schema = File.ReadAllText(Path.Combine(root, "sql", "schema", "031-service-account-requested-gmsa-name.sql"));
        string candidate = File.ReadAllText(Path.Combine(root, "sql", "pending", "service-accounts", "SA-005-requested-gmsa-name.sql"));

        migration.Should().Contain(":r ../schema/031-service-account-requested-gmsa-name.sql");
        schema.Should().Contain("requires reviewed 025 and 030").And.Contain(":r ../pending/service-accounts/SA-005-requested-gmsa-name.sql");
        candidate.Should().Contain("already applied; compare definitions, do not replay")
            .And.Contain("ALTER TABLE svcacct.WorkRequests ADD RequestedGmsaName nvarchar(256) NULL;")
            .And.Contain("ALTER TABLE svcacct.IdentityTransitions ADD RequestedGmsaName nvarchar(256) NULL;")
            .And.NotContain("DEFAULT").And.NotContain("UPDATE svcacct").And.NotContain("DROP ").And.NotContain("GRANT ").And.NotContain("ALTER ROLE");
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
