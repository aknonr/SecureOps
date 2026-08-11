param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$ZipPath
)

$required = 'System.DirectoryServices.AccountManagement.dll'
$assemblyPath = Join-Path $PublishDirectory $required
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Required Active Directory runtime assembly is missing: $required"
}

$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
if ($assemblyName.Name -ne 'System.DirectoryServices.AccountManagement' -or $assemblyName.Version -lt [version]'8.0.0.1') {
    throw "Unexpected Active Directory runtime assembly identity: $($assemblyName.FullName)"
}

if ($ZipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        if (-not ($archive.Entries.FullName -contains $required)) {
            throw "Required Active Directory runtime assembly is missing from ZIP: $required"
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Output "Active Directory runtime dependency gate passed: $($assemblyName.FullName)"
