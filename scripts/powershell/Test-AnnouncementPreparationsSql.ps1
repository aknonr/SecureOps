param([Parameter(Mandatory)][ValidatePattern('^OcoPreparation[A-Za-z0-9_]{1,25}$')][string]$DatabaseSuffix)
$ErrorActionPreference = 'Stop'
# The established harness refuses existing databases; never target Claude's source-job resources.
& "$PSScriptRoot/Test-ResourceCatalogueSql.ps1" -DatabaseSuffix $DatabaseSuffix -IncludeAnnouncementPreparations
if ($LASTEXITCODE -ne 0) { throw 'Fresh local draft setup failed.' }
