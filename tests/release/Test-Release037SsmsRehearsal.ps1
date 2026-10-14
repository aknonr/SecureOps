[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('(?-i)^[A-Za-z0-9_]{1,30}$')][string]$DatabaseSuffix,
    [Parameter(Mandatory)][string]$EvidenceDirectory)
# Local proof that a 033-037 review candidate really upgrades an installed-032 database: the module harness builds a NEW
# LocalDB copy at 032 (roles included, no members), then the package's DBA/ssms single files run in operator order through
# sqlcmd WITHOUT -I/-b (the files must carry those themselves), with the DBA notes' expected check values, replay refusal
# and the harness 033-037 guards. A normal-mode (non-SQLCMD) run is simulated first and must change nothing. Never targets
# a shared or corporate server. The database is dropped on success and kept for inspection on failure.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$server = '(localdb)\SecureOpsResourcesV1'
$database = 'SecureOps_Sa' + $DatabaseSuffix
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$checks = [Collections.Generic.List[string]]::new()
$log = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Condition) {
    if (!$Condition) { throw "Failed: $Name (database $database retained)" }
    $checks.Add($Name); Write-Host "PASS: $Name"
}
function Invoke-File([string]$File, [string[]]$Extra = @()) {
    $output = @(& $sqlcmd -S $server -d $database -E -f 65001 -W -s '|' @Extra -i $File 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $log.Add([ordered]@{ file=$File.Substring($package.Length+1).Replace('\','/'); exitCode=$code; output=$output })
    return [pscustomobject]@{ Code=$code; Output=$output }
}
# Result sets as rows of name->value; header is the line before a dash separator line.
function Get-Rows([string[]]$Output) {
    $sets = @(); $header = $null; $rows = $null
    for ($i = 0; $i -lt $Output.Count; $i++) {
        $line = $Output[$i]
        if ($line -match '^-+(\|-+)*$' -and $i -gt 0) {
            if ($rows) { $sets += ,$rows }
            $header = $Output[$i-1].Split('|'); $rows = [Collections.Generic.List[hashtable]]::new(); continue
        }
        if ($null -ne $rows -and $line.Trim() -ne '' -and ($i + 1 -ge $Output.Count -or $Output[$i+1] -notmatch '^-+(\|-+)*$')) {
            $values = $line.Split('|')
            if ($values.Count -eq $header.Count) { $row = @{}; for ($c = 0; $c -lt $header.Count; $c++) { $row[$header[$c]] = $values[$c] }; $rows.Add($row) }
        }
    }
    if ($rows) { $sets += ,$rows }
    return ,$sets
}
function Assert-Row([string]$Name, $Row, [hashtable]$Expected) {
    foreach ($key in $Expected.Keys) {
        Check "$Name $key = $($Expected[$key])" ($Row.ContainsKey($key) -and $Row[$key] -ceq [string]$Expected[$key])
    }
}

$candidate = Get-Content -LiteralPath (Join-Path $package 'candidate.json') -Raw | ConvertFrom-Json
Check 'candidate stays not ready for installation' ($candidate.readyForInstallation -is [bool] -and !$candidate.readyForInstallation)
Check 'candidate is the installed-032 to 037 review' ($candidate.requiredSchema -ceq '001-037' -and $candidate.sqlReview.installedBaseline -ceq '032')
foreach ($entry in $candidate.supportingFiles) {
    Check "supporting file hash $($entry.path)" ((Get-FileHash -LiteralPath (Join-Path $package $entry.path)).Hash -ceq $entry.sha256)
}
foreach ($relative in @($candidate.sqlSequence)) {
    $copy = Join-Path $package "DBA/sql/$($relative.Substring(4))"
    Check "packaged SQL equals source $relative" ((Get-FileHash -LiteralPath $copy).Hash -ceq (Get-FileHash -LiteralPath (Join-Path $root $relative)).Hash)
}
$manifest = Get-Content -LiteralPath (Join-Path $package 'DBA/ssms/ssms-scripts.json') -Raw | ConvertFrom-Json
$ssms = @{}; foreach ($script in $manifest.scripts) { $ssms[$script.file] = Join-Path $package "DBA/ssms/$($script.file)" }
foreach ($script in $manifest.scripts) { Check "SSMS hash $($script.file)" ((Get-FileHash -LiteralPath $ssms[$script.file]).Hash -ceq $script.sha256) }

$harness = Join-Path $root 'tests/sql/service-accounts/sa-sql-harness.ps1'
& (Get-Process -Id $PID).Path -NoProfile -ExecutionPolicy Bypass -File $harness -DatabaseSuffix $DatabaseSuffix -ThroughMigration 32 *> (Join-Path $evidence 'harness-032.log')
Check 'harness built a 032 copy with roles' ($LASTEXITCODE -eq 0)

# Inventories on the 032 copy.
$run = Invoke-File $ssms['00-envanter-025-032.sql']
Check '025-032 inventory ran' ($run.Code -eq 0)
$sets = Get-Rows $run.Output
$signatures = @($sets | Where-Object { $_.Count -gt 0 -and $_[0].ContainsKey('Migration') })[0]
Check '025-032 inventory has eight migrations' ($signatures.Count -eq 8)
foreach ($row in $signatures) { Check "025-032 inventory $($row.Migration) present" ($row.BulunanIsaret -ceq $row.BeklenenIsaret) }
$run = Invoke-File $ssms['01-envanter-033-037.sql']
$sets = Get-Rows $run.Output
$signatures = @($sets | Where-Object { $_.Count -gt 0 -and $_[0].ContainsKey('Migration') })[0]
Check '033-037 inventory before: nothing applied' ($run.Code -eq 0 -and $signatures.Count -eq 5 -and @($signatures | Where-Object { $_.BulunanIsaret -cne '0' }).Count -eq 0)
$roles = @($sets | Where-Object { $_.Count -gt 0 -and $_[0].ContainsKey('ApiRolu') })[0][0]
Assert-Row '033-037 inventory roles' $roles @{ ApiRolu='var'; WorkerRolu='var' }

# SSMS normal mode: every batch is sent as is and errors do not stop later batches. The file must execute nothing.
$connection = [System.Data.SqlClient.SqlConnection]::new("Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True")
$messages = [Collections.Generic.List[string]]::new()
$connection.Open()
try {
    $batches = [Collections.Generic.List[string]]::new(); $current = [Text.StringBuilder]::new()
    foreach ($line in [IO.File]::ReadAllLines($ssms['12-033-uygula.sql'])) {
        if ($line -match '^\s*GO\s*$') { $batches.Add($current.ToString()); $current = [Text.StringBuilder]::new() } else { $null = $current.AppendLine($line) }
    }
    $batches.Add($current.ToString())
    foreach ($batch in $batches) {
        if ($batch.Trim() -eq '') { continue }
        $command = $connection.CreateCommand(); $command.CommandText = $batch
        try { $null = $command.ExecuteNonQuery() } catch [System.Data.SqlClient.SqlException] { $messages.Add($_.Exception.Message) }
    }
} finally { $connection.Dispose() }
Check 'normal mode reports SQLCMD mode is off' (@($messages | Where-Object { $_ -like '*SQLCMD Modu kapali*' }).Count -eq 1)
$run = Invoke-File $ssms['01-envanter-033-037.sql']
$signatures = @((Get-Rows $run.Output) | Where-Object { $_.Count -gt 0 -and $_[0].ContainsKey('Migration') })[0]
Check 'normal mode changed nothing' (@($signatures | Where-Object { $_.BulunanIsaret -cne '0' }).Count -eq 0)

$expected = @{
    '033'=@{ pre=@{ OnKosul_025_030_031='var'; ApiRolu='var'; '033Tablosu'='0' };
             post=@{ Tablo='7'; Tetikleyici='8'; GuvenilmeyenKisit='0'; ApiIzni='18'; BeklenmeyenIzin='0'; PlanSatiri='0' } }
    '034'=@{ pre=@{ OnKosul_033='var'; KanitKisiti='eski'; SahipTuru='8' };
             post=@{ KanitKisiti='yeni'; SahipTuru='9'; GuvenilmeyenKisit='0'; PlanKaniti='0' } }
    '035'=@{ pre=@{ OnKosul_033='var'; OnKosul_034='var'; OrTablolari='yok'; ApiRolu='var' };
             post=@{ OrTablosu='2'; Tetikleyici='2'; FiltreliTekilIndeks='2'; GuvenilmeyenKisit='0'; ApiIzni_SelectInsert='4';
                     ApiIzni_SutunUpdate='7'; BaskaIzin='0'; GonderimSatiri='0' } }
    '036'=@{ pre=@{ OnKosul_025='var'; OlayTablolari='yok'; ApiRolu='var' };
             post=@{ OlayTablosu='2'; EklemeYalnizTetikleyici='2'; GuvenilmeyenKisit='0'; MetinSutunu='0'; ApiIzni_SelectInsert='4';
                     BaskaIzin='0'; OlaySatiri='0' } }
    '037'=@{ pre=@{ OnKosul_030='var'; TaramaIsleri='yok' };
             post=@{ Tablo='3'; Tetikleyici='3'; TekCalisanDizin='1'; GuvenilmeyenKisit='0'; Is='0' } } }
foreach ($number in 33..37) {
    $m = "0$number"; $k = $number - 32
    $files = @($manifest.scripts | Where-Object { $_.file -like "$k?-$m-*" } | ForEach-Object file | Sort-Object)
    Check "$m has pre, apply, grants and post files in order" ($files[0] -ceq "$($k)1-$m-on-kontrol.sql" -and $files[1] -ceq "$($k)2-$m-uygula.sql" -and
        $files[-1] -ceq "$($k)9-$m-son-kontrol.sql")
    foreach ($file in $files) {
        $run = Invoke-File $ssms[$file]
        Check "$file exit 0" ($run.Code -eq 0)
        if ($file -like '*-on-kontrol.sql' -or $file -like '*-son-kontrol.sql') {
            $phase = if ($file -like '*-on-kontrol.sql') { 'pre' } else { 'post' }
            $row = @((Get-Rows $run.Output) | Where-Object { $_.Count -gt 0 })[0][0]
            Assert-Row "$m $phase" $row $expected[$m][$phase]
        }
    }
    $replay = Invoke-File $ssms["$($k)2-$m-uygula.sql"]
    Check "$m replay refused by the single file" ($replay.Code -ne 0)
    $guard = Join-Path $root "tests/sql/service-accounts/sa-$m-guards.sql"
    $output = @(& $sqlcmd -S $server -d $database -E -I -b -v WithRole=1 -i $guard 2>&1 | ForEach-Object { "$_" })
    $log.Add([ordered]@{ file="tests/sql/service-accounts/sa-$m-guards.sql"; exitCode=$LASTEXITCODE; output=$output })
    Check "$m harness guards pass" ($LASTEXITCODE -eq 0)
}
$run = Invoke-File $ssms['01-envanter-033-037.sql']
$signatures = @((Get-Rows $run.Output) | Where-Object { $_.Count -gt 0 -and $_[0].ContainsKey('Migration') })[0]
Check '033-037 inventory after: all applied' (@($signatures | Where-Object { $_.BulunanIsaret -cne $_.BeklenenIsaret }).Count -eq 0)

& $sqlcmd -S $server -d master -E -b -Q "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];" | Out-Null
Check 'disposable database dropped' ($LASTEXITCODE -eq 0)
[ordered]@{ checks=$checks; count=$checks.Count; package=$package; database=$database; server='LocalDB (isolated per-user instance)';
    steps=$log; targetChanged=$false; corporateActions=$false } | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
Write-Host "033-037 SSMS rehearsal passed: $($checks.Count)"
