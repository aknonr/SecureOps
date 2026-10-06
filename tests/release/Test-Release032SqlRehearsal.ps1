[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_]{1,30}$')][string]$DatabaseSuffix,
    [Parameter(Mandatory)][ValidateSet(27,28)][int]$Baseline,
    [Parameter(Mandatory)][psobject]$SqlPlan,
    [Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$server='(localdb)\SecureOpsResourcesV1'
$database='SecureOps_Release'+$DatabaseSuffix
$sqlcmd=(Get-Command sqlcmd -ErrorAction Stop).Source
function Invoke-LocalSql([string]$Query,[string]$File) {
    $output=if ($File) { & $sqlcmd -S $server -d $database -E -I -b -i $File 2>&1 }
        else { & $sqlcmd -S $server -d $database -E -I -b -h -1 -W -Q $Query 2>&1 }
    if ($LASTEXITCODE -ne 0) { throw "Synthetic SQL step failed: $output" }
    return $output
}
& $sqlcmd -S $server -d master -E -I -b -Q "IF DB_ID(N'$database') IS NOT NULL THROW 51000,'Refuse existing database.',1; CREATE DATABASE [$database];" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Fresh LocalDB database required.' }
$export=Join-Path $EvidenceDirectory "delta-$Baseline"
if (Test-Path $export) { throw 'Refuse existing SQL export.' }
foreach ($relative in @($SqlPlan.DeltaFiles)+@($SqlPlan.RoleFiles)) {
    $target=Join-Path $export $relative
    New-Item -ItemType Directory (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
Push-Location (Join-Path $root 'sql/migrations')
try {
    Get-ChildItem -File -Filter '*.sql' | Where-Object { [int]$_.Name.Substring(0,3) -le $Baseline } | Sort-Object Name |
        ForEach-Object { Invoke-LocalSql -File $_.Name | Out-Null }
} finally { Pop-Location }
Invoke-LocalSql -File (Join-Path $root 'sql/pending/service-accounts/SA-API-permissions.sql') | Out-Null
Invoke-LocalSql -File (Join-Path $root 'sql/pending/service-accounts/SA-002-API-permissions.sql') | Out-Null
Invoke-LocalSql -Query @'
INSERT security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus)
VALUES('10000000-0000-0000-0000-000000000032','synthetic:release032','test','Approved');
INSERT security.AccessRequests(AccessRequestId,UserId,Status)
VALUES('20000000-0000-0000-0000-000000000032','10000000-0000-0000-0000-000000000032','Pending');
INSERT svcacct.Teams(Id,Name,NormalizedName,Provisional,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy)
VALUES('30000000-0000-0000-0000-000000000032',N'Synthetic release team',N'SYNTHETIC RELEASE TEAM',0,SYSUTCDATETIME(),
 '10000000-0000-0000-0000-000000000032',SYSUTCDATETIME(),'10000000-0000-0000-0000-000000000032');
'@ | Out-Null
$retained=@'
SET NOCOUNT ON;
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(
 SELECT * FROM svcacct.Teams ORDER BY Id FOR XML RAW))),2);
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(
 SELECT * FROM security.AccessRequests ORDER BY AccessRequestId FOR XML RAW))),2);
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(max),(
 SELECT * FROM security.AccessRequestHistory ORDER BY AccessRequestHistoryId FOR XML RAW))),2);
'@
$before=(Invoke-LocalSql -Query $retained) -join '|'
$inventory=Join-Path $root 'scripts/diagnostics/Get-InstalledMigrationInventory025To032.sql'
$initial=Invoke-LocalSql -File $inventory
$initial | Set-Content (Join-Path $EvidenceDirectory "inventory-$Baseline-before.txt")
foreach ($migration in 25..32) {
    $total=@(25,2,3,6,3,10,2,1)[$migration-25]
    $present=if ($migration -le $Baseline) { $total } else { 0 }
    if (($initial -join "`n") -notmatch "(?m)^0$migration\s+\S+\s+$present\s+$total\s+") { throw "Wrong initial inventory: $migration" }
}
if ($before -cne ((Invoke-LocalSql -Query $retained) -join '|')) { throw 'Read-only inventory changed retained rows.' }
$log=[Collections.Generic.List[string]]::new()
Push-Location (Join-Path $export 'sql/migrations')
try {
    foreach ($relative in $SqlPlan.ExecutionFiles) {
        Invoke-LocalSql -File (Join-Path $export $relative) | Out-Null
        $log.Add($relative)
        if ($relative -match '/030-') {
            Invoke-LocalSql -Query "IF COL_LENGTH('svcacct.WorkRequests','RequestedGmsaName') IS NOT NULL OR EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_AccessRequests_UserRequested') THROW 51401,'Wrong order.',1;" | Out-Null
        }
        if ($relative -match 'SA-004-API-permissions') {
            Invoke-LocalSql -Query "IF (SELECT COUNT(*) FROM sys.database_permissions WHERE class=1 AND grantee_principal_id=DATABASE_PRINCIPAL_ID('svcacct_api_runtime') AND OBJECT_NAME(major_id) LIKE 'UsageScan%' AND permission_name IN ('SELECT','INSERT') AND state='G')<>10 THROW 51401,'Missing API grants.',1; IF COL_LENGTH('svcacct.WorkRequests','RequestedGmsaName') IS NOT NULL THROW 51401,'031 ran before grants.',1;" | Out-Null
        }
    }
    foreach ($relative in $SqlPlan.ExecutionFiles | Where-Object { $_ -match '/migrations/' }) {
        $out=& $sqlcmd -S $server -d $database -E -I -b -i (Join-Path $export $relative) 2>&1
        if ($LASTEXITCODE -eq 0 -or ($out -join ' ') -notmatch 'already (applied|satisfied)') { throw "Expected replay refusal: $relative" }
        $log.Add("Replay refused: $relative")
    }
} finally { Pop-Location }
Invoke-LocalSql -Query @'
IF (SELECT COUNT(*) FROM sys.columns WHERE name='RequestedGmsaName' AND OBJECT_SCHEMA_NAME(object_id)='svcacct' AND system_type_id=231 AND max_length=512 AND is_nullable=1 AND default_object_id=0)<>2 THROW 51401,'031 contract.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('security.AccessRequests') AND name='IX_AccessRequests_UserRequested' AND is_unique=0 AND is_disabled=0 AND has_filter=0) THROW 51401,'032 contract.',1;
IF EXISTS(SELECT 1 FROM sys.database_role_members WHERE role_principal_id=DATABASE_PRINCIPAL_ID('svcacct_api_runtime')) THROW 51401,'Unexpected role membership.',1;
'@ | Out-Null
$final=Invoke-LocalSql -File $inventory
$final | Set-Content (Join-Path $EvidenceDirectory "inventory-$Baseline-after.txt")
foreach ($migration in 25..32) {
    $total=@(25,2,3,6,3,10,2,1)[$migration-25]
    if (($final -join "`n") -notmatch "(?m)^0$migration\s+\S+\s+$total\s+$total\s+1\s+") { throw "Wrong final inventory: $migration" }
}
if ($before -cne ((Invoke-LocalSql -Query $retained) -join '|')) { throw 'Synthetic predecessor rows changed.' }
# Partial 031 must be marked unsafe in the read-only inventory; roll back the probe.
$partial="BEGIN TRANSACTION; ALTER TABLE svcacct.IdentityTransitions DROP COLUMN RequestedGmsaName;`n" +
    [IO.File]::ReadAllText($inventory) + "`nROLLBACK TRANSACTION;"
$probe=Invoke-LocalSql -Query $partial
if (($probe -join ' ') -notmatch 'DUR:') { throw 'Partial schema was not flagged.' }
$log.Add('Partial 031 inventory refused; synthetic probe rolled back')
$log.Add('Synthetic team/access request/history SHA-256 retained; no role members assigned')
$log | Set-Content (Join-Path $EvidenceDirectory "sql-sequence-$Baseline.txt")
Write-Host "PASS: fresh $database retained; no corporate calls"
$global:LASTEXITCODE=0
