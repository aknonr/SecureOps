SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF COL_LENGTH(N'ops.JiraTransfers',N'InitiatorJson') IS NULL THROW 51220,'Migration 021 required.',1;
IF OBJECT_ID(N'ops.InUseServerReviews') IS NOT NULL OR OBJECT_ID(N'ops.InUseExecutions') IS NOT NULL
    THROW 51220,'022 objects exist; compare definitions, do not replay.',1;
BEGIN TRANSACTION;
CREATE TABLE ops.InUseServerReviews(
    ReviewId uniqueidentifier NOT NULL CONSTRAINT PK_InUseServerReviews PRIMARY KEY,
    RecordId uniqueidentifier NOT NULL REFERENCES ops.InUseRecords(Id),
    RecordVersion bigint NOT NULL CHECK(RecordVersion>0),
    IdentityKey char(64) NOT NULL,
    ReviewedAt datetimeoffset NOT NULL,
    SearchText nvarchar(max) NOT NULL,
    SnapshotJson nvarchar(max) NOT NULL CHECK(ISJSON(SnapshotJson)=1),
    CONSTRAINT UQ_InUseServerReviews UNIQUE(RecordId,RecordVersion,IdentityKey)
);
CREATE INDEX IX_InUseServerReviews_Identity ON ops.InUseServerReviews(IdentityKey,ReviewedAt DESC,ReviewId);
CREATE TABLE ops.InUseExecutions(
    OperationId uniqueidentifier NOT NULL CONSTRAINT PK_InUseExecutions PRIMARY KEY,
    RecordId uniqueidentifier NOT NULL REFERENCES ops.InUseRecords(Id),
    ReportVersion bigint NOT NULL CHECK(ReportVersion>0),
    InitiatorId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    State nvarchar(40) NOT NULL,
    Step int NOT NULL CHECK(Step BETWEEN 0 AND 7),
    Revision bigint NOT NULL CHECK(Revision>0),
    Active bit NOT NULL,
    LeaseToken uniqueidentifier NULL,
    LeaseUntil datetimeoffset NULL,
    CreatedAt datetimeoffset NOT NULL,
    UpdatedAt datetimeoffset NOT NULL,
    IntentJson nvarchar(max) NOT NULL CHECK(ISJSON(IntentJson)=1),
    Artifact varbinary(max) NOT NULL,
    CONSTRAINT UQ_InUseExecutions_Snapshot UNIQUE(RecordId,ReportVersion),
    CONSTRAINT CK_InUseExecutions_State CHECK(State IN('Queued','Running','Unknown','Failed','Blocked','Unconfirmed','Completed'))
);
CREATE UNIQUE INDEX UX_InUseExecutions_Active ON ops.InUseExecutions(RecordId) WHERE Active=1;
CREATE INDEX IX_InUseExecutions_Recovery ON ops.InUseExecutions(State,LeaseUntil,CreatedAt);
CREATE TABLE ops.InUseExecutionEvents(
    EventId bigint IDENTITY NOT NULL CONSTRAINT PK_InUseExecutionEvents PRIMARY KEY,
    OperationId uniqueidentifier NOT NULL REFERENCES ops.InUseExecutions(OperationId),
    Revision bigint NOT NULL,
    OccurredAt datetimeoffset NOT NULL,
    EvidenceJson nvarchar(max) NOT NULL CHECK(ISJSON(EvidenceJson)=1),
    CONSTRAINT UQ_InUseExecutionEvents_Revision UNIQUE(OperationId,Revision)
);
EXEC(N'CREATE TRIGGER ops.tr_InUseServerReviews_AppendOnly ON ops.InUseServerReviews INSTEAD OF UPDATE, DELETE AS
BEGIN THROW 51220,''In Use review history is append-only.'',1; END;');
EXEC(N'CREATE TRIGGER ops.tr_InUseExecutionEvents_AppendOnly ON ops.InUseExecutionEvents INSTEAD OF UPDATE, DELETE AS
BEGIN THROW 51220,''In Use execution evidence is append-only.'',1; END;');
EXEC(N'CREATE TRIGGER ops.tr_InUseExecutions_ImmutableIntent ON ops.InUseExecutions AFTER UPDATE AS
BEGIN IF UPDATE(IntentJson) OR UPDATE(Artifact) OR UPDATE(RecordId) OR UPDATE(ReportVersion) OR UPDATE(InitiatorId) OR UPDATE(CreatedAt)
THROW 51220,''In Use intent and artifact are immutable.'',1; END;');
COMMIT TRANSACTION;
-- Runtime: SELECT/INSERT reviews and events; SELECT/INSERT/UPDATE executions.
-- Existing access/record/audit grants remain. No DELETE/DDL or role assignment.
-- Rollback suspends new writes, restores reviewed binaries/configuration, retains additive data.
