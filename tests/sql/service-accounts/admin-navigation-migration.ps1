[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,40}$')]
    [string]$DatabaseSuffix
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
& (Join-Path $PSScriptRoot 'sa-sql-harness.ps1') -DatabaseSuffix $DatabaseSuffix -ThroughMigration 26 -SkipRoleScripts
$database = 'SecureOps_Sa' + $DatabaseSuffix
$connection = [System.Data.SqlClient.SqlConnection]::new("Server=(localdb)\SecureOpsResourcesV1;Database=$database;Integrated Security=True;TrustServerCertificate=True")
$connection.Open()
$checks = 0
function Scalar([string]$sql) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    try { return $command.ExecuteScalar() } finally { $command.Dispose() }
}
function Execute([string]$sql) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}
function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
try {
    Execute @'
INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus)
VALUES('00000000-0000-0000-0000-000000000271','synthetic:admin-027','test','Approved'),
      ('00000000-0000-0000-0000-000000000272','synthetic:ordinary-027','test','Approved');
INSERT INTO security.RoleAssignments(UserId,RoleId,GrantedByCorporateIdentity)
SELECT '00000000-0000-0000-0000-000000000271',RoleId,'synthetic-fixture' FROM security.Roles WHERE RoleCode='Admin';
INSERT INTO security.RoleAssignments(UserId,RoleId,GrantedByCorporateIdentity)
SELECT '00000000-0000-0000-0000-000000000272',RoleId,'synthetic-fixture' FROM security.Roles WHERE RoleCode='ReadOnly';
'@
    $previous = [string](Scalar "SELECT CapabilitiesJson FROM security.Roles WHERE RoleCode='Admin'")
    $version = [long](Scalar "SELECT Version FROM security.Roles WHERE RoleCode='Admin'")
    $accessVersion = [long](Scalar "SELECT AccessVersion FROM security.Users WHERE CorporateIdentity='synthetic:admin-027'")
    $ordinaryVersion = [long](Scalar "SELECT AccessVersion FROM security.Users WHERE CorporateIdentity='synthetic:ordinary-027'")
    $migration = Get-Content -LiteralPath (Join-Path $root 'sql/schema/027-admin-service-account-navigation.sql') -Raw
    Execute "CREATE TRIGGER audit.TR_Synthetic027AuditFailure ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE CorrelationId='migration:027:admin-service-account-navigation') THROW 51331,'Synthetic audit failure',1; END;"
    $rejected = $false
    try { Execute $migration } catch { $rejected = $_.Exception.ToString().Contains('Synthetic audit failure') }
    Assert $rejected 'Required audit failure did not reject the migration.'
    Assert ((Scalar "SELECT CapabilitiesJson FROM security.Roles WHERE RoleCode='Admin'") -eq $previous) 'Bundle changed after failed audit.'
    Assert ((Scalar "SELECT Version FROM security.Roles WHERE RoleCode='Admin'") -eq $version) 'Role version changed after failed audit.'
    Assert ((Scalar "SELECT AccessVersion FROM security.Users WHERE CorporateIdentity='synthetic:admin-027'") -eq $accessVersion) 'Access version changed after failed audit.'
    Assert ((Scalar "SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId='migration:027:admin-service-account-navigation'") -eq 0) 'Failed migration left audit.'
    Execute 'DROP TRIGGER audit.TR_Synthetic027AuditFailure;'
    Execute $migration
    $next = ConvertFrom-Json -InputObject ([string](Scalar "SELECT CapabilitiesJson FROM security.Roles WHERE RoleCode='Admin'"))
    $old = ConvertFrom-Json -InputObject $previous
    Assert (@($old | Where-Object { $_ -notin $next }).Count -eq 0) 'Existing capabilities were removed.'
    Assert ($next.Count -eq $old.Count + 2) 'Unexpected capabilities were added.'
    Assert (($next -contains 'ServiceAccounts.View') -and ($next -contains 'ServiceAccounts.Administer')) 'Navigation capabilities missing.'
    Assert (@($next | Where-Object { $_ -like 'ServiceAccounts.*' }).Count -eq 2) 'Operational module action granted.'
    Assert ((Scalar "SELECT Version FROM security.Roles WHERE RoleCode='Admin'") -eq $version+1) 'Role version not advanced.'
    Assert ((Scalar "SELECT AccessVersion FROM security.Users WHERE CorporateIdentity='synthetic:admin-027'") -eq $accessVersion+1) 'Admin access version not advanced.'
    Assert ((Scalar "SELECT AccessVersion FROM security.Users WHERE CorporateIdentity='synthetic:ordinary-027'") -eq $ordinaryVersion) 'Ordinary user changed.'
    Assert ((Scalar "SELECT COUNT(*) FROM svcacct.ScopeGrants") -eq 0) 'Migration granted module scope.'
    Assert ((Scalar "SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId='migration:027:admin-service-account-navigation'") -eq 1) 'Required audit missing.'
    $rejected = $false
    try { Execute $migration } catch { $rejected = $_.Exception.ToString().Contains('do not replay') }
    Assert $rejected 'Migration replay was accepted.'
    Assert ((Scalar "SELECT Version FROM security.Roles WHERE RoleCode='Admin'") -eq $version+1) 'Replay changed role version.'
    Assert ((Scalar "SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId='migration:027:admin-service-account-navigation'") -eq 1) 'Replay added audit.'
    Write-Output "PASS: $checks migration assertions; database $database retained. No target SQL."
} finally { $connection.Dispose() }
