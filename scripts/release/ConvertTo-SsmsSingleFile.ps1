[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][ValidatePattern('(?-i)^[A-Za-z0-9_./-]+\.sql$')][string]$SourcePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][ValidatePattern('(?-i)^[A-Za-z0-9 ._()-]{1,80}$')][string]$Title,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$PreparationSource,
    [ValidateSet('pre','post')][string]$Phase)
# One SSMS-openable file per SQLCMD step (TEST has no sqlcmd and SSMS did not resolve relative :r paths, 2026-10-13).
# The body is the exact source lines with every :r include inlined in place, resolved like SQLCMD from sql/migrations.
# The fixed preamble stands in for `sqlcmd -I -b`: :on error exit, QUOTED_IDENTIFIER ON, and a SQLCMD-mode check that
# turns on NOEXEC when SSMS runs the file in normal mode, so nothing after it executes. tests/release/Test-SsmsSingleFile.ps1
# re-derives every line independently.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$working = Join-Path $root 'sql/migrations'
$includes = [Collections.Generic.List[object]]::new()
function Get-Identity([string]$Full) {
    $item = Get-Item -LiteralPath $Full
    if ($item.PSIsContainer -or $item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SSMS source must be an ordinary file.' }
    [ordered]@{ path=$item.FullName.Substring($root.Length+1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $item.FullName).Hash }
}
function Expand-Source([string]$Full, [int]$Depth) {
    if ($Depth -gt 4) { throw 'SQLCMD include nesting is too deep.' }
    foreach ($line in [IO.File]::ReadAllLines($Full)) {
        if ($line -match '^\s*:r\s+(.+?)\s*$') {
            $resolved = [IO.Path]::GetFullPath((Join-Path $working $Matches[1].Trim('"').Replace('/', [IO.Path]::DirectorySeparatorChar)))
            if (!$resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'SQLCMD include escapes repository.' }
            if (!(Test-Path -LiteralPath $resolved -PathType Leaf)) { throw 'SQLCMD include is missing.' }
            $includes.Add((Get-Identity $resolved))
            Expand-Source $resolved ($Depth+1)
        } elseif ($line -match '^\s*:') {
            throw 'Only :r SQLCMD directives can be inlined.'
        } elseif ($line.Contains('$(') -and !($Phase -and $line.Replace('$(Phase)','').IndexOf('$(') -lt 0)) {
            throw 'Unexpected SQLCMD variable in source.'
        } else { $line }
    }
}
$source = Join-Path $root $SourcePath
$identity = Get-Identity $source
$body = @(Expand-Source $source 0)
# Inlining keeps each batch as SQLCMD would send it; a variable declared twice in one batch would already fail there.
$declared = @{}
foreach ($line in $body) {
    if ($line -match '^\s*GO\s*$') { $declared = @{}; continue }
    if ($line -match '^\s*DECLARE\s+(.+)$') {
        foreach ($name in [regex]::Matches($Matches[1], '(?:^|,)\s*(@\w+)\s+[A-Za-z]') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() }) {
            if ($declared.ContainsKey($name)) { throw "Variable $name is declared twice in one batch." }
            $declared[$name] = $true
        }
    }
}
$setvars = @(':setvar WasasSqlCmdMode "1"')
if ($Phase) { $setvars += ":setvar Phase `"$Phase`"" }
$preamble = @(
    "/* WASAS SSMS tek dosya: $Title",
    "   Kaynak $($identity.path) SHA256 $($identity.sha256), hazirlik kaynagi $PreparationSource.",
    '   Asagidaki satirlar kaynak ve :r ile eklenen dosyalarin birebir kopyasidir; degistirmeyin.',
    '   SSMS: Sorgu (Query) > SQLCMD Modu ACIK olmali. Kapaliysa ilk kontrol hata verir ve hicbir sey calismaz. */',
    'SET NOEXEC OFF;',
    'SET QUOTED_IDENTIFIER ON;',
    'GO') + $setvars + @(
    ':on error exit',
    'GO',
    "IF N'`$(WasasSqlCmdMode)' <> N'1' BEGIN RAISERROR(N'SQLCMD Modu kapali: hicbir sey calistirilmadi. Sorgu > SQLCMD Modu acip yeniden calistirin.', 16, 1); SET NOEXEC ON; END;",
    'GO')
$trailer = @('GO', 'SET NOEXEC OFF;')
$lines = @($preamble) + $body + $trailer
$parent = Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent
if (!(Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
if (Test-Path -LiteralPath $OutputPath) { throw 'Refuse to overwrite an SSMS script.' }
# UTF-8 with BOM so SSMS reads the non-ASCII comment characters of the sources unchanged; CRLF throughout.
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), (($lines -join "`r`n") + "`r`n"), [Text.UTF8Encoding]::new($true))
[pscustomobject][ordered]@{ path=$OutputPath; sha256=(Get-FileHash -LiteralPath $OutputPath).Hash; source=$identity;
    includes=@($includes); phase=$Phase; preambleLines=$preamble.Count; bodyLines=$body.Count; trailerLines=$trailer.Count }
