[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string[]]$ForbiddenText = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path.TrimEnd('\')
$allFiles = @(Get-ChildItem -LiteralPath $publishPath -File -Recurse)
if ($allFiles.Count -eq 0)
{
    throw 'Release payload validation found no publish files.'
}

$payloadFiles = @($allFiles | Where-Object {
    $_.Name -ne 'web.config' -and $_.Name -notmatch '^appsettings(\..+)?\.json$'
})
$forbiddenExtensions = @('.pdb', '.cs', '.csproj', '.sln', '.razor', '.cshtml', '.props', '.targets')
foreach ($file in $payloadFiles)
{
    $relativePath = $file.FullName.Substring($publishPath.Length).TrimStart('\') -replace '\\', '/'
    if (($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) -or
        ($relativePath -match '(^|/)(bin|obj|logs?|tests?)(/|$)') -or
        ($file.Name -match '(?i)(^|\.)tests?\.') -or
        ($file.Extension -ieq '.log'))
    {
        throw "Forbidden release payload file: $relativePath"
    }
}

$textExtensions = @('.config', '.json', '.xml', '.txt', '.md', '.yml', '.yaml', '.ps1', '.cmd', '.bat')
$secretPattern = '(?im)(password|pwd|clientsecret|api[_-]?key|access[_-]?token)\s*["'']?\s*[:=]\s*["'']?[^\s,"'';}]{4,}'
foreach ($file in $allFiles | Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() })
{
    $content = [System.IO.File]::ReadAllText($file.FullName)
    if ($content -match $secretPattern)
    {
        $relativePath = $file.FullName.Substring($publishPath.Length).TrimStart('\') -replace '\\', '/'
        throw "Potential secret assignment found in publish output: $relativePath"
    }
}

foreach ($value in @($ForbiddenText | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }))
{
    foreach ($file in $allFiles)
    {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $asciiText = [System.Text.Encoding]::ASCII.GetString($bytes)
        $unicodeText = [System.Text.Encoding]::Unicode.GetString($bytes)
        if ($asciiText.IndexOf($value, [System.StringComparison]::Ordinal) -ge 0 -or
            $unicodeText.IndexOf($value, [System.StringComparison]::Ordinal) -ge 0)
        {
            $relativePath = $file.FullName.Substring($publishPath.Length).TrimStart('\') -replace '\\', '/'
            throw "Forbidden personal text found in publish output: $relativePath"
        }
    }
}

[pscustomobject]@{
    Ready = $true
    PublishDirectory = $publishPath
    ScannedFiles = $allFiles.Count
    PayloadFiles = $payloadFiles.Count
    ForbiddenTextValues = @($ForbiddenText | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
}
