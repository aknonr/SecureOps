/*
  Read-only checks quoted in docs/service-accounts/DBA-035-TR.md (pre-check before 035, post-check after 035 and its grants).
  Changes nothing. Run with SQLCMD -I -b -v Phase=pre or Phase=post.
*/
SET NOCOUNT ON;
IF N'$(Phase)' = N'pre'
BEGIN
    SELECT CASE WHEN OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NOT NULL AND OBJECT_ID(N'svcacct.ChangePlanPreviews', N'U') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [OnKosul_033],
        CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner'
            AND definition LIKE N'%''ChangePlan''%') THEN 'var' ELSE 'YOK' END AS [OnKosul_034],
        CASE WHEN OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions', N'U') IS NULL AND OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents', N'U') IS NULL THEN 'yok'
             ELSE 'zaten-035' END AS [OrTablolari],
        CASE WHEN DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [ApiRolu];
END
ELSE
BEGIN
    SELECT (SELECT COUNT(*) FROM sys.tables WHERE object_id IN (OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions'), OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents'))) AS [OrTablosu],
        (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N'TR_SaRecordSubmissions_Guard', N'TR_SaRecordSubmissionEvents_AppendOnly') AND is_disabled = 0) AS [Tetikleyici],
        (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions')
            AND name IN (N'UX_SaRecordSubmissions_Active', N'UX_SaRecordSubmissions_Number') AND is_unique = 1 AND has_filter = 1 AND is_disabled = 0) AS [FiltreliTekilIndeks],
        (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions'), OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents'))
            AND (is_not_trusted = 1 OR is_disabled = 1)) AS [GuvenilmeyenKisit],
        (SELECT COUNT(*) FROM sys.database_permissions p
            WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') AND p.state = 'G' AND p.minor_id = 0
              AND p.major_id IN (OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions'), OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents'))
              AND p.permission_name IN (N'SELECT', N'INSERT')) AS [ApiIzni_SelectInsert],
        (SELECT COUNT(*) FROM sys.database_permissions p
            WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') AND p.state = 'G' AND p.minor_id <> 0
              AND p.major_id = OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions') AND p.permission_name = N'UPDATE') AS [ApiIzni_SutunUpdate],
        (SELECT COUNT(*) FROM sys.database_permissions p
            WHERE p.major_id IN (OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions'), OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents'))
              AND NOT (p.permission_name IN (N'SELECT', N'INSERT') AND p.minor_id = 0)
              AND NOT (p.permission_name = N'UPDATE' AND p.minor_id <> 0 AND p.major_id = OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions'))) AS [BaskaIzin],
        (SELECT COUNT(*) FROM svcacct.ChangePlanRecordSubmissions) AS [GonderimSatiri];
END;
