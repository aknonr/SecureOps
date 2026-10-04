#Requires -Version 5.1
<#
.SYNOPSIS
    PROPOSED (ADR-0024). Read-only: asks the SecureOps discovery JEA endpoint on many servers, in parallel, where the given
    service accounts run; optionally checks a gMSA conversion. Operator tooling for an admin machine, not for the Worker.

.DESCRIPTION
    Replaces only the discovery part of the team's former GUI script, without its slow parts:
    - one parallel Invoke-Command over all servers (ThrottleLimit) instead of serial New-PSSession loops;
    - short connection/operation timeouts so an unreachable server costs seconds, not minutes;
    - IIS identities read once from applicationHost.config on the server instead of one Get-WebConfiguration call per
      application and virtual directory; no per-item SID translation round trips to a domain controller;
    - no GUI thread, no ITSM login per click, no temporary files.
    It never changes anything: no password, service, task, IIS, file or account change. Passwords are never read.
    Results: one row per server and component; servers that could not be reached are reported as Unreachable, never as
    "account not used".

    Requires the endpoint from scripts/jea/proposed/SecureOps.ServiceAccountUsage on each target (after approval).

.EXAMPLE
    .\Invoke-ServiceAccountUsageScan.ps1 -ComputerName SYN-APP01,SYN-APP02 -Account 'SYN\svc_synapp' -OutputPath .\scan.json

.EXAMPLE
    .\Invoke-ServiceAccountUsageScan.ps1 -ComputerListPath .\servers.txt -Account 'SYN\svc_synapp' -ExpectedAccount 'SYN\gmsa_synapp$'
#>
[CmdletBinding(DefaultParameterSetName = 'Names')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Names')]
    [ValidateCount(1, 500)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$')]
    [string[]]$ComputerName,

    [Parameter(Mandatory, ParameterSetName = 'List')]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ComputerListPath,

    [Parameter(Mandatory)]
    [ValidateCount(1, 20)]
    [ValidatePattern('^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$?$')]
    [string[]]$Account,

    [ValidatePattern('^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$$')]
    [string]$ExpectedAccount,

    [ValidatePattern('^[A-Za-z0-9.]{1,64}$')]
    [string]$ConfigurationName = 'SecureOpsServiceAccountUsage',

    [ValidateRange(1, 64)]
    [int]$ThrottleLimit = 32,

    [ValidateRange(5, 120)]
    [int]$OpenTimeoutSeconds = 15,

    [ValidateRange(30, 900)]
    [int]$OperationTimeoutSeconds = 180,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if ($PSCmdlet.ParameterSetName -eq 'List') {
    $ComputerName = @(Get-Content -LiteralPath $ComputerListPath | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    $invalid = @($ComputerName | Where-Object { $_ -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$' })
    if ($invalid.Count -gt 0 -or $ComputerName.Count -eq 0 -or $ComputerName.Count -gt 500) {
        throw 'The computer list must hold 1-500 host names (letters, digits, dots, hyphens).'
    }
}

$ComputerName = @($ComputerName | Sort-Object -Unique)

# JEA sessions run in NoLanguage mode (no variables), so the remote command line is built here. Every value has already
# matched a strict pattern without quotes, spaces or operators, so the literal cannot inject anything.
$accountList = ($Account | ForEach-Object { "'$_'" }) -join ','
$commandText = "Get-SecureOpsAccountUsage -Account $accountList"
if ($ExpectedAccount) { $commandText += " -ExpectedAccount '$ExpectedAccount'" }
$remote = [scriptblock]::Create($commandText)

$sessionOption = New-PSSessionOption -OpenTimeout ($OpenTimeoutSeconds * 1000) -OperationTimeout ($OperationTimeoutSeconds * 1000) -NoMachineProfile
$started = Get-Date
$errors = @()
$answers = @(Invoke-Command -ComputerName $ComputerName -ConfigurationName $ConfigurationName -SessionOption $sessionOption `
        -ThrottleLimit $ThrottleLimit -ScriptBlock $remote -ErrorAction SilentlyContinue -ErrorVariable errors)

$rows = [System.Collections.Generic.List[object]]::new()
$answered = @{}
foreach ($answer in $answers) {
    $server = $answer.PSComputerName
    $answered[$server.ToLowerInvariant()] = $true
    $components = @($answer.components)
    if ($components.Count -eq 0) {
        $rows.Add([pscustomobject]@{ Server = $server; ScanResult = $answer.scanResult; ComponentType = $null; ComponentName = $null
                Identity = $null; MatchedAccount = $null; State = $null; Detail = $null; Verification = $answer.verification.Status })
    }

    foreach ($component in $components) {
        $rows.Add([pscustomobject]@{ Server = $server; ScanResult = $answer.scanResult; ComponentType = $component.ComponentType
                ComponentName = $component.ComponentName; Identity = $component.Identity; MatchedAccount = $component.MatchedAccount
                State = $component.State; Detail = $component.Detail
                Verification = if ($answer.verification) { $answer.verification.Status } else { $null } })
    }
}

foreach ($server in $ComputerName) {
    if ($answered.ContainsKey($server.ToLowerInvariant())) { continue }
    $failure = @($errors | Where-Object { $_.TargetObject -eq $server -or "$($_.OriginInfo.PSComputerName)" -eq $server }) | Select-Object -First 1
    $kind = if ($failure -and $failure.FullyQualifiedErrorId -match 'PSSessionStateBroken|WinRMOperationTimeout|CannotConnect|ComputerNotFound|AccessDenied') { 'Unreachable' } else { 'Failed' }
    $rows.Add([pscustomobject]@{ Server = $server; ScanResult = $kind; ComponentType = $null; ComponentName = $null; Identity = $null
            MatchedAccount = $null; State = $null; Detail = if ($failure) { $failure.FullyQualifiedErrorId } else { 'NoAnswer' }; Verification = $null })
}

$summary = [pscustomobject]@{
    StartedAt   = $started.ToUniversalTime().ToString('o')
    DurationSec = [int]((Get-Date) - $started).TotalSeconds
    Servers     = $ComputerName.Count
    Answered    = $answered.Count
    Components  = @($rows | Where-Object ComponentType).Count
    Note        = 'Unreachable or Failed never means "not used". Evidence only; a person verifies and closes.'
}

if ($OutputPath) {
    [pscustomobject]@{ summary = $summary; rows = $rows } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
}

$summary
$rows | Sort-Object Server, ComponentType, ComponentName
