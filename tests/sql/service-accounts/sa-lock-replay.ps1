[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [ValidateRange(1, 100)]
    [int]$Rounds = 10,
    [ValidatePattern('^[A-Za-z0-9_]{1,24}$')]
    [string]$Prefix = ('Lock' + (Get-Date -Format 'MMddHHmmss'))
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a new evidence directory.' }
$null = New-Item -ItemType Directory -Path $evidence
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$server = '(localdb)\SecureOpsResourcesV1'
$otherOptIns = @('SECUREOPS_SQL_TEST_CONNECTION', 'SECUREOPS_MAIL_SQL_CONNECTION',
    'SECUREOPS_ACCESS_REGISTRATION_CONNECTION', 'SECUREOPS_ACCESS_GUARD_CONNECTION',
    'SECUREOPS_PREPARATION_SQL_CONNECTION', 'SECUREOPS_RC621_DEFECT_CONNECTION',
    'SECUREOPS_SA_SQL_RUNTIME_CONNECTION', 'SECUREOPS_SA_SQL_TEST_CONNECTION_030',
    'SECUREOPS_SOURCE_HOST_ACCEPTANCE', 'SECUREOPS_PREPARATION_SQL',
    'SECUREOPS_ANNOUNCEMENT_BROWSER_EVIDENCE', 'SECUREOPS_UPDATE_OPENAPI')
foreach ($name in $otherOptIns) {
    if ([Environment]::GetEnvironmentVariable($name)) { throw "Unexpected opt-in enabled: $name" }
}
[ordered]@{
    sourceHead = (& git -C $root rev-parse HEAD)
    integrationAssemblySha256 = (Get-FileHash (Join-Path $root 'tests\SecureOps.Tests.Integration\bin\Debug\net8.0\SecureOps.Tests.Integration.dll')).Hash
    sdk = (& $dotnet --version)
    rounds = $Rounds
    prefix = $Prefix
    server = $server
    pollMilliseconds = 250
    command = 'dotnet test tests\SecureOps.Tests.Integration --no-build --no-restore --logger trx'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'manifest.json') -Encoding UTF8

# The monitor reads only task-owned databases. It never changes instance settings.
$monitor = {
    param($connectionString, $directory, $stopFile)
    $ErrorActionPreference = 'Stop'
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 5
    $command.CommandText = @'
SELECT SYSUTCDATETIME() AS observedUtc, r.session_id AS sessionId,
       r.blocking_session_id AS blockerId, r.status, r.wait_type AS waitType,
       r.wait_time AS waitMs, r.wait_resource AS waitResource,
       r.open_transaction_count AS openTransactions,
       s.program_name AS program, s.transaction_isolation_level AS isolation,
       SUBSTRING(t.text, r.statement_start_offset / 2 + 1,
         (CASE WHEN r.statement_end_offset = -1 THEN DATALENGTH(t.text)
          ELSE r.statement_end_offset END - r.statement_start_offset) / 2 + 1) AS statement,
       bs.program_name AS blockerProgram, bs.status AS blockerStatus,
       bs.open_transaction_count AS blockerTransactions,
       bs.last_request_start_time AS blockerLastStart,
       bs.last_request_end_time AS blockerLastEnd,
       CASE WHEN bs.database_id = DB_ID() THEN bt.text END AS blockerBatch
FROM sys.dm_exec_requests r
JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
LEFT JOIN sys.dm_exec_sessions bs ON bs.session_id = r.blocking_session_id
LEFT JOIN sys.dm_exec_connections bc ON bc.session_id = bs.session_id
OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
OUTER APPLY sys.dm_exec_sql_text(bc.most_recent_sql_handle) bt
WHERE r.database_id = DB_ID() AND r.session_id <> @@SPID;

SELECT SYSUTCDATETIME() AS observedUtc, l.request_session_id AS sessionId,
       l.resource_type AS resourceType, l.resource_description AS resource,
       l.resource_associated_entity_id AS entityId, l.request_mode AS mode,
       l.request_status AS status, l.request_owner_type AS ownerType,
       OBJECT_SCHEMA_NAME(COALESCE(p.object_id,
         CASE WHEN l.resource_type = 'OBJECT' THEN l.resource_associated_entity_id END)) AS schemaName,
       OBJECT_NAME(COALESCE(p.object_id,
         CASE WHEN l.resource_type = 'OBJECT' THEN l.resource_associated_entity_id END)) AS objectName,
       i.name AS indexName
FROM sys.dm_tran_locks l
LEFT JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id
LEFT JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
WHERE l.resource_database_id = DB_ID() AND l.request_session_id <> @@SPID
  AND (l.request_status <> 'GRANT' OR l.request_session_id IN
      (SELECT session_id FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id > 0
       UNION SELECT blocking_session_id FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id > 0));
'@
    $writer = New-Object IO.StreamWriter (Join-Path $directory 'blocking.jsonl'), $false
    $writer.AutoFlush = $true
    try {
        $null = New-Item -ItemType File -Path (Join-Path $directory 'monitor.ready')
        while (-not (Test-Path -LiteralPath $stopFile)) {
            $reader = $command.ExecuteReader()
            $set = 0
            do {
                while ($reader.Read()) {
                    $row = [ordered]@{ kind = @('request', 'lock')[$set]; timestampFormat = 'utc-iso8601' }
                    for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                        $value = if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) }
                        if ($value -is [DateTime]) {
                            $kind = if ($reader.GetName($i) -eq 'observedUtc') { [DateTimeKind]::Utc } else { [DateTimeKind]::Local }
                            $value = [DateTime]::SpecifyKind($value, $kind).ToUniversalTime().ToString('O')
                        }
                        $row[$reader.GetName($i)] = $value
                    }
                    $writer.WriteLine(($row | ConvertTo-Json -Compress))
                }
                $set++
            } while ($reader.NextResult())
            $reader.Close()
            Start-Sleep -Milliseconds 250
        }
    } finally { $writer.Dispose(); $connection.Dispose() }
}

function Invoke-Query([string]$ConnectionString, [string]$Sql) {
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 30
        $command.ExecuteScalar()
    } finally { $connection.Dispose() }
}

$anyFailure = $false
for ($round = 1; $round -le $Rounds; $round++) {
    $suffix = $Prefix + ('R{0:D2}' -f $round)
    $database = 'SecureOps_Sa' + $suffix
    $directory = Join-Path $evidence ('round-{0:D2}' -f $round)
    $null = New-Item -ItemType Directory -Path $directory
    & powershell -NoProfile -File (Join-Path $PSScriptRoot 'sa-sql-harness.ps1') -DatabaseSuffix $suffix *> (Join-Path $directory 'schema.log')
    if ($LASTEXITCODE -ne 0) { throw "Schema setup failed: $database" }
    $connectionString = "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15"
    $session = 'SaLock_' + $suffix
    $null = Invoke-Query $connectionString "CREATE EVENT SESSION [$session] ON SERVER ADD EVENT sqlserver.xml_deadlock_report ADD TARGET package0.ring_buffer(SET max_memory=4096) WITH (MAX_DISPATCH_LATENCY=1 SECONDS); ALTER EVENT SESSION [$session] ON SERVER STATE=START;"
    $stopFile = Join-Path $directory 'monitor.stop'
    $job = $null
    try {
        $job = Start-Job -ScriptBlock $monitor -ArgumentList $connectionString, $directory, $stopFile
        $readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        while (-not (Test-Path -LiteralPath (Join-Path $directory 'monitor.ready'))) {
            if ($job.State -eq 'Failed' -or [DateTimeOffset]::UtcNow -gt $readyDeadline) {
                throw 'SQL monitor did not become ready.'
            }
            Start-Sleep -Milliseconds 100
        }
        $env:SECUREOPS_SA_SQL_TEST_CONNECTION = $connectionString
        $env:SECUREOPS_SA_SQL_DIAGNOSTICS = Join-Path $directory 'sql-errors.log'
        $env:SECUREOPS_SA_SQL_TRACE = Join-Path $directory 'test-sql.jsonl'
        $started = [DateTimeOffset]::UtcNow
        $arguments = @('test', 'tests\SecureOps.Tests.Integration', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=integration.trx', '--results-directory', ('"' + $directory + '"'))
        $process = Start-Process -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $directory 'test.log') -RedirectStandardError (Join-Path $directory 'test-error.log')
        $exitCode = $process.ExitCode
        $xml = Invoke-Query $connectionString "SELECT CAST(t.target_data AS nvarchar(max)) FROM sys.dm_xe_session_targets t JOIN sys.dm_xe_sessions s ON s.address=t.event_session_address WHERE s.name=N'$session' AND t.target_name='ring_buffer';"
        if ($xml) {
            [xml]$events = $xml
            $dbId = Invoke-Query $connectionString 'SELECT DB_ID();'
            $own = @($events.SelectNodes('//event') | Where-Object { $_.SelectNodes('.//process[@currentdb="' + $dbId + '"]').Count -gt 0 })
            ('<deadlocks>' + ($own.OuterXml -join '') + '</deadlocks>') | Set-Content -LiteralPath (Join-Path $directory 'deadlocks.xml') -Encoding UTF8
            [ordered]@{ databaseId=$dbId; matchingEvents=$own.Count; truncated=$events.RingBufferTarget.truncated; droppedCount=$events.RingBufferTarget.droppedCount } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'deadlock-health.json') -Encoding UTF8
        } else {
            throw 'Deadlock event target unavailable.'
        }
    } finally {
        $monitorState = 'NotStarted'
        if ($job) {
            $null = New-Item -ItemType File -Path $stopFile
            $null = Wait-Job $job -Timeout 10
            Receive-Job $job -ErrorAction Continue *> (Join-Path $directory 'monitor.log')
            $monitorState = $job.State
            if ($monitorState -eq 'Running') { Stop-Job $job }
            Remove-Job $job
        }
        $null = Invoke-Query $connectionString "ALTER EVENT SESSION [$session] ON SERVER STATE=STOP; DROP EVENT SESSION [$session] ON SERVER;"
        if ($monitorState -ne 'Completed') { throw "SQL monitor failed: $monitorState" }
    }
    if (-not (Test-Path -LiteralPath $env:SECUREOPS_SA_SQL_TRACE) -or
        -not (Select-String -LiteralPath $env:SECUREOPS_SA_SQL_TRACE -SimpleMatch '"kind":"Microsoft.Data.SqlClient.WriteCommandBefore"' -Quiet)) {
        throw 'Test/SPID trace missing. Build the integration project before replaying.'
    }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'integration.trx')
    $counters = $trx.TestRun.ResultSummary.Counters
    $summary = [ordered]@{ round=$round; database=$database; startedUtc=$started.ToString('O'); endedUtc=[DateTimeOffset]::UtcNow.ToString('O'); exitCode=$exitCode; total=$counters.total; passed=$counters.passed; failed=$counters.failed; skipped=([int]$counters.total - [int]$counters.executed) }
    $summary | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $evidence 'summary.jsonl')
    Write-Output ($summary | ConvertTo-Json -Compress)
    if ($exitCode -ne 0 -or [int]$counters.failed -gt 0) { $anyFailure = $true }
}
if ($anyFailure) { exit 1 }
exit 0
