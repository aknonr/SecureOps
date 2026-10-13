-- Number reserved for TARAMA-S by the module owner, 2026-10-13 (035 and 036 are held by parallel branches).
-- Preserve the reviewed SA-008 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL OR OBJECT_ID(N'svcacct.UsageScans', N'U') IS NULL
    THROW 51410, 'Service account usage scan jobs 037 requires reviewed 025 and 030.', 1;
GO
:r ../pending/service-accounts/SA-008-usage-scan-jobs.sql
