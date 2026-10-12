[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$BaselineRepositoryRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$baselineRoot = (Resolve-Path -LiteralPath $BaselineRepositoryRoot).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$source = (& git -C $root rev-parse HEAD).Trim()
$baselineSource = (& git -C $baselineRoot rev-parse HEAD).Trim()
$helper = Join-Path $root 'scripts/release/Get-ReleaseSqlPlan.ps1'
$baselineHelper = Join-Path $baselineRoot 'scripts/release/Get-ReleaseSqlPlan.ps1'
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
function Same-Bytes([string]$Left, [string]$Right) {
    return [Convert]::ToBase64String([IO.File]::ReadAllBytes($Left)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($Right))
}
function Export-SqlCandidate($Plan, [string]$SourceRoot, [string]$Destination) {
    $identities = @($Plan.DeltaFiles + $Plan.RoleFiles | Sort-Object | ForEach-Object {
        $target = Join-Path $Destination $_
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $SourceRoot $_) -Destination $target
        [ordered]@{path=$_;bytes=(Get-Item -LiteralPath $target).Length;sha256=(Get-FileHash -LiteralPath $target).Hash}
    })
    $record = [ordered]@{kind='SYNTHETIC offline SQL selection evidence';readyForInstallation=$false;
        source=$source;plan=$Plan;files=$identities;targetChanged=$false}
    [IO.File]::WriteAllText((Join-Path $Destination 'candidate.json'), ($record | ConvertTo-Json -Depth 9), [Text.UTF8Encoding]::new($false))
}
foreach ($number in @('028','032')) {
    $paths = if ($number -ceq '028') {
        @(Get-ChildItem (Join-Path $root 'sql/migrations'),(Join-Path $root 'sql/schema') -File -Filter '*.sql' |
            Where-Object { [int]$_.Name.Substring(0,3) -ge 29 -and [int]$_.Name.Substring(0,3) -le 32 } |
            ForEach-Object { $_.FullName.Substring($root.Length+1).Replace('\','/') }) +
            @('sql/pending/service-accounts/SA-003-scope-bootstrap.sql','sql/pending/service-accounts/SA-004-usage-scans.sql',
                'sql/pending/service-accounts/SA-005-requested-gmsa-name.sql','sql/pending/service-accounts/SA-004-API-permissions.sql')
    } else {
        @('sql/migrations/033-service-account-change-plans.sql','sql/schema/033-service-account-change-plans.sql',
            'sql/pending/service-accounts/SA-006-change-plans.sql','sql/pending/service-accounts/SA-006-API-permissions.sql')
    }
    $review = [ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;RuntimeApiRoleVerified=$true;
        InstalledInventoryEvidenceReference='SYNTHETIC offline baseline; target NOT observed';
        Files=@($paths | ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })}
    $review["Baseline${number}Verified"] = $true
    if ($number -ceq '028') {
        $review.Inventory025To032Verified = $true; $review.Unapplied029To032Verified = $true; $review.Delta029To032Reviewed = $true
        $review.Delta029To032ReviewReference = 'SYNTHETIC unchanged 029-032 bytes'
    } else {
        $review.Inventory025To033Verified = $true; $review.Unapplied033Verified = $true; $review.Delta033Reviewed = $true
        $review.Delta033ReviewReference = 'SYNTHETIC unchanged 033 bytes'
    }
    Save-Review
    $selection = @{RepositoryRoot=$root;ExpectedSource=$source;IncludeServiceAccounts=$true;SqlUpgradeReview=$file}
    $selection["UpgradeFromInstalled$number"] = $true
    $oldSelection = $selection.Clone(); $oldSelection.RepositoryRoot = $baselineRoot
    $old = & $baselineHelper @oldSelection
    $plan = Select-Plan
    Check "$number complete plan unchanged with 034 in source" (($old | ConvertTo-Json -Depth 9) -ceq ($plan | ConvertTo-Json -Depth 9))
    Check "$number excludes every 034/SA-007 asset" (@($plan.AllFiles + $plan.DeltaFiles + $plan.RoleFiles + $plan.ExecutionFiles |
        Where-Object { $_ -match '/034-|/SA-007-' }).Count -eq 0)
    $before = Join-Path $evidence "before-$number"; $after = Join-Path $evidence "after-$number"
    Export-SqlCandidate $old $baselineRoot $before
    Export-SqlCandidate $plan $root $after
    Check "$number exact exported file set" (@(Get-ChildItem -LiteralPath $after -File -Recurse).Count -eq $paths.Count + 1)
    foreach ($relative in @('candidate.json') + @($paths | Sort-Object)) {
        Check "$number identical bytes $relative" (Same-Bytes (Join-Path $before $relative) (Join-Path $after $relative))
    }
    Copy-Item -LiteralPath $file -Destination (Join-Path $evidence "review-$number.json")
}
$paths034 = @('sql/migrations/034-service-account-plan-evidence.sql','sql/schema/034-service-account-plan-evidence.sql',
    'sql/pending/service-accounts/SA-007-plan-evidence.sql')
$review = [ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;Baseline033Verified=$true;
    Inventory025To034Verified=$true;Unapplied034Verified=$true;RuntimeApiRoleVerified=$true;Delta034Reviewed=$true;
    InstalledInventoryEvidenceReference='SYNTHETIC offline 033 baseline; target NOT observed';
    Delta034ReviewReference='SYNTHETIC separate plan evidence selection';
    Files=@($paths034 | ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })}
$selection = @{RepositoryRoot=$root;ExpectedSource=$source;IncludeServiceAccounts=$true;SqlUpgradeReview=$file;UpgradeFromInstalled033=$true}
Save-Review
$plan034 = Select-Plan
Check '034 separate exact files, schema and range' ($plan034.RequiredSchema -ceq '001-034' -and $plan034.DeltaRange -ceq '034' -and
    (($plan034.DeltaFiles | Sort-Object) -join '|') -ceq (($paths034 | Sort-Object) -join '|'))
Check '034 executes only migration 034 without grants' ($plan034.RoleFiles.Count -eq 0 -and
    ($plan034.ExecutionFiles -join '|') -ceq $paths034[0] -and $plan034.Review.installedBaseline -ceq '033')
$separate = Join-Path $evidence 'separate-034'
Export-SqlCandidate $plan034 $root $separate
$candidate = Get-Content -LiteralPath (Join-Path $separate 'candidate.json') -Raw | ConvertFrom-Json
Check '034 evidence remains not ready for installation' ($candidate.readyForInstallation -is [bool] -and !$candidate.readyForInstallation)
foreach ($relative in $paths034) {
    Check "034 exported bytes $relative" (Same-Bytes (Join-Path $root $relative) (Join-Path $separate $relative))
}
foreach ($flag in @($review.Keys | Where-Object { $review[$_] -is [bool] })) {
    $review[$flag] = $false; Save-Review; Reject "034 false $flag" { Select-Plan } 'not verified'
    $review[$flag] = 'true'; Save-Review; Reject "034 string $flag" { Select-Plan } 'not verified'
    $review.Remove($flag); Save-Review; Reject "034 absent $flag" { Select-Plan }
    $review[$flag] = $true
}
foreach ($reference in @('InstalledInventoryEvidenceReference','Delta034ReviewReference')) {
    $saved = $review[$reference]
    $review[$reference] = ''; Save-Review; Reject "034 empty $reference" { Select-Plan } 'reference is missing'
    $review[$reference] = 1; Save-Review; Reject "034 non-string $reference" { Select-Plan } 'reference is missing'
    $review.Remove($reference); Save-Review; Reject "034 absent $reference" { Select-Plan }
    $review[$reference] = $saved
}
$review.Source = '0'*40; Save-Review; Reject '034 stale source' { Select-Plan } 'source mismatch'; $review.Source = $source
$review.Schema = 'unknown'; Save-Review; Reject '034 wrong review schema' { Select-Plan } 'schema/source mismatch'
$review.Schema = 'wasas.sql-upgrade-review.v1'
foreach ($entry in $review.Files) {
    $saved = $entry.Sha256; $entry.Sha256 = '0'*64; Save-Review
    Reject "034 changed hash $($entry.Path)" { Select-Plan } 'hash mismatch'; $entry.Sha256 = $saved
}
$saved = $review.Files
$review.Files = @($saved | Select-Object -Skip 1); Save-Review; Reject '034 missing reviewed dependency' { Select-Plan } 'exact selected'
$review.Files = @($saved) + @($saved[0]); Save-Review; Reject '034 duplicate reviewed file' { Select-Plan } 'exact selected'
$review.Files = @($saved) + @(@{Path='sql/pending/service-accounts/SA-006-API-permissions.sql';Sha256='0'*64})
Save-Review; Reject '034 refuses replayed 033 grants' { Select-Plan } 'exact selected'
$review.Files = $saved; Save-Review
Copy-Item -LiteralPath $file -Destination (Join-Path $evidence 'review-034.json')
Reject '034 requires explicit module review' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled033 } 'explicit Service Accounts'
Reject '034 requires source-bound review' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled033 -IncludeServiceAccounts } '034 requires'
foreach ($baseline in @('Rc622','Rc624','Rc626','Installed026','Installed027','Installed028','Installed032')) {
    $mixed = $selection.Clone(); $mixed["UpgradeFrom$baseline"] = $true
    Reject "034 conflicts with $baseline baseline" { & $helper @mixed } 'exactly one'
}
foreach ($number in @('028','032')) {
    $older = $selection.Clone(); $older.Remove('UpgradeFromInstalled033'); $older["UpgradeFromInstalled$number"] = $true
    Reject "034 review cannot authorize $number baseline" { & $helper @older }
    $earlierReview = $selection.Clone(); $earlierReview.SqlUpgradeReview = Join-Path $evidence "review-$number.json"
    Reject "034 before 033 refused with $number review" { & $helper @earlierReview }
}
Reject '034 refuses source ending at 033' { & $helper -RepositoryRoot $baselineRoot -ExpectedSource $source -UpgradeFromInstalled033 -IncludeServiceAccounts -SqlUpgradeReview $file } 'exact complete'
$fixture = Join-Path $evidence 'fixture'
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'sql') -Destination (Join-Path $fixture 'sql') -Recurse
$selection.RepositoryRoot = $fixture
$wrapper = $paths034[1]
$entry = @($review.Files | Where-Object Path -CEQ $wrapper)[0]
$original = [IO.File]::ReadAllBytes((Join-Path $fixture $wrapper))
foreach ($probe in @(
    @{Line=':r ../pending/service-accounts/SA-006-change-plans.sql';Name='034 cannot replay installed 033 include';Error='replay an installed'},
    @{Line=':r ../pending/service-accounts/missing.sql';Name='034 missing include';Error='include is missing'},
    @{Line=':r ../../../../outside.sql';Name='034 escaping include';Error='escapes repository'})) {
    [IO.File]::WriteAllText((Join-Path $fixture $wrapper), $probe.Line)
    $entry.Sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $wrapper)).Hash; Save-Review
    Reject $probe.Name { Select-Plan } $probe.Error
}
[IO.File]::WriteAllBytes((Join-Path $fixture $wrapper), $original)
$entry.Sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $wrapper)).Hash; Save-Review
foreach ($relative in $paths034) {
    $asset = Join-Path $fixture $relative
    Move-Item -LiteralPath $asset -Destination "$asset.absent"
    Reject "034 missing physical asset $relative" { Select-Plan }
    Move-Item -LiteralPath "$asset.absent" -Destination $asset
}
foreach ($folder in @('migrations','schema')) {
    foreach ($name in @('033-service-account-change-plans.sql','034-service-account-plan-evidence.sql')) {
        $asset = Join-Path $fixture "sql/$folder/$name"; $unknown = Join-Path $fixture "sql/$folder/$($name.Substring(0,3))-unknown.sql"
        Move-Item -LiteralPath $asset -Destination $unknown
        Reject "034 unknown $folder $name" { Select-Plan } "Unexpected $($name.Substring(0,3))"
        Move-Item -LiteralPath $unknown -Destination $asset
    }
    $asset = Join-Path $fixture "sql/$folder/033-service-account-change-plans.sql"
    Move-Item -LiteralPath $asset -Destination "$asset.absent"
    Reject "034 before 033 refused with missing $folder prerequisite" { Select-Plan } 'exact complete'
    Move-Item -LiteralPath "$asset.absent" -Destination $asset
}
[IO.File]::WriteAllText((Join-Path $fixture 'sql/migrations/035-unreviewed.sql'), 'SELECT 1;')
foreach ($number in @('028','032','033')) {
    $future = @{RepositoryRoot=$fixture;ExpectedSource=$source;IncludeServiceAccounts=$true;SqlUpgradeReview=$file}
    $future["UpgradeFromInstalled$number"] = $true
    Reject "$number refuses future 035" { & $helper @future } 'exact complete'
}
[ordered]@{checks=$checks;count=$checks.Count;baselineSource=$baselineSource;source=$source;
    old028And033PlansAndSqlBytesIdentical=$true;readyForInstallation=$false;databaseConnections=$false;
    corporateActions=$false;fullApiUiPackagesCreated=$false} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
