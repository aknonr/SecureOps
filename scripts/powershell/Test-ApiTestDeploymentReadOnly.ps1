[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][uri]$BaseUri,
    [Parameter(Mandatory = $true)][string]$DemoActor,
    [string]$DemoActorHeaderName = 'X-SecureOps-Demo-Actor'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$headers = @{ $DemoActorHeaderName = $DemoActor }
$checks = @(
    @{ Path = '/api/v1/health'; Headers = $headers },
    @{ Path = '/api/v1/health/audit-store'; Headers = $headers },
    @{ Path = '/api/v1/health/identity-provider'; Headers = $headers },
    @{ Path = '/swagger/index.html'; Headers = @{} },
    @{ Path = '/swagger/v1/swagger.json'; Headers = @{} }
)

foreach ($check in $checks)
{
    $uri = [uri]::new($BaseUri, $check.Path)
    $response = Invoke-WebRequest -Uri $uri -Headers $check.Headers -Method Get -UseBasicParsing
    if ($response.StatusCode -ne 200)
    {
        throw "Read-only TEST smoke check failed: $uri returned $($response.StatusCode)."
    }

    [pscustomobject]@{ Uri = $uri.AbsoluteUri; StatusCode = $response.StatusCode }
}
