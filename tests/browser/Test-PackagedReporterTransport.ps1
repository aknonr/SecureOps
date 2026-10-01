[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Fresh evidence filename required.' }
$headers = @{ 'X-SecureOps-Demo-Actor' = 'platform-admin' }
$api = 'http://localhost:63949/api/v1/in-use'
$stub = 'https://localhost:63948'
function Invoke-Refresh {
    [CmdletBinding()]
    param()
    Invoke-RestMethod "$api/refresh" -Method Post -Headers $headers -ContentType application/json -Body (@{ commandId = [guid]::NewGuid() } | ConvertTo-Json) | Out-Null
    (Invoke-RestMethod $api -Headers $headers).items | Where-Object { $_.source.code -eq 'OR-930000918' }
}
Invoke-RestMethod "$stub/mode/current" -Method Post | Out-Null
$first = Invoke-Refresh
$stats = Invoke-RestMethod "$stub/stats"
if ($first.source.servers.Count -ne 4 -or $stats.targets -ne 2 -or $stats.logins -ne 1) { throw 'Four links / two exact shared lookups / real session login failed.' }
if ($first.source.servers[0].relatedRequestReporter.display -eq $first.source.servers[2].relatedRequestReporter.display) { throw 'Different reporters collapsed.' }
if ($first.source.servers[3].relatedRequestReporter.state -ne 'MissingRfc') { throw 'Null RFC state missing.' }
Invoke-RestMethod "$stub/mode/partial" -Method Post | Out-Null
$partial = Invoke-Refresh
$previous = $first.source.servers[2].relatedRequestReporter
$retained = $partial.source.servers[2].relatedRequestReporter
if ($retained.state -ne 'Forbidden' -or $retained.display -ne $previous.display -or $retained.lastVerifiedAt -ne $previous.lastVerifiedAt) { throw 'Failed evidence was incorrectly renewed or discarded.' }
$reads = (Invoke-RestMethod "$stub/stats").queries
Invoke-RestMethod "$api/$($partial.id)" -Headers $headers | Out-Null
Invoke-RestMethod $api -Headers $headers | Out-Null
if ((Invoke-RestMethod "$stub/stats").queries -ne $reads) { throw 'Rendering reads queried source.' }
Invoke-RestMethod "$stub/mode/outage" -Method Post | Out-Null
$stale = Invoke-Refresh
if (@($stale.source.servers | Where-Object { $_.relatedRequestReporter.state -ne 'Stale' }).Count -ne 0) { throw 'Outage evidence not stale.' }
$stats = Invoke-RestMethod "$stub/stats"
if ($stats.rejected -ne 0) { throw 'Unexpected integration destination or write attempted.' }
@{ passed = $true; persistence = 'InMemory'; fourLinks = 4; distinctTargetsPerSuccessfulRefresh = 2;
    realTransportAndSession = $true; retainedVerificationTime = $true; noSourceReadsDuringGet = $true;
    readOnly = $true; controlledWrites = $false; sourceClose = $false; stats = $stats } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
