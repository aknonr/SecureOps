[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string[]]$ForbiddenText = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($path in @($PublishDirectory, (Split-Path -Parent $ZipPath), (Split-Path -Parent $ManifestPath)))
{
    if (-not (Test-Path -LiteralPath $path -PathType Container))
    {
        throw "Required release directory does not exist: $path"
    }
}

foreach ($path in @($ZipPath, $ManifestPath))
{
    if (Test-Path -LiteralPath $path)
    {
        throw "Refusing to overwrite an existing release artifact: $path"
    }
}

& (Join-Path $PSScriptRoot 'Test-ApiReleasePayload.ps1') `
    -PublishDirectory $PublishDirectory `
    -ForbiddenText $ForbiddenText

$files = @(Get-ChildItem -LiteralPath $PublishDirectory -File -Recurse | Where-Object {
    $_.Name -ne 'web.config' -and $_.Name -notmatch '^appsettings(\..+)?\.json$'
} | Sort-Object FullName)
if ($files.Count -eq 0)
{
    throw 'No API publish payload files were selected.'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try
{
    foreach ($file in $files)
    {
        $relativePath = $file.FullName.Substring($PublishDirectory.Length).TrimStart('\') -replace '\\', '/'
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $file.FullName,
            $relativePath,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally
{
    $archive.Dispose()
}

$manifestLines = foreach ($file in $files)
{
    $relativePath = $file.FullName.Substring($PublishDirectory.Length).TrimStart('\') -replace '\\', '/'
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    "$hash  $relativePath"
}
[System.IO.File]::WriteAllLines($ManifestPath, [string[]]$manifestLines, [System.Text.ASCIIEncoding]::new())

& (Join-Path $PSScriptRoot 'Validate-ApiAdRuntimeDependencies.ps1') `
    -PublishDirectory $PublishDirectory `
    -ZipPath $ZipPath `
    -ManifestPath $ManifestPath

Write-Output "Created validated path-preserving API package with $($files.Count) files: $ZipPath"
