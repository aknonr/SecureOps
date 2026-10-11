-- Number reserved by the module owner from the actual 001-033 inventory and all published branches, 2026-10-12.
-- Preserve the reviewed SA-007 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.Evidence', N'U') IS NULL OR OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NULL
    THROW 51396, 'Service account plan evidence 034 requires reviewed 033.', 1;
GO
:r ../pending/service-accounts/SA-007-plan-evidence.sql
