-- Number reserved for TARAMA-S by the module owner, 2026-10-13 (035 and the SA-008 name are left to OR-2; 036 is SA-009).
-- Preserve the reviewed SA-010 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL OR OBJECT_ID(N'svcacct.UsageScans', N'U') IS NULL
    THROW 51430, 'Service account usage scan jobs 037 requires reviewed 025 and 030.', 1;
GO
:r ../pending/service-accounts/SA-010-usage-scan-jobs.sql
