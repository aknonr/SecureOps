/* Reviewed offline reporting read model. Execute only through the approved DBA migration process. */
IF SCHEMA_ID(N'reporting') IS NULL
    EXEC(N'CREATE SCHEMA reporting');
GO

CREATE OR ALTER VIEW reporting.ManagementAuditEvents
AS
    SELECT OccurredAt, Actor, Action, CorrelationId, DetailsJson
    FROM audit.AuditLog;
GO

CREATE OR ALTER VIEW reporting.ManagementWorkflowEvents
AS
    SELECT OperationalRecordId, WorkflowState, Actor, CorrelationId, ErrorCode, OccurredAt
    FROM ops.OperationalRecordWorkflowHistory;
GO

CREATE OR ALTER VIEW reporting.ManagementOperationalStatus
AS
    SELECT records.OperationalRecordId, records.WorkflowState, records.UpdatedAt,
        transfers.ReconciliationRequired, transfers.UpdatedAt AS TransferUpdatedAt
    FROM ops.OperationalRecords records
    INNER JOIN ops.JiraTransfers transfers ON transfers.OperationalRecordId = records.OperationalRecordId;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_ActionOccurredAt' AND object_id = OBJECT_ID(N'audit.AuditLog'))
    CREATE INDEX IX_AuditLog_ActionOccurredAt
        ON audit.AuditLog (Action, OccurredAt)
        INCLUDE (Actor, CorrelationId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OperationalRecordWorkflowHistory_StateOccurredAt' AND object_id = OBJECT_ID(N'ops.OperationalRecordWorkflowHistory'))
    CREATE INDEX IX_OperationalRecordWorkflowHistory_StateOccurredAt
        ON ops.OperationalRecordWorkflowHistory (WorkflowState, OccurredAt)
        INCLUDE (OperationalRecordId, Actor, CorrelationId, ErrorCode);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JiraTransfers_ReconciliationUpdatedAt' AND object_id = OBJECT_ID(N'ops.JiraTransfers'))
    CREATE INDEX IX_JiraTransfers_ReconciliationUpdatedAt
        ON ops.JiraTransfers (ReconciliationRequired, UpdatedAt)
        INCLUDE (OperationalRecordId);
GO
