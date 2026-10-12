[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$BaselineRepositoryRoot,
    [string]$ExistingCandidateDirectory)
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
function Reject([string]$Name, [scriptblock]$Action, [string]$Expected) {
    $message = ''; try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Check $Name ($message -like "*$Expected*")
}
function Save-Review { $review | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding UTF8 }
function Select-Plan { & $helper @selection }
function Same-Bytes([string]$Left, [string]$Right) {
    return [Convert]::ToBase64String([IO.File]::ReadAllBytes($Left)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($Right))
}
function Export-SqlCandidate($Plan, [string]$SourceRoot, [string]$Destination) {
    $files = @($Plan.DeltaFiles + $Plan.RoleFiles | Sort-Object)
    $identities = @($files | ForEach-Object {
        $target = Join-Path $Destination $_
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $SourceRoot $_) -Destination $target
        [ordered]@{path=$_;bytes=(Get-Item -LiteralPath $target).Length;sha256=(Get-FileHash -LiteralPath $target).Hash}
    })
    $record = [ordered]@{kind='SYNTHETIC offline SQL selection evidence';readyForInstallation=$false;
        source=$source;plan=$Plan;files=$identities;targetChanged=$false}
    [IO.File]::WriteAllText((Join-Path $Destination 'candidate.json'), ($record | ConvertTo-Json -Depth 9), [Text.UTF8Encoding]::new($false))
}
$paths = @('sql/migrations/029-service-account-scope-bootstrap.sql','sql/schema/029-service-account-scope-bootstrap.sql',
    'sql/migrations/030-service-account-usage-scans.sql','sql/schema/030-service-account-usage-scans.sql',
    'sql/migrations/031-service-account-requested-gmsa-name.sql','sql/schema/031-service-account-requested-gmsa-name.sql',
    'sql/migrations/032-access-request-user-index.sql','sql/schema/032-access-request-user-index.sql',
    'sql/pending/service-accounts/SA-003-scope-bootstrap.sql','sql/pending/service-accounts/SA-004-usage-scans.sql',
    'sql/pending/service-accounts/SA-005-requested-gmsa-name.sql','sql/pending/service-accounts/SA-004-API-permissions.sql')
$review = [ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;Baseline028Verified=$true;
    Inventory025To032Verified=$true;Unapplied029To032Verified=$true;RuntimeApiRoleVerified=$true;Delta029To032Reviewed=$true;
    InstalledInventoryEvidenceReference='SYNTHETIC offline baseline; target NOT observed';
    Delta029To032ReviewReference='SYNTHETIC SQL candidate byte comparison';
    Files=@($paths | ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })}
Save-Review
$selection = @{RepositoryRoot=$root;ExpectedSource=$source;IncludeServiceAccounts=$true;SqlUpgradeReview=$file;UpgradeFromInstalled028=$true}
$old = & $baselineHelper -RepositoryRoot $baselineRoot -ExpectedSource $source -IncludeServiceAccounts -SqlUpgradeReview $file -UpgradeFromInstalled028
$plan = Select-Plan
Check '028 complete plan unchanged with 033 in source' (($old | ConvertTo-Json -Depth 9) -ceq ($plan | ConvertTo-Json -Depth 9))
Check '028 excludes every 033/SA-006 asset' (@($plan.AllFiles + $plan.DeltaFiles + $plan.RoleFiles + $plan.ExecutionFiles | Where-Object { $_ -match '/033-|/SA-006-' }).Count -eq 0)
$before = Join-Path $evidence 'before-029-032'
$after = Join-Path $evidence 'after-029-032'
Export-SqlCandidate $old $baselineRoot $before
Export-SqlCandidate $plan $root $after
$relativeFiles = @('candidate.json') + @($paths | Sort-Object)
Check '028 exported candidate has exact old file set' (@(Get-ChildItem -LiteralPath $after -File -Recurse).Count -eq $relativeFiles.Count)
foreach ($relative in $relativeFiles) {
    Check "028 identical bytes $relative" (Same-Bytes (Join-Path $before $relative) (Join-Path $after $relative))
}
if ($ExistingCandidateDirectory) {
    $existingRoot = (Resolve-Path -LiteralPath $ExistingCandidateDirectory).Path
    $existing = Get-Content -LiteralPath (Join-Path $existingRoot 'candidate.json') -Raw | ConvertFrom-Json
    $existingSql = @($existing.supportingFiles | Where-Object path -Like 'DBA/sql/*' | Sort-Object path)
    Check '028 retained candidate exact SQL file set' (($existingSql.path -join '|') -ceq
        (($paths | Sort-Object | ForEach-Object { "DBA/$_" }) -join '|'))
    Check '028 retained candidate schema, sequence and readiness' ($existing.requiredSchema -ceq $plan.RequiredSchema -and
        ($existing.sqlSequence -join '|') -ceq ($plan.ExecutionFiles -join '|') -and
        $existing.readyForInstallation -is [bool] -and !$existing.readyForInstallation)
    foreach ($identity in $existingSql) {
        $oldFile = Join-Path $existingRoot $identity.path
        $newFile = Join-Path $after $identity.path.Substring(4)
        Check "028 retained candidate identical bytes $($identity.path)" ((Same-Bytes $oldFile $newFile) -and
            (Get-FileHash -LiteralPath $oldFile).Hash -ceq $identity.sha256 -and (Get-Item -LiteralPath $oldFile).Length -eq $identity.bytes)
    }
}
Copy-Item -LiteralPath $file -Destination (Join-Path $evidence 'review-028.json')
$paths033 = @('sql/migrations/033-service-account-change-plans.sql','sql/schema/033-service-account-change-plans.sql',
    'sql/pending/service-accounts/SA-006-change-plans.sql','sql/pending/service-accounts/SA-006-API-permissions.sql')
$review = [ordered]@{Schema='wasas.sql-upgrade-review.v1';Source=$source;Baseline032Verified=$true;
    Inventory025To033Verified=$true;Unapplied033Verified=$true;RuntimeApiRoleVerified=$true;Delta033Reviewed=$true;
    InstalledInventoryEvidenceReference='SYNTHETIC offline 032 baseline; target NOT observed';
    Delta033ReviewReference='SYNTHETIC separate change-plan selection';
    Files=@($paths033 | ForEach-Object { @{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })}
$selection.Remove('UpgradeFromInstalled028')
$selection.UpgradeFromInstalled032 = $true
Save-Review
$plan033 = Select-Plan
Check '033 separate exact files and range' ($plan033.RequiredSchema -ceq '001-033' -and $plan033.DeltaRange -ceq '033' -and
    (($plan033.DeltaFiles + $plan033.RoleFiles | Sort-Object) -join '|') -ceq (($paths033 | Sort-Object) -join '|'))
Check '033 exact execution order and review baseline' (($plan033.ExecutionFiles -join '|') -ceq
    'sql/migrations/033-service-account-change-plans.sql|sql/pending/service-accounts/SA-006-API-permissions.sql' -and $plan033.Review.installedBaseline -ceq '032')
$separate = Join-Path $evidence 'separate-033'
Export-SqlCandidate $plan033 $root $separate
$candidate = Get-Content -LiteralPath (Join-Path $separate 'candidate.json') -Raw | ConvertFrom-Json
Check '033 separate evidence remains not ready for installation' ($candidate.readyForInstallation -is [bool] -and !$candidate.readyForInstallation)
foreach ($flag in @($review.Keys | Where-Object { $review[$_] -is [bool] })) {
    $review[$flag] = $false; Save-Review; Reject "033 false $flag" { Select-Plan } 'not verified'
    $review[$flag] = 'true'; Save-Review; Reject "033 string $flag" { Select-Plan } 'not verified'
    $review[$flag] = $true
    $review.Remove($flag); Save-Review; Reject "033 absent $flag" { Select-Plan } $flag
    $review[$flag] = $true
}
foreach ($reference in @('InstalledInventoryEvidenceReference','Delta033ReviewReference')) {
    $saved = $review[$reference]
    $review[$reference] = ''; Save-Review; Reject "033 missing $reference" { Select-Plan } 'reference is missing'
    $review[$reference] = 1; Save-Review; Reject "033 non-string $reference" { Select-Plan } 'reference is missing'
    $review[$reference] = $saved
}
$review.Source = '0'*40; Save-Review; Reject '033 stale source' { Select-Plan } 'source mismatch'; $review.Source = $source
foreach ($entry in $review.Files) {
    $saved = $entry.Sha256; $entry.Sha256 = '0'*64; Save-Review
    Reject "033 changed hash $($entry.Path)" { Select-Plan } 'hash mismatch'; $entry.Sha256 = $saved
}
$saved = $review.Files
$review.Files = @($saved | Select-Object -Skip 1); Save-Review
Reject '033 missing reviewed dependency' { Select-Plan } 'exact selected'
$review.Files = @($saved) + @($saved[0]); Save-Review
Reject '033 duplicate reviewed file' { Select-Plan } 'exact selected'
$review.Files = @($saved) + @(@{Path=$paths[0];Sha256=(Get-FileHash -LiteralPath (Join-Path $root $paths[0])).Hash}); Save-Review
Reject '033 extra installed file' { Select-Plan } 'exact selected'
$review.Files = $saved; Save-Review
Copy-Item -LiteralPath $file -Destination (Join-Path $evidence 'review-033.json')
Reject '033 requires explicit module review' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled032 } 'explicit Service Accounts'
Reject '033 requires source-bound review' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled032 -IncludeServiceAccounts } '033 requires'
Reject '033 conflicts with 028 baseline' { & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled032 -UpgradeFromInstalled028 -IncludeServiceAccounts } 'exactly one'
Reject '033 review cannot extend 028 selection' {
    & $helper -RepositoryRoot $root -ExpectedSource $source -UpgradeFromInstalled028 -IncludeServiceAccounts -SqlUpgradeReview $file
} 'Baseline028Verified'
Reject '033 selection cannot accept source ending at 032' {
    & $helper -RepositoryRoot $baselineRoot -ExpectedSource $source -UpgradeFromInstalled032 -IncludeServiceAccounts -SqlUpgradeReview $file
} 'exact complete'
$fixture = Join-Path $evidence 'fixture'
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'sql') -Destination (Join-Path $fixture 'sql') -Recurse
$selection.RepositoryRoot = $fixture
$wrapper = 'sql/schema/033-service-account-change-plans.sql'
$entry = @($review.Files | Where-Object Path -CEQ $wrapper)[0]
$original = [IO.File]::ReadAllText((Join-Path $fixture $wrapper))
foreach ($probe in @(
    @{Line=':r ../pending/service-accounts/SA-001-service-accounts.sql';Name='033 cannot replay installed include';Error='replay an installed'},
    @{Line=':r ../pending/service-accounts/missing.sql';Name='033 missing include';Error='include is missing'},
    @{Line=':r ../../../../outside.sql';Name='033 escaping include';Error='escapes repository'})) {
    [IO.File]::WriteAllText((Join-Path $fixture $wrapper), $probe.Line)
    $entry.Sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $wrapper)).Hash; Save-Review
    Reject $probe.Name { Select-Plan } $probe.Error
}
[IO.File]::WriteAllText((Join-Path $fixture $wrapper), $original)
$entry.Sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $wrapper)).Hash; Save-Review
$role = $paths033[3]
$roleEntry = @($review.Files | Where-Object Path -CEQ $role)[0]
$originalRole = [IO.File]::ReadAllText((Join-Path $fixture $role))
[IO.File]::WriteAllText((Join-Path $fixture $role), ':r ../pending/service-accounts/SA-001-service-accounts.sql')
$roleEntry.Sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $role)).Hash; Save-Review
Reject '033 role cannot replay installed include' { Select-Plan } 'replay an installed'
[IO.File]::WriteAllText((Join-Path $fixture $role), $originalRole)
$roleEntry.Sha256 = (Get-FileHash -LiteralPath (Join-Path $fixture $role)).Hash; Save-Review
$asset = Join-Path $fixture $paths033[2]
Move-Item -LiteralPath $asset -Destination "$asset.absent"
$failure = $null
try { Select-Plan | Out-Null } catch { $failure = $_ }
Check '033 missing physical include' ($null -ne $failure -and $failure.TargetObject -ieq $asset)
Move-Item -LiteralPath "$asset.absent" -Destination $asset
$asset = Join-Path $fixture $wrapper
Move-Item -LiteralPath $asset -Destination (Join-Path (Split-Path $asset -Parent) '033-unknown.sql')
Reject '033 unknown schema name' { Select-Plan } 'Unexpected 033'
Move-Item -LiteralPath (Join-Path (Split-Path $asset -Parent) '033-unknown.sql') -Destination $asset
$asset = Join-Path $fixture $paths033[0]
Move-Item -LiteralPath $asset -Destination "$asset.absent"
Reject '033 incomplete numbered chain' { Select-Plan } 'exact complete'
Move-Item -LiteralPath "$asset.absent" -Destination $asset
[IO.File]::WriteAllText((Join-Path $fixture 'sql/migrations/035-unreviewed.sql'), 'SELECT 1;')
Reject '033 rejects future 035' { Select-Plan } 'exact complete'
$selection.Remove('UpgradeFromInstalled032'); $selection.UpgradeFromInstalled028 = $true
Reject '028 rejects future 035' { Select-Plan } 'exact complete'
[ordered]@{checks=$checks;count=$checks.Count;baselineSource=$baselineSource;combinedSource=$source;
    sqlCandidateBytesIdentical=$true;readyForInstallation=$false;databaseConnections=$false;corporateActions=$false;
    retainedCandidateSqlCompared=[bool]$ExistingCandidateDirectory;
    fullApiUiPackagesCreated=$false} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
