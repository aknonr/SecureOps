[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.Path]::IsPathRooted($OutputDirectory) -or (Test-Path -LiteralPath $output) -or
    $output.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use a new absolute delivery directory outside the repository; overwrite is forbidden.'
}
$source = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $source -notmatch '^[0-9a-f]{40}$') { throw 'Cannot verify source HEAD.' }
& git -C $repo diff --quiet HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Commit reviewed tracked changes before building an exact-source delivery.' }
$project = Join-Path $repo 'scripts/diagnostics/InUseEvidence'
$tool = Join-Path $output 'tool'
New-Item -ItemType Directory -Path $tool | Out-Null
& dotnet publish (Join-Path $project 'InUseEvidence.csproj') -c Release -r win-x64 --self-contained false -o $tool `
    '-p:DebugType=None' '-p:DebugSymbols=false' '-p:GenerateDocumentationFile=false' `
    '-p:ContinuousIntegrationBuild=true' "-p:PathMap=$repo=/_/" "-p:SourceRevisionId=$source"
if ($LASTEXITCODE -ne 0) { throw 'Collector publish failed; incomplete delivery is not usable.' }
# Native dependency packages can carry symbols independently of project DebugType.
foreach ($symbol in Get-ChildItem -LiteralPath $tool -Recurse -File -Filter '*.pdb') {
    if (!$symbol.FullName.StartsWith($tool + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $symbol.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Symbol outside fresh collector staging.' }
    Remove-Item -LiteralPath $symbol.FullName
}
& (Join-Path $PSScriptRoot 'Test-ApiReleasePayload.ps1') -PublishDirectory $tool -ForbiddenText @($repo, $env:USERPROFILE)
foreach ($required in @('InUseEvidence.exe', 'InUseEvidence.dll', 'InUseEvidence.deps.json', 'InUseEvidence.runtimeconfig.json',
        'SecureOps.Infrastructure.dll', 'SecureOps.Shared.dll', 'SecureOps.Domain.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $tool $required) -PathType Leaf)) { throw "Missing collector dependency: $required" }
}
if (Get-ChildItem -LiteralPath $tool -Recurse -File | Where-Object { $_.Name -eq 'web.config' -or $_.Name -like 'appsettings*.json' }) {
    throw 'Server-owned configuration is forbidden in the collector payload.'
}
$runtime = Get-Content -LiteralPath (Join-Path $tool 'InUseEvidence.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.tfm -ne 'net8.0' -or @($runtime.runtimeOptions.frameworks).Count -ne 2) { throw 'Unexpected runtime requirements.' }
$deps = Get-Content -LiteralPath (Join-Path $tool 'InUseEvidence.deps.json') -Raw | ConvertFrom-Json
if ($deps.runtimeTarget.name -notlike '*/win-x64') { throw 'Expected Windows x64 dependency graph.' }
foreach ($name in @('README.md', 'operator-completion-tr.md', 'operator-reporter-tr.md', 'dictionary.json', 'candidate-dictionary.json', 'rfc-contract.template.json', 'server-config.example.json')) {
    Copy-Item -LiteralPath (Join-Path $project $name) -Destination (Join-Path $output $name)
}
if ((Get-Content -LiteralPath (Join-Path $output 'dictionary.json') -Raw).Trim() -ne '{}') { throw 'Initial dictionary must be empty.' }
# Launch only the no-argument usage path: it exits before configuration or networking.
& (Join-Path $tool 'InUseEvidence.exe')
if ($LASTEXITCODE -ne 2) { throw 'Published collector usage smoke check failed.' }
$metadata = [ordered]@{
    BuildSource = $source; RuntimeIdentifier = 'win-x64'; SelfContained = $false
    Frameworks = $runtime.runtimeOptions.frameworks
    Scope = 'Completion: one selected OR dynamic-case shape and bounded eligible activities, read-only. Historical A/B reporter modes retained.'
    Mapping = 'CollectedNotMapped is not runtime mapping or closure evidence; attachment readback and final OR-state semantics require source-owner contracts.'
    Validation = 'Release payload scan and published no-network usage smoke passed; inspect task test evidence separately'
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'delivery-metadata.json') -Encoding UTF8
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName)
$entries = @($files | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($output.Length + 1).Replace('\', '/'); Bytes = $_.Length;
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'payload-manifest.json') -Encoding UTF8
$lines = @($entries | ForEach-Object { "$($_.SHA256)  $($_.Path)" })
[IO.File]::WriteAllLines((Join-Path $output 'payload.sha256'), [string[]]$lines, [Text.Encoding]::ASCII)
$zip = Join-Path $output "inuse-evidence-win-x64-$($source.Substring(0, 7)).zip"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipFiles = @(Get-ChildItem -LiteralPath $output -Recurse -File)
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $zipFiles) {
        $relative = $file.FullName.Substring($output.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if ($archive.Entries.Count -ne $zipFiles.Count) { throw 'ZIP count mismatch.' }
    $seen = @{}
    foreach ($entry in $archive.Entries) {
        if ($seen.ContainsKey($entry.FullName) -or $entry.FullName -match '(^/|\.\.|:)') { throw 'Invalid ZIP entry.' }
        $seen[$entry.FullName] = $true
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $output $entry.FullName) -Algorithm SHA256).Hash) { throw 'ZIP hash mismatch.' }
    }
} finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $output 'archive.sha256'), "$zipHash  $([IO.Path]::GetFileName($zip))`r`n", [Text.Encoding]::ASCII)
[pscustomobject]@{ Ready = $true; BuildSource = $source; Tool = (Join-Path $tool 'InUseEvidence.exe');
    ToolSHA256 = (Get-FileHash -LiteralPath (Join-Path $tool 'InUseEvidence.exe') -Algorithm SHA256).Hash;
    Zip = $zip; ZipSHA256 = $zipHash; Bytes = (Get-Item -LiteralPath $zip).Length; PayloadFiles = $entries.Count }
