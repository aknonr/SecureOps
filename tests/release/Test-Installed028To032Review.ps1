[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$VerifySqlCmd,
    [ValidatePattern('^[A-Za-z0-9_]{1,25}$')][string]$DatabaseSuffix)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Refuse existing evidence.' }
if ($VerifySqlCmd -and !$DatabaseSuffix) { throw 'Fresh synthetic database suffix required.' }
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$source=(& git -C $root rev-parse HEAD).Trim()
$helper=Join-Path $root 'scripts/release/Get-ReleaseSqlPlan.ps1'
$file=Join-Path $EvidenceDirectory 'review.json'
$checks=[Collections.Generic.List[string]]::new()
function Check([string]$Name,[bool]$Condition) { if (!$Condition) { throw "Failed: $Name" }; $checks.Add($Name); Write-Host "PASS: $Name" }
function Reject([string]$Name,[scriptblock]$Action,[string]$Expected) {
    $message=''; try { & $Action | Out-Null } catch { $message=$_.Exception.Message }
    Check $Name ($message -like "*$Expected*")
}
function Save-Review { $review | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding UTF8 }
function Select-Plan { & $helper @selection }
$tail=@('sql/migrations/029-service-account-scope-bootstrap.sql','sql/migrations/030-service-account-usage-scans.sql',
    'sql/pending/service-accounts/SA-004-API-permissions.sql','sql/migrations/031-service-account-requested-gmsa-name.sql',
    'sql/migrations/032-access-request-user-index.sql')
foreach ($baseline in @(27,28)) {
    $number='{0:D3}' -f $baseline
    $selection=@{RepositoryRoot=$root;ExpectedSource=$source;IncludeServiceAccounts=$true;SqlUpgradeReview=$file}
    $selection["UpgradeFromInstalled$number"]=$true
    $paths=@(Get-ChildItem (Join-Path $root 'sql/migrations'),(Join-Path $root 'sql/schema') -File -Filter '*.sql' |
        Where-Object { [int]$_.Name.Substring(0,3) -gt $baseline } |
        ForEach-Object { $_.FullName.Substring($root.Length+1).Replace('\','/') })
    $paths+=@('sql/pending/service-accounts/SA-003-scope-bootstrap.sql','sql/pending/service-accounts/SA-004-usage-scans.sql',
        'sql/pending/service-accounts/SA-005-requested-gmsa-name.sql','sql/pending/service-accounts/SA-004-API-permissions.sql')
    $review=[ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;
        Inventory025To032Verified=$true;Unapplied029To032Verified=$true;RuntimeApiRoleVerified=$true;Delta029To032Reviewed=$true;
        InstalledInventoryEvidenceReference="SYNTHETIC LocalDB baseline $number; target NOT observed";
        Delta029To032ReviewReference='SYNTHETIC ordered SQLCMD and replay checks';
        Files=@($paths | ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })}
    $review["Baseline${number}Verified"]=$true
    if ($baseline -eq 27) {
        $review.Baseline028AbsentVerified=$true; $review.AdminOperations028Reviewed=$true
        $review.AdminOperations028ReviewReference='SYNTHETIC 028 capability/audit comparison'
    }
    Save-Review
    $plan=Select-Plan
    $expected=if ($baseline -eq 27) { @('sql/migrations/028-admin-service-account-operations.sql') + $tail } else { $tail }
    Check "$number exact execution order" (($plan.ExecutionFiles -join '|') -ceq ($expected -join '|'))
    Check "$number no installed migration replay or old grants" ($plan.RequiredSchema -ceq '001-032' -and
        @($plan.DeltaFiles | Where-Object { $_ -match '/0(2[0-7]|28)-' -and [int]([IO.Path]::GetFileName($_).Substring(0,3)) -le $baseline }).Count -eq 0 -and
        ($plan.RoleFiles -join '|') -ceq 'sql/pending/service-accounts/SA-004-API-permissions.sql')
    foreach ($flag in @($review.Keys | Where-Object { $review[$_] -is [bool] })) {
        $review[$flag]=$false; Save-Review; Reject "$number false $flag" { Select-Plan } 'not verified'
        $review[$flag]='true'; Save-Review; Reject "$number string $flag" { Select-Plan } 'not verified'
        $review[$flag]=$true
    }
    $review.Source='0'*40; Save-Review; Reject "$number source drift" { Select-Plan } 'source mismatch'; $review.Source=$source
    $review.InstalledInventoryEvidenceReference=''; Save-Review; Reject "$number missing inventory" { Select-Plan } 'reference is missing'
    $review.InstalledInventoryEvidenceReference='SYNTHETIC only'
    $hash=$review.Files[0].Sha256; $review.Files[0].Sha256='0'*64; Save-Review
    Reject "$number stale hash" { Select-Plan } 'hash mismatch'; $review.Files[0].Sha256=$hash
    $entries=$review.Files; $review.Files=@($entries | Select-Object -Skip 1); Save-Review
    Reject "$number missing include/hash" { Select-Plan } 'exact selected'; $review.Files=$entries
    Save-Review
    Copy-Item $file (Join-Path $EvidenceDirectory "review-$number.json")
    if ($baseline -eq 28) {
        $fixtureRoot=Join-Path $EvidenceDirectory 'fixture'
        New-Item -ItemType Directory $fixtureRoot | Out-Null
        Copy-Item (Join-Path $root 'sql') (Join-Path $fixtureRoot 'sql') -Recurse
        $selection.RepositoryRoot=$fixtureRoot
        $wrapper='sql/schema/029-service-account-scope-bootstrap.sql'
        $entry=@($review.Files | Where-Object Path -CEQ $wrapper)[0]
        $original=[IO.File]::ReadAllText((Join-Path $root $wrapper))
        [IO.File]::WriteAllText((Join-Path $fixtureRoot $wrapper), ':r ../pending/service-accounts/SA-001-service-accounts.sql')
        $entry.Sha256=(Get-FileHash (Join-Path $fixtureRoot $wrapper)).Hash; Save-Review
        Reject 'installed include cannot enter executable delta' { Select-Plan } 'replay an installed dependency'
        [IO.File]::WriteAllText((Join-Path $fixtureRoot $wrapper), ':r ../../../../outside.sql')
        $entry.Sha256=(Get-FileHash (Join-Path $fixtureRoot $wrapper)).Hash; Save-Review
        Reject 'include cannot escape source' { Select-Plan } 'escapes repository'
        [IO.File]::WriteAllText((Join-Path $fixtureRoot $wrapper), $original)
        $entry.Sha256=(Get-FileHash (Join-Path $root $wrapper)).Hash; Save-Review
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'sql/migrations/033-unreviewed.sql'), 'SELECT 1;')
        Reject 'future migration cannot be silently accepted' { Select-Plan } 'exact complete'
        $selection.RepositoryRoot=$root
    }
    if ($VerifySqlCmd) {
        & "$PSScriptRoot/Test-Release032SqlRehearsal.ps1" -DatabaseSuffix "${DatabaseSuffix}$number" -Baseline $baseline `
            -SqlPlan $plan -EvidenceDirectory $EvidenceDirectory
        Check "$number fresh LocalDB sequence, inventory and replay" ($LASTEXITCODE -eq 0)
    }
}
Reject 'mixed baselines' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled027 -UpgradeFromInstalled028 -IncludeServiceAccounts } 'exactly one'
Reject 'implicit module review' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled028 } 'explicit Service Accounts'
Reject 'legacy refuses 032 source' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled026 -IncludeServiceAccounts -SqlUpgradeReview $file } 'exact complete'
Reject 'review generator cannot resume new path' {
    & "$root/scripts/release/New-ServiceAccountsTestReview.ps1" -FromExactSource -ApiUiOnly -UpgradeFromInstalled028 `
        -ResumeFailedReview -ExpectedSource $source -TestedProductSource $source -SqlUpgradeReview $file -OutputDirectory (Join-Path $EvidenceDirectory 'resume')
} 'fresh output'
Reject 'review generator requires ExpectedSource' {
    & "$root/scripts/release/New-ServiceAccountsTestReview.ps1" -FromExactSource -ApiUiOnly -UpgradeFromInstalled028 `
        -TestedProductSource $source -SqlUpgradeReview $file -OutputDirectory (Join-Path $EvidenceDirectory 'missing-source')
} 'ExpectedSource'
Reject 'review generator rejects preparation drift' {
    & "$root/scripts/release/New-ServiceAccountsTestReview.ps1" -FromExactSource -ApiUiOnly -UpgradeFromInstalled028 `
        -ExpectedSource ('0'*40) -TestedProductSource $source -SqlUpgradeReview $file -OutputDirectory (Join-Path $EvidenceDirectory 'source-drift')
} 'exact preparation source'
Reject 'paired review rejects legacy switches' {
    & "$root/scripts/release/New-PairedTestRelease.ps1" -ReviewCandidate -UpgradeFromInstalled028 -UpgradeFromRc626 -IncludeServiceAccounts `
        -ExpectedSource $source -TestedProductSource $source -SqlUpgradeReview $file -OutputDirectory (Join-Path $EvidenceDirectory 'mixed')
} 'exactly one'
foreach ($script in @('Get-ReleaseSqlPlan','New-ServiceAccountsTestReview','New-PairedTestRelease')) {
    $tokens=$null; $errors=$null
    [Management.Automation.Language.Parser]::ParseFile("$root/scripts/release/$script.ps1",[ref]$tokens,[ref]$errors) | Out-Null
    Check "$script syntax" ($errors.Count -eq 0)
}
@{checks=$checks;count=$checks.Count;corporateActions=$false;packagesCreated=$false} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'result.json') -Encoding UTF8
