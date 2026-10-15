[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory)
# Offline selector regression for the installed-032 to 037 review (Get-ReleaseSqlPlan -UpgradeFromInstalled032Through037).
# Synthetic review records only; no database connection, no target observed.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$source = (& git -C $root rev-parse HEAD).Trim()
$helper = Join-Path $root 'scripts/release/Get-ReleaseSqlPlan.ps1'
$file = Join-Path $evidence 'review.json'
$checks = [Collections.Generic.List[string]]::new()
function Check([string]$Name, [bool]$Condition) {
    if (!$Condition) { throw "Failed: $Name" }
    $checks.Add($Name); Write-Host "PASS: $Name"
}
function Reject([string]$Name, [scriptblock]$Action, [string]$Expected = '') {
    $failure = $null; try { & $Action | Out-Null } catch { $failure = $_ }
    Check $Name ($null -ne $failure -and (!$Expected -or $failure.Exception.Message -like "*$Expected*"))
}
function Save-Review { $review | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding UTF8 }
function Select-Plan { & $helper @selection }
$names = @{ 33='033-service-account-change-plans.sql'; 34='034-service-account-plan-evidence.sql';
    35='035-service-account-plan-operational-records.sql'; 36='036-service-account-events.sql'; 37='037-service-account-usage-scan-jobs.sql' }
$includes = @{ 33='SA-006-change-plans.sql'; 34='SA-007-plan-evidence.sql'; 35='SA-008-plan-operational-records.sql';
    36='SA-009-account-events.sql'; 37='SA-010-usage-scan-jobs.sql' }
$roleFiles = @{ 33=@('SA-006-API-permissions.sql'); 34=@(); 35=@('SA-008-API-permissions.sql'); 36=@('SA-009-API-permissions.sql');
    37=@('SA-010-API-permissions.sql','SA-010-Worker-permissions.sql') }
$pending = 'sql/pending/service-accounts'
$delta = @(33..37 | ForEach-Object { "sql/migrations/$($names[$_])"; "sql/schema/$($names[$_])"; "$pending/$($includes[$_])" })
$roles = @(33..37 | ForEach-Object { foreach ($r in $roleFiles[$_]) { "$pending/$r" } })
$guidance = @(33..37 | ForEach-Object { "docs/service-accounts/DBA-0$_-TR.md"; "tests/sql/service-accounts/sa-0$_-dba-checks.sql" })
$execution = @(33..37 | ForEach-Object { "sql/migrations/$($names[$_])"; foreach ($r in $roleFiles[$_]) { "$pending/$r" } })
$paths = @($delta + $roles + $guidance)
$review = [ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;Baseline032Verified=$true;Inventory025To032Verified=$true;
    Unapplied033To037Verified=$true;RuntimeApiRoleVerified=$true;RuntimeWorkerRoleVerified=$true;
    Delta033Reviewed=$true;Delta034Reviewed=$true;Delta035Reviewed=$true;Delta036Reviewed=$true;Delta037Reviewed=$true;
    InstalledInventoryEvidenceReference='SYNTHETIC offline 032 baseline; target NOT observed'}
foreach ($number in 33..37) { $review["Delta0${number}ReviewReference"] = "SYNTHETIC 0$number review" }
$review.Files = @($paths | ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })
$selection = @{RepositoryRoot=$root;ExpectedSource=$source;IncludeServiceAccounts=$true;SqlUpgradeReview=$file;UpgradeFromInstalled032Through037=$true}
Save-Review
$plan = Select-Plan
Check '033-037 schema and range' ($plan.RequiredSchema -ceq '001-037' -and $plan.DeltaRange -ceq '033-037')
Check '033-037 exact delta files' ((($plan.DeltaFiles | Sort-Object) -join '|') -ceq (($delta | Sort-Object) -join '|'))
Check '033-037 exact role candidates' ((($plan.RoleFiles) -join '|') -ceq ($roles -join '|'))
Check '033-037 execution order' (($plan.ExecutionFiles -join '|') -ceq ($execution -join '|'))
Check '033-037 guidance binds DBA notes and checks' (($plan.GuidanceFiles -join '|') -ceq ($guidance -join '|'))
Check '033-037 all files stop at 037 and keep installed includes' (@($plan.AllFiles | Where-Object { $_ -match '/03[89]-' }).Count -eq 0 -and
    @('SA-001-service-accounts.sql','SA-002-usage-rules.sql','SA-003-scope-bootstrap.sql','SA-004-usage-scans.sql','SA-005-requested-gmsa-name.sql' |
        Where-Object { $plan.AllFiles -ccontains "$pending/$_" -and $plan.DeltaFiles -cnotcontains "$pending/$_" }).Count -eq 5)
Check '033-037 never replays earlier grants' (@($plan.ExecutionFiles + $plan.RoleFiles | Where-Object { $_ -match 'SA-(API|Worker|002|004)-' }).Count -eq 0)
Check '033-037 five ordered steps' (@($plan.Steps).Count -eq 5)
foreach ($number in 33..37) {
    $step = $plan.Steps[$number-33]
    Check "step 0$number binds migration, include, roles, note and checks" ($step.migration -ceq "0$number" -and
        ($step.files -join '|') -ceq "sql/migrations/$($names[$number])|sql/schema/$($names[$number])|$pending/$($includes[$number])" -and
        ($step.execution -join '|') -ceq ((@("sql/migrations/$($names[$number])") + @($roleFiles[$number] | ForEach-Object { "$pending/$_" })) -join '|') -and
        $step.dbaNote -ceq "docs/service-accounts/DBA-0$number-TR.md" -and $step.dbaChecks -ceq "tests/sql/service-accounts/sa-0$number-dba-checks.sql")
    Check "review identity keeps 0$number reference" ($plan.Review.deltaReviews["0$number"] -ceq "SYNTHETIC 0$number review")
}
Check 'review identity is the installed 032 baseline with file hashes' ($plan.Review.installedBaseline -ceq '032' -and
    $plan.Review.sha256 -ceq (Get-FileHash -LiteralPath $file).Hash -and @($plan.Review.files).Count -eq $paths.Count)
Copy-Item -LiteralPath $file -Destination (Join-Path $evidence 'review-033-037.json')

foreach ($flag in @($review.Keys | Where-Object { $review[$_] -is [bool] })) {
    $review[$flag] = $false; Save-Review; Reject "false $flag" { Select-Plan } 'not verified'
    $review[$flag] = 'true'; Save-Review; Reject "string $flag" { Select-Plan } 'not verified'
    $review.Remove($flag); Save-Review; Reject "absent $flag" { Select-Plan }
    $review[$flag] = $true
}
foreach ($reference in @($review.Keys | Where-Object { $_ -like '*Reference' })) {
    $saved = $review[$reference]
    $review[$reference] = ' '; Save-Review; Reject "blank $reference" { Select-Plan } 'reference is missing'
    $review[$reference] = 1; Save-Review; Reject "non-string $reference" { Select-Plan } 'reference is missing'
    $review.Remove($reference); Save-Review; Reject "absent $reference" { Select-Plan }
    $review[$reference] = $saved
}
$review.Source = '0'*40; Save-Review; Reject 'stale source' { Select-Plan } 'source mismatch'; $review.Source = $source
$review.Schema = 'unknown'; Save-Review; Reject 'wrong review schema' { Select-Plan } 'schema/source mismatch'
$review.Schema = 'wasas.sql-upgrade-review.v1'
foreach ($entry in $review.Files) {
    $saved = $entry.Sha256; $entry.Sha256 = '0'*64; Save-Review
    Reject "changed hash $($entry.Path)" { Select-Plan } 'hash mismatch'; $entry.Sha256 = $saved
}
$saved = $review.Files
foreach ($index in 0..($saved.Count-1)) {
    $review.Files = @($saved | Select-Object -First $index) + @($saved | Select-Object -Skip ($index+1)); Save-Review
    Reject "missing reviewed $($saved[$index].Path)" { Select-Plan } 'exact selected'
}
$review.Files = @($saved) + @($saved[0]); Save-Review; Reject 'duplicate reviewed file' { Select-Plan } 'exact selected'
foreach ($extra in @("$pending/SA-004-API-permissions.sql","$pending/SA-API-permissions.sql","$pending/SA-Worker-permissions.sql",
    'sql/migrations/032-access-request-user-index.sql','docs/service-accounts/DBA-029-030-TR.md')) {
    $review.Files = @($saved) + @(@{Path=$extra;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $extra)).Hash}); Save-Review
    Reject "refuses replayed or extra $extra" { Select-Plan } 'exact selected'
}
$review.Files = $saved; Save-Review
Reject 'requires explicit module review' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled032Through037 } 'explicit Service Accounts'
Reject 'requires source-bound review' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled032Through037 -IncludeServiceAccounts } '033-037 requires'
foreach ($baseline in @('Rc622','Rc624','Rc626','Installed026','Installed027','Installed028','Installed032','Installed033')) {
    $mixed = $selection.Clone(); $mixed["UpgradeFrom$baseline"] = $true
    Reject "conflicts with $baseline baseline" { & $helper @mixed } 'exactly one'
}
# A review written for the separate 033 path cannot authorize 033-037, nor the other way round.
$single = [ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;Baseline032Verified=$true;Inventory025To033Verified=$true;
    Unapplied033Verified=$true;RuntimeApiRoleVerified=$true;Delta033Reviewed=$true;
    InstalledInventoryEvidenceReference='SYNTHETIC';Delta033ReviewReference='SYNTHETIC 033 only';
    Files=@(@("sql/migrations/$($names[33])","sql/schema/$($names[33])","$pending/$($includes[33])","$pending/SA-006-API-permissions.sql") |
        ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })}
$singleFile = Join-Path $evidence 'review-033-only.json'
$single | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $singleFile -Encoding UTF8
$narrow = $selection.Clone(); $narrow.SqlUpgradeReview = $singleFile
Reject '033-only review cannot authorize 033-037' { & $helper @narrow }
$wide = $selection.Clone(); $wide.Remove('UpgradeFromInstalled032Through037'); $wide.UpgradeFromInstalled032 = $true
Reject '033-037 review cannot authorize the separate 033 path' { & $helper @wide }
$separate = $wide.Clone(); $separate.SqlUpgradeReview = $singleFile
$plan033 = & $helper @separate
Check 'separate 033 path still selects only 033 with 035-037 in source' ($plan033.RequiredSchema -ceq '001-033' -and
    @($plan033.AllFiles + $plan033.DeltaFiles + $plan033.RoleFiles + $plan033.ExecutionFiles | Where-Object { $_ -match '/03[4-7]-|/SA-0(07|08|09|10)-' }).Count -eq 0 -and
    !$plan033.PSObject.Properties['Steps'])

$fixture = Join-Path $evidence 'fixture'
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'sql') -Destination (Join-Path $fixture 'sql') -Recurse
foreach ($relative in $guidance) {
    New-Item -ItemType Directory -Path (Split-Path (Join-Path $fixture $relative) -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination (Join-Path $fixture $relative)
}
$current = Select-Plan
$selection.RepositoryRoot = $fixture
Check 'fixture copy selects the same plan' (((Select-Plan) | ConvertTo-Json -Depth 9) -ceq ($current | ConvertTo-Json -Depth 9))
foreach ($probe in @(
    @{Wrapper="sql/schema/$($names[35])";Line=":r ../pending/service-accounts/SA-005-requested-gmsa-name.sql";Name='cannot replay installed 031 include';Error='replay an installed'},
    @{Wrapper="sql/schema/$($names[37])";Line=":r ../pending/service-accounts/missing.sql";Name='missing include';Error='include is missing'},
    @{Wrapper="sql/schema/$($names[36])";Line=":r ../../../../outside.sql";Name='escaping include';Error='escapes repository'})) {
    $entry = @($review.Files | Where-Object Path -CEQ $probe.Wrapper)[0]
    $original = [IO.File]::ReadAllBytes((Join-Path $fixture $probe.Wrapper)); $hash = $entry.Sha256
    [IO.File]::WriteAllText((Join-Path $fixture $probe.Wrapper), $probe.Line)
    $entry.Sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $probe.Wrapper)).Hash; Save-Review
    Reject $probe.Name { Select-Plan } $probe.Error
    [IO.File]::WriteAllBytes((Join-Path $fixture $probe.Wrapper), $original); $entry.Sha256 = $hash; Save-Review
}
foreach ($relative in $delta + $roles + $guidance) {
    $asset = Join-Path $fixture $relative
    Move-Item -LiteralPath $asset -Destination "$asset.absent"
    Reject "missing physical asset $relative" { Select-Plan }
    Move-Item -LiteralPath "$asset.absent" -Destination $asset
}
foreach ($folder in @('migrations','schema')) {
    foreach ($number in 33..37) {
        $asset = Join-Path $fixture "sql/$folder/$($names[$number])"; $unknown = Join-Path $fixture "sql/$folder/0$number-unknown.sql"
        Move-Item -LiteralPath $asset -Destination $unknown
        Reject "unknown $folder 0$number name" { Select-Plan } "Unexpected 0$number"
        Move-Item -LiteralPath $unknown -Destination $asset
    }
    $asset = Join-Path $fixture "sql/$folder/$($names[36])"
    Move-Item -LiteralPath $asset -Destination "$asset.absent"
    Reject "gap at 036 in $folder refused" { Select-Plan } 'exact complete'
    Move-Item -LiteralPath "$asset.absent" -Destination $asset
}
# A source that ends at 034 (no 035-037) cannot satisfy the 037 selection.
$short = Join-Path $evidence 'fixture-034'
Copy-Item -LiteralPath $fixture -Destination $short -Recurse
foreach ($folder in @('migrations','schema')) { foreach ($number in 35..37) { Remove-Item -LiteralPath (Join-Path $short "sql/$folder/$($names[$number])") } }
$shortSelection = $selection.Clone(); $shortSelection.RepositoryRoot = $short
Reject 'source ending at 034 refused' { & $helper @shortSelection } 'exact complete'
# 038 and later are never recognized, for this path or any installed baseline.
[IO.File]::WriteAllText((Join-Path $fixture 'sql/migrations/038-unreviewed.sql'), 'SELECT 1;')
Reject '033-037 refuses future 038' { Select-Plan } 'exact complete'
foreach ($number in @('028','032','033')) {
    $future = @{RepositoryRoot=$fixture;ExpectedSource=$source;IncludeServiceAccounts=$true;SqlUpgradeReview=$file}
    $future["UpgradeFromInstalled$number"] = $true
    Reject "$number refuses future 038" { & $helper @future } 'exact complete'
}
Remove-Item -LiteralPath (Join-Path $fixture 'sql/migrations/038-unreviewed.sql')
[IO.File]::WriteAllText((Join-Path $fixture 'sql/schema/038-unreviewed.sql'), 'SELECT 1;')
Reject '033-037 refuses future 038 schema' { Select-Plan } 'exact complete'
[ordered]@{checks=$checks;count=$checks.Count;source=$source;readyForInstallation=$false;databaseConnections=$false;
    corporateActions=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
Write-Host "033-037 selector checks passed: $($checks.Count)"
