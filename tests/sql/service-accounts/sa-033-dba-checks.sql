/*
  Read-only checks quoted in docs/service-accounts/DBA-033-TR.md (pre-check before 033, post-check after 033 and its grants).
  Changes nothing. Run with SQLCMD -I -b -v Phase=pre or Phase=post.
*/
SET NOCOUNT ON;
IF N'$(Phase)' = N'pre'
BEGIN
    SELECT CASE WHEN OBJECT_ID(N'svcacct.Accounts', N'U') IS NOT NULL AND OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NOT NULL
            AND COL_LENGTH(N'svcacct.WorkRequests', N'RequestedGmsaName') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [OnKosul_025_030_031],
        CASE WHEN DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [ApiRolu],
        (SELECT COUNT(*) FROM sys.tables WHERE SCHEMA_NAME(schema_id) = N'svcacct' AND (name LIKE N'ChangePlan%' OR name = N'ChangeItemChecks')) AS [033Tablosu];
END
ELSE
BEGIN
    SELECT (SELECT COUNT(*) FROM sys.tables WHERE SCHEMA_NAME(schema_id) = N'svcacct' AND (name LIKE N'ChangePlan%' OR name = N'ChangeItemChecks')) AS [Tablo],
        (SELECT COUNT(*) FROM sys.triggers t JOIN sys.tables p ON p.object_id = t.parent_id
            WHERE p.name LIKE N'ChangePlan%' OR p.name = N'ChangeItemChecks') AS [Tetikleyici],
        (SELECT COUNT(*) FROM sys.check_constraints c JOIN sys.tables p ON p.object_id = c.parent_object_id
            WHERE c.is_not_trusted = 1 AND (p.name LIKE N'ChangePlan%' OR p.name = N'ChangeItemChecks')) AS [GuvenilmeyenKisit],
        (SELECT COUNT(*) FROM sys.database_permissions WHERE class = 1 AND USER_NAME(grantee_principal_id) = N'svcacct_api_runtime'
            AND (OBJECT_NAME(major_id) LIKE N'ChangePlan%' OR OBJECT_NAME(major_id) = N'ChangeItemChecks')) AS [ApiIzni],
        (SELECT COUNT(*) FROM sys.database_permissions WHERE class = 1 AND (OBJECT_NAME(major_id) LIKE N'ChangePlan%' OR OBJECT_NAME(major_id) = N'ChangeItemChecks')
            AND (permission_name = N'DELETE' OR USER_NAME(grantee_principal_id) <> N'svcacct_api_runtime')) AS [BeklenmeyenIzin],
        (SELECT COUNT(*) FROM svcacct.ChangePlans) AS [PlanSatiri];
END;
