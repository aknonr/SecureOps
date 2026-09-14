[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string[]]$ForbiddenText = @(),
    [ValidateSet('Ui','Worker')][string]$Component = 'Ui'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path.TrimEnd('\')
foreach ($outputPath in @($ZipPath, $ManifestPath)) {
    if (Test-Path -LiteralPath $outputPath) { throw 'Refusing to overwrite a release artifact.' }
    if (-not (Test-Path -LiteralPath (Split-Path -Parent $outputPath) -PathType Container)) {
        throw 'The release output directory must already exist.'
    }
}

& (Join-Path $PSScriptRoot 'Test-ApiReleasePayload.ps1') -PublishDirectory $publishPath -ForbiddenText $ForbiddenText
$requiredFiles = @("SecureOps.$Component.dll", "SecureOps.$Component.deps.json", "SecureOps.$Component.runtimeconfig.json")
if ($Component -eq 'Ui') { $requiredFiles += @('wwwroot/css/secureops-theme.css','wwwroot/_content/MudBlazor/MudBlazor.min.css') }
foreach ($required in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $required) -PathType Leaf)) {
        throw "Missing $Component artifact: $required"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $publishPath "SecureOps.$Component.runtimeconfig.json") -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.tfm -ne 'net8.0') { throw 'Unexpected target framework.' }
$dependencies = Get-Content -LiteralPath (Join-Path $publishPath "SecureOps.$Component.deps.json") -Raw | ConvertFrom-Json
if (-not ($dependencies.libraries.PSObject.Properties.Name -like "SecureOps.$Component/*")) {
    throw 'Dependency manifest does not identify the packaged project.'
}
if ($Component -eq 'Worker') {
    foreach ($name in @('Hangfire.Core','Hangfire.SqlServer','Microsoft.Data.SqlClient','SecureOps.Infrastructure')) {
        if (!($dependencies.libraries.PSObject.Properties.Name -like "$name/*")) { throw "Missing Worker dependency: $name" }
    }
    foreach ($target in $dependencies.targets.PSObject.Properties.Value) {
        foreach ($library in $target.PSObject.Properties.Value) {
            foreach ($kind in @('runtime','native','runtimeTargets','resources')) {
                $group = $library.PSObject.Properties[$kind]
                if ($null -eq $group) { continue }
                foreach ($asset in $group.Value.PSObject.Properties) {
                    if ($asset.Name.EndsWith('/_._')) { continue }
                    if ([IO.Path]::GetExtension($asset.Name) -eq '.pdb') { continue } # Debug symbols are not runtime dependencies.
                    $relative = if ($asset.Name.StartsWith('runtimes/')) { $asset.Name }
                        elseif ($kind -eq 'resources') { $asset.Value.locale + '/' + [IO.Path]::GetFileName($asset.Name) }
                        else { [IO.Path]::GetFileName($asset.Name) }
                    if (!(Test-Path -LiteralPath (Join-Path $publishPath $relative) -PathType Leaf)) {
                        throw "Missing Worker runtime asset: $relative"
                    }
                }
            }
        }
    }
}

$files = @(Get-ChildItem -LiteralPath $publishPath -Recurse -File | Where-Object {
    $_.Name -ne 'web.config' -and $_.Name -notmatch '^appsettings(\..+)?\.json$'
} | Sort-Object FullName)
$expected = @{}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($publishPath.Length).TrimStart('\') -replace '\\', '/'
        $expected[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }

$archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    if ($archive.Entries.Count -ne $expected.Count) { throw 'ZIP entry count mismatch.' }
    $seen = @{}
    foreach ($entry in $archive.Entries) {
        if (-not $expected.ContainsKey($entry.FullName) -or $seen.ContainsKey($entry.FullName)) {
            throw 'Unexpected or duplicate ZIP entry.'
        }
        $seen[$entry.FullName] = $true
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $expected[$entry.FullName]) { throw 'ZIP content hash mismatch.' }
    }
} finally { $archive.Dispose() }
$lines = @($expected.Keys | Sort-Object | ForEach-Object { "$($expected[$_])  $_" })
[IO.File]::WriteAllLines($ManifestPath, [string[]]$lines, [Text.Encoding]::ASCII)
[pscustomobject]@{ Ready = $true; Files = $files.Count; ZipPath = $ZipPath; SHA256 = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash }
