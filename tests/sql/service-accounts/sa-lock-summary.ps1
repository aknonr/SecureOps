[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$rounds = @(Get-ChildItem -LiteralPath $evidence -Directory -Filter 'round-*' |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'integration.trx') } | Sort-Object Name)
if ($rounds.Count -eq 0) { throw 'No completed TRX rounds found.' }

function Find-Test($events, [int]$SessionId, [DateTimeOffset]$At) {
    $items = $events[$SessionId]
    if (-not $items) { return $null }
    $low = 0
    $high = $items.Count - 1
    $found = -1
    while ($low -le $high) {
        $mid = [int][Math]::Floor(($low + $high) / 2)
        if ($items[$mid].time -le $At) { $found = $mid; $low = $mid + 1 } else { $high = $mid - 1 }
    }
    if ($found -ge 0) { return $items[$found].test }
    return $null
}

$results = @(foreach ($round in $rounds) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $round.FullName 'integration.trx')
    $tests = @($trx.TestRun.Results.UnitTestResult)
    $counters = $trx.TestRun.ResultSummary.Counters
    $events = @{}
    $trace = Join-Path $round.FullName 'test-sql.jsonl'
    if (Test-Path -LiteralPath $trace) {
        Get-Content -LiteralPath $trace | ForEach-Object { $_ | ConvertFrom-Json } |
            Where-Object { $_.kind -eq 'Microsoft.Data.SqlClient.WriteCommandBefore' } |
            Group-Object sessionId | ForEach-Object {
                $events[[int]$_.Name] = @($_.Group | ForEach-Object {
                    [pscustomobject]@{ time=[DateTimeOffset]::Parse($_.utc); test=$_.test }
                } | Sort-Object time)
            }
    }
    $waits = @(Get-Content -LiteralPath (Join-Path $round.FullName 'blocking.jsonl') |
        ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.kind -eq 'request' -and $_.blockerId -gt 0 -and $_.waitMs -ge 1000 } |
        ForEach-Object {
            $utc = [DateTimeOffset]$_.observedUtc
            if ($_.timestampFormat -ne 'utc-iso8601') {
                # The initial replay serialized SQL's unspecified DateTime as local time; restore its UTC meaning.
                $utc = $utc.Add([TimeZoneInfo]::Local.GetUtcOffset($utc))
            }
            [pscustomobject]@{
                utc=$utc.ToString('O'); sessionId=$_.sessionId; blockerId=$_.blockerId
                waiterTest=(Find-Test $events $_.sessionId $utc)
                blockerTest=(Find-Test $events $_.blockerId $utc)
                waitType=$_.waitType; waitMs=$_.waitMs; resource=$_.waitResource
            }
        })
    $worst = @($waits | Group-Object waiterTest,blockerTest,waitType,resource |
        ForEach-Object { $_.Group | Sort-Object waitMs -Descending | Select-Object -First 1 })
    $worst | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $round.FullName 'correlated-waits.json') -Encoding UTF8
    [pscustomobject]@{
        round=$round.Name; total=[int]$counters.total; passed=[int]$counters.passed
        failed=[int]$counters.failed; skipped=([int]$counters.total - [int]$counters.executed)
        failures=@($tests | Where-Object outcome -eq 'Failed' | Select-Object testName,duration)
        slowest=@($tests | Where-Object outcome -eq 'Passed' |
            Sort-Object { [TimeSpan]::Parse($_.duration) } -Descending |
            Select-Object -First 5 testName,duration)
        observedLongWaits=$worst
    }
})
$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'analysis.json') -Encoding UTF8
$results | Select-Object round,total,passed,failed,skipped | Format-Table -AutoSize
