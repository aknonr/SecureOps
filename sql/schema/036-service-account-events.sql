-- Number reserved by the module owner in koordinasyon-ortak\00-ORTAK-MERGE-SIRASI.md, 2026-10-13 (035 is reserved for OR-2).
-- Preserve the reviewed SA-009 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL
    THROW 51410, 'Service account events 036 requires reviewed 025.', 1;
GO
:r ../pending/service-accounts/SA-009-account-events.sql
