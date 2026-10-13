-- Number reserved by the module owner from the actual 001-029 inventory and all published branches, 2026-10-04 (ADR-0027).
-- Preserve the reviewed SA-004 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL OR OBJECT_ID(N'svcacct.AccountUsages', N'U') IS NULL
    THROW 51360, 'Service account usage scans 030 requires reviewed 025/026.', 1;
GO
:r ../pending/service-accounts/SA-004-usage-scans.sql
