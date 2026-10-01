[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_]{1,40}$')]
    [string]$DatabaseSuffix = ([Guid]::NewGuid().ToString('N')),
    [switch]$RunTests,
    [switch]$IncludeAnnouncementDrafts,
    [switch]$IncludeAnnouncementSources,
    [switch]$IncludeAnnouncementPreparations,
    [switch]$DeferInUseFollowup
)

$ErrorActionPreference = 'Stop'
if ($DeferInUseFollowup -and $RunTests) { throw 'Current regression requires 024. Deferred mode is for the isolated upgrade checkpoint only.' }
# Fresh isolated acceptance includes additive 022-024; never targets an installed corporate database.
$IncludeAnnouncementPreparations = $true
if ($IncludeAnnouncementPreparations) { $IncludeAnnouncementSources = $true }
if ($IncludeAnnouncementSources) { $IncludeAnnouncementDrafts = $true }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$database = 'SecureOps_ResourcesV1_' + $DatabaseSuffix
$server = '(localdb)\SecureOpsResourcesV1'
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$null = Get-Command SqlLocalDB -ErrorAction Stop
# Only the separately authorized per-user instance is supported. Never create/reconfigure a shared service.
& SqlLocalDB info SecureOpsResourcesV1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Provision the isolated SecureOpsResourcesV1 LocalDB instance first.' }

function Invoke-ResourceTestSql {
    [CmdletBinding()]
    param([string]$Query, [string]$File)
    if ($File) {
        & $sqlcmd -S $server -d $database -E -I -b -i $File
    } else {
        & $sqlcmd -S $server -d $database -E -I -b -Q $Query
    }
    if ($LASTEXITCODE -ne 0) { throw 'Isolated resource SQL validation failed; database retained for inspection.' }
}

# Database name is restricted to the validated test prefix and ASCII suffix. Existing databases are never overwritten.
& $sqlcmd -S $server -d master -E -I -b -Q "IF DB_ID(N'$database') IS NOT NULL THROW 51000, 'Refuse existing database.', 1; CREATE DATABASE [$database];"
if ($LASTEXITCODE -ne 0) { throw 'Fresh isolated database creation failed.' }
Push-Location (Join-Path $root 'sql\migrations')
try {
    Get-ChildItem -File -Filter '*.sql' | Where-Object { $_.Name -match '^00[1-8]-' } | Sort-Object Name | ForEach-Object {
        Invoke-ResourceTestSql -File $_.Name
    }
    Invoke-ResourceTestSql -Query @'
INSERT INTO security.Users(UserId, CorporateIdentity, AuthenticationSource, AccessStatus)
VALUES('10000000-0000-0000-0000-000000000001','synthetic:resource-upgrade','test','Approved');
INSERT INTO ops.OperationalRecords(OperationalRecordId,SourceRecordId,OrCode,Title,Description,Classification,JiraEligible,EligibilityReason,WorkflowState,UpdatedAt)
VALUES('20000000-0000-0000-0000-000000000001','123','OR-123','Synthetic upgrade fixture','Synthetic description','NeedsManualReview',0,'Unproven','NeedsManualReview',SYSUTCDATETIME());
'@
    Invoke-ResourceTestSql -File '009-sdm-evaluation-foundation.sql'
    Invoke-ResourceTestSql -File '010-resource-catalogue.sql'
    Invoke-ResourceTestSql -Query @'
INSERT INTO ops.JiraTransfers(JiraTransferId,OperationalRecordId,MappingVersion,IdempotencyKey,CreatedByActor,CreatedAt,UpdatedAt)
VALUES(NEWID(),'20000000-0000-0000-0000-000000000001','synthetic-legacy',REPLICATE('a',64),'synthetic:upgrade',SYSUTCDATETIME(),SYSUTCDATETIME());
INSERT INTO ops.OperationalRecordWorkflowHistory(OperationalRecordId,WorkflowState,Actor,CorrelationId,OccurredAt)
VALUES('20000000-0000-0000-0000-000000000001','NeedsManualReview','synthetic:upgrade','synthetic',SYSUTCDATETIME());
'@
    Invoke-ResourceTestSql -File '011-independent-source-close.sql'
    Invoke-ResourceTestSql -File '011-independent-source-close.sql'
    Invoke-ResourceTestSql -Query @'
IF NOT EXISTS(SELECT 1 FROM ops.OperationalRecords WHERE OperationalRecordId='20000000-0000-0000-0000-000000000001'
    AND SdmEvaluationJson IS NULL AND Classification='NeedsManualReview' AND JiraEligible=0)
    THROW 51091, 'Pre-upgrade SDM row was not preserved.', 1;
IF EXISTS(SELECT 1 FROM resources.Links) OR EXISTS(SELECT 1 FROM resources.Categories) OR EXISTS(SELECT 1 FROM resources.PersonalPreferences)
    THROW 51092, 'Migration must not seed catalogue or personal data.', 1;
IF EXISTS(SELECT 1 FROM ops.JiraTransfers WHERE SourceCloseRequested<>0)
    OR EXISTS(SELECT 1 FROM ops.OperationalRecordWorkflowHistory WHERE SourceCloseRequested<>0)
    THROW 51094, 'Legacy transfers must not gain close intent.', 1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE name IN ('TR_AuditLog_AppendOnly','TR_OperationalRecordWorkflowHistory_AppendOnly') AND is_disabled=1)
    THROW 51093, 'Append-only protection is disabled.', 1;
'@
    # Guarded rerun must not create duplicate role seeds or alter user data.
    Invoke-ResourceTestSql -File '010-resource-catalogue.sql'
} finally {
    Pop-Location
}

Push-Location (Join-Path $root 'sql\migrations')
try {
    Invoke-ResourceTestSql -File '012-in-use-workspace.sql'
    Invoke-ResourceTestSql -File '013-sdm-pilot-policy.sql'
    if ($IncludeAnnouncementDrafts) { Invoke-ResourceTestSql -File '014-announcement-drafts.sql' }
    if ($IncludeAnnouncementDrafts) { Invoke-ResourceTestSql -File '015-announcement-owner-index.sql' }
    if ($IncludeAnnouncementSources) { Invoke-ResourceTestSql -File '016-announcement-source-jobs.sql' }
    if ($IncludeAnnouncementSources) { Invoke-ResourceTestSql -File '017-announcement-source-recovery.sql' }
    if ($IncludeAnnouncementPreparations) { Invoke-ResourceTestSql -File '018-announcement-preparations.sql' }
    Invoke-ResourceTestSql -File '019-access-role-bundles.sql'
    Invoke-ResourceTestSql -File '020-operational-mail-commands.sql'
    Invoke-ResourceTestSql -File '021-workflow-actor-and-closure-evidence.sql'
    if (!$DeferInUseFollowup) {
        Invoke-ResourceTestSql -File '022-in-use-review-and-execution.sql'
        Invoke-ResourceTestSql -File '023-workflow-report-snapshots.sql'
        Invoke-ResourceTestSql -File '024-in-use-report-catalogue.sql'
    }
}
finally { Pop-Location }

if ($RunTests) {
    $previous = $env:SECUREOPS_SQL_TEST_CONNECTION
    try {
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
        $builder['Data Source'] = $server
        $builder['Initial Catalog'] = $database
        $builder['Integrated Security'] = $true
        $builder['Encrypt'] = $false # LocalDB-only test transport; not a deployment connection policy.
        $builder['Connect Timeout'] = 15
        $env:SECUREOPS_SQL_TEST_CONNECTION = $builder.ConnectionString
        Push-Location $root
        try {
            & dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-build --filter FullyQualifiedName~ResourceSqlTests
            if ($LASTEXITCODE -ne 0) { throw 'Isolated SQL integration tests failed.' }
        } finally { Pop-Location }
    } finally { $env:SECUREOPS_SQL_TEST_CONNECTION = $previous }
}
[PSCustomObject]@{ Database = $database; Migrations = $(if ($DeferInUseFollowup) { '001-021' } else { '001-024' }); UpgradeFixture = 'Passed'; SqlTestsRequested = [bool]$RunTests; RetainedForInspection = $true }
