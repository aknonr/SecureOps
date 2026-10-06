[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z]:[\\/]')][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$TestedProductSource,
    [switch]$ResumeFailedReview,
    [switch]$FromVerifiedMaster,
    [switch]$FromExactSource,
    [ValidatePattern('^[a-f0-9]{40}$')][string]$ExpectedSource,
    [switch]$ApiUiOnly,
    [switch]$UpgradeFromInstalled026,
    [switch]$UpgradeFromInstalled027, [switch]$UpgradeFromInstalled028,
    [Parameter(Mandatory)][string]$SqlUpgradeReview)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$current = $UpgradeFromInstalled027 -or $UpgradeFromInstalled028
if ($current -and (!$FromExactSource -or !$ApiUiOnly -or !$ExpectedSource -or $ResumeFailedReview -or $FromVerifiedMaster)) {
    throw '028-032 review requires exact ExpectedSource, API/UI-only, fresh output and FromExactSource.'
}
if ($FromExactSource -and !$current) { throw 'Exact-source mode is reserved for explicit installed 027/028 review.' }
if ($destination.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $destination -ieq $repo) {
    throw 'Candidate output must be outside the source tree.'
}
if (Test-Path $destination) {
    if (!$ResumeFailedReview -or (Test-Path (Join-Path $destination 'candidate.json')) -or
        @(Get-ChildItem $destination -File -Recurse -Filter '*.zip').Count -ne 0) {
        throw 'Refuse existing candidate or package. Resume is only for unsealed failed publish output.'
    }
}
Push-Location $repo
try {
    $preparation = (& git rev-parse HEAD).Trim()
    $branch = (& git branch --show-current).Trim()
    if ($FromExactSource) {
        if ($branch -cnotin @('master','fix/release-packaging-028-032-20261007') -or $preparation -cne $ExpectedSource) {
            throw 'Expected authorized branch and exact preparation source.'
        }
        $master = (& git rev-parse origin/master).Trim()
        if ($LASTEXITCODE -ne 0 -or $TestedProductSource -cne $master) { throw 'Tested product source must equal verified remote master.' }
        & git merge-base --is-ancestor $TestedProductSource HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Preparation must descend from tested master.' }
    } elseif ($FromVerifiedMaster) {
        if ($branch -cne 'master' -or $preparation -cne $TestedProductSource -or
            $preparation -cne (& git rev-parse origin/master).Trim()) { throw 'Expected clean exact verified remote master.' }
    } elseif ($branch -ne 'feature/service-accounts-pinned-integration-20260929') {
        throw 'Unexpected review source branch.'
    }
    $dirty = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Commit the scoped preparation before publishing.' }
    if (!(& git ls-files -- scripts/release/New-ServiceAccountsTestReview.ps1)) { throw 'Review generator must be tracked.' }
    & git diff --quiet $TestedProductSource HEAD -- src contracts Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.config
    if ($LASTEXITCODE -ne 0) { throw 'Product inputs differ from tested source; new verification is required.' }
    if ($UpgradeFromInstalled026 -and (!$FromVerifiedMaster -or !$ApiUiOnly)) { throw '027 review requires exact master and API/UI-only selection.' }
    $baseline = if ($UpgradeFromInstalled027) { @{ UpgradeFromInstalled027=$true } } elseif ($UpgradeFromInstalled028) { @{ UpgradeFromInstalled028=$true } } elseif ($UpgradeFromInstalled026) { @{ UpgradeFromInstalled026=$true } } else { @{ UpgradeFromRc626=$true } }
    if (@($UpgradeFromInstalled026,$UpgradeFromInstalled027,$UpgradeFromInstalled028 | Where-Object { $_ }).Count -gt 1) { throw 'Choose exactly one upgrade baseline.' }
    $sqlPlan = & "$PSScriptRoot/Get-ReleaseSqlPlan.ps1" -RepositoryRoot $repo @baseline -IncludeServiceAccounts `
        -ExpectedSource $preparation -SqlUpgradeReview $SqlUpgradeReview
    $directories = @('API','UI','manifests','staging/api','staging/ui','payload/api','payload/ui','DBA/sql/migrations','DBA/sql/schema','DBA/sql/pending/service-accounts','configuration')
    if (!$ApiUiOnly) { $directories += @('Worker','staging/worker','payload/worker') }
    foreach ($directory in $directories) {
        $folder = Join-Path $destination $directory
        if (!(Test-Path $folder)) { New-Item -ItemType Directory $folder | Out-Null }
    }
    $payloads = @()
    $components = if ($ApiUiOnly) { @('Api','Ui') } else { @('Api','Ui','Worker') }
    foreach ($component in $components) {
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
    $sql = @($sqlPlan.DeltaFiles + $sqlPlan.RoleFiles | ForEach-Object { $_.Substring(4) })
    foreach ($relative in $sql) {
        Copy-Item -LiteralPath "sql/$relative" -Destination (Join-Path $destination "DBA/sql/$relative")
    }
    Copy-Item sql/README.md (Join-Path $destination 'DBA/README.md')
    Copy-Item "$PSScriptRoot/configuration/AdminLookupRecovery.delta.xml" (Join-Path $destination 'configuration/recovery.delta.xml')
    $operatorEntry = 'operator/docs/service-accounts/ADMIN-LOOKUP-OPERATOR-CHECKLIST-20261003.md'
    if ($current) {
        $operatorEntry = 'operator/docs/release/TEST-028-032-OPERATOR-TR.md'
        $guidance = @('docs/release/TEST-028-032-OPERATOR-TR.md','docs/service-accounts/DBA-029-030-TR.md',
            'docs/access-registration-dba-032.md','docs/adr/ADR-0029-api-csrf-origin-guard.md')
        Copy-Item 'scripts/diagnostics/Get-InstalledMigrationInventory025To032.sql' (Join-Path $destination 'DBA/Get-InstalledMigrationInventory025To032.sql')
    } else {
        & "$PSScriptRoot/../powershell/Export-CompletionGuidance.ps1" -OutputDirectory (Join-Path $destination 'operator')
        $guidance = @('docs/service-accounts/WINDOWS-ACCEPTANCE.md','docs/service-accounts/SPEC.md','docs/service-accounts/INTEGRATION-FOLLOWUP-20261001.md',
        'docs/service-accounts/COMBINED-INTEGRATION-20261002.md',
        'docs/service-accounts/ADMIN-ACCESS-AND-GENERAL-LOOKUP-20261002.md',
        'docs/service-accounts/ADMIN-LOOKUP-DELIVERY-20261003.md',
        'docs/service-accounts/ADMIN-LOOKUP-OPERATOR-CHECKLIST-20261003.md')
    }
    foreach ($relative in $guidance) {
        $source = Get-Item -LiteralPath $relative
        if ($source.PSIsContainer -or $source.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Expected ordinary source guidance.' }
        $target = Join-Path $destination "operator/$relative"
        New-Item -ItemType Directory (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item $relative $target
        if ((Get-FileHash -LiteralPath $relative).Hash -cne (Get-FileHash -LiteralPath $target).Hash) { throw 'Guidance copy hash mismatch.' }
    }
    $support = @(Get-ChildItem (Join-Path $destination 'DBA'),(Join-Path $destination 'operator'),(Join-Path $destination 'configuration') -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=$_.FullName.Substring($destination.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash $_.FullName).Hash}
    })
    $sequence = if ($UpgradeFromInstalled026) { @('preserve installed 024-026; verify baseline','027 only if not already satisfied','API grant scripts are existing references, not replay or membership commands') } else { @('024 after verified 023','025 if missing','026 after 025','separate reviewed API/Worker roles and 026 API grants') }
    if ($current) { $sequence = $sqlPlan.ExecutionFiles }
    $record = [ordered]@{kind='Matched TEST review candidate, not a numbered release';readyForInstallation=$false;
        testedProductSource=$TestedProductSource;preparationSource=$preparation;expectedSource=$ExpectedSource;requiredSchema=$sqlPlan.RequiredSchema;
        sqlReview=$sqlPlan.Review;sqlSequence=$sequence;components=$components;
        operatorEntry=$operatorEntry;
        targetChanged=$false;corporateAcceptance='Not executed';payloads=$payloads;supportingFiles=$support;
        exclusions=@('appsettings*.json','web.config','secrets','private evidence','local test outputs','diagnostic package');
        releaseGuard='Numbered release requires the existing combined branch, clean exact ExpectedSource and explicit source-bound 023/024/025/026 SQL review; not run'}
    [IO.File]::WriteAllText((Join-Path $destination 'candidate.json'), ($record | ConvertTo-Json -Depth 9), [Text.UTF8Encoding]::new($false))
    Write-Output ($payloads | ForEach-Object { [pscustomobject]$_ } | Select-Object component,path,sha256,entrySha256 | ConvertTo-Json)
} finally { Pop-Location }
