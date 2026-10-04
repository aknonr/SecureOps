[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,40}$')]
    [string]$DatabaseSuffix
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$server = '(localdb)\SecureOpsResourcesV1'
$database = 'SecureOps_Sa' + $DatabaseSuffix
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$migration = Join-Path $root 'sql/schema/028-admin-service-account-operations.sql'
$checks = 0

# The existing harness refuses reuse and is hard-bound to isolated per-user LocalDB.
& (Join-Path $PSScriptRoot 'sa-sql-harness.ps1') -DatabaseSuffix $DatabaseSuffix -ThroughMigration 27 -SkipRoleScripts

function Invoke-CheckSql {
    param([string]$Query)
    & $sqlcmd -S $server -d $database -E -I -b -V 16 -Q $Query
    if ($LASTEXITCODE -ne 0) { throw 'Local Admin operations SQL assertion failed; database retained.' }
    $script:checks++
}

function Invoke-RejectedSql {
    param([string]$File, [string]$ExpectedMessage)
    $output = & $sqlcmd -S $server -d $database -E -I -b -V 16 -i $File 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notlike "*$ExpectedMessage*") {
        throw 'Expected local migration refusal was not observed; database retained.'
    }
    $script:checks++
}

Invoke-CheckSql -Query @'
DECLARE @admin uniqueidentifier=NEWID(), @ordinary uniqueidentifier=NEWID(), @revoked uniqueidentifier=NEWID();
INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus)
VALUES(@admin,N'synthetic-admin-operations-active','test','Approved'),
      (@ordinary,N'synthetic-admin-operations-ordinary','test','Approved'),
      (@revoked,N'synthetic-admin-operations-revoked','test','Approved');
INSERT INTO security.RoleAssignments(RoleAssignmentId,UserId,RoleId,GrantedByCorporateIdentity)
SELECT NEWID(),@admin,RoleId,N'synthetic-fixture' FROM security.Roles WHERE RoleCode=N'Admin';
INSERT INTO security.RoleAssignments(RoleAssignmentId,UserId,RoleId,GrantedByCorporateIdentity)
SELECT NEWID(),@ordinary,RoleId,N'synthetic-fixture' FROM security.Roles WHERE RoleCode=N'ReadOnly';
INSERT INTO security.RoleAssignments(RoleAssignmentId,UserId,RoleId,GrantedByCorporateIdentity,RevokedAt)
SELECT NEWID(),@revoked,RoleId,N'synthetic-fixture',SYSUTCDATETIME() FROM security.Roles WHERE RoleCode=N'Admin';
SELECT RoleId,RoleCode,Version,CapabilitiesJson INTO dbo.SyntheticRoleBaseline FROM security.Roles;
SELECT UserId,CorporateIdentity,AccessVersion,AccessStatus INTO dbo.SyntheticUserBaseline FROM security.Users;
'@

Invoke-CheckSql -Query @'
EXEC(N'CREATE TRIGGER audit.TR_SyntheticAdminOperationsFailure ON audit.AuditLog AFTER INSERT AS
IF EXISTS(SELECT 1 FROM inserted WHERE CorrelationId=N''migration:028:admin-service-account-operations'')
    THROW 51441,''Synthetic Admin operations audit failure'',1;');
'@
Invoke-RejectedSql -File $migration -ExpectedMessage 'Synthetic Admin operations audit failure'
Invoke-CheckSql -Query @'
IF EXISTS(SELECT 1 FROM security.Roles r JOIN dbo.SyntheticRoleBaseline b ON b.RoleId=r.RoleId
    WHERE r.Version<>b.Version OR CONVERT(varbinary(max),r.CapabilitiesJson)<>CONVERT(varbinary(max),b.CapabilitiesJson))
    THROW 51442,'Audit failure changed role state.',1;
IF EXISTS(SELECT 1 FROM security.Users u JOIN dbo.SyntheticUserBaseline b ON b.UserId=u.UserId WHERE u.AccessVersion<>b.AccessVersion)
    THROW 51442,'Audit failure changed access versions.',1;
IF EXISTS(SELECT 1 FROM audit.AuditLog WHERE CorrelationId=N'migration:028:admin-service-account-operations')
    THROW 51442,'Audit failure left a migration event.',1;
DROP TRIGGER audit.TR_SyntheticAdminOperationsFailure;
'@

& $sqlcmd -S $server -d $database -E -I -b -V 16 -i $migration
if ($LASTEXITCODE -ne 0) { throw 'Local 028 upgrade failed; database retained.' }
$checks++
Invoke-CheckSql -Query @'
IF (SELECT COUNT(*) FROM security.Roles r CROSS APPLY OPENJSON(r.CapabilitiesJson) c
    WHERE r.RoleCode=N'Admin' AND c.value LIKE N'ServiceAccounts.%')<>7
    THROW 51442,'Admin does not have exactly seven reviewed module capabilities.',1;
IF EXISTS(SELECT 1 FROM security.Roles r JOIN dbo.SyntheticRoleBaseline b ON b.RoleId=r.RoleId
    WHERE (r.RoleCode=N'Admin' AND r.Version<>b.Version+1)
       OR (r.RoleCode<>N'Admin' AND (r.Version<>b.Version OR CONVERT(varbinary(max),r.CapabilitiesJson)<>CONVERT(varbinary(max),b.CapabilitiesJson))))
    THROW 51442,'Role versions or unrelated bundles changed incorrectly.',1;
IF EXISTS(SELECT c.value FROM dbo.SyntheticRoleBaseline b CROSS APPLY OPENJSON(b.CapabilitiesJson) c WHERE b.RoleCode=N'Admin'
    EXCEPT SELECT c.value FROM security.Roles r CROSS APPLY OPENJSON(r.CapabilitiesJson) c WHERE r.RoleCode=N'Admin')
    THROW 51442,'Prior Admin capability was lost.',1;
IF EXISTS(SELECT 1 FROM security.Users u JOIN dbo.SyntheticUserBaseline b ON b.UserId=u.UserId
    WHERE u.AccessVersion<>b.AccessVersion+CASE WHEN b.CorporateIdentity=N'synthetic-admin-operations-active' THEN 1 ELSE 0 END
       OR u.AccessStatus<>b.AccessStatus)
    THROW 51442,'Access versions/status changed incorrectly.',1;
IF (SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId=N'migration:028:admin-service-account-operations'
    AND JSON_VALUE(DetailsJson,N'$.outcome')=N'Applied' AND JSON_VALUE(DetailsJson,N'$.affectedUsers')=N'1')<>1
    THROW 51442,'Required migration audit outcome missing.',1;
IF EXISTS(SELECT 1 FROM svcacct.ScopeGrants) OR DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NOT NULL
    THROW 51442,'Migration added scope or SQL runtime authority.',1;
'@
Invoke-RejectedSql -File $migration -ExpectedMessage '028 already satisfied'
Invoke-RejectedSql -File (Join-Path $root 'sql/schema/027-admin-service-account-navigation.sql') -ExpectedMessage '027 already satisfied'
Invoke-CheckSql -Query @'
IF EXISTS(SELECT 1 FROM security.Roles r JOIN dbo.SyntheticRoleBaseline b ON b.RoleId=r.RoleId
    WHERE r.RoleCode=N'Admin' AND r.Version<>b.Version+1)
    OR (SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId=N'migration:028:admin-service-account-operations')<>1
    THROW 51442,'Rejected replay changed migration state.',1;
'@
Write-Host "PASS: $checks isolated Admin operations SQL checks; database $database retained. No target changes."
