/*
  Read-only checks quoted in docs/service-accounts/DBA-034-TR.md (pre-check before 034, post-check after 034).
  Changes nothing. Run with SQLCMD -I -b -v Phase=pre or Phase=post.
*/
SET NOCOUNT ON;
DECLARE @definition nvarchar(max) = (SELECT definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner');
DECLARE @owners int = CASE WHEN @definition IS NULL THEN 0
    ELSE (LEN(@definition) - LEN(REPLACE(@definition, N'[OwnerEntityType]=', N''))) / LEN(N'[OwnerEntityType]=') END;
IF N'$(Phase)' = N'pre'
BEGIN
    SELECT CASE WHEN OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [OnKosul_033],
        CASE WHEN @definition IS NULL THEN 'YOK' WHEN @definition LIKE N'%''ChangePlan''%' THEN 'zaten-034' ELSE 'eski' END AS [KanitKisiti],
        @owners AS [SahipTuru];
END
ELSE
BEGIN
    SELECT CASE WHEN @definition LIKE N'%''ChangePlan''%' THEN 'yeni' WHEN @definition IS NULL THEN 'YOK' ELSE 'eski' END AS [KanitKisiti],
        @owners AS [SahipTuru],
        (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence')
            AND (is_not_trusted = 1 OR is_disabled = 1)) AS [GuvenilmeyenKisit],
        (SELECT COUNT(*) FROM svcacct.Evidence WHERE OwnerEntityType = 'ChangePlan') AS [PlanKaniti];
END;
