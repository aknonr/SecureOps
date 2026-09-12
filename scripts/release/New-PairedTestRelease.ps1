[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^\d{4}-\d{2}-\d{2}-pilot-rc6\.\d+$')][string]$ReleaseName)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = Join-Path 'C:\SecureOpsBuild\release' $ReleaseName
Push-Location $repo
try {
    $sha = (& git rev-parse HEAD).Trim()
    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne 'feature/sql-runtime-hardening-20260902') { throw 'Unexpected source branch.' }
    $dirty = @(& git status --porcelain | Where-Object { $_ -notmatch '^\?\? \.vscode/' })
    if ($dirty.Count -ne 0) { throw 'Commit and verify the complete source before publishing.' }
    if (Test-Path -LiteralPath $destination) { throw 'Refusing to overwrite an existing release.' }
    foreach ($folder in @('API','UI','DBA','manifests','evidence','staging/api','staging/ui','staging/database/sql/migrations','staging/database/sql/schema')) {
        New-Item -ItemType Directory -Path (Join-Path $destination $folder) | Out-Null
    }
    $assemblies = @()
    foreach ($component in @('Api','Ui')) {
        & dotnet publish "src/SecureOps.$component/SecureOps.$component.csproj" -c Release --no-restore -o "$destination/staging/$($component.ToLowerInvariant())" `
            -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true "-p:PathMap=$repo=/_/" "-p:SourceRevisionId=$sha"
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed; partial delivery retained, not ready.' }
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo("$destination/staging/$($component.ToLowerInvariant())/SecureOps.$component.dll")
        if ($version.ProductVersion -ne "0.1.0+$sha") { throw 'Assembly does not identify exact build source.' }
        $runtime = Get-Content -LiteralPath "$destination/staging/$($component.ToLowerInvariant())/SecureOps.$component.runtimeconfig.json" -Raw | ConvertFrom-Json
        $assemblies += [ordered]@{ component=$component; productVersion=$version.ProductVersion; fileVersion=$version.FileVersion; frameworks=@($runtime.runtimeOptions.frameworks) }
    }
    $doc = Get-Content -LiteralPath 'docs/24-api-test-deployment-readiness.md' -Raw -Encoding UTF8
    $start = '<!-- TEST-RELEASE-RUNBOOK:START -->'; $end = '<!-- TEST-RELEASE-RUNBOOK:END -->'
    $from = $doc.IndexOf($start, [StringComparison]::Ordinal) + $start.Length
    $to = $doc.IndexOf($end, [StringComparison]::Ordinal)
    if ($from -lt $start.Length -or $to -le $from) { throw 'Canonical runbook export markers missing.' }
    $runbook = $doc.Substring($from, $to-$from).Trim().Replace('{{RELEASE_NAME}}', $ReleaseName).Replace('{{BUILD_SHA}}', $sha)
    [IO.File]::WriteAllText("$destination/operator-runbook-tr.md", $runbook, [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath "$destination/operator-runbook-tr.md" -Destination "$destination/staging/database/operator-runbook-tr.md"
    Copy-Item -LiteralPath 'sql/README.md' -Destination "$destination/staging/database/DBA-README.md"
    foreach ($folder in @('migrations','schema')) {
        $files = @(Get-ChildItem "sql/$folder" -File -Filter '*.sql' | Where-Object Name -Match '^0(0[1-9]|1[0-3])-')
        if ($files.Count -ne 13) { throw 'Expected the complete 001-013 SQL chain.' }
        foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination "$destination/staging/database/sql/$folder/$($file.Name)" }
    }
    $short = $sha.Substring(0,7)
    $apiZip = "$destination/API/secureops-api-TEST-$short.zip"
    $uiZip = "$destination/UI/secureops-ui-TEST-$short.zip"
    $dbZip = "$destination/DBA/secureops-database-001-013-TEST-$($ReleaseName.Split('-')[-1]).zip"
    & "$PSScriptRoot/New-ApiDeploymentPackage.ps1" -PublishDirectory "$destination\staging\api" -ZipPath $apiZip -ManifestPath "$destination/manifests/api-payload.sha256" -ForbiddenText @($env:USERPROFILE)
    & "$PSScriptRoot/Validate-ApiAdRuntimeDependencies.ps1" -PublishDirectory "$destination\staging\api" -ZipPath $apiZip -ManifestPath "$destination/manifests/api-payload.sha256"
    & "$PSScriptRoot/New-UiDeploymentPackage.ps1" -PublishDirectory "$destination\staging\ui" -ZipPath $uiZip -ManifestPath "$destination/manifests/ui-payload.sha256" -ForbiddenText @($env:USERPROFILE)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($dbZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem "$destination/staging/database" -File -Recurse) {
            $relative = $file.FullName.Substring(("$destination\staging\database").Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    $packages = @()
    foreach ($item in @(@('API','api',$apiZip), @('UI','ui',$uiZip), @('DBA','database',$dbZip))) {
        $root = "$destination\staging\$($item[1])"
        $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Name -ne 'web.config' -and $_.Name -notmatch '^appsettings(\..+)?\.json$' } | Sort-Object FullName | ForEach-Object {
            [ordered]@{ path=$_.FullName.Substring($root.Length+1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
        $zip = [IO.Compression.ZipFile]::OpenRead($item[2])
        try {
            if ($zip.Entries.Count -ne $files.Count) { throw 'Archive count mismatch.' }
            foreach ($file in $files) {
                $entry = $zip.GetEntry($file.path)
                if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw 'Archive path/size mismatch.' }
                $stream = $entry.Open(); $hash = [Security.Cryptography.SHA256]::Create()
                try { if ([BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','') -ne $file.sha256) { throw 'Archive hash mismatch.' } }
                finally { $hash.Dispose(); $stream.Dispose() }
            }
        } finally { $zip.Dispose() }
        [IO.File]::WriteAllText("$destination/manifests/$($item[1])-files.json", (ConvertTo-Json -InputObject $files -Depth 5), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllLines("$destination/manifests/$($item[1])-payload.sha256", [string[]]@($files | ForEach-Object { "$($_.sha256)  $($_.path)" }), [Text.Encoding]::ASCII)
        $packages += [ordered]@{ component=$item[0]; path=$item[2].Substring($destination.Length+1); bytes=(Get-Item -LiteralPath $item[2]).Length; sha256=(Get-FileHash -LiteralPath $item[2] -Algorithm SHA256).Hash; payloadFiles=$files.Count; manifest="manifests/$($item[1])-payload.sha256"; manifestSha256=(Get-FileHash -LiteralPath "$destination/manifests/$($item[1])-payload.sha256" -Algorithm SHA256).Hash }
    }
    $metadata = [ordered]@{ release=$ReleaseName; branch=$branch; buildSource=$sha; requiredSchema='001-013'; productVersion="0.1.0+$sha"; fileVersion='0.1.0.0'; targetFramework='net8.0'; selfContained=$false; packages=$packages; corporateCallsPerformed=$false; deploymentPerformed=$false; requiredFences=@{ ReadOnlyIntegrationMode=$true; ControlledTestWritesEnabled=$false; SourceCloseEnabled=$false } }
    $metadata.assemblies = $assemblies
    $metadata.requiredHost = 'Windows x64 IIS Hosting Bundle; Microsoft.NETCore.App 8.0 and Microsoft.AspNetCore.App 8.0; no SDK'
    $metadata.payloadValidated = $true
    $metadata.readyForInstallation = $false
    $metadata.validationEvidence = 'evidence/validation.json'
    $metadata.readiness = 'Pending mandatory release gates and separate installation approval'
    [IO.File]::WriteAllText("$destination/release-metadata.json", ($metadata | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllLines("$destination/release-artifacts.sha256", [string[]]@($packages | ForEach-Object { "$($_.sha256)  $($_.path)" }), [Text.Encoding]::ASCII)
    [pscustomobject]@{ PayloadValidated=$true; ReadyForInstallation=$false; BuildSource=$sha; Directory=$destination; Packages=$packages }
} finally { Pop-Location }
