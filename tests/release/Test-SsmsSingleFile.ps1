[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$PackageDirectory)
# Offline proof that every SSMS single-file script is the exact source lines with each :r include inlined, framed only
# by the fixed preamble/trailer. Re-derives the expected lines independently of ConvertTo-SsmsSingleFile.ps1. With
# -PackageDirectory it also verifies a produced package's DBA/ssms scripts against the repository. No SQL connection.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$converter = Join-Path $root 'scripts/release/ConvertTo-SsmsSingleFile.ps1'
$source = (& git -C $root rev-parse HEAD).Trim()
$checks = [Collections.Generic.List[string]]::new()
function Check([string]$Name, [bool]$Condition) {
    if (!$Condition) { throw "Failed: $Name" }
    $checks.Add($Name); Write-Host "PASS: $Name"
}
function Reject([string]$Name, [scriptblock]$Action, [string]$Expected) {
    $message = ''; try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Check $Name ($message -like "*$Expected*")
}
# Independent expansion: a work stack of (file, next line) pairs, includes resolved from sql/migrations.
function Get-ExpectedBody([string]$SourceRoot, [string]$Relative) {
    $out = [Collections.Generic.List[string]]::new()
    $stack = [Collections.Generic.Stack[object]]::new()
    $stack.Push(@{ Lines=[IO.File]::ReadAllLines((Join-Path $SourceRoot $Relative)); Index=0 })
    while ($stack.Count -gt 0) {
        $top = $stack.Peek()
        if ($top.Index -ge $top.Lines.Count) { $null = $stack.Pop(); continue }
        $line = $top.Lines[$top.Index]; $top.Index++
        $include = [regex]::Match($line, '^\s*:r\s+"?([^"]+?)"?\s*$')
        if ($include.Success) {
            $path = [IO.Path]::GetFullPath([IO.Path]::Combine($SourceRoot, 'sql', 'migrations', $include.Groups[1].Value))
            $stack.Push(@{ Lines=[IO.File]::ReadAllLines($path); Index=0 })
        } else { $out.Add($line) }
    }
    return ,$out.ToArray()
}
function Get-ExpectedFrame([string]$Title, [string]$Relative, [string]$Hash, [string]$Prep, [string]$Phase) {
    $head = @("/* WASAS SSMS tek dosya: $Title", "   Kaynak $Relative SHA256 $Hash, hazirlik kaynagi $Prep.",
        '   Asagidaki satirlar kaynak ve :r ile eklenen dosyalarin birebir kopyasidir; degistirmeyin.',
        '   SSMS: Sorgu (Query) > SQLCMD Modu ACIK olmali. Kapaliysa ilk kontrol hata verir ve hicbir sey calismaz. */',
        'SET NOEXEC OFF;', 'SET QUOTED_IDENTIFIER ON;', 'GO', ':setvar WasasSqlCmdMode "1"')
    if ($Phase) { $head += ":setvar Phase `"$Phase`"" }
    $head += @(':on error exit', 'GO',
        "IF N'`$(WasasSqlCmdMode)' <> N'1' BEGIN RAISERROR(N'SQLCMD Modu kapali: hicbir sey calistirilmadi. Sorgu > SQLCMD Modu acip yeniden calistirin.', 16, 1); SET NOEXEC ON; END;",
        'GO')
    return @{ Head=$head; Tail=@('GO', 'SET NOEXEC OFF;') }
}
function Test-SingleFile([string]$Name, [string]$File, [string]$SourceRoot, [string]$Relative, [string]$Prep, [string]$Phase) {
    $bytes = [IO.File]::ReadAllBytes($File)
    Check "$Name UTF-8 BOM" ($bytes.Length -gt 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes, 3, $bytes.Length - 3)
    Check "$Name CRLF only" (!($text -replace "`r`n", '').Contains("`n") -and !($text -replace "`r`n", '').Contains("`r"))
    $actual = $text.Substring(0, $text.Length - 2) -split "`r`n"
    $titleLine = [regex]::Match($actual[0], '^/\* WASAS SSMS tek dosya: (.+)$')
    Check "$Name title line" $titleLine.Success
    $frame = Get-ExpectedFrame $titleLine.Groups[1].Value $Relative (Get-FileHash -LiteralPath (Join-Path $SourceRoot $Relative)).Hash $Prep $Phase
    $body = Get-ExpectedBody $SourceRoot $Relative
    $expected = @($frame.Head) + @($body) + @($frame.Tail)
    Check "$Name line count $($expected.Count)" ($actual.Count -eq $expected.Count)
    $mismatch = @(0..($expected.Count-1) | Where-Object { $actual[$_] -cne $expected[$_] })
    Check "$Name every line matches source" ($mismatch.Count -eq 0)
    Check "$Name has no unresolved include" (@($actual | Where-Object { $_ -match '^\s*:r\s' }).Count -eq 0)
}
$prep = $source
$plan = @(
    @{ Relative='sql/migrations/033-service-account-change-plans.sql' }, @{ Relative='sql/pending/service-accounts/SA-006-API-permissions.sql' },
    @{ Relative='sql/migrations/034-service-account-plan-evidence.sql' },
    @{ Relative='sql/migrations/035-service-account-plan-operational-records.sql' }, @{ Relative='sql/pending/service-accounts/SA-008-API-permissions.sql' },
    @{ Relative='sql/migrations/036-service-account-events.sql' }, @{ Relative='sql/pending/service-accounts/SA-009-API-permissions.sql' },
    @{ Relative='sql/migrations/037-service-account-usage-scan-jobs.sql' }, @{ Relative='sql/pending/service-accounts/SA-010-API-permissions.sql' },
    @{ Relative='sql/pending/service-accounts/SA-010-Worker-permissions.sql' },
    @{ Relative='scripts/diagnostics/Get-InstalledMigrationInventory025To032.sql' },
    @{ Relative='scripts/release/sql/Get-InstalledMigrationInventory033To037.sql' })
foreach ($number in 33..37) {
    foreach ($phase in @('pre','post')) { $plan += @{ Relative="tests/sql/service-accounts/sa-0$number-dba-checks.sql"; Phase=$phase } }
}
$index = 0
foreach ($entry in $plan) {
    $index++
    $phase = if ($entry.ContainsKey('Phase')) { $entry.Phase } else { $null }
    $out = Join-Path $evidence ("{0:D2}-{1}" -f $index, [IO.Path]::GetFileName($entry.Relative))
    $arguments = @{ RepositoryRoot=$root; SourcePath=$entry.Relative; OutputPath=$out; Title="step $index"; PreparationSource=$prep }
    if ($phase) { $arguments.Phase = $phase }
    $result = & $converter @arguments
    Check "$($entry.Relative) $phase identity" ($result.sha256 -ceq (Get-FileHash -LiteralPath $out).Hash -and
        $result.source.sha256 -ceq (Get-FileHash -LiteralPath (Join-Path $root $entry.Relative)).Hash)
    Test-SingleFile "$($entry.Relative) $phase" $out $root $entry.Relative $prep $phase
    if ($entry.Relative -like 'sql/migrations/*') {
        Check "$($entry.Relative) inlines schema and module include" (@($result.includes).Count -eq 2)
    }
}
Reject 'refuses overwrite' { & $converter -RepositoryRoot $root -SourcePath $plan[0].Relative -OutputPath (Join-Path $evidence '01-033-service-account-change-plans.sql') -Title 'x' -PreparationSource $prep } 'overwrite'
Reject 'refuses unbound $(Phase)' { & $converter -RepositoryRoot $root -SourcePath 'tests/sql/service-accounts/sa-033-dba-checks.sql' -OutputPath (Join-Path $evidence 'nophase.sql') -Title 'x' -PreparationSource $prep } 'SQLCMD variable'

# Mutation probes on a private copy of the sources: the converter must refuse and the line check must catch tampering.
$fixture = Join-Path $evidence 'fixture'
foreach ($folder in @('sql/migrations','sql/schema','sql/pending/service-accounts')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $folder) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'sql/migrations/033-service-account-change-plans.sql') -Destination (Join-Path $fixture 'sql/migrations')
Copy-Item -LiteralPath (Join-Path $root 'sql/schema/033-service-account-change-plans.sql') -Destination (Join-Path $fixture 'sql/schema')
Copy-Item -LiteralPath (Join-Path $root 'sql/pending/service-accounts/SA-006-change-plans.sql') -Destination (Join-Path $fixture 'sql/pending/service-accounts')
$probe = 'sql/migrations/probe.sql'
foreach ($case in @(
    @{ Name='escaping include'; Text=":r ../../../outside.sql"; Error='escapes repository' },
    @{ Name='missing include'; Text=":r ../schema/missing.sql"; Error='include is missing' },
    @{ Name='other SQLCMD directive'; Text=":setvar X 1"; Error='Only :r' },
    @{ Name='SQLCMD variable'; Text="SELECT N'`$(X)';"; Error='SQLCMD variable' },
    @{ Name='duplicate DECLARE in one batch'; Text="DECLARE @a int = 1;`r`nDECLARE @b int, @A int;"; Error='declared twice' })) {
    [IO.File]::WriteAllText((Join-Path $fixture $probe), $case.Text)
    Reject "refuses $($case.Name)" { & $converter -RepositoryRoot $fixture -SourcePath $probe -OutputPath (Join-Path $evidence "probe-$([guid]::NewGuid()).sql") -Title 'probe' -PreparationSource $prep } $case.Error
}
[IO.File]::WriteAllText((Join-Path $fixture $probe), "DECLARE @a int = 1;`r`nGO`r`nDECLARE @a int = 2;")
$separate = Join-Path $evidence 'probe-separate-batches.sql'
$null = & $converter -RepositoryRoot $fixture -SourcePath $probe -OutputPath $separate -Title 'probe' -PreparationSource $prep
Check 'same variable in separate batches allowed' (Test-Path -LiteralPath $separate)
$good = Join-Path $evidence 'probe-good.sql'
$null = & $converter -RepositoryRoot $fixture -SourcePath 'sql/migrations/033-service-account-change-plans.sql' -OutputPath $good -Title 'probe' -PreparationSource $prep
$lines = [IO.File]::ReadAllLines($good)
foreach ($mutation in @(
    @{ Name='changed body line'; Apply={ param($l) $i = [array]::IndexOf($l, 'BEGIN TRANSACTION;'); $l[$i] = 'BEGIN TRANSACTION; '; $l } },
    @{ Name='dropped error exit'; Apply={ param($l) @($l | Where-Object { $_ -cne ':on error exit' }) } },
    @{ Name='kept :r line'; Apply={ param($l) @($l[0..13]) + @(':r ../schema/033-service-account-change-plans.sql') + @($l[14..($l.Count-1)]) } })) {
    $file = Join-Path $evidence "mutated-$($checks.Count).sql"
    [IO.File]::WriteAllText($file, ((& $mutation.Apply ([string[]]$lines.Clone())) -join "`r`n") + "`r`n", [Text.UTF8Encoding]::new($true))
    Reject "detects $($mutation.Name)" { Test-SingleFile 'mutant' $file $fixture 'sql/migrations/033-service-account-change-plans.sql' $prep $null } 'Failed'
}

if ($PackageDirectory) {
    $package = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $manifest = Get-Content -LiteralPath (Join-Path $package 'DBA/ssms/ssms-scripts.json') -Raw | ConvertFrom-Json
    $prepared = [string]$manifest.preparationSource
    Check 'package preparation source is a full SHA' ($prepared -match '^[0-9a-f]{40}$')
    $files = @(Get-ChildItem -LiteralPath (Join-Path $package 'DBA/ssms') -File -Filter '*.sql' | ForEach-Object Name | Sort-Object)
    Check 'package manifest lists every SSMS script' (($files -join '|') -ceq ((@($manifest.scripts | ForEach-Object file) | Sort-Object) -join '|'))
    foreach ($script in $manifest.scripts) {
        $file = Join-Path $package "DBA/ssms/$($script.file)"
        Check "package $($script.file) hash" ((Get-FileHash -LiteralPath $file).Hash -ceq $script.sha256)
        Check "package $($script.file) source unchanged in this checkout" ((Get-FileHash -LiteralPath (Join-Path $root $script.source.path)).Hash -ceq $script.source.sha256)
        foreach ($include in @($script.includes)) {
            Check "package $($script.file) include $($include.path)" ((Get-FileHash -LiteralPath (Join-Path $root $include.path)).Hash -ceq $include.sha256)
        }
        $phase = if ($script.phase) { [string]$script.phase } else { $null }
        Test-SingleFile "package $($script.file)" $file $root $script.source.path $prepared $phase
    }
}
[ordered]@{ checks=$checks; count=$checks.Count; source=$source; package=$PackageDirectory; databaseConnections=$false } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
Write-Host "SSMS single-file checks passed: $($checks.Count)"
