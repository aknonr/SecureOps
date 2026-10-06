[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,40}$')]
    [string]$DatabaseSuffix,
    [ValidateRange(31, 32)]
    [int]$ThroughMigration = 31
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$server = '(localdb)\SecureOpsAccessG34'
$database = 'SecureOps_AccessG34_' + $DatabaseSuffix
$evidence = Join-Path $root "artifacts\access-registration\$DatabaseSuffix"
if (Test-Path -LiteralPath $evidence) { throw 'Refuse an existing evidence directory.' }
$null = New-Item -ItemType Directory -Path $evidence
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$instances = @(& SqlLocalDB info)
if ('SecureOpsAccessG34' -notin $instances) {
    & SqlLocalDB create SecureOpsAccessG34 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated LocalDB instance.' }
}
& SqlLocalDB start SecureOpsAccessG34 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot start isolated LocalDB instance.' }
& sqlcmd -S $server -d master -E -I -b -Q "IF DB_ID(N'$database') IS NOT NULL THROW 51000, 'Refuse existing database.', 1; CREATE DATABASE [$database];"
if ($LASTEXITCODE -ne 0) { throw 'Cannot create fresh synthetic database.' }
Push-Location (Join-Path $root 'sql\migrations')
try {
    Get-ChildItem -File -Filter '*.sql' | Where-Object { [int]$_.Name.Substring(0, 3) -le $ThroughMigration } | Sort-Object Name | ForEach-Object {
        & sqlcmd -S $server -d $database -E -I -b -i $_.Name *> (Join-Path $evidence "$($_.BaseName).log")
        if ($LASTEXITCODE -ne 0) { throw "Migration $($_.Name) failed; retain database and evidence." }
    }
} finally { Pop-Location }

# This instance is harness-owned; its databases contain synthetic data only.
$connection = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True"
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandText = @"
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = 'SecureOpsAccessG34')
    DROP EVENT SESSION SecureOpsAccessG34 ON SERVER;
CREATE EVENT SESSION SecureOpsAccessG34 ON SERVER
ADD EVENT sqlserver.xml_deadlock_report
ADD TARGET package0.ring_buffer (SET max_memory = 4096)
WITH (MAX_DISPATCH_LATENCY = 1 SECONDS);
ALTER EVENT SESSION SecureOpsAccessG34 ON SERVER STATE = START;
"@
    $null = $command.ExecuteNonQuery()
} finally { $connection.Dispose() }
$env:SECUREOPS_ACCESS_REGISTRATION_CONNECTION = "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15"
$env:SECUREOPS_ACCESS_REGISTRATION_EVIDENCE = $evidence
Push-Location $root
try {
    $ErrorActionPreference = 'Continue'
    & $dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj --filter FullyQualifiedName~AccessRegistrationSqlTests --logger "trx;LogFileName=registration.trx" --results-directory $evidence *> (Join-Path $evidence 'test.log')
    $testExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Get-Content (Join-Path $evidence 'test.log') -Tail 16
    Write-Host "Evidence: $evidence"
} finally {
    Pop-Location
    Remove-Item Env:\SECUREOPS_ACCESS_REGISTRATION_CONNECTION,Env:\SECUREOPS_ACCESS_REGISTRATION_EVIDENCE -ErrorAction SilentlyContinue
}
exit $testExit
