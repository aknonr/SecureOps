[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('ApiMemory', 'ApiSql', 'Ui')][string]$Role,
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$PrivateRoot,
    [string]$Database
)
$ErrorActionPreference = 'Stop'
$Payload = (Resolve-Path -LiteralPath $Payload).Path
$PrivateRoot = [IO.Path]::GetFullPath($PrivateRoot)
if ($Payload.StartsWith('\\') -or $PrivateRoot.StartsWith('\\') -or
    $PrivateRoot.StartsWith($Payload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $PrivateRoot.Equals($Payload, [StringComparison]::OrdinalIgnoreCase)) { throw 'Private state must be local and outside the payload.' }
if (Get-ChildItem -LiteralPath $Payload -Filter 'appsettings*.json') { throw 'Use a fresh configuration-free archive extraction.' }
if ($Role -eq 'ApiSql' -and $Database -notmatch '^SecureOps_ResourcesV1_[A-Za-z0-9]+$') { throw 'Existing isolated test-owned database required.' }
New-Item -ItemType Directory -Path $PrivateRoot -Force | Out-Null
$common = @('--Logging:LogLevel:Default=Warning', '--DataProtection:Mode=FileSystemDpapi',
    "--DataProtection:ApplicationName=SecureOps.Rc617.Local.$Role", "--DataProtection:KeyRingPath=$PrivateRoot\keys-$Role")
Push-Location $Payload
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    if ($Role -eq 'Ui') {
        $env:ASPNETCORE_ENVIRONMENT = 'Demo'
        & dotnet SecureOps.Ui.dll --urls https://localhost:63950 --DemoMode:Enabled=true --DemoMode:AllowMockAuthentication=true `
            --DemoMode:ApiDemoActor=platform-admin --IdentityLookupApi:BaseAddress=http://localhost:63949/ @common
    }
    else {
        $env:ASPNETCORE_ENVIRONMENT = 'Test'
        $store = if ($Role -eq 'ApiSql') { 'SqlServer' } else { 'InMemory' }
        $connection = if ($Role -eq 'ApiSql') { "Data Source=(localdb)\SecureOpsResourcesV1;Initial Catalog=$Database;Integrated Security=True;Encrypt=False;Connect Timeout=15" } else { '' }
        & dotnet SecureOps.Api.dll --urls http://localhost:63949 --DemoAuth:Enabled=true --Access:DemoCompatibilityEnabled=true `
            "--Access:RepositoryProvider=$store" "--Audit:Provider=$store" --Audit:Queue:Enabled=false `
            "--SessionSecurity:RepositoryProvider=$store" "--OperationalRecords:RepositoryProvider=$store" `
            "--ConnectionStrings:SecureOpsDb=$connection" "--InUseReports:Directory=$PrivateRoot\reports" `
            --IdentityLookup:Provider=Mock --OperationalRecords:SourceProvider=TuruncuHat --Jira:Provider=Corporate `
            --OperationalRecords:ReadOnlyIntegrationMode=true --OperationalRecords:ControlledTestWritesEnabled=false `
            --OperationalRecords:SourceCloseEnabled=false --TuruncuHat:BaseUrl=https://localhost:63948/source/ `
            --TuruncuHat:Authorization=fixture-only --TuruncuHat:Username=fixture --TuruncuHat:Password=fixture `
            --TuruncuHat:TenantId=1 --TuruncuHat:RelatedGroupId=68 --TuruncuHat:ExcludedDccIds:0=4241 `
            --TuruncuHat:SourceBaseObject=SMSS_oRFF --TuruncuHat:SessionLifetimeSeconds=300 `
            --Jira:BaseUrl=https://localhost:63948/jira/ '--Jira:Authorization=Basic Zml4dHVyZTpmaXh0dXJl' `
            --Jira:IssueTypeId=1 --Jira:TeamCustomField=customfield_1 --Jira:TeamValue=fixture `
            --Jira:RequesterWatcherCustomField=customfield_2 --Jira:Labels:0=fixture @common
    }
    if ($LASTEXITCODE -ne 0) { throw "Packaged $Role exited with $LASTEXITCODE" }
}
finally { $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment; Pop-Location }
