SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'ops.InUseExecutionEvents') IS NULL THROW 51230,'Migration 022 required.',1;
IF OBJECT_ID(N'reporting.WorkflowSnapshots') IS NOT NULL THROW 51230,'023 exists; compare definitions, do not replay.',1;
BEGIN TRANSACTION;
ALTER TABLE ops.OperationalRecords ADD SourceSynthetic bit NULL;
CREATE TABLE reporting.WorkflowSnapshots(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OwnerId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    AccessVersion bigint NOT NULL,
    ScopeHash char(64) NOT NULL,
    AsOf datetimeoffset NOT NULL,
    ExpiresAt datetimeoffset NOT NULL,
    RequestJson nvarchar(4000) NOT NULL CHECK(ISJSON(RequestJson)=1),
    LimitationsJson nvarchar(4000) NOT NULL CHECK(ISJSON(LimitationsJson)=1),
    ReadinessJson nvarchar(4000) NOT NULL CHECK(ISJSON(ReadinessJson)=1)
);
CREATE INDEX IX_WorkflowSnapshots_Owner ON reporting.WorkflowSnapshots(OwnerId,AsOf DESC);
CREATE TABLE reporting.WorkflowFacts(
    SnapshotId uniqueidentifier NOT NULL REFERENCES reporting.WorkflowSnapshots(Id),
    Metric varchar(64) NOT NULL,
    LogicalId nvarchar(200) NOT NULL,
    Module varchar(16) NOT NULL,
    RecordId uniqueidentifier NOT NULL,
    Reference nvarchar(128) NOT NULL,
    RecordType nvarchar(64) NOT NULL,
    Status nvarchar(64) NOT NULL,
    Actor nvarchar(256) NULL,
    Assignee nvarchar(256) NULL,
    OccurredAt datetimeoffset NULL,
    Detail nvarchar(500) NULL,
    CONSTRAINT PK_WorkflowFacts PRIMARY KEY(SnapshotId,Metric,LogicalId)
);
CREATE INDEX IX_WorkflowFacts_Filter ON reporting.WorkflowFacts(SnapshotId,Module,Status,RecordType,Metric,LogicalId);
CREATE TABLE reporting.InUseArchiveReceipts(
    RecordId uniqueidentifier NOT NULL REFERENCES ops.InUseRecords(Id),
    ReportVersion bigint NOT NULL,
    SourceVersion bigint NOT NULL,
    Sha256 char(64) NOT NULL,
    PreparedBy uniqueidentifier NOT NULL,
    PreparedByLabel nvarchar(256) NULL,
    PreparedAt datetimeoffset NOT NULL,
    ObservedAt datetimeoffset NOT NULL,
    Synthetic bit NOT NULL,
    CONSTRAINT PK_InUseArchiveReceipts PRIMARY KEY(RecordId,ReportVersion)
);
CREATE INDEX IX_InUseArchiveReceipts_Time ON reporting.InUseArchiveReceipts(PreparedAt,RecordId,ReportVersion);
EXEC(N'CREATE TRIGGER reporting.TR_WorkflowSnapshots_Immutable ON reporting.WorkflowSnapshots INSTEAD OF UPDATE,DELETE AS BEGIN THROW 51231,''Report snapshots are immutable.'',1; END;');
EXEC(N'CREATE TRIGGER reporting.TR_WorkflowFacts_Immutable ON reporting.WorkflowFacts INSTEAD OF UPDATE,DELETE AS BEGIN THROW 51231,''Report facts are immutable.'',1; END;');
EXEC(N'CREATE TRIGGER reporting.TR_InUseArchiveReceipts_Immutable ON reporting.InUseArchiveReceipts INSTEAD OF UPDATE,DELETE AS BEGIN THROW 51231,''Archive receipts are immutable.'',1; END;');
COMMIT;
-- Runtime delta: SELECT/INSERT on these three objects only. No runtime DDL/DELETE.
-- Preserve additive data on rollback. Expired snapshots are inaccessible, not automatically deleted.
