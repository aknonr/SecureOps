[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApiReport,
    [Parameter(Mandatory)][string]$WorkerReport
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Readiness([string]$Path) {
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or $file.Length -gt 1048576) { throw 'DUR: tanilama dosyasi gecersiz veya cok buyuk.' }
    $report = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$report.Settings -or !$report.RuntimeIdentity -or !$report.ProcessId) { throw 'DUR: yeni surumun tanilama raporu gerekli.' }
    return $report
}
$api = Read-Readiness $ApiReport
$worker = Read-Readiness $WorkerReport
$allowed = '^(Announcements:Enabled|Hangfire:(Enabled|SchemaName|Queue|PrepareSchema)|AnnouncementSource:(Enabled|CollectionProvider|ServiceProvider|SiteCode|ProviderMachineName|ServiceInstanceBaseObject|ServiceNameSelect|ChangeBaseObject|Profiles:(NonProd|Prod01|Prod02|ProdSingle|ProdRPA):(CollectionId|Fingerprint))|ConnectionStrings:SecureOpsDb:TargetFingerprint|TuruncuHat:TargetFingerprint)$'
$required = @('Announcements:Enabled','Hangfire:Enabled','Hangfire:SchemaName','Hangfire:Queue','Hangfire:PrepareSchema',
    'AnnouncementSource:Enabled','AnnouncementSource:CollectionProvider','AnnouncementSource:ServiceProvider',
    'AnnouncementSource:SiteCode','AnnouncementSource:ProviderMachineName','AnnouncementSource:ServiceInstanceBaseObject',
    'AnnouncementSource:ServiceNameSelect','AnnouncementSource:ChangeBaseObject',
    'ConnectionStrings:SecureOpsDb:TargetFingerprint','TuruncuHat:TargetFingerprint')
$keys = @($required + @($api.Settings.Key) + @($worker.Settings.Key) | Where-Object { $_ -cmatch $allowed } | Sort-Object -Unique)
$different = 0
foreach ($key in $keys) {
    $left = @($api.Settings | Where-Object { $_.Key -ceq $key })
    $right = @($worker.Settings | Where-Object { $_.Key -ceq $key })
    $matches = $left.Count -eq 1 -and $right.Count -eq 1 -and $left[0].Value -ceq $right[0].Value
    if (!$matches) { $different++ }
    [pscustomobject]@{ Ayar=$key; Sonuc=$(if ($matches) {'Eslesiyor'} else {'FARKLI / EKSIK'}); API=($left | ForEach-Object { $_.Value }) -join ','; Worker=($right | ForEach-Object { $_.Value }) -join ',' }
}
Write-Information "API kimligi: $($api.RuntimeIdentity); PID: $($api.ProcessId); Worker kimligi: $($worker.RuntimeIdentity); PID: $($worker.ProcessId)" -InformationAction Continue
Write-Information "Fark sayisi: $different. Eslesme kaynak baglantisi, SMTP veya yazma izni kaniti degildir. Yapilandirma degistirilmedi." -InformationAction Continue
if ($different -gt 0) { exit 2 }
