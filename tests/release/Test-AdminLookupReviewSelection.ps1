[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Refuse existing evidence.' }
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$source=(& git -C $root rev-parse HEAD).Trim()
$selectionRoot=Join-Path $EvidenceDirectory 'source-027'
foreach ($folder in @('migrations','schema','pending/service-accounts')) {
    $target=Join-Path $selectionRoot "sql/$folder"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem (Join-Path $root "sql/$folder") -File -Filter '*.sql' | Where-Object {
        $folder -eq 'pending/service-accounts' -or [int]$_.Name.Substring(0,3) -le 27
    } | Copy-Item -Destination $target
}
$paths=@('sql/migrations/027-admin-service-account-navigation.sql','sql/schema/027-admin-service-account-navigation.sql',
    'sql/pending/service-accounts/SA-API-permissions.sql','sql/pending/service-accounts/SA-002-API-permissions.sql')
$review=[ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;Baseline026Verified=$true;AdminNavigation027Reviewed=$true;
    Baseline026EvidenceReference='isolated local 001-026; not target acceptance';AdminNavigation027ReviewReference='isolated atomic/replay harness';
    Files=@($paths | ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })}
$file=Join-Path $EvidenceDirectory 'review.json'
function Save-Review { $review | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding UTF8 }
function Select-Plan { & "$root/scripts/release/Get-ReleaseSqlPlan.ps1" -RepositoryRoot $selectionRoot -UpgradeFromInstalled026 -IncludeServiceAccounts -ExpectedSource $source -SqlUpgradeReview $file }
$checks=[Collections.Generic.List[string]]::new()
function Check([string]$Name,[bool]$Condition) { if (!$Condition) { throw "Failed: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }
function Reject([string]$Name,[scriptblock]$Action) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $Name $failed }
Save-Review
$plan=Select-Plan
Check 'exact 027 inventory accepted' ($plan.RequiredSchema -ceq '001-027')
Check 'only 027 is an executable delta' ($plan.DeltaRange -ceq '027' -and $plan.DeltaFiles.Count -eq 2 -and !(@($plan.DeltaFiles | Where-Object { $_ -notmatch '/027-' }).Count))
Check 'API references only, no Worker role' ($plan.RoleFiles.Count -eq 2 -and !(@($plan.RoleFiles | Where-Object { $_ -match 'Worker' }).Count))
Reject '027 requires explicit module selection' { & "$root/scripts/release/Get-ReleaseSqlPlan.ps1" -RepositoryRoot $root -UpgradeFromInstalled026 -ExpectedSource $source }
Reject 'legacy 026 selection cannot accept 027 inventory' { & "$root/scripts/release/Get-ReleaseSqlPlan.ps1" -RepositoryRoot $root -UpgradeFromRc626 -IncludeServiceAccounts -ExpectedSource $source -SqlUpgradeReview $file }
Reject 'mixed upgrade baselines rejected' { & "$root/scripts/release/Get-ReleaseSqlPlan.ps1" -RepositoryRoot $root -UpgradeFromRc626 -UpgradeFromInstalled026 -IncludeServiceAccounts -ExpectedSource $source -SqlUpgradeReview $file }
foreach ($flag in @('Baseline026Verified','AdminNavigation027Reviewed')) {
    $review[$flag]=$false; Save-Review; Reject "$flag false rejected" { Select-Plan }
    $review[$flag]='true'; Save-Review; Reject "$flag string rejected" { Select-Plan }
    $review[$flag]=$true
}
$review.Source='0000000000000000000000000000000000000000'; Save-Review; Reject 'source drift rejected' { Select-Plan }; $review.Source=$source
$review.AdminNavigation027ReviewReference=''; Save-Review; Reject 'missing evidence rejected' { Select-Plan }; $review.AdminNavigation027ReviewReference='isolated atomic/replay harness'
$old=$review.Files[0].Sha256; $review.Files[0].Sha256='0'*64; Save-Review; Reject 'changed SQL hash rejected' { Select-Plan }; $review.Files[0].Sha256=$old
$entries=$review.Files; $review.Files=@($entries | Select-Object -Skip 1); Save-Review; Reject 'missing hash entry rejected' { Select-Plan }; $review.Files=$entries
Save-Review
$tokens=$null; $errors=$null
[Management.Automation.Language.Parser]::ParseFile("$root/scripts/release/New-ServiceAccountsTestReview.ps1",[ref]$tokens,[ref]$errors) | Out-Null
Check 'review generator syntax parses' ($errors.Count -eq 0)
@{checks=$checks;count=$checks.Count;targetChanged=$false;packagesCreated=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'result.json') -Encoding UTF8
