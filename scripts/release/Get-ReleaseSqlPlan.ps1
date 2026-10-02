[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot,
    [switch]$UpgradeFromRc622, [switch]$UpgradeFromRc624, [switch]$UpgradeFromRc626,
    [switch]$IncludeServiceAccounts,
    [string]$SqlUpgradeReview,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSource)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (@($UpgradeFromRc622,$UpgradeFromRc624,$UpgradeFromRc626 | Where-Object { $_ }).Count -gt 1) {
    throw 'Choose exactly one upgrade baseline.'
}
if ($IncludeServiceAccounts -and !$UpgradeFromRc626) {
    throw '025 selection requires the reviewed rc6.26/023 upgrade path.'
}
if (!$IncludeServiceAccounts -and $SqlUpgradeReview) { throw 'SQL upgrade review requires explicit 025 selection.' }
$last = if ($IncludeServiceAccounts) { 26 } else { 24 }
$first = if ($UpgradeFromRc626) { 24 } elseif ($UpgradeFromRc624) { 23 } elseif ($UpgradeFromRc622) { 22 } else { 19 }
$all = @()
foreach ($folder in @('migrations','schema')) {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $root "sql/$folder") -File -Filter '*.sql' | Sort-Object Name)
    $numbers = @($files | ForEach-Object {
        if ($_.Name -notmatch '^\d{3}-.+\.sql$') { throw 'Unexpected SQL asset name.' }
        $_.Name.Substring(0,3)
    }) -join ','
    if ($numbers -cne ((1..$last | ForEach-Object { '{0:D3}' -f $_ }) -join ',')) {
        throw "Expected the exact complete 001-$last SQL chain; 025/026 must be explicitly reviewed."
    }
    $all += @($files | ForEach-Object { "sql/$folder/$($_.Name)" })
}
$delta = @($all | Where-Object { [int]([IO.Path]::GetFileName($_).Substring(0,3)) -ge $first })
$roles = @()
$reviewIdentity = $null
if ($IncludeServiceAccounts) {
    $include = 'sql/pending/service-accounts/SA-001-service-accounts.sql'
    $usageInclude = 'sql/pending/service-accounts/SA-002-usage-rules.sql'
    $roles = @('sql/pending/service-accounts/SA-API-permissions.sql','sql/pending/service-accounts/SA-Worker-permissions.sql',
        'sql/pending/service-accounts/SA-002-API-permissions.sql')
    $all += $include
    $delta += $include
    $all += $usageInclude
    $delta += $usageInclude
    if (!$SqlUpgradeReview) { throw '025 requires a source-bound 023 comparison and reviewed 024/025 file identities.' }
    $reviewFile = Get-Item -LiteralPath $SqlUpgradeReview
    if ($reviewFile.PSIsContainer -or $reviewFile.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Expected an ordinary SQL review file.' }
    $review = Get-Content -LiteralPath $reviewFile.FullName -Raw | ConvertFrom-Json
    if ($review.Schema -cne 'wasas.sql-upgrade-review.v1' -or $review.Source -cne $ExpectedSource) { throw 'SQL review schema/source mismatch.' }
    foreach ($flag in @('Baseline023Verified','Delta024Reviewed','ServiceAccounts025Reviewed','ServiceAccounts026Reviewed')) {
        if ($review.$flag -isnot [bool] -or !$review.$flag) { throw 'SQL baseline/delta review gate is not verified.' }
    }
    foreach ($reference in @('BaselineEvidenceReference','Delta024ReviewReference','ServiceAccounts025ReviewReference','ServiceAccounts026ReviewReference')) {
        if ($review.$reference -isnot [string] -or [string]::IsNullOrWhiteSpace($review.$reference)) { throw 'SQL review evidence reference is missing.' }
    }
    $required = @($delta + $roles | Sort-Object)
    $reviewed = @($review.Files | Sort-Object Path)
    if (($required -join '|') -cne (($reviewed | ForEach-Object { $_.Path }) -join '|')) { throw 'SQL review must cover the exact 024/025/026 dependency and role files.' }
    foreach ($entry in $reviewed) {
        if ($entry.Sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            (Get-FileHash -LiteralPath (Join-Path $root $entry.Path)).Hash -ine $entry.Sha256) { throw 'Reviewed SQL file hash mismatch.' }
    }
    $reviewIdentity = [ordered]@{ sha256=(Get-FileHash -LiteralPath $reviewFile.FullName).Hash;
        baselineEvidence=$review.BaselineEvidenceReference; delta024Review=$review.Delta024ReviewReference;
        module025Review=$review.ServiceAccounts025ReviewReference; module026Review=$review.ServiceAccounts026ReviewReference }
}
# SQLCMD resolves nested :r paths from its working directory, not from the including file.
$working = Join-Path $root 'sql/migrations'
foreach ($relative in @($all) + @($roles)) {
    $file = Get-Item -LiteralPath (Join-Path $root $relative)
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SQL dependency must be an ordinary file.' }
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        if ($line -match '^\s*:r\s+(.+?)\s*$') {
            $includePath = $Matches[1].Trim('"').Replace('/', [IO.Path]::DirectorySeparatorChar)
            $resolved = [IO.Path]::GetFullPath((Join-Path $working $includePath))
            if (!$resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'SQLCMD include escapes repository.' }
            $dependency = $resolved.Substring($root.Length+1).Replace('\','/')
            if ($all -cnotcontains $dependency -or !(Test-Path -LiteralPath $resolved -PathType Leaf)) { throw 'SQLCMD include is missing from selected dependency closure.' }
        }
    }
}
[pscustomobject]@{ RequiredSchema="001-$('{0:D3}' -f $last)";
    DeltaRange=if ($first -eq $last) { '{0:D3}' -f $first } else { '{0:D3}-{1:D3}' -f $first,$last };
    AllFiles=$all; DeltaFiles=$delta; RoleFiles=$roles; Review=$reviewIdentity }
