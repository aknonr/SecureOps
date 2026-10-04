#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the usage-scan upload file (schema service-account-usage-scan-v1) that the Service Accounts module attaches to an
    account as evidence (ADR-0027). Read-only: it changes nothing on any server.

.DESCRIPTION
    Combine mode (ADR-0027, use this): run on your own workstation after Get-ServiceAccountUsage.ps1 has printed one
    document per server. -CombinePath takes the saved documents (one .json file per server, or one JSON-Lines file with one
    document per line); -ComputerName / -ComputerListPath is the PLANNED server list. Every planned server
    without a document is written as NoResult, or as Unreachable when you name it in -UnreachableComputerName. Nothing is
    dropped, and a server without a document never means "account not used".

    JEA mode (ADR-0024, PROPOSED and shelved): -UseJeaEndpoint asks the SecureOps discovery JEA endpoint on many servers in
    parallel. It works only after ADR-0024 is accepted and the endpoint is registered; it never uses the default endpoint.

    The upload file contains server names, component names and identities only: no passwords, no person names. A document
    with a password-like property is refused here, and the module refuses the whole file as well.

.EXAMPLE
    .\Invoke-ServiceAccountUsageScan.ps1 -CombinePath .\results.jsonl -ComputerListPath .\planned.txt -Account 'SYN\svc_synapp' -OutputPath .\scan.json

.EXAMPLE
    .\Invoke-ServiceAccountUsageScan.ps1 -CombinePath .\out\*.json -ComputerName SYN-APP01,SYN-APP02 -UnreachableComputerName SYN-APP02 -Account 'SYN\svc_synapp' -ExpectedAccount 'SYN\gmsa_synapp$' -OutputPath .\gmsa-check.json
#>
[CmdletBinding(DefaultParameterSetName = 'CombineNames')]
param(
    [Parameter(Mandatory, ParameterSetName = 'CombineNames')]
    [Parameter(Mandatory, ParameterSetName = 'CombineList')]
    [ValidateCount(1, 2000)]
    [string[]]$CombinePath,

    [Parameter(Mandatory, ParameterSetName = 'Names')]
    [Parameter(Mandatory, ParameterSetName = 'List')]
    [switch]$UseJeaEndpoint,

    [Parameter(Mandatory, ParameterSetName = 'Names')]
    [Parameter(Mandatory, ParameterSetName = 'CombineNames')]
    [ValidateCount(1, 500)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$')]
    [string[]]$ComputerName,

    [Parameter(Mandatory, ParameterSetName = 'List')]
    [Parameter(Mandatory, ParameterSetName = 'CombineList')]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ComputerListPath,

    [Parameter(ParameterSetName = 'CombineNames')]
    [Parameter(ParameterSetName = 'CombineList')]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$')]
    [string[]]$UnreachableComputerName = @(),

    [Parameter(Mandatory)]
    [ValidateCount(1, 20)]
    [ValidatePattern('^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$?$')]
    [string[]]$Account,

    [ValidatePattern('^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$$')]
    [string]$ExpectedAccount,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(ParameterSetName = 'Names')]
    [Parameter(ParameterSetName = 'List')]
    [ValidatePattern('^[A-Za-z0-9.]{1,64}$')]
    [string]$ConfigurationName = 'SecureOpsServiceAccountUsage',

    [Parameter(ParameterSetName = 'Names')]
    [Parameter(ParameterSetName = 'List')]
    [ValidateRange(1, 64)]
    [int]$ThrottleLimit = 32,

    [Parameter(ParameterSetName = 'Names')]
    [Parameter(ParameterSetName = 'List')]
    [ValidateRange(5, 120)]
    [int]$OpenTimeoutSeconds = 15,

    [Parameter(ParameterSetName = 'Names')]
    [Parameter(ParameterSetName = 'List')]
    [ValidateRange(30, 900)]
    [int]$OperationTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$serverPattern = '^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$'
$documentProperties = @('schema', 'serverName', 'generatedAt', 'durationMs', 'accounts', 'scanResult', 'sources', 'components', 'verification', 'warnings')
# Same words the module refuses (ADR-0027); checked here too so a bad document is caught before it leaves the workstation.
$secretPattern = 'password|passwd|pwd|secret|credential|token|apikey|privatekey|connectionstring|parola|[s\u015f]ifre'

if ($PSCmdlet.ParameterSetName -in @('List', 'CombineList')) {
    $ComputerName = @(Get-Content -LiteralPath $ComputerListPath | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    $invalid = @($ComputerName | Where-Object { $_ -notmatch $serverPattern })
    if ($invalid.Count -gt 0 -or $ComputerName.Count -eq 0 -or $ComputerName.Count -gt 500) {
        throw 'The computer list must hold 1-500 host names (letters, digits, dots, hyphens).'
    }
}

$planned = [System.Collections.Generic.List[string]]::new()
$plannedKeys = @{}
foreach ($name in $ComputerName) {
    if (-not $plannedKeys.ContainsKey($name.ToUpperInvariant())) {
        $plannedKeys[$name.ToUpperInvariant()] = $name
        $planned.Add($name)
    }
}

function Test-SecretName {
    # True when any property name in the object graph looks like a secret (password, token, parola, ...).
    param($Value)
    if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) { return $false }
    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) { if (Test-SecretName -Value $item) { return $true } }
        return $false
    }

    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -match $secretPattern) { return $true }
        if (Test-SecretName -Value $property.Value) { return $true }
    }

    return $false
}

function Read-UsageDocument {
    # One document per .json file (any formatting) or JSON Lines (one document per line, as the collector prints it).
    # Each document's text is kept exactly as written, so the upload carries what the collector produced.
    param([string]$Path)
    $text = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($text)) { return }
    $lines = @($text -split "`r?`n" | Where-Object { $_.Trim() })
    $first = $null
    if ($lines.Count -gt 1) {
        try { $first = $lines[0].Trim() | ConvertFrom-Json } catch { $first = $null }
    }

    $raws = if ($null -ne $first) { @($lines | ForEach-Object { $_.Trim() }) } else { @($text.Trim()) }
    foreach ($raw in $raws) {
        $document = $raw | ConvertFrom-Json
        if ($document -is [array]) { throw "$Path holds a JSON array; save one document per file or one per line." }
        [pscustomobject]@{ Document = $document; Raw = $raw }
    }
}

function Assert-UsageDocument {
    param($Document, [string]$Origin)
    $names = @($Document.PSObject.Properties | ForEach-Object { $_.Name })
    $unknown = @($names | Where-Object { $documentProperties -notcontains $_ })
    if ($unknown.Count -gt 0 -or "$($Document.schema)" -ne 'service-account-usage-v1') {
        throw "$Origin is not a service-account-usage-v1 document from Get-ServiceAccountUsage.ps1."
    }

    if (Test-SecretName -Value $Document) {
        throw "$Origin contains a password-like property. Do not upload it; run Get-ServiceAccountUsage.ps1 again."
    }

    if ("$($Document.serverName)" -notmatch $serverPattern) { throw "$Origin has no valid serverName." }
    $wanted = (@($Account | ForEach-Object { $_.ToUpperInvariant() }) | Sort-Object) -join '|'
    $found = (@($Document.accounts | ForEach-Object { "$_".ToUpperInvariant() }) | Sort-Object) -join '|'
    if ($wanted -ne $found) { throw "$Origin searched other accounts ($found); every document must use exactly -Account." }
    $verification = $Document.verification
    if ($ExpectedAccount) {
        if ($null -eq $verification -or "$($verification.ExpectedAccount)".ToUpperInvariant() -ne $ExpectedAccount.ToUpperInvariant()) {
            throw "$Origin was not run with -ExpectedAccount $ExpectedAccount."
        }
    }
    elseif ($null -ne $verification) {
        throw "$Origin is a gMSA check; give the same -ExpectedAccount here."
    }
}

$results = [System.Collections.Generic.List[object]]::new()
$answered = @{}
$notReached = [System.Collections.Generic.List[object]]::new()
$tool = 'Combined'

if ($PSCmdlet.ParameterSetName -in @('CombineNames', 'CombineList')) {
    $files = @(@(foreach ($pattern in $CombinePath) { Get-ChildItem -Path $pattern -File | ForEach-Object { $_.FullName } }) | Sort-Object -Unique)
    if ($files.Count -eq 0) { throw 'No document file matched -CombinePath.' }
    foreach ($file in $files) {
        foreach ($entry in @(Read-UsageDocument -Path $file)) {
            $document = $entry.Document
            Assert-UsageDocument -Document $document -Origin $file
            $key = "$($document.serverName)".ToUpperInvariant()
            if (-not $plannedKeys.ContainsKey($key)) { throw "Server $($document.serverName) in $file is not in the planned list." }
            if ($answered.ContainsKey($key)) { throw "Server $($document.serverName) appears twice; keep one document per server." }
            $answered[$key] = $true
            $results.Add($entry.Raw)
        }
    }

    $unreachable = @{}
    foreach ($name in $UnreachableComputerName) {
        $key = $name.ToUpperInvariant()
        if (-not $plannedKeys.ContainsKey($key)) { throw "Unreachable server $name is not in the planned list." }
        if ($answered.ContainsKey($key)) { throw "Unreachable server $name has a document; remove one of them." }
        $unreachable[$key] = $true
    }

    foreach ($name in $planned) {
        $key = $name.ToUpperInvariant()
        if ($answered.ContainsKey($key)) { continue }
        $reason = if ($unreachable.ContainsKey($key)) { 'Unreachable' } else { 'NoResult' }
        $notReached.Add([pscustomobject][ordered]@{ serverName = $name; reason = $reason })
    }
}
else {
    # PROPOSED (ADR-0024): JEA sessions run in NoLanguage mode (no variables), so the remote command line is built here.
    # Every value has already matched a strict pattern without quotes, spaces or operators, so the literal cannot inject.
    $tool = 'Jea'
    $accountList = ($Account | ForEach-Object { "'$_'" }) -join ','
    $commandText = "Get-SecureOpsAccountUsage -Account $accountList"
    if ($ExpectedAccount) { $commandText += " -ExpectedAccount '$ExpectedAccount'" }
    $remote = [scriptblock]::Create($commandText)
    $sessionOption = New-PSSessionOption -OpenTimeout ($OpenTimeoutSeconds * 1000) -OperationTimeout ($OperationTimeoutSeconds * 1000) -NoMachineProfile
    $errors = @()
    $answers = @(Invoke-Command -ComputerName @($planned) -ConfigurationName $ConfigurationName -SessionOption $sessionOption `
            -ThrottleLimit $ThrottleLimit -ScriptBlock $remote -ErrorAction SilentlyContinue -ErrorVariable errors)
    foreach ($answer in $answers) {
        $key = "$($answer.PSComputerName)".ToUpperInvariant()
        if (-not $plannedKeys.ContainsKey($key) -or $answered.ContainsKey($key)) { continue }
        $document = $answer | Select-Object -Property $documentProperties
        $document.serverName = $plannedKeys[$key]
        $answered[$key] = $true
        $results.Add(($document | ConvertTo-Json -Depth 10 -Compress))
    }

    foreach ($name in $planned) {
        if ($answered.ContainsKey($name.ToUpperInvariant())) { continue }
        $failure = @($errors | Where-Object { $_.TargetObject -eq $name -or "$($_.OriginInfo.PSComputerName)" -eq $name }) | Select-Object -First 1
        $reason = if ($failure -and $failure.FullyQualifiedErrorId -match 'PSSessionStateBroken|WinRMOperationTimeout|CannotConnect|ComputerNotFound|AccessDenied') { 'Unreachable' } else { 'NoResult' }
        $notReached.Add([pscustomobject][ordered]@{ serverName = $name; reason = $reason })
    }
}

# The header is serialized here; each server document is appended as the exact text that was read, so no date or number is
# re-formatted on the way to the upload.
$header = [pscustomobject][ordered]@{
    schema          = 'service-account-usage-scan-v1'
    generatedAt     = [DateTimeOffset]::UtcNow.ToString('o')
    tool            = $tool
    accounts        = @($Account)
    expectedAccount = if ($ExpectedAccount) { $ExpectedAccount } else { $null }
    plannedServers  = @($planned)
    notReached      = @($notReached)
}
$headerJson = ConvertTo-Json -InputObject $header -Depth 4 -Compress
$bundleJson = $headerJson.Substring(0, $headerJson.Length - 1) + ',"results":[' + ($results -join ',') + ']}'

$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
[System.IO.File]::WriteAllText($target, $bundleJson, [System.Text.UTF8Encoding]::new($false))

$componentCount = 0
foreach ($raw in $results) { $componentCount += @(($raw | ConvertFrom-Json).components).Count }
[pscustomobject][ordered]@{
    OutputPath = $target
    Planned    = $planned.Count
    Answered   = $results.Count
    NotReached = $notReached.Count
    Components = $componentCount
    Note       = 'Upload this file on the account page (Kullanim taramasi). NoResult/Unreachable never means "not used"; a person verifies.'
}
