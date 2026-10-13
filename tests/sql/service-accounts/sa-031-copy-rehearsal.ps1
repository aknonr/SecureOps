[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^SecureOps_Sa[A-Za-z0-9_]{1,40}$')]
    [string]$Database,
    [switch]$SeedSynthetic
)

# Rehearses migration 031 (requested gMSA name) on a LOCAL copy that is at 030, like an installed system: optional synthetic
# seed, before/after fingerprints of every svcacct table (rows and definition, the new column excluded) and of all database
# permissions, a failure-injected run that must leave no trace, the real 031, replay refusal, and the runtime roles' access to
# the new column (through a loginless user created only in this copy). Only the isolated per-user LocalDB instance is used;
# never a shared, installed or corporate server. Writes nothing to the repository; its working SQL files stay in a fresh
# folder under %TEMP% (printed at the end).
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$server = '(localdb)\SecureOpsResourcesV1'
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$work = Join-Path ([IO.Path]::GetTempPath()) ('sa-031-rehearsal-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work

function Invoke-Sql([string]$Query, [switch]$AllowFailure) {
    $file = Join-Path $work ([Guid]::NewGuid().ToString('N') + '.sql')
    Set-Content -LiteralPath $file -Value $Query -Encoding UTF8
    $output = & $sqlcmd -S $server -d $Database -E -I -b -h -1 -W -i $file 2>&1
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) { throw "Rehearsal step failed; $Database retained for inspection. $output" }
    return , @($output | ForEach-Object { "$_" } | Where-Object { $_ -ne '' })
}

$state = (Invoke-Sql "SET NOCOUNT ON; SELECT CONCAT(CASE WHEN OBJECT_ID(N'svcacct.UsageScans', N'U') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'svcacct.WorkRequests', N'RequestedGmsaName') IS NULL THEN 0 ELSE 1 END);")[0]
if ($state -ne '10') { throw "Expected a database at 030 without 031 (state $state)." }

if ($SeedSynthetic) {
    $null = Invoke-Sql @'
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @by uniqueidentifier = '5A000000-0000-4000-8000-0000000031AA', @now datetimeoffset(7) = SYSUTCDATETIME();
BEGIN TRANSACTION;
INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, Domain, NormalizedDomain, IdentityKey, IdentityState, LifecycleState, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
SELECT NEWID(), CONCAT(N'svc_syn031_', n), UPPER(CONCAT(N'svc_syn031_', n)), N'SYN', N'SYN', CONCAT(N'D:SYN|', UPPER(CONCAT(N'svc_syn031_', n))), 'Provisional', 'Active',
    CASE WHEN n % 3 = 0 THEN N'Sentetik not' END, @now, @by, @now, @by
FROM (SELECT TOP (300) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects) x;
INSERT INTO svcacct.WorkRequests(Id, AccountId, ActionType, Status, PlanStart, PlanEnd, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
SELECT NEWID(), a.Id, CASE ABS(CHECKSUM(a.Id)) % 4 WHEN 0 THEN 'GmsaConversion' WHEN 1 THEN 'GmsaHandover' WHEN 2 THEN 'Review' ELSE 'Evaluate' END, 'Open',
    CASE WHEN ABS(CHECKSUM(a.Id)) % 2 = 0 THEN CAST('2026-10-01' AS date) END, CASE WHEN ABS(CHECKSUM(a.Id)) % 2 = 0 THEN CAST('2026-11-01' AS date) END,
    N'Sentetik talep', @now, @by, @now, @by
FROM svcacct.Accounts a WHERE a.AccountName LIKE N'svc[_]syn031[_]%';
INSERT INTO svcacct.IdentityTransitions(Id, AccountId, Target, Suitability, DecisionNote, DecidedBy, DecidedAt, PlannedOn, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
SELECT TOP (120) NEWID(), a.Id, 'gMSA', CASE WHEN ABS(CHECKSUM(a.Id)) % 2 = 0 THEN 'Review' ELSE 'Eligible' END,
    CASE WHEN ABS(CHECKSUM(a.Id)) % 2 = 0 THEN NULL ELSE N'Sentetik karar' END, CASE WHEN ABS(CHECKSUM(a.Id)) % 2 = 0 THEN NULL ELSE @by END,
    CASE WHEN ABS(CHECKSUM(a.Id)) % 2 = 0 THEN NULL ELSE @now END, CAST('2026-12-01' AS date), @now, @by, @now, @by
FROM svcacct.Accounts a WHERE a.AccountName LIKE N'svc[_]syn031[_]%' ORDER BY a.AccountName;
COMMIT TRANSACTION;
'@
}

$fingerprint = @'
SET NOCOUNT ON;
DECLARE @t sysname, @cols nvarchar(max), @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'svcacct') ORDER BY name;
OPEN c; FETCH NEXT FROM c INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @cols = STRING_AGG(CONVERT(nvarchar(max), N't.' + QUOTENAME(name)), N',') WITHIN GROUP (ORDER BY column_id)
    FROM sys.columns WHERE object_id = OBJECT_ID(N'svcacct.' + QUOTENAME(@t)) AND name <> N'RequestedGmsaName';
    SET @sql = N'SELECT CONCAT(N''rows:' + @t + N'='', COUNT(*), N'':'', CONVERT(varchar(64), HASHBYTES(''SHA2_256'', CONVERT(varbinary(max), '
        + N'STRING_AGG(CONVERT(varchar(max), h), '''') WITHIN GROUP (ORDER BY h))), 2)) FROM (SELECT CONVERT(varchar(64), HASHBYTES(''SHA2_256'', '
        + N'(SELECT ' + @cols + N' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)), 2) AS h FROM svcacct.' + QUOTENAME(@t) + N' t) x;';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM c INTO @t;
END;
SELECT CONCAT(N'def:', t.name, N'=', CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), CONCAT(
    (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(c.name, N':', TYPE_NAME(c.user_type_id), N':', c.max_length, N':', c.is_nullable, N':', c.column_id)), N'|')
        WITHIN GROUP (ORDER BY c.column_id) FROM sys.columns c WHERE c.object_id = t.object_id AND c.name <> N'RequestedGmsaName'), N'#',
    (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(i.name, N':', i.type, N':', i.is_unique, N':', i.filter_definition)), N'|') WITHIN GROUP (ORDER BY i.index_id)
        FROM sys.indexes i WHERE i.object_id = t.object_id), N'#',
    (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(ic.index_id, N':', ic.column_id, N':', ic.key_ordinal, N':', ic.is_included_column)), N'|')
        WITHIN GROUP (ORDER BY ic.index_id, ic.index_column_id) FROM sys.index_columns ic WHERE ic.object_id = t.object_id), N'#',
    (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(k.name, N':', k.definition, N':', k.is_not_trusted)), N'|') WITHIN GROUP (ORDER BY k.name)
        FROM sys.check_constraints k WHERE k.parent_object_id = t.object_id), N'#',
    (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(f.name, N':', f.is_not_trusted)), N'|') WITHIN GROUP (ORDER BY f.name)
        FROM sys.foreign_keys f WHERE f.parent_object_id = t.object_id), N'#',
    (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(d.name, N':', d.definition)), N'|') WITHIN GROUP (ORDER BY d.name)
        FROM sys.default_constraints d WHERE d.parent_object_id = t.object_id), N'#',
    (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(tr.name, N':', OBJECT_DEFINITION(tr.object_id))), N'|') WITHIN GROUP (ORDER BY tr.name)
        FROM sys.triggers tr WHERE tr.parent_id = t.object_id)))), 2))
FROM sys.tables t WHERE t.schema_id = SCHEMA_ID(N'svcacct') ORDER BY t.name;
SELECT CONCAT(N'perm=', COUNT(*), N':', CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), STRING_AGG(CONVERT(nvarchar(max), CONCAT(p.class, N':',
    COALESCE(OBJECT_SCHEMA_NAME(p.major_id) + N'.' + OBJECT_NAME(p.major_id), CONVERT(nvarchar(20), p.major_id)), N':', p.minor_id, N':', USER_NAME(p.grantee_principal_id),
    N':', p.permission_name, N':', p.state_desc)), N'|') WITHIN GROUP (ORDER BY p.class, p.major_id, p.minor_id, p.grantee_principal_id, p.permission_name))), 2))
FROM sys.database_permissions p;
'@

$before = Invoke-Sql $fingerprint
Write-Host "fingerprint before: $($before.Count) lines; requests/transitions: $(($before | Where-Object { $_ -like 'rows:WorkRequests=*' -or $_ -like 'rows:IdentityTransitions=*' }) -join ', ')"

# Failure injection: the reviewed candidate with a THROW just before COMMIT must leave nothing behind.
$candidate = Get-Content -LiteralPath (Join-Path $root 'sql\pending\service-accounts\SA-005-requested-gmsa-name.sql') -Raw
if (-not $candidate.Contains("COMMIT TRANSACTION;")) { throw 'Candidate layout changed; update the injection point.' }
$injected = Join-Path $work 'SA-005-injected.sql'
Set-Content -LiteralPath $injected -Encoding UTF8 -Value $candidate.Replace("COMMIT TRANSACTION;", "THROW 51999, 'Injected rehearsal failure before commit.', 1;`r`nCOMMIT TRANSACTION;")
& $sqlcmd -S $server -d $Database -E -I -b -i $injected 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) { throw 'Injected failure did not fail.' }
$afterInjection = Invoke-Sql $fingerprint
if ((Compare-Object $before $afterInjection)) { throw 'Injected failure left a trace.' }
if ((Invoke-Sql "SET NOCOUNT ON; SELECT CASE WHEN COL_LENGTH(N'svcacct.WorkRequests', N'RequestedGmsaName') IS NULL AND COL_LENGTH(N'svcacct.IdentityTransitions', N'RequestedGmsaName') IS NULL THEN 'clean' ELSE 'TRACE' END;")[0] -ne 'clean') { throw 'Injected failure left a column.' }
Write-Host 'failure before commit: no trace (fingerprints identical, no column)'

Push-Location (Join-Path $root 'sql\migrations')
try {
    & $sqlcmd -S $server -d $Database -E -I -b -i '031-service-account-requested-gmsa-name.sql'
    if ($LASTEXITCODE -ne 0) { throw '031 failed.' }
    Write-Host 'applied 031-service-account-requested-gmsa-name.sql'
    & $sqlcmd -S $server -d $Database -E -I -b -i '031-service-account-requested-gmsa-name.sql' 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { throw '031 replay was not refused.' }
    Write-Host '031 replay refused as expected'
} finally { Pop-Location }

$after = Invoke-Sql $fingerprint
$diff = Compare-Object $before $after
if ($diff) { $diff | Format-Table -AutoSize | Out-String -Width 250 | Write-Host; throw 'Existing rows, definitions or permissions changed.' }
Write-Host "fingerprint after: identical ($($after.Count) lines: every svcacct table's rows and definition without the new column, all permissions)"

$columns = Invoke-Sql @'
SET NOCOUNT ON;
SELECT CONCAT(OBJECT_NAME(c.object_id), N'.', c.name, N' ', TYPE_NAME(c.user_type_id), N'(', c.max_length / 2, N') ', CASE WHEN c.is_nullable = 1 THEN N'NULL' ELSE N'NOT NULL' END,
    CASE WHEN c.default_object_id = 0 THEN N'' ELSE N' DEFAULT' END)
FROM sys.columns c WHERE c.name = N'RequestedGmsaName' AND OBJECT_SCHEMA_NAME(c.object_id) = N'svcacct' ORDER BY 1;
SELECT CONCAT(N'non-null names: ', (SELECT COUNT(*) FROM svcacct.WorkRequests WHERE RequestedGmsaName IS NOT NULL) + (SELECT COUNT(*) FROM svcacct.IdentityTransitions WHERE RequestedGmsaName IS NOT NULL));
'@
$columns | ForEach-Object { Write-Host $_ }
if ($columns -notcontains 'IdentityTransitions.RequestedGmsaName nvarchar(256) NULL' -or $columns -notcontains 'WorkRequests.RequestedGmsaName nvarchar(256) NULL' -or $columns -notcontains 'non-null names: 0') {
    throw 'Unexpected column shape or values.'
}

# Runtime roles: the API role reads and writes the column through its existing table grants; the Worker role reads requests only.
$roles = Invoke-Sql @'
SET NOCOUNT ON;
IF DATABASE_PRINCIPAL_ID(N'syn_031_api') IS NULL CREATE USER syn_031_api WITHOUT LOGIN;
IF DATABASE_PRINCIPAL_ID(N'syn_031_worker') IS NULL CREATE USER syn_031_worker WITHOUT LOGIN;
ALTER ROLE svcacct_api_runtime ADD MEMBER syn_031_api;
ALTER ROLE svcacct_worker_runtime ADD MEMBER syn_031_worker;
EXECUTE AS USER = N'syn_031_api';
SELECT CONCAT(N'api: select ', HAS_PERMS_BY_NAME(N'svcacct.WorkRequests', N'OBJECT', N'SELECT', N'RequestedGmsaName', N'COLUMN'),
    N', update requests ', HAS_PERMS_BY_NAME(N'svcacct.WorkRequests', N'OBJECT', N'UPDATE', N'RequestedGmsaName', N'COLUMN'),
    N', update transitions ', HAS_PERMS_BY_NAME(N'svcacct.IdentityTransitions', N'OBJECT', N'UPDATE', N'RequestedGmsaName', N'COLUMN'));
REVERT;
EXECUTE AS USER = N'syn_031_worker';
SELECT CONCAT(N'worker: select ', HAS_PERMS_BY_NAME(N'svcacct.WorkRequests', N'OBJECT', N'SELECT', N'RequestedGmsaName', N'COLUMN'),
    N', update requests ', HAS_PERMS_BY_NAME(N'svcacct.WorkRequests', N'OBJECT', N'UPDATE', N'RequestedGmsaName', N'COLUMN'),
    N', transitions ', COALESCE(HAS_PERMS_BY_NAME(N'svcacct.IdentityTransitions', N'OBJECT', N'SELECT'), 0));
REVERT;
'@
$roles | ForEach-Object { Write-Host $_ }
if ($roles -notcontains 'api: select 1, update requests 1, update transitions 1' -or $roles -notcontains 'worker: select 1, update requests 0, transitions 0') {
    throw 'Unexpected runtime role access.'
}

Write-Host "031 rehearsal passed on $Database (working files: $work)"
exit 0
