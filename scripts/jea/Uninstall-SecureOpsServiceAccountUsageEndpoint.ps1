#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    ADR-0024 R2. Removes the JEA endpoint SecureOpsServiceAccountUsage from THIS server, run by the server owner.

.DESCRIPTION
    Unregisters the session configuration (this RESTARTS the WinRM service) and, unless -KeepModule, removes the installed module
    folder %ProgramFiles%\WindowsPowerShell\Modules\SecureOps.ServiceAccountUsage. JEA transcripts in
    C:\ProgramData\SecureOps\JEA\Transcripts are kept: they are this server's evidence of every call. The AD group and the gMSA are
    not touched. After removal the Worker reports this server as NoEndpoint ("bilgi yok"), never as "not used".

.PARAMETER KeepModule
    Keep the module files (only unregister the endpoint).

.EXAMPLE
    .\Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param([switch]$KeepModule)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$configurationName = 'SecureOpsServiceAccountUsage'
$moduleRoot = Microsoft.PowerShell.Management\Join-Path -Path $env:ProgramFiles -ChildPath 'WindowsPowerShell\Modules\SecureOps.ServiceAccountUsage'
$registered = [bool](Microsoft.PowerShell.Core\Get-PSSessionConfiguration -Name $configurationName -ErrorAction SilentlyContinue)

if ($registered -and $PSCmdlet.ShouldProcess($env:COMPUTERNAME, "Unregister $configurationName (restarts WinRM)")) {
    Microsoft.PowerShell.Core\Unregister-PSSessionConfiguration -Name $configurationName -Force
}

$moduleExists = Microsoft.PowerShell.Management\Test-Path -LiteralPath $moduleRoot
if (-not $KeepModule -and $moduleExists -and $PSCmdlet.ShouldProcess($moduleRoot, 'Remove the SecureOps.ServiceAccountUsage module files')) {
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $moduleRoot -Recurse
}

[pscustomobject][ordered]@{
    ConfigurationName = $configurationName
    WasRegistered     = $registered
    ModuleRemoved     = (-not $KeepModule) -and $moduleExists -and -not $WhatIfPreference
    TranscriptsKept   = $true
}
