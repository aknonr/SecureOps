-- Number reserved by the module owner from the actual 001-032 inventory and all published branches, 2026-10-07 (032 is Access, G-34).
-- Preserve the reviewed SA-006 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL OR OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NULL
    OR COL_LENGTH(N'svcacct.WorkRequests', N'RequestedGmsaName') IS NULL
    THROW 51390, 'Service account change plans 033 requires reviewed 025, 030 and 031.', 1;
GO
:r ../pending/service-accounts/SA-006-change-plans.sql
