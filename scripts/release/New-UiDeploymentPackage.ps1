[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string[]]$ForbiddenText = @()
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
foreach ($required in @('SecureOps.Ui.dll', 'SecureOps.Ui.deps.json', 'SecureOps.Ui.runtimeconfig.json',
        'wwwroot/css/secureops-theme.css', 'wwwroot/_content/MudBlazor/MudBlazor.min.css')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $required) -PathType Leaf)) {
        throw "Missing UI artifact: $required"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $publishPath 'SecureOps.Ui.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.tfm -ne 'net8.0') { throw 'Unexpected UI target framework.' }
$dependencies = Get-Content -LiteralPath (Join-Path $publishPath 'SecureOps.Ui.deps.json') -Raw | ConvertFrom-Json
if (-not ($dependencies.libraries.PSObject.Properties.Name -like 'SecureOps.Ui/*')) {
    throw 'UI dependency manifest does not identify the UI project.'
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
    if ($archive.Entries.Count -ne $expected.Count) { throw 'UI ZIP entry count mismatch.' }
    $seen = @{}
    foreach ($entry in $archive.Entries) {
        if (-not $expected.ContainsKey($entry.FullName) -or $seen.ContainsKey($entry.FullName)) {
            throw 'Unexpected or duplicate UI ZIP entry.'
        }
        $seen[$entry.FullName] = $true
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $expected[$entry.FullName]) { throw 'UI ZIP content hash mismatch.' }
    }
} finally { $archive.Dispose() }
$lines = @($expected.Keys | Sort-Object | ForEach-Object { "$($expected[$_])  $_" })
[IO.File]::WriteAllLines($ManifestPath, [string[]]$lines, [Text.Encoding]::ASCII)
[pscustomobject]@{ Ready = $true; Files = $files.Count; ZipPath = $ZipPath; SHA256 = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash }
