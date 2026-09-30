[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z]:[\\/]')][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$TestedProductSource,
    [switch]$ResumeFailedReview)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $destination) {
    if (!$ResumeFailedReview -or (Test-Path (Join-Path $destination 'candidate.json')) -or
        @(Get-ChildItem $destination -File -Recurse -Filter '*.zip').Count -ne 0) {
        throw 'Refuse existing candidate or package. Resume is only for unsealed failed publish output.'
    }
}
Push-Location $repo
try {
    $preparation = (& git rev-parse HEAD).Trim()
    if ((& git branch --show-current).Trim() -ne 'feature/service-accounts-pinned-integration-20260929') {
        throw 'Unexpected review source branch.'
    }
    if (@(& git status --porcelain).Count -ne 0) { throw 'Commit the scoped preparation before publishing.' }
    if (!(& git ls-files -- scripts/release/New-ServiceAccountsTestReview.ps1)) { throw 'Review generator must be tracked.' }
    & git diff --quiet $TestedProductSource HEAD -- src contracts Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.config
    if ($LASTEXITCODE -ne 0) { throw 'Product inputs differ from tested source; new verification is required.' }
    foreach ($directory in @('API','UI','Worker','manifests','staging/api','staging/ui','staging/worker','payload/api','payload/ui','payload/worker','DBA/sql/migrations','DBA/sql/schema','DBA/sql/pending/service-accounts')) {
        $folder = Join-Path $destination $directory
        if (!(Test-Path $folder)) { New-Item -ItemType Directory $folder | Out-Null }
    }
    $payloads = @()
    foreach ($component in @('Api','Ui','Worker')) {
        $lower = $component.ToLowerInvariant()
        $raw = Join-Path $destination "staging/$lower"
        if (!(Test-Path (Join-Path $raw "SecureOps.$component.dll"))) {
        & dotnet publish "src/SecureOps.$component/SecureOps.$component.csproj" -c Release --no-restore -o $raw `
            -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true "-p:PathMap=$repo=/_/" "-p:SourceRevisionId=$TestedProductSource"
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed; partial review retained.' }
        }
        $publish = Join-Path $destination "payload/$lower"
        if (@(Get-ChildItem $publish -File -Recurse).Count -ne 0) { throw 'Refuse existing payload view.' }
        # NuGet native symbols are kept in raw staging, never shipped. The unchanged scanner validates the clean view.
        foreach ($file in Get-ChildItem $raw -File -Recurse | Where-Object Extension -ne '.pdb') {
            $target = Join-Path $publish $file.FullName.Substring($raw.Length+1)
            $parent = Split-Path $target -Parent
            if (!(Test-Path $parent)) { New-Item -ItemType Directory $parent | Out-Null }
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
        $dll = Join-Path $publish "SecureOps.$component.dll"
        if ((Get-Item $dll).VersionInfo.ProductVersion -ne "0.1.0+$TestedProductSource") { throw 'Entry product identity differs.' }
        $zip = Join-Path $destination "$($component.ToUpperInvariant())/secureops-$lower-TEST-$($TestedProductSource.Substring(0,7)).zip"
        $manifest = Join-Path $destination "manifests/$component.sha256"
        if ($component -eq 'Api') {
            & "$PSScriptRoot/New-ApiDeploymentPackage.ps1" -PublishDirectory $publish -ZipPath $zip -ManifestPath $manifest -ForbiddenText @($env:USERPROFILE)
            & "$PSScriptRoot/Validate-ApiAdRuntimeDependencies.ps1" -PublishDirectory $publish -ZipPath $zip -ManifestPath $manifest
        } else {
            & "$PSScriptRoot/New-UiDeploymentPackage.ps1" -PublishDirectory $publish -ZipPath $zip -ManifestPath $manifest -Component $component -ForbiddenText @($env:USERPROFILE)
        }
        $files = @(Get-ChildItem $publish -File -Recurse | Where-Object {
            $_.Name -ne 'web.config' -and $_.Name -notmatch '^appsettings(\..+)?\.json$'
        } | Sort-Object FullName | ForEach-Object {
            [ordered]@{path=$_.FullName.Substring($publish.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash $_.FullName).Hash}
        })
        $payloads += [ordered]@{component=$component;productVersion="0.1.0+$TestedProductSource";
            path=$zip.Substring($destination.Length+1).Replace('\','/');sha256=(Get-FileHash $zip).Hash;
            entrySha256=(Get-FileHash $dll).Hash;manifest="manifests/$component.sha256";files=$files}
    }
    $sql = @('migrations/024-in-use-report-catalogue.sql','schema/024-in-use-report-catalogue.sql',
        'migrations/025-service-accounts.sql','schema/025-service-accounts.sql',
        'pending/service-accounts/SA-001-service-accounts.sql','pending/service-accounts/SA-API-permissions.sql',
        'pending/service-accounts/SA-Worker-permissions.sql')
    foreach ($relative in $sql) {
        Copy-Item -LiteralPath "sql/$relative" -Destination (Join-Path $destination "DBA/sql/$relative")
    }
    Copy-Item sql/README.md (Join-Path $destination 'DBA/README.md')
    & "$PSScriptRoot/../powershell/Export-CompletionGuidance.ps1" -OutputDirectory (Join-Path $destination 'operator')
    foreach ($relative in @('docs/service-accounts/WINDOWS-ACCEPTANCE.md','docs/service-accounts/SPEC.md','docs/service-accounts/INTEGRATION-FOLLOWUP-20261001.md')) {
        $target = Join-Path $destination "operator/$relative"
        New-Item -ItemType Directory (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item $relative $target
    }
    $support = @(Get-ChildItem (Join-Path $destination 'DBA'),(Join-Path $destination 'operator') -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=$_.FullName.Substring($destination.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash $_.FullName).Hash}
    })
    $record = [ordered]@{kind='Matched TEST review candidate, not a numbered release';readyForInstallation=$false;
        testedProductSource=$TestedProductSource;preparationSource=$preparation;sqlSequence=@('024 after verified 023','025','separate reviewed API/Worker roles');
        targetChanged=$false;corporateAcceptance='Not executed';payloads=$payloads;supportingFiles=$support;
        exclusions=@('appsettings*.json','web.config','secrets','private evidence','local test outputs','diagnostic package');
        releaseGuard='New-PairedTestRelease still requires the existing combined branch and exact 001-024 chain; unchanged, not run'}
    [IO.File]::WriteAllText((Join-Path $destination 'candidate.json'), ($record | ConvertTo-Json -Depth 9), [Text.UTF8Encoding]::new($false))
    Write-Output ($payloads | Select-Object component,path,sha256,entrySha256 | ConvertTo-Json)
} finally { Pop-Location }
