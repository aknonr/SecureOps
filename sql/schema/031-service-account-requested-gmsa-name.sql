-- Number reserved by the module owner from the actual 001-030 inventory and all published branches, 2026-10-05.
-- Preserve the reviewed SA-005 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.WorkRequests', N'U') IS NULL OR OBJECT_ID(N'svcacct.IdentityTransitions', N'U') IS NULL
    OR OBJECT_ID(N'svcacct.UsageScans', N'U') IS NULL
    THROW 51370, 'Service account requested gMSA name 031 requires reviewed 025 and 030.', 1;
GO
:r ../pending/service-accounts/SA-005-requested-gmsa-name.sql
