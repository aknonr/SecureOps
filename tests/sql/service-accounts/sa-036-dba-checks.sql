/*
  Read-only checks quoted in docs/service-accounts/DBA-036-TR.md (pre-check before 036, post-check after 036 and its grants).
  Changes nothing. Run with SQLCMD -I -b -v Phase=pre or Phase=post.
*/
SET NOCOUNT ON;
IF N'$(Phase)' = N'pre'
BEGIN
    SELECT CASE WHEN OBJECT_ID(N'svcacct.Accounts', N'U') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [OnKosul_025],
        CASE WHEN OBJECT_ID(N'svcacct.AccountEvents', N'U') IS NULL AND OBJECT_ID(N'svcacct.AccountEventItems', N'U') IS NULL THEN 'yok'
             ELSE 'zaten-036' END AS [OlayTablolari],
        CASE WHEN DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [ApiRolu];
END
ELSE
BEGIN
    SELECT (SELECT COUNT(*) FROM sys.tables WHERE object_id IN (OBJECT_ID(N'svcacct.AccountEvents'), OBJECT_ID(N'svcacct.AccountEventItems'))) AS [OlayTablosu],
        (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N'TR_SaAccountEvents_AppendOnly', N'TR_SaAccountEventItems_AppendOnly') AND is_disabled = 0) AS [EklemeYalnizTetikleyici],
        (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'svcacct.AccountEvents'), OBJECT_ID(N'svcacct.AccountEventItems'))
            AND (is_not_trusted = 1 OR is_disabled = 1)) AS [GuvenilmeyenKisit],
        (SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id IN (OBJECT_ID(N'svcacct.AccountEvents'), OBJECT_ID(N'svcacct.AccountEventItems')) AND t.name IN (N'nvarchar', N'nchar', N'ntext', N'text', N'xml', N'varbinary')) AS [MetinSutunu],
        (SELECT COUNT(*) FROM sys.database_permissions p
            WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') AND p.state = 'G'
              AND p.major_id IN (OBJECT_ID(N'svcacct.AccountEvents'), OBJECT_ID(N'svcacct.AccountEventItems'))
              AND p.permission_name IN (N'SELECT', N'INSERT')) AS [ApiIzni_SelectInsert],
        (SELECT COUNT(*) FROM sys.database_permissions p
            WHERE p.major_id IN (OBJECT_ID(N'svcacct.AccountEvents'), OBJECT_ID(N'svcacct.AccountEventItems'))
              AND p.permission_name NOT IN (N'SELECT', N'INSERT')) AS [BaskaIzin],
        (SELECT COUNT(*) FROM svcacct.AccountEvents) AS [OlaySatiri];
END;
