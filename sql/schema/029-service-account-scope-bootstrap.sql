-- Number reserved from the actual 001-028 inventory and all published branches, 2026-10-03 (ADR-0026).
-- Preserve the reviewed SA-003 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.ScopeGrants', N'U') IS NULL
    OR OBJECT_ID(N'svcacct.TeamRoles', N'U') IS NULL
    THROW 51350, 'Service account scope bootstrap 029 requires reviewed 025/026.', 1;
GO
:r ../pending/service-accounts/SA-003-scope-bootstrap.sql
