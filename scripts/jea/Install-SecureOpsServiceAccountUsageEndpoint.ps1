#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    ADR-0024 R2. Installs the read-only JEA endpoint SecureOpsServiceAccountUsage on THIS server, once, run by the server owner.

.DESCRIPTION
    What it does on this server (and nothing else):
      1. Copies the module folder SecureOps.ServiceAccountUsage (next to this script) to
         %ProgramFiles%\WindowsPowerShell\Modules\SecureOps.ServiceAccountUsage\<ModuleVersion>\ (refuses if that version exists).
      2. Creates the transcript directory if it is missing (C:\ProgramData\SecureOps\JEA\Transcripts).
      3. Writes the session configuration file from the checked-in template, with the read group you name mapped to the only role
         (one visible function, NoLanguage, RestrictedRemoteServer, virtual run-as account), checks it with
         Test-PSSessionConfigurationFile and registers it as SecureOpsServiceAccountUsage.

    Register-PSSessionConfiguration RESTARTS the WinRM service: open remote sessions on this server are interrupted. Run it in an
    approved window. No password, credential or account is created or stored; the Worker connects with Kerberos as the read gMSA,
    a member of -ReadGroup. Undo with Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1.

    SecureOps never runs this script; it is not called by the product.

.PARAMETER ReadGroup
    DOMAIN\group that holds the Worker's read gMSA (created by the AD team). The placeholder CONTOSO\SecureOps-SaRead is refused.

.EXAMPLE
    .\Install-SecureOpsServiceAccountUsageEndpoint.ps1 -ReadGroup 'SYN\SecureOps-SaRead' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\[A-Za-z0-9_][A-Za-z0-9 _.-]{0,63}$')]
    [string]$ReadGroup
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$configurationName = 'SecureOpsServiceAccountUsage'
$roleName = 'SecureOpsServiceAccountUsage'
$placeholderGroup = 'CONTOSO\SecureOps-SaRead'
if ($ReadGroup -eq $placeholderGroup) { throw "Replace the placeholder group $placeholderGroup with the read group the AD team created." }

$source = Microsoft.PowerShell.Management\Join-Path -Path $PSScriptRoot -ChildPath 'SecureOps.ServiceAccountUsage'
$manifest = Microsoft.PowerShell.Utility\Import-PowerShellDataFile -Path (Microsoft.PowerShell.Management\Join-Path -Path $source -ChildPath 'SecureOps.ServiceAccountUsage.psd1')
$template = Microsoft.PowerShell.Utility\Import-PowerShellDataFile -Path (Microsoft.PowerShell.Management\Join-Path -Path $source -ChildPath 'SecureOpsServiceAccountUsage.pssc')
$version = [version]$manifest.ModuleVersion
$target = Microsoft.PowerShell.Management\Join-Path -Path $env:ProgramFiles -ChildPath "WindowsPowerShell\Modules\SecureOps.ServiceAccountUsage\$version"

if (Microsoft.PowerShell.Core\Get-PSSessionConfiguration -Name $configurationName -ErrorAction SilentlyContinue) {
    throw "$configurationName is already registered. Run Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1 first."
}
if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $target) { throw "Module version $version already exists at $target." }

if ($PSCmdlet.ShouldProcess($env:COMPUTERNAME, "Install module $version, register $configurationName for $ReadGroup (restarts WinRM)")) {
    $null = Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $target
    Microsoft.PowerShell.Management\Copy-Item -Path (Microsoft.PowerShell.Management\Join-Path -Path $source -ChildPath '*') -Destination $target -Recurse

    if (-not (Microsoft.PowerShell.Management\Test-Path -LiteralPath $template.TranscriptDirectory)) {
        $null = Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $template.TranscriptDirectory
    }

    $sessionFile = Microsoft.PowerShell.Management\Join-Path -Path $target -ChildPath "$configurationName.registered.pssc"
    Microsoft.PowerShell.Core\New-PSSessionConfigurationFile -Path $sessionFile -Guid $template.GUID -Description $template.Description `
        -SessionType $template.SessionType -LanguageMode $template.LanguageMode -RunAsVirtualAccount `
        -TranscriptDirectory $template.TranscriptDirectory -RoleDefinitions @{ $ReadGroup = @{ RoleCapabilities = $roleName } }
    if (-not (Microsoft.PowerShell.Core\Test-PSSessionConfigurationFile -Path $sessionFile)) { throw "Generated session configuration file is not valid: $sessionFile" }

    $null = Microsoft.PowerShell.Core\Register-PSSessionConfiguration -Name $configurationName -Path $sessionFile -Force
}

[pscustomobject][ordered]@{
    ConfigurationName = $configurationName
    ModuleVersion     = [string]$version
    ReadGroup         = $ReadGroup
    ModulePath        = $target
    WhatIf            = [bool]$WhatIfPreference
}
