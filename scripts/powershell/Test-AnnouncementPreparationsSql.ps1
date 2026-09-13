param([Parameter(Mandatory)][ValidatePattern('^OcoPreparation[A-Za-z0-9_]{1,25}$')][string]$DatabaseSuffix)
$ErrorActionPreference = 'Stop'
# The established harness refuses existing databases; never target Claude's source-job resources.
& "$PSScriptRoot/Test-ResourceCatalogueSql.ps1" -DatabaseSuffix $DatabaseSuffix -IncludeAnnouncementDrafts
if ($LASTEXITCODE -ne 0) { throw 'Fresh local draft setup failed.' }
$schema = Join-Path $PSScriptRoot '../../sql/pending/announcement-preparations.sql'
& sqlcmd -S '(localdb)\SecureOpsResourcesV1' -d "SecureOps_ResourcesV1_$DatabaseSuffix" -E -I -b -i $schema
if ($LASTEXITCODE -ne 0) { throw 'Preparation candidate failed; retain the test database.' }
