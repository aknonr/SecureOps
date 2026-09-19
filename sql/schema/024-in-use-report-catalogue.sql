SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'reporting.InUseArchiveReceipts') IS NULL THROW 51240,'Migration 023 required.',1;
IF OBJECT_ID(N'reporting.InUseReportCatalogue') IS NOT NULL THROW 51240,'024 exists; compare definitions, do not replay.',1;
BEGIN TRANSACTION;
CREATE TABLE reporting.InUseReportCatalogue(
    RecordId uniqueidentifier NOT NULL,
    ReportVersion bigint NOT NULL,
    SourceCode nvarchar(128) NULL,
    HostsJson nvarchar(max) NOT NULL CHECK(ISJSON(HostsJson)=1),
    PreparedByAccount nvarchar(256) NULL,
    DownloadName nvarchar(256) NOT NULL,
    MetadataHash char(64) NOT NULL,
    IndexedAt datetimeoffset NOT NULL,
    CONSTRAINT PK_InUseReportCatalogue PRIMARY KEY(RecordId,ReportVersion),
    CONSTRAINT FK_InUseReportCatalogue_Receipt FOREIGN KEY(RecordId,ReportVersion)
        REFERENCES reporting.InUseArchiveReceipts(RecordId,ReportVersion)
);
CREATE INDEX IX_InUseReportCatalogue_Code ON reporting.InUseReportCatalogue(SourceCode,RecordId,ReportVersion);
EXEC(N'CREATE TRIGGER reporting.TR_InUseReportCatalogue_Immutable ON reporting.InUseReportCatalogue INSTEAD OF UPDATE,DELETE AS BEGIN THROW 51241,''Archive catalogue metadata is immutable.'',1; END;');
COMMIT;
-- Reviewed runtime delta: SELECT/INSERT on this object only. No startup DDL or backfill.
