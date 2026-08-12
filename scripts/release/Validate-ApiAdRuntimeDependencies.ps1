[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StreamSha256([System.IO.Stream]$Stream)
{
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try
    {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($Stream))).Replace('-', '')
    }
    finally
    {
        $sha256.Dispose()
    }
}

function Get-OutputPathForRuntimeAsset([string]$AssetPath)
{
    if ($AssetPath.StartsWith('runtimes/', [System.StringComparison]::OrdinalIgnoreCase))
    {
        return $AssetPath
    }

    return [System.IO.Path]::GetFileName($AssetPath)
}

foreach ($path in @($PublishDirectory, $ZipPath, $ManifestPath))
{
    if (-not (Test-Path -LiteralPath $path))
    {
        throw "Required release input does not exist: $path"
    }
}

$manifestEntries = @{}
foreach ($line in Get-Content -LiteralPath $ManifestPath)
{
    if ($line -notmatch '^([A-F0-9]{64})  ([^\\/].*)$')
    {
        throw "Invalid SHA256 manifest entry: $line"
    }

    $relativePath = $Matches[2]
    if ($relativePath.Contains('..') -or $relativePath.Contains(':') -or $manifestEntries.ContainsKey($relativePath))
    {
        throw "Unsafe or duplicate SHA256 manifest path: $relativePath"
    }

    $manifestEntries[$relativePath] = $Matches[1]
}

if ($manifestEntries.Count -eq 0)
{
    throw 'SHA256 manifest has no payload entries.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try
{
    $zipEntries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    $duplicateEntries = $zipEntries | Group-Object FullName | Where-Object Count -gt 1
    if ($duplicateEntries)
    {
        throw "ZIP has duplicate paths: $($duplicateEntries.Name -join ', ')"
    }

    $zipByPath = @{}
    foreach ($entry in $zipEntries)
    {
        $zipByPath[$entry.FullName] = $entry
    }

    if ($zipByPath.Count -ne $manifestEntries.Count)
    {
        throw "ZIP entry count $($zipByPath.Count) differs from SHA256 manifest count $($manifestEntries.Count)."
    }

    foreach ($relativePath in $manifestEntries.Keys)
    {
        $publishPath = Join-Path $PublishDirectory ($relativePath -replace '/', '\\')
        if (-not (Test-Path -LiteralPath $publishPath -PathType Leaf))
        {
            throw "Manifest payload is missing from publish output: $relativePath"
        }

        if (-not $zipByPath.ContainsKey($relativePath))
        {
            throw "Manifest payload is missing from ZIP: $relativePath"
        }

        $publishHash = (Get-FileHash -LiteralPath $publishPath -Algorithm SHA256).Hash
        if ($publishHash -ne $manifestEntries[$relativePath])
        {
            throw "Publish SHA256 does not match manifest: $relativePath"
        }

        $stream = $zipByPath[$relativePath].Open()
        try
        {
            $zipHash = Get-StreamSha256 $stream
        }
        finally
        {
            $stream.Dispose()
        }

        if ($zipHash -ne $publishHash)
        {
            throw "ZIP SHA256 does not match publish output: $relativePath"
        }
    }

    foreach ($requiredPath in @('SecureOps.Api.dll', 'SecureOps.Infrastructure.dll', 'SecureOps.Api.deps.json', 'SecureOps.Api.runtimeconfig.json'))
    {
        if (-not $manifestEntries.ContainsKey($requiredPath))
        {
            throw "Required application artifact is absent from the manifest: $requiredPath"
        }
    }

    $depsPath = Join-Path $PublishDirectory 'SecureOps.Api.deps.json'
    $runtimeConfigPath = Join-Path $PublishDirectory 'SecureOps.Api.runtimeconfig.json'
    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    if ($deps.runtimeTarget.name -ne '.NETCoreApp,Version=v8.0' -or $runtimeConfig.runtimeOptions.tfm -ne 'net8.0')
    {
        throw 'The deps.json and runtimeconfig.json target frameworks are inconsistent with net8.0.'
    }

    $frameworkNames = @($runtimeConfig.runtimeOptions.frameworks | ForEach-Object Name)
    foreach ($frameworkName in @('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'))
    {
        if ($frameworkNames -notcontains $frameworkName)
        {
            throw "Required shared framework is missing from runtimeconfig.json: $frameworkName"
        }
    }

    $target = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
    if ($null -eq $target)
    {
        throw "Runtime target is missing from deps.json: $($deps.runtimeTarget.name)"
    }

    $apiTarget = @($target.PSObject.Properties | Where-Object { $_.Name -like 'SecureOps.Api/*' })
    $infrastructureTarget = @($target.PSObject.Properties | Where-Object { $_.Name -like 'SecureOps.Infrastructure/*' })
    if ($apiTarget.Count -ne 1 -or $infrastructureTarget.Count -ne 1 -or $apiTarget.Value.dependencies.'SecureOps.Infrastructure' -ne $infrastructureTarget.Name.Split('/')[-1])
    {
        throw 'SecureOps.Api and SecureOps.Infrastructure are not represented as one consistent project dependency graph.'
    }

    $accountManagementTarget = @($target.PSObject.Properties | Where-Object { $_.Name -like 'System.DirectoryServices.AccountManagement/*' })
    if ($accountManagementTarget.Count -ne 1)
    {
        throw 'System.DirectoryServices.AccountManagement is not represented exactly once in deps.json.'
    }

    $expectedRuntimeAsset = 'lib/net8.0/System.DirectoryServices.AccountManagement.dll'
    $expectedWindowsAsset = 'runtimes/win/lib/net8.0/System.DirectoryServices.AccountManagement.dll'
    $accountManagementRuntime = $accountManagementTarget.Value.PSObject.Properties['runtime'].Value
    $accountManagementRuntimeTargets = $accountManagementTarget.Value.PSObject.Properties['runtimeTargets'].Value
    if ($null -eq $accountManagementRuntime -or
        $null -eq $accountManagementRuntimeTargets -or
        $null -eq $accountManagementRuntime.PSObject.Properties[$expectedRuntimeAsset] -or
        $null -eq $accountManagementRuntimeTargets.PSObject.Properties[$expectedWindowsAsset] -or
        $accountManagementRuntimeTargets.PSObject.Properties[$expectedWindowsAsset].Value.rid -ne 'win')
    {
        throw 'System.DirectoryServices.AccountManagement runtime and Windows RID assets are inconsistent in deps.json.'
    }

    $queue = [System.Collections.Generic.Queue[string]]::new()
    $visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $runtimeAssets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $queue.Enqueue($accountManagementTarget.Name)
    while ($queue.Count -gt 0)
    {
        $packageKey = $queue.Dequeue()
        if (-not $visited.Add($packageKey))
        {
            continue
        }

        $packageTarget = $target.PSObject.Properties[$packageKey].Value
        if ($null -eq $packageTarget)
        {
            throw "Dependency target is missing from deps.json: $packageKey"
        }

        $runtimeProperty = $packageTarget.PSObject.Properties['runtime']
        if ($null -ne $runtimeProperty)
        {
            $runtimeProperty.Value.PSObject.Properties | ForEach-Object { [void]$runtimeAssets.Add((Get-OutputPathForRuntimeAsset $_.Name)) }
        }

        $runtimeTargetsProperty = $packageTarget.PSObject.Properties['runtimeTargets']
        if ($null -ne $runtimeTargetsProperty)
        {
            $runtimeTargetsProperty.Value.PSObject.Properties | Where-Object { $_.Value.assetType -eq 'runtime' } | ForEach-Object { [void]$runtimeAssets.Add((Get-OutputPathForRuntimeAsset $_.Name)) }
        }

        $dependenciesProperty = $packageTarget.PSObject.Properties['dependencies']
        if ($null -ne $dependenciesProperty)
        {
            $dependenciesProperty.Value.PSObject.Properties | ForEach-Object { $queue.Enqueue("$($_.Name)/$($_.Value)") }
        }
    }

    foreach ($runtimeAsset in $runtimeAssets)
    {
        if (-not $manifestEntries.ContainsKey($runtimeAsset))
        {
            throw "AccountManagement dependency runtime asset is absent from manifest: $runtimeAsset"
        }
    }

    $assemblyPath = Join-Path $PublishDirectory 'System.DirectoryServices.AccountManagement.dll'
    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
    if ($assemblyName.Name -ne 'System.DirectoryServices.AccountManagement' -or $assemblyName.Version -lt [version]'8.0.0.1')
    {
        throw "Unexpected Active Directory runtime assembly identity: $($assemblyName.FullName)"
    }
}
finally
{
    $archive.Dispose()
}

Write-Output "Active Directory runtime dependency gate passed: $($assemblyName.FullName); manifest entries: $($manifestEntries.Count); dependency runtime assets: $($runtimeAssets.Count)."
