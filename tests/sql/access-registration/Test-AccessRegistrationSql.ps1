[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('(?-i)^[A-Za-z0-9_]{1,40}$')]
    [string]$DatabaseSuffix,
    [ValidateRange(31, 32)]
    [int]$ThroughMigration = 32,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
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
$snapshotSql = @'
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), (
    SELECT
      (SELECT * FROM security.Users ORDER BY UserId FOR JSON PATH) AS Users,
      (SELECT * FROM security.AccessRequests ORDER BY AccessRequestId FOR JSON PATH) AS Requests,
      (SELECT * FROM security.AccessRequestHistory ORDER BY AccessRequestHistoryId FOR JSON PATH) AS History,
      (SELECT * FROM sys.database_permissions ORDER BY class, major_id, minor_id, grantee_principal_id, grantor_principal_id, type FOR JSON PATH) AS Permissions
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER))), 2);
'@
function Get-RegistrationSnapshot {
    $snapshotConnection = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True"
    $snapshotConnection.Open()
    try {
        $snapshotCommand = $snapshotConnection.CreateCommand()
        $snapshotCommand.CommandText = $snapshotSql
        return $snapshotCommand.ExecuteScalar()
    } finally { $snapshotConnection.Dispose() }
}
Push-Location (Join-Path $root 'sql\migrations')
try {
    Get-ChildItem -File -Filter '*.sql' | Where-Object { [int]$_.Name.Substring(0, 3) -le $ThroughMigration } | Sort-Object Name | ForEach-Object {
        if ($_.Name.StartsWith('032-')) {
            & sqlcmd -S $server -d $database -E -I -b -i (Join-Path $PSScriptRoot 'predecessor-fixture.sql') *> (Join-Path $evidence 'predecessor.log')
            if ($LASTEXITCODE -ne 0) { throw 'Cannot seed synthetic predecessor rows.' }
            $before32 = Get-RegistrationSnapshot
        }
        & sqlcmd -S $server -d $database -E -I -b -i $_.Name *> (Join-Path $evidence "$($_.BaseName).log")
        if ($LASTEXITCODE -ne 0) { throw "Migration $($_.Name) failed; retain database and evidence." }
        if ($_.Name.StartsWith('032-')) {
            if ($before32 -ne (Get-RegistrationSnapshot)) { throw '032 changed existing access rows or database permissions.' }
            & sqlcmd -S $server -d $database -E -I -b -i $_.Name *> (Join-Path $evidence '032-replay.log')
            if ($LASTEXITCODE -eq 0 -or -not (Select-String -Path (Join-Path $evidence '032-replay.log') -SimpleMatch '51381' -Quiet)) { throw '032 replay did not fail with its explicit refusal.' }
            Write-Host '032 preserved access rows/permissions (SHA-256); replay refused (51381).'
        }
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
    & $dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c $Configuration --filter FullyQualifiedName~AccessRegistrationSqlTests --logger "trx;LogFileName=registration.trx" --results-directory $evidence *> (Join-Path $evidence 'test.log')
    $testExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $planConnection = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True"
    $planConnection.Open()
    try {
        $planCommand = $planConnection.CreateCommand()
        $planCommand.CommandText = @'
SELECT TOP (1) CONVERT(nvarchar(max), q.query_plan)
FROM sys.dm_exec_cached_plans p
CROSS APPLY sys.dm_exec_sql_text(p.plan_handle) t
CROSS APPLY sys.dm_exec_query_plan(p.plan_handle) q
CROSS APPLY sys.dm_exec_plan_attributes(p.plan_handle) a
WHERE a.attribute = 'dbid' AND CONVERT(int, a.value) = DB_ID()
  AND t.text LIKE N'%SELECT TOP (1) AccessRequestId FROM security.AccessRequests WITH (UPDLOCK, HOLDLOCK)%'
  AND t.text NOT LIKE N'%sys.dm_exec_cached_plans%'
ORDER BY p.usecounts DESC;
'@
        $plan = $planCommand.ExecuteScalar()
        if ($plan -isnot [string]) { throw 'Latest-request query plan was not captured.' }
        [IO.File]::WriteAllText((Join-Path $evidence 'latest-request.sqlplan'), $plan)
    } finally { $planConnection.Dispose() }
    Get-Content (Join-Path $evidence 'test.log') -Tail 16
    Write-Host "Evidence: $evidence"
} finally {
    Pop-Location
    Remove-Item Env:\SECUREOPS_ACCESS_REGISTRATION_CONNECTION,Env:\SECUREOPS_ACCESS_REGISTRATION_EVIDENCE -ErrorAction SilentlyContinue
    $cleanupConnection = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True"
    $cleanupConnection.Open()
    try {
        $cleanupCommand = $cleanupConnection.CreateCommand()
        $cleanupCommand.CommandText = 'DROP EVENT SESSION SecureOpsAccessG34 ON SERVER;'
        $null = $cleanupCommand.ExecuteNonQuery()
    } finally { $cleanupConnection.Dispose() }
}
exit $testExit
