/*
  Read-only checks quoted in docs/service-accounts/DBA-037-TR.md (pre-check before 037, post-check after 037).
  Changes nothing. Run with SQLCMD -I -b -W -v Phase=pre or Phase=post.
*/
SET NOCOUNT ON;
IF N'$(Phase)' = N'pre'
BEGIN
    SELECT CASE WHEN OBJECT_ID(N'svcacct.UsageScans', N'U') IS NOT NULL THEN 'var' ELSE 'YOK' END AS [OnKosul_030],
        CASE WHEN OBJECT_ID(N'svcacct.UsageScanJobs', N'U') IS NULL THEN 'yok' ELSE 'zaten-037' END AS [TaramaIsleri];
END
ELSE
BEGIN
    SELECT (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'svcacct')
                AND name IN (N'UsageScanJobs', N'UsageScanJobAccounts', N'UsageScanJobServers')) AS [Tablo],
        (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N'TR_SaScanJobs_Guard', N'TR_SaScanJobAccounts_Guard', N'TR_SaScanJobServers_Guard')
                AND is_disabled = 0) AS [Tetikleyici],
        (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'svcacct.UsageScanJobAccounts') AND name = N'UX_SaScanJobAccounts_Active'
                AND is_unique = 1 AND has_filter = 1) AS [TekCalisanDizin],
        (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'svcacct.UsageScanJobs'),
                OBJECT_ID(N'svcacct.UsageScanJobAccounts'), OBJECT_ID(N'svcacct.UsageScanJobServers'))
                AND (is_not_trusted = 1 OR is_disabled = 1)) AS [GuvenilmeyenKisit],
        (SELECT COUNT(*) FROM svcacct.UsageScanJobs) AS [Is];
END;
