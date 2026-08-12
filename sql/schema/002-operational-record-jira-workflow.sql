/* Reviewed offline SQL contract. Execute only through the approved DBA migration process. */
IF SCHEMA_ID(N'ops') IS NULL
    EXEC(N'CREATE SCHEMA ops');
GO

CREATE TABLE ops.OperationalRecords
(
    OperationalRecordId uniqueidentifier NOT NULL CONSTRAINT PK_OperationalRecords PRIMARY KEY,
    SourceRecordId nvarchar(128) NOT NULL,
    OrCode nvarchar(64) NOT NULL,
    Title nvarchar(500) NOT NULL,
    Description nvarchar(8000) NOT NULL,
    Requester nvarchar(256) NULL,
    SourceCreatedAt datetimeoffset(7) NOT NULL,
    EnvironmentName nvarchar(128) NULL,
    ServerReference nvarchar(255) NULL,
    ApplicationReference nvarchar(255) NULL,
    Classification nvarchar(64) NOT NULL,
    JiraEligible bit NOT NULL,
    EligibilityReason nvarchar(500) NOT NULL,
    WorkflowState nvarchar(64) NOT NULL,
    LastErrorCode nvarchar(128) NULL,
    CorrelationId nvarchar(256) NULL,
    RetryCount int NOT NULL CONSTRAINT DF_OperationalRecords_RetryCount DEFAULT 0,
    UpdatedAt datetimeoffset(7) NOT NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT UQ_OperationalRecords_SourceRecordId UNIQUE (SourceRecordId),
    CONSTRAINT CK_OperationalRecords_RetryCount CHECK (RetryCount >= 0),
    CONSTRAINT CK_OperationalRecords_WorkflowState CHECK (WorkflowState IN
        ('Imported', 'Classified', 'NeedsManualReview', 'Eligible', 'Previewed', 'CreateRequested',
         'CreatingJira', 'JiraCreated', 'ClosingOperationalRecord', 'Completed',
         'JiraCreateFailed', 'OperationalRecordCloseFailed')),
    CONSTRAINT CK_OperationalRecords_Classification CHECK (Classification IN
        ('ServerRequest', 'EnvironmentRequest', 'SoftwareInstallation', 'ConfigurationRequest',
         'OperationalSupport', 'NotJiraEligible', 'NeedsManualReview'))
);
GO
CREATE INDEX IX_OperationalRecords_StateUpdatedAt ON ops.OperationalRecords (WorkflowState, UpdatedAt DESC);
CREATE INDEX IX_OperationalRecords_OrCode ON ops.OperationalRecords (OrCode);
GO

CREATE TABLE ops.JiraTransfers
(
    JiraTransferId uniqueidentifier NOT NULL CONSTRAINT PK_JiraTransfers PRIMARY KEY,
    OperationalRecordId uniqueidentifier NOT NULL,
    MappingVersion nvarchar(64) NOT NULL,
    IdempotencyKey char(64) NOT NULL,
    JiraIssueKey nvarchar(64) NULL,
    ReconciliationRequired bit NOT NULL CONSTRAINT DF_JiraTransfers_ReconciliationRequired DEFAULT 0,
    LastErrorCode nvarchar(128) NULL,
    CreatedByActor nvarchar(256) NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT FK_JiraTransfers_OperationalRecord FOREIGN KEY (OperationalRecordId)
        REFERENCES ops.OperationalRecords(OperationalRecordId),
    CONSTRAINT UQ_JiraTransfers_OperationalRecord UNIQUE (OperationalRecordId),
    CONSTRAINT UQ_JiraTransfers_IdempotencyKey UNIQUE (IdempotencyKey)
);
GO
CREATE UNIQUE INDEX UX_JiraTransfers_JiraIssueKey ON ops.JiraTransfers (JiraIssueKey) WHERE JiraIssueKey IS NOT NULL;
GO

CREATE TABLE ops.OperationalRecordWorkflowHistory
(
    OperationalRecordWorkflowHistoryId bigint IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_OperationalRecordWorkflowHistory PRIMARY KEY,
    OperationalRecordId uniqueidentifier NOT NULL,
    WorkflowState nvarchar(64) NOT NULL,
    Actor nvarchar(256) NOT NULL,
    CorrelationId nvarchar(256) NOT NULL,
    ErrorCode nvarchar(128) NULL,
    OccurredAt datetimeoffset(7) NOT NULL,
    CONSTRAINT FK_OperationalRecordWorkflowHistory_Record FOREIGN KEY (OperationalRecordId)
        REFERENCES ops.OperationalRecords(OperationalRecordId)
);
GO
CREATE INDEX IX_OperationalRecordWorkflowHistory_RecordOccurredAt
    ON ops.OperationalRecordWorkflowHistory (OperationalRecordId, OccurredAt DESC);
GO
CREATE TRIGGER ops.TR_OperationalRecordWorkflowHistory_AppendOnly
ON ops.OperationalRecordWorkflowHistory
AFTER UPDATE, DELETE
AS
BEGIN
    THROW 51011, 'ops.OperationalRecordWorkflowHistory is append-only.', 1;
END;
GO
