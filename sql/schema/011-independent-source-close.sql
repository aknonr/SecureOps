SET XACT_ABORT ON;
GO
BEGIN TRANSACTION;
IF COL_LENGTH('ops.JiraTransfers', 'SourceCloseRequested') IS NULL
    ALTER TABLE ops.JiraTransfers ADD SourceCloseRequested bit NOT NULL
        CONSTRAINT DF_JiraTransfers_SourceCloseRequested DEFAULT (0) WITH VALUES;
IF COL_LENGTH('ops.OperationalRecordWorkflowHistory', 'SourceCloseRequested') IS NULL
    ALTER TABLE ops.OperationalRecordWorkflowHistory ADD SourceCloseRequested bit NOT NULL
        CONSTRAINT DF_WorkflowHistory_SourceCloseRequested DEFAULT (0) WITH VALUES;
COMMIT TRANSACTION;
GO
