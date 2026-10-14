#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Registers the SecureOps Worker as the Windows service SecureOps.Worker running as a gMSA (no password), on THIS Worker host.

.DESCRIPTION
    Run once by the Worker host's administrator, in the approved window, after the matched Worker payload is in place
    (docs/worker-service-operations-tr.md). It:
      1. refuses the placeholder account and anything that is not a gMSA (DOMAIN\name$); there is no password parameter at all;
      2. checks, when the ActiveDirectory module is present, that this host may use the gMSA (Test-ADServiceAccount);
      3. refuses when the service already exists (use the upgrade procedure instead) or the data directory is missing;
      4. creates the service with StartupType Manual, then sets its log-on account to the gMSA (sc.exe config obj=, no password);
      5. creates the Application event log source SecureOps.Worker if missing (the Worker writes fixed-text lifecycle entries there and
         never creates the source itself).
    It does NOT start the service, set recovery or delayed start, grant "Log on as a service", change ACLs or touch SQL: those stay
    separate, approved steps of the operations procedure. Undo with Uninstall-SecureOpsWorkerService.ps1.

.PARAMETER ExecutablePath
    Absolute path of the approved SecureOps.Worker.exe.

.PARAMETER ServiceAccount
    The Worker's gMSA (the read gMSA of ADR-0024 R2-1, from the AD team). The placeholder CONTOSO\gmsa-placeholder$ is refused.

.PARAMETER Environment
    Host environment passed as --environment (Test by default).

.PARAMETER DataDirectory
    Existing absolute WorkerHosting:DataDirectory; only checked here, configured in appsettings.<Environment>.json.

.EXAMPLE
    .\Install-SecureOpsWorkerService.ps1 -ExecutablePath 'D:\SecureOps\Worker\SecureOps.Worker.exe' -ServiceAccount 'SYN\gmsa-so-sa-read$' -DataDirectory 'D:\SecureOps\WorkerData' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\[A-Za-z0-9_][A-Za-z0-9_.-]{0,14}\$$')]
    [string]$ServiceAccount = 'CONTOSO\gmsa-placeholder$',
    [ValidateSet('Test', 'Production')][string]$Environment = 'Test',
    [Parameter(Mandatory)][string]$DataDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$serviceName = 'SecureOps.Worker'
$eventSource = 'SecureOps.Worker'
if ($ServiceAccount -eq 'CONTOSO\gmsa-placeholder$') { throw 'Pass the real Worker gMSA (-ServiceAccount DOMAIN\name$); the placeholder is refused.' }
if (-not [IO.Path]::IsPathRooted($ExecutablePath) -or $ExecutablePath.Contains('"') -or
    -not (Microsoft.PowerShell.Management\Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) { throw 'ExecutablePath must be an existing absolute path without quotes.' }
if (-not [IO.Path]::IsPathRooted($DataDirectory) -or
    -not (Microsoft.PowerShell.Management\Test-Path -LiteralPath $DataDirectory -PathType Container)) { throw 'DataDirectory must be an existing absolute directory.' }
if (Microsoft.PowerShell.Management\Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw "$serviceName already exists; use the upgrade procedure." }

$gmsaChecked = $false
if (Microsoft.PowerShell.Core\Get-Module -ListAvailable -Name ActiveDirectory) {
    $gmsaName = $ServiceAccount.Split('\')[1].TrimEnd('$')
    if (-not (ActiveDirectory\Test-ADServiceAccount -Identity $gmsaName)) { throw 'This host cannot use the gMSA (Test-ADServiceAccount failed); ask the AD team to allow this computer.' }
    $gmsaChecked = $true
}

if ($PSCmdlet.ShouldProcess($serviceName, "Create the service as $ServiceAccount (Manual start, not started)")) {
    $binaryPath = '"' + $ExecutablePath + '" --environment ' + $Environment
    $null = Microsoft.PowerShell.Management\New-Service -Name $serviceName -DisplayName 'WASAS Worker' -BinaryPathName $binaryPath -StartupType Manual `
        -Description 'SecureOps Worker: Hangfire job server (read-only JEA calls as the read gMSA).'
    # A gMSA has no password: sc.exe takes the account alone.
    & "$env:SystemRoot\System32\sc.exe" config $serviceName obj= $ServiceAccount | Microsoft.PowerShell.Core\Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe config obj= failed with exit code $LASTEXITCODE; the service exists with LocalSystem and must not be started. Run Uninstall-SecureOpsWorkerService.ps1." }

    if (-not [Diagnostics.EventLog]::SourceExists($eventSource)) {
        [Diagnostics.EventLog]::CreateEventSource($eventSource, 'Application')
    }
}

[pscustomobject][ordered]@{
    ServiceName     = $serviceName
    ServiceAccount  = $ServiceAccount
    GmsaChecked     = $gmsaChecked
    StartupType     = 'Manual'
    Started         = $false
    EventLogSource  = $eventSource
    WhatIf          = [bool]$WhatIfPreference
}
