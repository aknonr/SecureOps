[CmdletBinding()]
param([Parameter(Mandatory)][string]$ApiReport, [Parameter(Mandatory)][string]$WorkerDll,
    [Parameter(Mandatory)][ValidatePattern('^Oco[A-Za-z0-9_]{1,36}$')][string]$DatabaseSuffix,
    [Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (New-Item -ItemType Directory -Path $EvidenceRoot -ErrorAction Stop).FullName
$api = Get-Content -LiteralPath $ApiReport -Raw -Encoding UTF8 | ConvertFrom-Json
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = 'dotnet'
$start.Arguments = '"' + (Resolve-Path -LiteralPath $WorkerDll).Path + '" --diagnostics'
$start.WorkingDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $WorkerDll).Path
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Demo'
foreach ($setting in $api.Settings) {
    if ($setting.Key -notmatch 'Fingerprint' -and $setting.Key -match '^(Announcements:Enabled|Hangfire:|AnnouncementSource:)') {
        $start.EnvironmentVariables[$setting.Key.Replace(':','__')] = $setting.Value
    }
}
$start.EnvironmentVariables['ConnectionStrings__SecureOpsDb'] = "Data Source=(localdb)\SecureOpsResourcesV1;Initial Catalog=SecureOps_ResourcesV1_$DatabaseSuffix;Integrated Security=True;Encrypt=False;Connect Timeout=15"
foreach ($profile in @('NonProd','Prod01','Prod02')) {
    $values = @{ CollectionId=$profile;Scope="$profile scope";Impact='Local impact';Checks='Local checks';Description='Local description';'To__0'="$profile@example.invalid";'To__1'='remove@example.invalid';'Cc__0'='copy@example.invalid' }
    foreach ($key in $values.Keys) { $start.EnvironmentVariables["AnnouncementSource__Profiles__${profile}__$key"] = $values[$key] }
}
$process = [Diagnostics.Process]::Start($start)
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()
if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'Diagnostic process timed out.' }
$stdout = $outputTask.Result
if ($process.ExitCode -ne 0 -or $errorTask.Result -or $stdout -match 'job server started') { throw 'Worker diagnostics must exit without starting jobs.' }
$worker = $stdout | ConvertFrom-Json
if ($worker.Source.State -ne 'Ready') { throw 'Expected the matching local queue.' }
function Write-Report([string]$Name, $Value) {
    $path = Join-Path $root $Name
    [IO.File]::WriteAllText($path, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    return $path
}
$workerFile = Write-Report 'worker.json' $worker
$comparison = Join-Path $PSScriptRoot '../../scripts/powershell/Compare-OperationsReadiness.ps1'
function Compare-Report([string]$Name, [string]$Left, [string]$Right, [int]$Expected) {
    $output = & powershell -NoProfile -File $comparison -ApiReport $Left -WorkerReport $Right 2>&1
    if ($LASTEXITCODE -ne $Expected) { throw "$Name returned $LASTEXITCODE instead of $Expected." }
    $text = $output | Out-String
    if ($text.Contains('synthetic-secret-not-for-output')) { throw 'Secret output detected.' }
    [IO.File]::WriteAllText((Join-Path $root "$Name.txt"), $text, [Text.UTF8Encoding]::new($false))
}
Compare-Report 'matching' $ApiReport $workerFile 0
($worker.Settings | Where-Object Key -eq 'Hangfire:Queue').Value = 'different-local-queue'
Compare-Report 'wrong-queue' $ApiReport (Write-Report 'wrong-queue.json' $worker) 2
$worker = $stdout | ConvertFrom-Json
$worker.Settings = @($worker.Settings | Where-Object Key -ne 'Hangfire:Queue')
$api.Settings = @($api.Settings | Where-Object Key -ne 'Hangfire:Queue')
Compare-Report 'missing-both' (Write-Report 'missing-api.json' $api) (Write-Report 'missing-worker.json' $worker) 2
$worker = $stdout | ConvertFrom-Json
$worker.Settings += [pscustomobject]@{Key='TuruncuHat:Password';Value='synthetic-secret-not-for-output';Provider='Synthetic'}
Compare-Report 'allowlist' $ApiReport (Write-Report 'allowlist.json' $worker) 0
Write-Report 'result.json' @{passed=$true;checks=@('Worker CLI exits without jobs','effective alignment','wrong queue','missing required key in both','secret exclusion');corporateCalls=$false} | Out-Null
