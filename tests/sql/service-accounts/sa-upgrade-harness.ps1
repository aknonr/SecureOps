[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_]{1,30}$')][string]$DatabaseSuffix,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$SqlAssetRoot)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sql = if ($SqlAssetRoot) { (Resolve-Path -LiteralPath $SqlAssetRoot).Path } else { Join-Path $root 'sql' }
$server = '(localdb)\SecureOpsResourcesV1'
$database = 'SecureOps_SaUpgrade' + $DatabaseSuffix
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!(Test-Path $evidence -PathType Container)) { throw 'An existing private evidence directory is required.' }
$failureFile = Join-Path $evidence '025-failure-injection.sql'
if (Test-Path $failureFile) { throw 'Refuse existing failure evidence.' }
function Invoke-LocalSql([string]$Query, [string]$File) {
    if ($File) { & sqlcmd -S $server -E -I -b -d $database -i $File }
    else { & sqlcmd -S $server -E -I -b -d $database -Q $Query }
    if ($LASTEXITCODE -ne 0) { throw 'Isolated upgrade failed; database retained.' }
}
& sqlcmd -S $server -E -I -b -d master -Q "IF DB_ID(N'$database') IS NOT NULL THROW 51000,'Refuse existing database.',1; CREATE DATABASE [$database];"
if ($LASTEXITCODE -ne 0) { throw 'Fresh disposable creation failed.' }
Push-Location (Join-Path $sql 'migrations')
try {
    Get-ChildItem -File '*.sql' | Where-Object { [int]$_.Name.Substring(0,3) -le 23 } | Sort-Object Name | ForEach-Object {
        Invoke-LocalSql -File $_.Name
    }
    Invoke-LocalSql -Query @'
INSERT security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus)
VALUES('10000000-0000-0000-0000-000000000025','synthetic:sa-upgrade','test','Approved');
INSERT ops.OperationalRecords(OperationalRecordId,SourceRecordId,OrCode,Title,Description,Classification,JiraEligible,EligibilityReason,WorkflowState,UpdatedAt)
VALUES('20000000-0000-0000-0000-000000000025','synthetic-sa-upgrade','OR-SYN-SA-UPGRADE','Synthetic retained request','Synthetic','NeedsManualReview',0,'Synthetic','NeedsManualReview',SYSUTCDATETIME());
INSERT audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
VALUES(SYSUTCDATETIME(),'synthetic:sa-upgrade','Synthetic.Upgrade','synthetic-sa-upgrade','{}');
'@
    $definitions = @'
SET NOCOUNT ON;
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(
 SELECT s.name AS SchemaName,t.name AS TableName,c.name AS ColumnName,c.column_id,c.system_type_id,c.max_length,c.is_nullable
 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
 WHERE t.name IN('InUseExecutions','InUseExecutionEvents','InUseServerReviews','WorkflowSnapshots','WorkflowFacts','InUseArchiveReceipts')
 ORDER BY s.name,t.name,c.column_id FOR XML RAW))),2);
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(
 SELECT OBJECT_SCHEMA_NAME(t.object_id) AS SchemaName,t.name,m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id=t.object_id
 WHERE t.parent_id IN(OBJECT_ID('ops.InUseExecutions'),OBJECT_ID('ops.InUseExecutionEvents'),OBJECT_ID('ops.InUseServerReviews'),
 OBJECT_ID('reporting.WorkflowSnapshots'),OBJECT_ID('reporting.WorkflowFacts'),OBJECT_ID('reporting.InUseArchiveReceipts'))
 ORDER BY OBJECT_SCHEMA_NAME(t.object_id),t.name FOR XML RAW))),2);
'@
    $before = @(& sqlcmd -S $server -E -I -b -d $database -h -1 -W -Q $definitions) -join "`n"
    if ($LASTEXITCODE -ne 0) { Write-Output $before; throw 'Baseline fingerprint failed.' }
    # 025 must fail without 024, before creating any module object.
    & sqlcmd -S $server -E -I -b -d $database -i '025-service-accounts.sql'
    if ($LASTEXITCODE -eq 0) { throw 'Missing 024 prerequisite was accepted.' }
    Invoke-LocalSql -Query "IF SCHEMA_ID(N'svcacct') IS NOT NULL THROW 51000,'Partial module after prerequisite refusal.',1;"
    Invoke-LocalSql -File '024-in-use-report-catalogue.sql'
    $candidate = [IO.File]::ReadAllText((Join-Path $sql 'pending/service-accounts/SA-001-service-accounts.sql'))
    if (($candidate.Split(@('COMMIT TRANSACTION;'),[StringSplitOptions]::None)).Length -ne 2) { throw 'Unexpected commit boundary.' }
    [IO.File]::WriteAllText($failureFile, $candidate.Replace('COMMIT TRANSACTION;', "THROW 51399, 'Synthetic install failure before commit.', 1;`nCOMMIT TRANSACTION;"))
    & sqlcmd -S $server -E -I -b -d $database -i $failureFile
    if ($LASTEXITCODE -eq 0) { throw 'Failure injection did not fail.' }
    Invoke-LocalSql -Query "IF SCHEMA_ID(N'svcacct') IS NOT NULL THROW 51000,'Partial module survived failed transaction.',1;"
    Invoke-LocalSql -File '025-service-accounts.sql'
    Invoke-LocalSql -File '../pending/service-accounts/SA-API-permissions.sql'
    Invoke-LocalSql -File '../pending/service-accounts/SA-Worker-permissions.sql'
    $after = @(& sqlcmd -S $server -E -I -b -d $database -h -1 -W -Q $definitions) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $before -cne $after) { throw 'Baseline column/trigger definitions changed.' }
    Invoke-LocalSql -Query @'
IF NOT EXISTS(SELECT 1 FROM security.Users WHERE CorporateIdentity='synthetic:sa-upgrade' AND AccessStatus='Approved')
 OR NOT EXISTS(SELECT 1 FROM ops.OperationalRecords WHERE SourceRecordId='synthetic-sa-upgrade' AND JiraEligible=0 AND SourceSynthetic IS NULL)
 OR NOT EXISTS(SELECT 1 FROM audit.AuditLog WHERE CorrelationId='synthetic-sa-upgrade')
 THROW 51000,'Existing synthetic data changed.',1;
IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('svcacct')) <> 25
 THROW 51000,'Module table inventory differs.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id IN(SELECT object_id FROM sys.tables WHERE schema_id=SCHEMA_ID('svcacct')) AND is_disabled=1)
 THROW 51000,'Protective trigger disabled.',1;
SELECT 'Baseline retained; prerequisite and atomicity refusals passed; 024 then 025 installed' AS Outcome;
'@
} finally { Pop-Location }
[pscustomobject]@{Database=$database;Baseline='001-023';Delta='024 then 025';Retained=$true}
