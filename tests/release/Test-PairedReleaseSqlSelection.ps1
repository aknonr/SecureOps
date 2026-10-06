[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$VerifySqlCmd,
    [ValidatePattern('^[A-Za-z0-9_]{1,30}$')][string]$DatabaseSuffix)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a new private evidence directory.' }
if ($VerifySqlCmd -and !$DatabaseSuffix) { throw 'A unique disposable database suffix is required.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$helper = Join-Path $root 'scripts/release/Get-ReleaseSqlPlan.ps1'
$source = (& git -C $root rev-parse HEAD).Trim()
$reviewPath = Join-Path $evidence 'synthetic-review.json'
$paths = @('sql/migrations/024-in-use-report-catalogue.sql','sql/schema/024-in-use-report-catalogue.sql',
    'sql/migrations/025-service-accounts.sql','sql/schema/025-service-accounts.sql',
    'sql/pending/service-accounts/SA-001-service-accounts.sql',
    'sql/pending/service-accounts/SA-API-permissions.sql','sql/pending/service-accounts/SA-Worker-permissions.sql',
    'sql/migrations/026-service-account-usage-rules.sql','sql/schema/026-service-account-usage-rules.sql',
    'sql/pending/service-accounts/SA-002-usage-rules.sql','sql/pending/service-accounts/SA-002-API-permissions.sql')
$review = [ordered]@{ Schema='wasas.sql-upgrade-review.v1'; Source=$source;
    Baseline023Verified=$true; Delta024Reviewed=$true; ServiceAccounts025Reviewed=$true; ServiceAccounts026Reviewed=$true;
    BaselineEvidenceReference='SYNTHETIC local test, not target acceptance';
    Delta024ReviewReference='SYNTHETIC local file review'; ServiceAccounts025ReviewReference='SYNTHETIC local file review';
    ServiceAccounts026ReviewReference='SYNTHETIC local 026 atomic/upgrade/role review';
    Files=@($paths | ForEach-Object { [ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} }) }
function Save-Review { [IO.File]::WriteAllText($reviewPath, ($review | ConvertTo-Json -Depth 6)) }
$results = [Collections.Generic.List[object]]::new()
function Assert-Case([string]$Name, [scriptblock]$Action) {
    & $Action
    $results.Add([pscustomobject]@{Case=$Name;Result='PASS'})
}
function Assert-Refusal([string]$Name, [scriptblock]$Action, [string]$Expected) {
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    if (!$message -or $message -notlike "*$Expected*") { throw "Unexpected refusal in $Name : $message" }
    $results.Add([pscustomobject]@{Case=$Name;Result='PASS';ExpectedRefusal=$Expected})
}
$selectionRoot = Join-Path $evidence 'source-026'
foreach ($folder in @('migrations','schema','pending/service-accounts')) {
    $target = Join-Path $selectionRoot "sql/$folder"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem (Join-Path $root "sql/$folder") -File -Filter '*.sql' | Where-Object {
        $folder -eq 'pending/service-accounts' -or [int]$_.Name.Substring(0,3) -le 26
    } | Copy-Item -Destination $target
}
$args025 = @{RepositoryRoot=$selectionRoot; ExpectedSource=$source; UpgradeFromRc626=$true; IncludeServiceAccounts=$true; SqlUpgradeReview=$reviewPath}
Save-Review
$plan = & $helper @args025
Assert-Case 'Explicit reviewed 024/025/026 selection excludes installed 022/023' {
    if ($plan.RequiredSchema -cne '001-026' -or $plan.DeltaRange -cne '024-026' -or $plan.DeltaFiles.Count -ne 8 -or
        @($plan.DeltaFiles | Where-Object { $_ -match '/02[23]-' }).Count -ne 0 -or $plan.RoleFiles.Count -ne 3) { throw 'Wrong upgrade selection.' }
}
Assert-Refusal 'No implicit 025' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromRc626 } 'exact complete'
Assert-Refusal 'No missing review' { & $helper -RepositoryRoot $selectionRoot -ExpectedSource $source -UpgradeFromRc626 -IncludeServiceAccounts } '025 requires'
Assert-Refusal 'No older baseline for 025' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromRc624 -IncludeServiceAccounts } 'rc6.26/023'
Assert-Refusal 'No conflicting baselines' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromRc624 -UpgradeFromRc626 } 'exactly one'
foreach ($flag in @('Baseline023Verified','Delta024Reviewed','ServiceAccounts025Reviewed','ServiceAccounts026Reviewed')) {
    $review[$flag] = $false; Save-Review
    Assert-Refusal "False $flag" { & $helper @args025 } 'not verified'
    $review[$flag] = 'true'; Save-Review
    Assert-Refusal "String $flag" { & $helper @args025 } 'not verified'
    $review[$flag] = $true
}
$review.Source = ('0' * 40); Save-Review
Assert-Refusal 'Source mismatch' { & $helper @args025 } 'schema/source mismatch'
$review.Source = $source
$review.Files[0].Sha256 = ('0' * 64); Save-Review
Assert-Refusal 'Changed reviewed SQL bytes' { & $helper @args025 } 'hash mismatch'
$review.Files[0].Sha256 = (Get-FileHash -LiteralPath (Join-Path $root $paths[0])).Hash
$review.BaselineEvidenceReference = ''; Save-Review
Assert-Refusal 'No evidence reference' { & $helper @args025 } 'reference is missing'
$review.BaselineEvidenceReference = 'SYNTHETIC local test, not target acceptance'; Save-Review
$fixture = Join-Path $evidence 'selected-tree'
foreach ($relative in @($plan.AllFiles) + @($plan.RoleFiles)) {
    $target = Join-Path $fixture $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
Assert-Case 'Exported SQLCMD include closure resolves from migrations' {
    & $helper -RepositoryRoot $fixture -ExpectedSource $source -UpgradeFromRc626 -IncludeServiceAccounts -SqlUpgradeReview $reviewPath | Out-Null
}
$wrapper = Join-Path $fixture 'sql/migrations/025-service-accounts.sql'
$original = [IO.File]::ReadAllText($wrapper)
[IO.File]::WriteAllText($wrapper, ':r ../schema/missing.sql')
$review.Files[2].Sha256 = (Get-FileHash -LiteralPath $wrapper).Hash; Save-Review
Assert-Refusal 'Missing nested include fails before publish' {
    & $helper -RepositoryRoot $fixture -ExpectedSource $source -UpgradeFromRc626 -IncludeServiceAccounts -SqlUpgradeReview $reviewPath
} 'include is missing'
[IO.File]::WriteAllText($wrapper, $original)
$review.Files[2].Sha256 = (Get-FileHash -LiteralPath $wrapper).Hash; Save-Review
Assert-Case 'Release branch, source and clean-tree guards retained before publish' {
    $script = [IO.File]::ReadAllText((Join-Path $root 'scripts/release/New-PairedTestRelease.ps1'))
    foreach ($guard in @("Unexpected source branch.","HEAD does not match the exact reviewed release source.","Commit and verify the complete source before publishing.")) {
        if ($script.IndexOf($guard) -lt 0 -or $script.IndexOf($guard) -gt $script.IndexOf('& dotnet publish')) { throw 'Release guard missing or too late.' }
    }
}
if ($VerifySqlCmd) {
    & (Join-Path $root 'tests/sql/service-accounts/sa-upgrade-harness.ps1') -DatabaseSuffix $DatabaseSuffix `
        -EvidenceDirectory $evidence -SqlAssetRoot (Join-Path $fixture 'sql')
    $results.Add([pscustomobject]@{Case='Selected-tree SQLCMD: 024/025/026 dependencies, atomic rollback, upgrade, replay and restricted roles';Result='PASS'})
}
[IO.File]::WriteAllText((Join-Path $evidence 'results.json'), ($results | ConvertTo-Json -Depth 5))
$results
[pscustomobject]@{Passed=$results.Count;Source=$source;CorporateActions=$false;PackagesCreated=$false}
