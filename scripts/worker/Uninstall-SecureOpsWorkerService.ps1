#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the SecureOps.Worker service registration from THIS Worker host.

.DESCRIPTION
    Stops the service if it runs (Hangfire gets the host's 90 s shutdown budget; unfinished scans close by their lease, see
    docs/service-accounts/USAGE-SCAN-JOB-CONTRACT.md), then deletes the registration with sc.exe delete. Binaries, configuration, the data
    directory, logs, SQL and audit are NOT removed. The event log source is kept unless -RemoveEventLogSource (old entries stay readable
    only while it exists).

.EXAMPLE
    .\Uninstall-SecureOpsWorkerService.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param([switch]$RemoveEventLogSource)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$serviceName = 'SecureOps.Worker'
$eventSource = 'SecureOps.Worker'
$service = Microsoft.PowerShell.Management\Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service -and $PSCmdlet.ShouldProcess($serviceName, 'Stop (if running) and delete the service registration')) {
    if ($service.Status -ne 'Stopped') {
        Microsoft.PowerShell.Management\Stop-Service -Name $serviceName
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(120))
    }

    & "$env:SystemRoot\System32\sc.exe" delete $serviceName | Microsoft.PowerShell.Core\Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed with exit code $LASTEXITCODE." }
}

if ($RemoveEventLogSource -and [Diagnostics.EventLog]::SourceExists($eventSource) -and $PSCmdlet.ShouldProcess($eventSource, 'Remove the event log source')) {
    [Diagnostics.EventLog]::DeleteEventSource($eventSource)
}

[pscustomobject][ordered]@{
    ServiceName   = $serviceName
    WasRegistered = [bool]$service
    FilesKept     = $true
    WhatIf        = [bool]$WhatIfPreference
}
