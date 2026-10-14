@{
    # ADR-0024 R2. Read-only service account usage discovery; the JEA role exposes only Get-SecureOpsAccountUsage.
    # The version is the endpoint version: Install-SecureOpsServiceAccountUsageEndpoint.ps1 installs it under this number.
    RootModule        = 'SecureOps.ServiceAccountUsage.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = '5d3f2c71-6a0e-4b8f-9d4e-2c1a7f0b6e93'
    Author            = 'SecureOps'
    CompanyName       = 'CONTOSO (placeholder)'
    Description       = 'Read-only discovery of where service accounts run (Windows services, scheduled tasks, IIS) and post-conversion gMSA check.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @('Get-SecureOpsAccountUsage', 'ConvertTo-SoAccountKey', 'Test-SoAccountMatch', 'Read-SoIisIdentity', 'Find-SoAccountUsage', 'Test-SoGmsaConversion')
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
