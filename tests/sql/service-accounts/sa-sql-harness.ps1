[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,40}$')]
    [string]$DatabaseSuffix,
    [ValidateRange(26, 29)]
    [int]$ThroughMigration = 29,
    [switch]$SkipRoleScripts
)

# Windows counterpart of sa-sql-harness.sh (NOT executed in the Linux container that produced it).
# Creates a NEW database SecureOps_Sa<suffix> on the isolated per-user LocalDB instance, applies the reviewed
# numbered migrations in order (now 001-029; 029 numbers SA-003), verifies module replay refusal, and
# (unless -SkipRoleScripts) the two unnumbered role scripts. No role member is assigned. Never targets a
# shared or corporate server and never reuses an existing database.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$server = '(localdb)\SecureOpsResourcesV1'
$database = 'SecureOps_Sa' + $DatabaseSuffix
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$null = Get-Command SqlLocalDB -ErrorAction Stop
& SqlLocalDB info SecureOpsResourcesV1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Provision the isolated SecureOpsResourcesV1 LocalDB instance first.' }

function Invoke-SaSql {
    [CmdletBinding()]
    param([string]$Database, [string]$Query, [string]$File)
    if ($File) { & $sqlcmd -S $server -d $Database -E -I -b -i $File } else { & $sqlcmd -S $server -d $Database -E -I -b -Q $Query }
    if ($LASTEXITCODE -ne 0) { throw "Service Accounts harness step failed; database $database retained for inspection." }
}

Invoke-SaSql -Database master -Query "IF DB_ID(N'$database') IS NOT NULL THROW 51000, 'Refuse existing database.', 1; CREATE DATABASE [$database];"
Push-Location (Join-Path $root 'sql\migrations')
try {
    Get-ChildItem -File -Filter '*.sql' | Where-Object { [int]$_.Name.Substring(0, 3) -le $ThroughMigration } | Sort-Object Name | ForEach-Object {
        Invoke-SaSql -Database $database -File $_.Name
        Write-Host "applied $($_.Name)"
    }
} finally { Pop-Location }

Push-Location (Join-Path $root 'sql\pending\service-accounts')
try {
    if (-not (Test-Path (Join-Path $root 'sql/migrations/025-service-accounts.sql'))) {
        Invoke-SaSql -Database $database -File 'SA-001-service-accounts.sql'
        Write-Host 'applied SA-001-service-accounts.sql (candidate)'
    } else { Write-Host 'Service Accounts installed through numbered 025' }
    & $sqlcmd -S $server -d $database -E -I -b -i 'SA-001-service-accounts.sql' | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'Candidate replay was not refused.' }
    Write-Host 'replay refused as expected'
    if (-not (Test-Path (Join-Path $root 'sql/migrations/026-service-account-usage-rules.sql'))) {
        Invoke-SaSql -Database $database -File 'SA-002-usage-rules.sql'
        Write-Host 'applied SA-002-usage-rules.sql (candidate 2)'
    } else { Write-Host 'Service account usage rules installed through numbered 026' }
    & $sqlcmd -S $server -d $database -E -I -b -i 'SA-002-usage-rules.sql' | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'Candidate 2 replay was not refused.' }
    Write-Host 'candidate 2 replay refused as expected'
    $hasBootstrap = (& $sqlcmd -S $server -d $database -E -I -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN COL_LENGTH(N'svcacct.ScopeGrants', N'IsBootstrap') IS NULL THEN 0 ELSE 1 END" | Select-Object -First 1).Trim()
    if ($hasBootstrap -eq '0') {
        Invoke-SaSql -Database $database -File 'SA-003-scope-bootstrap.sql'
        Write-Host 'applied SA-003-scope-bootstrap.sql (candidate 3)'
    } else { Write-Host 'Scope bootstrap installed through a numbered migration' }
    & $sqlcmd -S $server -d $database -E -I -b -i 'SA-003-scope-bootstrap.sql' | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'Candidate 3 replay was not refused.' }
    Write-Host 'candidate 3 replay refused as expected'
    if (-not $SkipRoleScripts) {
        Invoke-SaSql -Database $database -File 'SA-API-permissions.sql'
        Invoke-SaSql -Database $database -File 'SA-Worker-permissions.sql'
        Invoke-SaSql -Database $database -File 'SA-002-API-permissions.sql'
        Write-Host 'applied SA-API-permissions.sql, SA-Worker-permissions.sql and SA-002-API-permissions.sql (no role member assigned)'
    }
} finally { Pop-Location }

Write-Host "SECUREOPS_SA_SQL_TEST_CONNECTION=Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15"
