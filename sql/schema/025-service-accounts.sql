-- Number reserved after local branch/reference inventory, 2026-10-01.
-- Preserve the reviewed candidate bytes; SQLCMD paths resolve from sql/migrations.
IF OBJECT_ID(N'reporting.InUseArchiveReceipts', N'U') IS NULL
    OR OBJECT_ID(N'reporting.InUseReportCatalogue', N'U') IS NULL
    OR OBJECT_ID(N'reporting.TR_InUseReportCatalogue_Immutable', N'TR') IS NULL
    THROW 51300, 'Service Accounts 025 requires the reviewed 023/024 baseline.', 1;
GO
:r ../pending/service-accounts/SA-001-service-accounts.sql
