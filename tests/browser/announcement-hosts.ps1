param([Parameter(Mandatory)][string]$EvidenceRoot, [Parameter(Mandatory)][ValidatePattern('^Oco[A-Za-z0-9_]{1,36}$')][string]$DatabaseSuffix, [string]$PayloadRoot = $EvidenceRoot, [ValidateRange(1024,65533)][int]$Port = 5431, [switch]$FinalPresentation)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$payload = (Resolve-Path -LiteralPath $PayloadRoot).Path
if (!(Test-Path "$payload/api/SecureOps.Api.dll") -or !(Test-Path "$payload/ui/SecureOps.Ui.dll")) { throw 'Publish both local payloads first.' }
foreach ($candidate in @($Port,($Port+1),($Port+2))) { if (Get-NetTCPConnection -LocalPort $candidate -State Listen -ErrorAction SilentlyContinue) { throw "Port $candidate is occupied." } }
if (Test-Path "$root/assets") { throw 'Use a fresh test-owned evidence directory.' }
New-Item -ItemType Directory "$root/assets" | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap(320,64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try { $graphics.Clear([System.Drawing.Color]::White); $graphics.DrawString('YEREL TEST', [System.Drawing.SystemFonts]::DefaultFont, [System.Drawing.Brushes]::Black, 12, 20); $bitmap.Save("$root/assets/banner.png", [System.Drawing.Imaging.ImageFormat]::Png) }
finally { $graphics.Dispose(); $bitmap.Dispose() }
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
        $bitmap = New-Object System.Drawing.Bitmap(320,64)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.Clear([System.Drawing.Color]::White); $graphics.DrawString("LOCAL TEST $role", [System.Drawing.SystemFonts]::DefaultFont, [System.Drawing.Brushes]::Black, 4, 20); $bitmap.Save("$root/assets/$role.png", [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
        [Environment]::SetEnvironmentVariable("Announcements__Banners__$role", "$role.png", 'Process')
        [Environment]::SetEnvironmentVariable("Announcements__Bundles__bundle-v1__Assets__$role", $role, 'Process')
    }
    $env:Announcements__Bundles__bundle_v1 = $null
    [Environment]::SetEnvironmentVariable('Announcements__Bundles__bundle-v1__Footer', 'LOCAL TEST - not approved corporate branding', 'Process')
    [Environment]::SetEnvironmentVariable('Announcements__Bundles__bundle-v1__Label', 'Yerel test paketi', 'Process')
}
$env:DataProtection__Mode = 'Ephemeral'
$env:Oidc__Enabled = 'false'
$api = Start-Process dotnet -ArgumentList @('SecureOps.Api.dll',"--urls=http://127.0.0.1:$Port") -WorkingDirectory "$payload/api" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/api-host.log" -RedirectStandardError "$root/api-host.err"
$env:IdentityLookupApi__BaseAddress = "http://127.0.0.1:$Port/"
$env:DemoMode__Enabled = 'true'
$env:DemoMode__AllowMockAuthentication = 'true'
$env:DemoMode__ApiDemoActor = 'platform-admin'
$ui = Start-Process dotnet -ArgumentList @('SecureOps.Ui.dll',"--urls=https://localhost:$($Port+1)") -WorkingDirectory "$payload/ui" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/ui-host.log" -RedirectStandardError "$root/ui-host.err"
$env:DemoMode__ApiDemoActor = 'team-lead'
$denied = Start-Process dotnet -ArgumentList @('SecureOps.Ui.dll',"--urls=https://localhost:$($Port+2)") -WorkingDirectory "$payload/ui" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$root/denied-host.log" -RedirectStandardError "$root/denied-host.err"
@{ Api = $api.Id; Ui = $ui.Id; Denied = $denied.Id; Database = "SecureOps_ResourcesV1_$DatabaseSuffix" } | ConvertTo-Json | Set-Content "$root/hosts.json"
