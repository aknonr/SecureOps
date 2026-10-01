SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
IF COL_LENGTH('ops.JiraTransfers', 'SourceCloseRequested') IS NULL
    OR COL_LENGTH('ops.OperationalRecordWorkflowHistory', 'SourceCloseRequested') IS NULL
    THROW 51012, 'Apply the reviewed baseline before migration 012.', 1;
IF OBJECT_ID(N'ops.InUseRecords', N'U') IS NOT NULL
    THROW 51012, 'In Use migration already present; verify the existing definition.', 1;

CREATE TABLE ops.InUseRecords
(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_InUseRecords PRIMARY KEY,
    SourceId nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Code nvarchar(100) NOT NULL,
    Title nvarchar(1000) NOT NULL,
    AssigneeId uniqueidentifier NULL CONSTRAINT FK_InUseRecords_Assignee REFERENCES security.Users(UserId),
    ReviewStatus varchar(20) NOT NULL,
    Version bigint NOT NULL,
    RecordJson nvarchar(max) NOT NULL,
    CONSTRAINT UQ_InUseRecords_SourceId UNIQUE(SourceId),
    CONSTRAINT CK_InUseRecords_Json CHECK(ISJSON(RecordJson) = 1),
    CONSTRAINT CK_InUseRecords_Version CHECK(Version > 0),
    CONSTRAINT CK_InUseRecords_Status CHECK(ReviewStatus IN ('Unreviewed','Draft','Stale'))
);
CREATE INDEX IX_InUseRecords_Assignee ON ops.InUseRecords(AssigneeId, ReviewStatus, Code);
CREATE TABLE ops.InUseRefresh
(
    Id int NOT NULL CONSTRAINT PK_InUseRefresh PRIMARY KEY CONSTRAINT CK_InUseRefresh_Id CHECK(Id = 1),
    Version bigint NOT NULL,
    StateJson nvarchar(max) NOT NULL CONSTRAINT CK_InUseRefresh_Json CHECK(ISJSON(StateJson) = 1)
);
INSERT INTO ops.InUseRefresh(Id, Version, StateJson)
VALUES(1, 0, N'{"Version":0,"LastAttemptAt":null,"LastSuccessfulAt":null,"Complete":false,"Issue":"NotRefreshed"}');
IF EXISTS (SELECT 1 FROM security.Roles WHERE RoleId IN (8,9))
    OR EXISTS (SELECT 1 FROM security.Roles WHERE RoleCode IN (N'InUseReviewer',N'InUseCoordinator'))
    THROW 51012, 'In Use role collision; DBA review required.', 1;
INSERT INTO security.Roles(RoleId, RoleCode) VALUES(8,N'InUseReviewer'),(9,N'InUseCoordinator');
COMMIT;
/* Runtime delta: SELECT, INSERT, UPDATE on ops.InUseRecords;
   SELECT, UPDATE on ops.InUseRefresh. Existing audit.AuditLog INSERT and
   ops.CommandExecutions grants are reused. No DELETE, DDL or audit mutation. */
