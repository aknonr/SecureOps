-- Read-only DBA inventory for the installed-032 to 037 review (docs/release/TEST-033-037-OPERATOR-TR.md).
-- Run in the intended database after Get-InstalledMigrationInventory025To032.sql. Changes nothing, needs no SQLCMD mode
-- and returns no business rows. Presence is a signature, not proof of exact DDL or installation approval.
SET NOCOUNT ON;
SELECT DB_NAME() AS Veritabani, CONVERT(varchar(40), SERVERPROPERTY('ProductVersion')) AS SqlSurumu;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'), 0) <> 1
    THROW 51440, 'Inventory requires database VIEW DEFINITION; absence cannot be inferred.', 1;

;WITH Signatures AS (
    SELECT v.Migration, v.Expected, v.Present
    FROM (VALUES
        ('033',7,(SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'svcacct') AND name IN
            (N'ChangePlans',N'ChangePlanAccounts',N'ChangePlanPreviews',N'ChangePlanItems',N'ChangePlanApprovals',
             N'ChangeItemChecks',N'ChangePlanEvents'))),
        ('034',1,(SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'svcacct.Evidence')
            AND name=N'CK_SaEvidence_Owner' AND definition LIKE N'%''ChangePlan''%')),
        ('035',2,(SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'svcacct') AND name IN
            (N'ChangePlanRecordSubmissions',N'ChangePlanRecordSubmissionEvents'))),
        ('036',2,(SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'svcacct') AND name IN
            (N'AccountEvents',N'AccountEventItems'))),
        ('037',3,(SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'svcacct') AND name IN
            (N'UsageScanJobs',N'UsageScanJobAccounts',N'UsageScanJobServers')))
    ) v(Migration,Expected,Present)
)
SELECT Migration,
    CASE WHEN Present=Expected THEN N'uygulanmış' WHEN Present=0 THEN N'uygulanmamış' ELSE N'KISMI' END AS Durum,
    Present AS BulunanIsaret, Expected AS BeklenenIsaret,
    CASE WHEN Present<>0 AND Present<>Expected THEN N'DUR: kısmi veya çelişkili; çalıştırmayın'
         WHEN Present=Expected THEN N'DUR: bu taban için zaten uygulanmış; tekrar çalıştırmayın'
         ELSE N'Beklenen: uygulanmamış' END AS Kontrol
FROM Signatures ORDER BY Migration;

-- Runtime roles the 033-037 permission candidates grant to; no membership or identities are disclosed.
SELECT CASE WHEN DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THEN N'EKSIK' ELSE N'var' END AS ApiRolu,
    CASE WHEN DATABASE_PRINCIPAL_ID(N'svcacct_worker_runtime') IS NULL THEN N'EKSIK' ELSE N'var' END AS WorkerRolu;
