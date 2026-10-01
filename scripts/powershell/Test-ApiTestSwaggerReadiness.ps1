[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Demo', 'Test')]
    [string] $ExpectedEnvironment,

    [Parameter(Mandatory = $true)]
    [bool] $SwaggerEnabled
)

$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not $SwaggerEnabled) {
    throw 'Swagger:Enabled must be explicitly true before a TEST artifact can be declared Swagger-ready.'
}

$requiredFiles = @(
    'SecureOps.Api.dll',
    'SecureOps.Infrastructure.dll',
    'SecureOps.Api.deps.json',
    'SecureOps.Api.runtimeconfig.json',
    'Swashbuckle.AspNetCore.Swagger.dll',
    'Swashbuckle.AspNetCore.SwaggerGen.dll',
    'Swashbuckle.AspNetCore.SwaggerUI.dll'
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $file) -PathType Leaf)) {
        throw "Swagger readiness failed: required publish file is missing: $file"
    }
}

$deps = Get-Content -LiteralPath (Join-Path $publishPath 'SecureOps.Api.deps.json') -Raw | ConvertFrom-Json
$dependencyNames = @($deps.libraries.PSObject.Properties.Name)
foreach ($package in @('Swashbuckle.AspNetCore.Swagger', 'Swashbuckle.AspNetCore.SwaggerGen', 'Swashbuckle.AspNetCore.SwaggerUI')) {
    if (-not ($dependencyNames | Where-Object { $_ -like "$package/*" })) {
        throw "Swagger readiness failed: deps.json does not contain $package."
    }
}

$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishPath 'SecureOps.Api.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.tfm -ne 'net8.0') {
    throw "Swagger readiness failed: expected net8.0 but found '$($runtimeConfig.runtimeOptions.tfm)'."
}

[pscustomobject]@{
    Ready = $true
    Environment = $ExpectedEnvironment
    SwaggerEnabled = $SwaggerEnabled
    PublishDirectory = $publishPath
    RequiredFiles = $requiredFiles.Count
}
