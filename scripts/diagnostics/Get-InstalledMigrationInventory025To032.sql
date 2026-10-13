-- Read-only DBA inventory. Run in the intended database with metadata visibility
-- and SELECT on security.Roles / audit.AuditLog. No business rows are returned.
-- Presence is a signature, not proof of exact DDL or installation approval.
SET NOCOUNT ON;
SELECT DB_NAME() AS Veritabani, CONVERT(varchar(40), SERVERPROPERTY('ProductVersion')) AS SqlSurumu;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'), 0) <> 1
    THROW 51400, 'Inventory requires database VIEW DEFINITION; absence cannot be inferred.', 1;

DECLARE @nav int = 0, @ops int = 0, @audit027 int = 0, @audit028 int = 0;
IF COL_LENGTH(N'security.Roles', N'CapabilitiesJson') IS NOT NULL
    AND COL_LENGTH(N'security.Roles', N'IsProtected') IS NOT NULL
    AND COL_LENGTH(N'security.Roles', N'IsSeeded') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT @nav = COALESCE(MAX(CASE WHEN j.value=N''ServiceAccounts.View'' THEN 1 ELSE 0 END),0)
                    + COALESCE(MAX(CASE WHEN j.value=N''ServiceAccounts.Administer'' THEN 1 ELSE 0 END),0),
               @ops = COALESCE(MAX(CASE WHEN j.value=N''ServiceAccounts.Work'' THEN 1 ELSE 0 END),0)
                    + COALESCE(MAX(CASE WHEN j.value=N''ServiceAccounts.Assign'' THEN 1 ELSE 0 END),0)
                    + COALESCE(MAX(CASE WHEN j.value=N''ServiceAccounts.Verify'' THEN 1 ELSE 0 END),0)
                    + COALESCE(MAX(CASE WHEN j.value=N''ServiceAccounts.Import'' THEN 1 ELSE 0 END),0)
                    + COALESCE(MAX(CASE WHEN j.value=N''ServiceAccounts.Report'' THEN 1 ELSE 0 END),0)
        FROM security.Roles r CROSS APPLY OPENJSON(CASE WHEN ISJSON(r.CapabilitiesJson)=1 AND LEFT(LTRIM(r.CapabilitiesJson),1)=N''['' THEN r.CapabilitiesJson ELSE N''[]'' END) j
        WHERE r.RoleCode=N''Admin'' AND r.IsProtected=1 AND r.IsSeeded=1;',
        N'@nav int OUTPUT, @ops int OUTPUT', @nav OUTPUT, @ops OUTPUT;
IF COL_LENGTH(N'audit.AuditLog', N'CorrelationId') IS NOT NULL
    AND COL_LENGTH(N'audit.AuditLog', N'Action') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT @a = COALESCE(SUM(CASE WHEN CorrelationId=N''migration:027:admin-service-account-navigation'' THEN 1 ELSE 0 END),0),
               @b = COALESCE(SUM(CASE WHEN CorrelationId=N''migration:028:admin-service-account-operations'' THEN 1 ELSE 0 END),0)
        FROM audit.AuditLog WHERE Action=N''AccessRoleDefinitionChanged''
          AND CorrelationId IN (N''migration:027:admin-service-account-navigation'',N''migration:028:admin-service-account-operations'');',
        N'@a int OUTPUT, @b int OUTPUT', @audit027 OUTPUT, @audit028 OUTPUT;

;WITH Signatures AS (
    SELECT v.Migration, v.Expected, v.Present
    FROM (VALUES
        ('025',25,(SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'svcacct') AND name IN
            (N'Organizations',N'Teams',N'People',N'PersonAliases',N'TeamMemberships',N'ScopeGrants',N'Accounts',N'AccountAliases',
             N'OwnershipAssignments',N'ExternalRecords',N'ExternalRecordLinks',N'Handovers',N'WorkRequests',N'ActionEvents',
             N'Communications',N'CommunicationAccounts',N'Findings',N'IdentityTransitions',N'ImportBatches',N'ImportRows',
             N'AccountObservations',N'Evidence',N'ReportSnapshots',N'ReminderOutbox',N'History'))),
        ('026',2,(SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'svcacct') AND name IN (N'AccountUsages',N'TeamRoles'))),
        ('027',3,@nav + CASE WHEN @audit027=1 THEN 1 WHEN @audit027>1 THEN 10 ELSE 0 END),
        ('028',6,@ops + CASE WHEN @audit028=1 THEN 1 WHEN @audit028>1 THEN 10 ELSE 0 END),
        ('029',3,CASE WHEN COL_LENGTH(N'svcacct.ScopeGrants',N'IsBootstrap') IS NOT NULL THEN 1 ELSE 0 END
            + CASE WHEN OBJECT_ID(N'svcacct.DF_SaScopeGrants_IsBootstrap',N'D') IS NOT NULL THEN 1 ELSE 0 END
            + (SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'svcacct.ScopeGrants') AND name=N'UX_SaScopeGrants_OneBootstrap')),
        ('030',10,(SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'svcacct') AND name IN
            (N'UsageScans',N'UsageScanServers',N'UsageScanItems',N'UsageScanLinks',N'UsageScanDecisions'))
            + (SELECT COUNT(*) FROM sys.triggers WHERE is_disabled=0 AND OBJECT_SCHEMA_NAME(object_id)=N'svcacct' AND name IN
            (N'TR_SaUsageScans_Immutable',N'TR_SaScanServers_Immutable',N'TR_SaScanItems_Immutable',N'TR_SaScanLinks_Immutable',N'TR_SaScanDecisions_Immutable'))),
        ('031',2,CASE WHEN COL_LENGTH(N'svcacct.WorkRequests',N'RequestedGmsaName') IS NOT NULL THEN 1 ELSE 0 END
            + CASE WHEN COL_LENGTH(N'svcacct.IdentityTransitions',N'RequestedGmsaName') IS NOT NULL THEN 1 ELSE 0 END),
        ('032',1,(SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'security.AccessRequests') AND name=N'IX_AccessRequests_UserRequested'))
    ) v(Migration,Expected,Present)
)
SELECT Migration,
    CASE WHEN Present=Expected THEN N'uygulanmış' ELSE N'uygulanmamış' END AS Durum,
    Present AS BulunanIsaret, Expected AS BeklenenIsaret,
    CONVERT(bit,CASE WHEN Present>0 THEN 1 ELSE 0 END) AS YenidenCalistirmaEngeli,
    CASE WHEN Present<>0 AND Present<>Expected THEN N'DUR: kısmi veya çelişkili; yeniden çalıştırmayın'
         WHEN Migration='028' AND @nav<>2 THEN N'DUR: 027 yetki ön koşulu eksik'
         ELSE N'Tanım, bağımlılık ve izinleri DBA ayrıca karşılaştırmalı' END AS Kontrol
FROM Signatures ORDER BY Migration;

-- Required API role and 030 grants: no membership or identities are disclosed.
SELECT CASE WHEN DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THEN N'EKSIK' ELSE N'var' END AS ApiRolu;
SELECT OBJECT_NAME(major_id) AS Tablo, permission_name, state_desc
FROM sys.database_permissions
WHERE class=1 AND grantee_principal_id=DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime')
  AND major_id IN (OBJECT_ID(N'svcacct.UsageScans'),OBJECT_ID(N'svcacct.UsageScanServers'),OBJECT_ID(N'svcacct.UsageScanItems'),
                  OBJECT_ID(N'svcacct.UsageScanLinks'),OBJECT_ID(N'svcacct.UsageScanDecisions'))
ORDER BY Tablo, permission_name;

-- 031 column contract; 032 exact index shape (compare with the separate DBA note).
SELECT OBJECT_NAME(object_id) AS Tablo, name, TYPE_NAME(user_type_id) AS Tur, max_length, is_nullable, default_object_id
FROM sys.columns WHERE name=N'RequestedGmsaName' AND object_id IN (OBJECT_ID(N'svcacct.WorkRequests'),OBJECT_ID(N'svcacct.IdentityTransitions'));
SELECT i.name,i.is_unique,i.is_disabled,i.has_filter,i.filter_definition,c.name AS Kolon,ic.key_ordinal,ic.is_descending_key,ic.is_included_column
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=OBJECT_ID(N'security.AccessRequests') AND i.name IN (N'IX_AccessRequests_StatusPage',N'IX_AccessRequests_UserRequested')
ORDER BY i.name,ic.key_ordinal,c.name;
