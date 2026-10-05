#Requires -Version 5.1
<#
    SecureOps.ServiceAccountUsage — PROPOSED (ADR-0024), not deployable until ADR-0024 is accepted and Bilgi Güvenliği
    has approved the JEA role capability. Read-only by construction:

    - One exported command, Get-SecureOpsAccountUsage, which a JEA role capability exposes as the only visible function.
      Function bodies run outside JEA's language restrictions (Microsoft PowerShell-Docs, "JEA role capabilities"), so the
      raw cmdlets it uses (Get-CimInstance, Get-ScheduledTask, Get-Content) never need to be visible to the caller and
      are called by fully qualified module name.
    - Sources: Win32_Service.StartName, Scheduled Task principals, and IIS identities read from applicationHost.config.
      For IIS only these attributes are read: application pool name, processModel identityType and userName; site,
      application and virtual directory path, userName and physicalPath. Password attributes are never read, selected,
      decrypted or returned. COM+ identities and user-right assignments are not scanned (see ADR-0024 "Coverage").
    - Nothing is written: no service, task, IIS, file, registry or account change; no temporary files.

    Pure helpers (ConvertTo-SoAccountKey, Test-SoAccountMatch, Read-SoIisIdentity, Find-SoAccountUsage,
    Test-SoGmsaConversion) take data in and return data, so they are unit-tested without a Windows server.
#>

Set-StrictMode -Version 3.0

$script:SchemaName = 'service-account-usage-v1'
$script:MaxAccounts = 20
$script:AccountPattern = '^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$?$'
$script:BuiltInIdentities = @('localsystem', 'nt authority\system', 'nt authority\localservice', 'nt authority\local service',
    'nt authority\networkservice', 'nt authority\network service')

function ConvertTo-SoAccountKey {
    <#
        Splits an identity as Windows shows it (DOMAIN\name, name@dns.suffix, .\name, name) into a comparison key.
        Name comparison is case-insensitive; a trailing "$" (computer or gMSA account) is significant.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Identity)

    $value = $Identity.Trim()
    $domain = $null
    $suffix = $null
    $isLocal = $false
    if ($value -match '^(?<d>[^\\]+)\\(?<n>.+)$') {
        $domain = $Matches.d
        $value = $Matches.n
        $isLocal = $domain -eq '.'
        if ($isLocal) { $domain = $null }
    }
    elseif ($value -match '^(?<n>[^@]+)@(?<s>.+)$') {
        $value = $Matches.n
        $suffix = $Matches.s
    }

    [pscustomobject]@{
        Name      = $value.ToLowerInvariant()
        Domain    = if ($domain) { $domain.ToUpperInvariant() } else { $null }
        UpnSuffix = if ($suffix) { $suffix.ToLowerInvariant() } else { $null }
        IsLocal   = $isLocal
        IsBuiltIn = $script:BuiltInIdentities -contains $Identity.Trim().ToLowerInvariant()
    }
}

function Test-SoAccountMatch {
    <#
        True when a configured identity refers to the wanted account. Names must be equal; when both sides carry a NetBIOS
        domain the domains must be equal too. Local accounts (".\name") and built-in identities never match a domain
        account. A UPN suffix cannot be compared with a NetBIOS domain, so it does not prevent a match.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Configured,
        [Parameter(Mandatory)][string]$Wanted
    )

    if ([string]::IsNullOrWhiteSpace($Configured)) { return $false }
    $c = ConvertTo-SoAccountKey -Identity $Configured
    $w = ConvertTo-SoAccountKey -Identity $Wanted
    if ($c.IsBuiltIn -or $c.IsLocal -or $c.Name -ne $w.Name) { return $false }
    if ($c.Domain -and $w.Domain -and $c.Domain -ne $w.Domain) { return $false }
    return $true
}

function Read-SoIisIdentity {
    <#
        Reads IIS identities from applicationHost.config content. Selects only names, identityType, userName and
        physicalPath; never touches password attributes. Returns one object per configured identity.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param([Parameter(Mandatory)][xml]$Configuration)

    $root = $Configuration.DocumentElement
    foreach ($pool in $root.SelectNodes('system.applicationHost/applicationPools/add')) {
        $model = $pool.SelectSingleNode('processModel')
        if ($null -eq $model) { continue }
        $identityType = $model.GetAttribute('identityType')
        $userName = $model.GetAttribute('userName')
        if ($identityType -eq 'SpecificUser' -and $userName) {
            [pscustomobject]@{ ComponentType = 'IisAppPool'; ComponentName = $pool.GetAttribute('name'); Identity = $userName; Detail = $null }
        }
    }

    foreach ($site in $root.SelectNodes('system.applicationHost/sites/site')) {
        $siteName = $site.GetAttribute('name')
        foreach ($application in $site.SelectNodes('application')) {
            $applicationPath = $application.GetAttribute('path')
            foreach ($directory in $application.SelectNodes('virtualDirectory')) {
                $userName = $directory.GetAttribute('userName')
                if (-not $userName) { continue }
                $directoryPath = $directory.GetAttribute('path')
                $type = if ($directoryPath -ne '/') { 'IisVirtualDirectory' } elseif ($applicationPath -eq '/') { 'IisSite' } else { 'IisApplication' }
                $name = ($siteName + $applicationPath.TrimEnd('/') + $(if ($directoryPath -ne '/') { $directoryPath } else { '' }))
                [pscustomobject]@{ ComponentType = $type; ComponentName = $name; Identity = $userName; Detail = $directory.GetAttribute('physicalPath') }
            }
        }
    }
}

function Find-SoAccountUsage {
    <#
        Matches collected identities (objects with ComponentType, ComponentName, Identity and optional State/Detail) against
        the wanted accounts. Returns one component per match, sorted for stable output.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)][string[]]$Account,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Identity
    )

    $found = foreach ($item in $Identity) {
        foreach ($wanted in $Account) {
            if (Test-SoAccountMatch -Configured $item.Identity -Wanted $wanted) {
                [pscustomobject]@{
                    ComponentType  = $item.ComponentType
                    ComponentName  = $item.ComponentName
                    Identity       = $item.Identity
                    MatchedAccount = $wanted
                    State          = if ($item.PSObject.Properties['State']) { $item.State } else { $null }
                    Detail         = $item.Detail
                }
            }
        }
    }

    @($found) | Microsoft.PowerShell.Utility\Sort-Object -Property ComponentType, ComponentName, MatchedAccount
}

function Test-SoGmsaConversion {
    <#
        Post-conversion check (ADR-0024): after the executing team changed the components to the gMSA, does anything still
        run as a former account, and which components now run as the gMSA? Evidence only; the verifier decides.
        Status: Converted (gMSA found, no former account left), NotConverted (a former account is still configured),
        NoComponents (neither found on this server).
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Identity,
        [Parameter(Mandatory)][string[]]$FormerAccount,
        [Parameter(Mandatory)][ValidatePattern('\$$')][string]$ExpectedAccount
    )

    $former = @(Find-SoAccountUsage -Account $FormerAccount -Identity $Identity)
    $gmsa = @(Find-SoAccountUsage -Account @($ExpectedAccount) -Identity $Identity)
    $status = if ($former.Count -gt 0) { 'NotConverted' } elseif ($gmsa.Count -gt 0) { 'Converted' } else { 'NoComponents' }
    [pscustomobject]@{
        ExpectedAccount     = $ExpectedAccount
        Status              = $status
        RunningAsGmsa       = $gmsa
        StillFormerAccount  = $former
    }
}

function Get-SoServiceIdentity {
    [CmdletBinding()]
    param()
    CimCmdlets\Get-CimInstance -ClassName Win32_Service -Property Name, StartName, State -ErrorAction Stop |
        Microsoft.PowerShell.Core\ForEach-Object {
            [pscustomobject]@{ ComponentType = 'WindowsService'; ComponentName = $_.Name; Identity = $_.StartName; State = [string]$_.State; Detail = $null }
        }
}

function Get-SoTaskIdentity {
    [CmdletBinding()]
    param()
    ScheduledTasks\Get-ScheduledTask -ErrorAction Stop | Microsoft.PowerShell.Core\ForEach-Object {
        [pscustomobject]@{
            ComponentType = 'ScheduledTask'
            ComponentName = $_.TaskPath + $_.TaskName
            Identity      = $_.Principal.UserId
            State         = [string]$_.State
            Detail        = 'LogonType=' + [string]$_.Principal.LogonType
        }
    }
}

function Get-SoIisConfiguration {
    [CmdletBinding()]
    param()
    $path = Microsoft.PowerShell.Management\Join-Path -Path $env:windir -ChildPath 'System32\inetsrv\config\applicationHost.config'
    if (-not (Microsoft.PowerShell.Management\Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    [xml](Microsoft.PowerShell.Management\Get-Content -LiteralPath $path -Raw -ErrorAction Stop)
}

function Get-SecureOpsAccountUsage {
    <#
    .SYNOPSIS
        Read-only: where do the given accounts run on this server (Windows services, scheduled tasks, IIS)?
    .PARAMETER Account
        1-20 account names: name, DOMAIN\name or gMSA name ending with "$". No wildcards.
    .PARAMETER ExpectedAccount
        Optional gMSA (ending with "$"): also report whether the components now run as it (post-conversion check).
    .OUTPUTS
        One object, schema service-account-usage-v1 (contracts/schemas/service-account-usage.schema.json).
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [ValidateCount(1, 20)]
        [ValidateScript({ $_ -match $script:AccountPattern })]
        [string[]]$Account,

        [ValidateScript({ $_ -match $script:AccountPattern -and $_.EndsWith('$') })]
        [string]$ExpectedAccount
    )

    $ErrorActionPreference = 'Stop'
    $started = [DateTimeOffset]::UtcNow
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $identities = [Collections.Generic.List[object]]::new()
    $sources = [ordered]@{}
    $warnings = [Collections.Generic.List[string]]::new()

    foreach ($source in @(
            @{ Name = 'WindowsServices'; Read = { Get-SoServiceIdentity } },
            @{ Name = 'ScheduledTasks'; Read = { Get-SoTaskIdentity } },
            @{ Name = 'Iis'; Read = {
                    $configuration = Get-SoIisConfiguration
                    if ($null -eq $configuration) { 'NotInstalled' } else { Read-SoIisIdentity -Configuration $configuration }
                } })) {
        try {
            $items = @(& $source.Read)
            if ($items.Count -eq 1 -and $items[0] -is [string] -and $items[0] -eq 'NotInstalled') {
                $sources[$source.Name] = 'NotInstalled'
                continue
            }

            foreach ($item in $items) { $identities.Add($item) }
            $sources[$source.Name] = 'Success'
        }
        catch {
            # The error type is reported, not its message: messages can carry paths or names from the server.
            $sources[$source.Name] = 'Failed'
            $warnings.Add("$($source.Name): $($_.Exception.GetType().Name)")
        }
    }

    $failed = @($sources.Values | Microsoft.PowerShell.Core\Where-Object { $_ -eq 'Failed' }).Count
    $scanResult = if ($failed -eq 0) { 'Success' } elseif ($failed -lt $sources.Count) { 'Partial' } else { 'Failed' }
    $components = @(Find-SoAccountUsage -Account $Account -Identity $identities.ToArray())
    $verification = if ($ExpectedAccount) {
        Test-SoGmsaConversion -Identity $identities.ToArray() -FormerAccount $Account -ExpectedAccount $ExpectedAccount
    } else { $null }

    $timer.Stop()
    [pscustomobject]@{
        schema       = $script:SchemaName
        serverName   = $env:COMPUTERNAME
        generatedAt  = $started.ToString('o')
        durationMs   = [int]$timer.ElapsedMilliseconds
        accounts     = @($Account)
        scanResult   = $scanResult
        sources      = [pscustomobject]$sources
        components   = $components
        verification = $verification
        warnings     = @($warnings)
    }
}

Export-ModuleMember -Function Get-SecureOpsAccountUsage, ConvertTo-SoAccountKey, Test-SoAccountMatch, Read-SoIisIdentity, Find-SoAccountUsage, Test-SoGmsaConversion
