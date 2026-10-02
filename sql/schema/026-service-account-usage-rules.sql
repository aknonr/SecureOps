-- Number reserved from the actual 001-025 inventory, 2026-10-02.
-- Preserve reviewed SA-002 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL
    OR OBJECT_ID(N'svcacct.TR_SaHistory_AppendOnly', N'TR') IS NULL
    THROW 51320, 'Service account usage rules 026 require reviewed 025.', 1;
GO
:r ../pending/service-accounts/SA-002-usage-rules.sql
