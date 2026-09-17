param([Parameter(Mandatory)][string]$EvidenceRoot, [Parameter(Mandatory)][ValidatePattern('^Oco[A-Za-z0-9_]{1,36}$')][string]$DatabaseSuffix, [string]$PayloadRoot = $EvidenceRoot, [ValidateRange(1024,65533)][int]$Port = 5431, [switch]$FinalPresentation, [switch]$ResumeApiOnly, [switch]$SourceReview, [switch]$OperationalRecordSimulation, [string]$OriginalAssetDirectory, [ValidateRange(1024,65535)][int]$LocalMailPort = 25251, [switch]$ResumeWorkerOnly)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$payload = (Resolve-Path -LiteralPath $PayloadRoot).Path
if ($ResumeWorkerOnly) {
    if ($ResumeApiOnly -or !$SourceReview) { throw 'Resume exactly one supported host.' }
    $previous = Get-Content -LiteralPath "$root/hosts.json" -Raw | ConvertFrom-Json
    if ($previous.Database -ne "SecureOps_ResourcesV1_$DatabaseSuffix" -or (Get-Process -Id $previous.Worker -ErrorAction SilentlyContinue)) { throw 'Stop only the previous task-owned Worker before resuming.' }
    $ResumeApiOnly = $true
}
if (!(Test-Path "$payload/api/SecureOps.Api.dll") -or !(Test-Path "$payload/ui/SecureOps.Ui.dll")) { throw 'Publish both local payloads first.' }
foreach ($candidate in $(if ($ResumeWorkerOnly) { @() } elseif ($ResumeApiOnly) { @($Port) } else { @($Port,($Port+1),($Port+2)) })) { if (Get-NetTCPConnection -LocalPort $candidate -State Listen -ErrorAction SilentlyContinue) { throw "Port $candidate is occupied." } }
if ($ResumeApiOnly -and (!(Test-Path "$root/hosts.json") -or !(Test-Path "$root/assets/banner.png"))) { throw 'Resume only an existing task-owned acceptance host.' }
if (!$ResumeApiOnly) {
if (Test-Path "$root/assets") { throw 'Use a fresh test-owned evidence directory.' }
New-Item -ItemType Directory "$root/assets" | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap(320,64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try { $graphics.Clear([System.Drawing.Color]::White); $graphics.DrawString('YEREL TEST', [System.Drawing.SystemFonts]::DefaultFont, [System.Drawing.Brushes]::Black, 12, 20); $bitmap.Save("$root/assets/banner.png", [System.Drawing.Imaging.ImageFormat]::Png) }
finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$env:ASPNETCORE_ENVIRONMENT = 'Demo'
$env:DOTNET_ENVIRONMENT = 'Demo'
$env:DemoAuth__Enabled = 'true'
$env:Access__DemoCompatibilityEnabled = 'true'
$env:Access__RepositoryProvider = 'SqlServer'
$env:SessionSecurity__RepositoryProvider = 'SqlServer'
$env:ConnectionStrings__SecureOpsDb = "Data Source=(localdb)\SecureOpsResourcesV1;Initial Catalog=SecureOps_ResourcesV1_$DatabaseSuffix;Integrated Security=True;Encrypt=False;Connect Timeout=15"
$env:IdentityLookup__Provider = 'Mock'
$env:Audit__Provider = 'SqlServer'
$env:OperationalRecords__SourceProvider = 'Disabled'
$env:Jira__Provider = 'Disabled'
if ($OperationalRecordSimulation) {
    $env:OperationalRecords__SourceProvider = 'Simulation'
    $env:Jira__Provider = 'Simulation'
    $env:OperationalRecords__RepositoryProvider = 'SqlServer'
    $env:InUseReports__Directory = "$root/private-reports"
}
$env:OperationalRecords__ReadOnlyIntegrationMode = 'false' # Supported Demo + disabled providers, not corporate read mode.
$env:OperationalRecords__ControlledTestWritesEnabled = 'false'
$env:OperationalRecords__SourceCloseEnabled = 'false'
$env:Announcements__Enabled = 'true'
$env:Announcements__DefaultDisplayOffset = '+03:00'
$env:Announcements__Sender = 'announcements@example.invalid'
$env:Announcements__AssetDirectory = "$root/assets"
$env:Announcements__Banners__synthetic_v1 = $null
$env:Announcements__Banners__synthetic = 'banner.png'
$env:Announcements__BannerLabels__synthetic = 'Yerel test'
if ($FinalPresentation) {
    foreach ($role in @('header','main','logo','linkedin','instagram','youtube')) {
        if (!$ResumeApiOnly) {
        $bitmap = New-Object System.Drawing.Bitmap(320,64)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.Clear([System.Drawing.Color]::White); $graphics.DrawString("LOCAL TEST $role", [System.Drawing.SystemFonts]::DefaultFont, [System.Drawing.Brushes]::Black, 4, 20); $bitmap.Save("$root/assets/$role.png", [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
        [Environment]::SetEnvironmentVariable("Announcements__Banners__$role", "$role.png", 'Process')
        [Environment]::SetEnvironmentVariable("Announcements__Bundles__bundle-v1__Assets__$role", $role, 'Process')
    }
    $env:Announcements__Bundles__bundle_v1 = $null
    [Environment]::SetEnvironmentVariable('Announcements__Bundles__bundle-v1__Footer', 'LOCAL TEST - not approved corporate branding', 'Process')
    [Environment]::SetEnvironmentVariable('Announcements__Bundles__bundle-v1__Label', 'Yerel test paketi', 'Process')
}
$env:DataProtection__Mode = 'FileSystemDpapi' # Test-owned persistent keys keep a transport restart distinct from session revocation.
if ($OriginalAssetDirectory) {
    if (!$FinalPresentation) { throw 'Original assets require the six-role presentation.' }
    $manifest = Get-Content -LiteralPath (Join-Path $OriginalAssetDirectory 'manifest.json') -Raw | ConvertFrom-Json
    $assetNames = @{header='planlimail_duyuru_header.jpg';main='planlimail_duyuru_main.jpg';logo='turkish_technology_logo.jpg';linkedin='planlimail_duyuru_linkedin.png';instagram='planlimail_duyuru_instagram.png';youtube='planlimail_duyuru_youtube.png'}
    foreach ($role in $assetNames.Keys) {
        $name = $assetNames[$role]
        $source = Join-Path $OriginalAssetDirectory $name
        $entry = @($manifest | Where-Object { $_.Name -eq $name })
        if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $source).Hash -ne $entry[0].Sha256 -or (Get-Item -LiteralPath $source).Length -ne $entry[0].Bytes) { throw 'Original asset evidence mismatch.' }
        if (!$ResumeApiOnly) { Copy-Item -LiteralPath $source -Destination (Join-Path "$root/assets" $name) }
        [Environment]::SetEnvironmentVariable("Announcements__Banners__$role", $name, 'Process')
    }
}
$env:AnnouncementMail__Enabled = $SourceReview.ToString()
$env:AnnouncementMail__SelfTestEnabled = $SourceReview.ToString()
$env:AnnouncementMail__SendEnabled = $SourceReview.ToString()
$env:AnnouncementMail__Host = '127.0.0.1'
$env:AnnouncementMail__Port = $LocalMailPort.ToString()
$env:AnnouncementMail__Security = 'PlaintextLoopback'
$env:AnnouncementMail__PolicyRevision = 'synthetic-local-sink-v1'
$env:AnnouncementMail__EnvelopeMode = 'Actor'
$env:AnnouncementMail__AllowedRecipientDomains__0 = 'example.invalid'
$env:AnnouncementMail__TimeoutSeconds = '10'
$env:AnnouncementMail__UserName = $null
$env:AnnouncementMail__Password = $null
$env:DataProtection__ApplicationName = 'SecureOps.Api'
$env:DataProtection__KeyRingPath = "$root/private-api-keys"
$env:Oidc__Enabled = 'false'
$env:Hangfire__Enabled = $SourceReview.ToString()
$env:AnnouncementSource__Enabled = $SourceReview.ToString()
if ($SourceReview) {
    if (!(Test-Path "$payload/worker/SecureOps.Worker.dll")) { throw 'Publish the isolated Worker first.' }
    $env:Hangfire__Queue = 'oco-' + $DatabaseSuffix.Substring($DatabaseSuffix.Length-8).ToLowerInvariant()
    $env:Hangfire__SchemaName = 'HangFire'
    $env:Hangfire__PrepareSchema = 'false'
    $env:Hangfire__WorkerCount = '2'
    $env:Hangfire__QueuePollIntervalSeconds = '1'
    $env:AnnouncementSource__CollectionProvider = 'Fixture'
    $env:AnnouncementSource__ServiceProvider = 'Fixture'
    $env:AnnouncementSource__FixtureDirectory = "$root/fixtures"
    if (!$ResumeApiOnly) { foreach ($area in @('collections','services','changes')) { New-Item -ItemType Directory "$root/fixtures/$area" -Force | Out-Null } }
    foreach ($profile in @('NonProd','Prod01','Prod02')) {
        $values = @{ CollectionId=$profile;Scope="$profile scope";Impact='Local impact';Checks='Local checks';Description='Local description';'To__0'="$profile@example.invalid";'To__1'='remove@example.invalid';'Cc__0'='copy@example.invalid' }
        foreach ($key in $values.Keys) { [Environment]::SetEnvironmentVariable("AnnouncementSource__Profiles__${profile}__$key",$values[$key],'Process') }
        if (!$ResumeApiOnly) {
        $devices = if ($profile -eq 'Prod02') { @('AMBIGUOUS','MISSING') } else { @(1..155 | ForEach-Object { 'DEVICE-'+$_ }) }
        @{ devices=$devices;complete=$true;delayMilliseconds=4000 } | ConvertTo-Json -Depth 4 | Set-Content "$root/fixtures/collections/$profile.json" -Encoding UTF8
        }
    }
    if (!$ResumeApiOnly) {
    foreach ($n in 1..155) { @{candidates=@("Service $n <b>")} | ConvertTo-Json | Set-Content "$root/fixtures/services/DEVICE-$n.json" -Encoding UTF8 }
    @{candidates=@('Candidate A','Candidate B')} | ConvertTo-Json | Set-Content "$root/fixtures/services/AMBIGUOUS.json" -Encoding UTF8
    @{rows=1;startText='2026-09-15T01:00:00.123+03:00';finishText='2026-09-15T02:00:00.456+03:00'} | ConvertTo-Json | Set-Content "$root/fixtures/changes/OCO-SYNTHETIC.json" -Encoding UTF8
    }
    if ($ResumeWorkerOnly) {
        $log = 'worker-resumed-' + [guid]::NewGuid().ToString('N')
        $worker = Start-Process dotnet -ArgumentList 'SecureOps.Worker.dll' -WorkingDirectory "$payload/worker" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/$log.log" -RedirectStandardError "$root/$log.err"
        @{Worker=$worker.Id;Database=$previous.Database} | ConvertTo-Json | Set-Content "$root/$log.json"
        return
    }
    if (!$ResumeApiOnly) { $worker = Start-Process dotnet -ArgumentList 'SecureOps.Worker.dll' -WorkingDirectory "$payload/worker" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/worker-host.log" -RedirectStandardError "$root/worker-host.err" }
}
$log = if ($ResumeApiOnly) { 'api-resumed-' + [guid]::NewGuid().ToString('N') } else { 'api-host' }
$api = Start-Process dotnet -ArgumentList @('SecureOps.Api.dll',"--urls=http://127.0.0.1:$Port") -WorkingDirectory "$payload/api" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/$log.log" -RedirectStandardError "$root/$log.err"
if ($ResumeApiOnly) { @{Api=$api.Id;Port=$Port} | ConvertTo-Json | Set-Content "$root/$log.json"; return }
$env:DataProtection__ApplicationName = 'SecureOps.Ui'
$env:DataProtection__KeyRingPath = "$root/private-ui-keys"
$env:IdentityLookupApi__BaseAddress = "http://127.0.0.1:$Port/"
$env:DemoMode__Enabled = 'true'
$env:DemoMode__AllowMockAuthentication = 'true'
$env:DemoMode__ApiDemoActor = 'platform-admin'
$ui = Start-Process dotnet -ArgumentList @('SecureOps.Ui.dll',"--urls=https://localhost:$($Port+1)") -WorkingDirectory "$payload/ui" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/ui-host.log" -RedirectStandardError "$root/ui-host.err"
$env:DemoMode__ApiDemoActor = 'team-lead'
$denied = Start-Process dotnet -ArgumentList @('SecureOps.Ui.dll',"--urls=https://localhost:$($Port+2)") -WorkingDirectory "$payload/ui" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/denied-host.log" -RedirectStandardError "$root/denied-host.err"
@{ Api = $api.Id; Ui = $ui.Id; Denied = $denied.Id; Worker = $worker.Id; Database = "SecureOps_ResourcesV1_$DatabaseSuffix" } | ConvertTo-Json | Set-Content "$root/hosts.json"
