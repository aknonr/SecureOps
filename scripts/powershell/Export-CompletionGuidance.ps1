[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($OutputDirectory -notmatch '^[A-Za-z]:[\\/]') { throw 'An absolute local output directory is required.' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Refusing to overwrite existing guidance.' }
$files = @(
    'docs/post-rc626-continuation-tr.md',
    'docs/worker-service-operations-tr.md',
    'docs/integrated-test-activation.md',
    'docs/continuation-b596058-evidence.md',
    'docs/rc626-mail-source-activation-tr.md',
    'scripts/diagnostics/InUseEvidence/operator-completion-tr.md'
)
foreach ($relative in $files) {
    $source = Get-Item -LiteralPath (Join-Path $repo $relative)
    if ($source.PSIsContainer -or $source.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Expected an ordinary repository guidance file.'
    }
}
foreach ($relative in $files) {
    $source = Join-Path $repo $relative
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
        throw 'Guidance copy hash mismatch; partial output retained.'
    }
}
