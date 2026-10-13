/*
  Service Accounts module — candidate 10 (SA-010), numbered 037 by the module owner (web usage scan, 2026-10-13).
  Requires 025 and 030 (svcacct.Accounts, svcacct.UsageScans). Contract: docs/service-accounts/USAGE-SCAN-JOB-CONTRACT.md;
  decision: ADR-0024 Revision 2 (R2-4) and the owner decision of 2026-10-13.
  Additive only: three new tables for the state of a running web scan; no existing table, constraint, row, role or grant changes.
  - UsageScanJobs: one row per job (who started it, searched accounts, plan hash, collector kind, state, lease, the evidence
    scan it produced). Operational state, not evidence: the evidence is the ordinary 030 scan record (tool = Jea) written once
    at the end, which stays append-only.
  - UsageScanJobAccounts: the accounts of a job. IsActive = 1 while the job is Queued or Running; a filtered unique index
    allows at most one active job per account (ADR-0024 R2-4). LinkOutcome records whether the evidence was linked.
  - UsageScanJobServers: one row per planned server with its live state, the collector status (UsageCollectionStatus), the
    validated document (bounded bytes) and the matched items as JSON for the live view. A finished server row never changes.
  Rows are never deleted (trigger). No secret column exists; documents pass the ADR-0027 parser (secret guard first) before
  they are stored. Refuses replay. Run with SQLCMD -I -b. No down script: rollback retains these objects (see DBA-037-TR.md).
  Runtime grants: SA-010-API-permissions.sql and SA-010-Worker-permissions.sql.
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL OR OBJECT_ID(N'svcacct.UsageScans', N'U') IS NULL
    THROW 51430, 'Service Accounts candidate 10 requires 025 and 030.', 1;
IF OBJECT_ID(N'svcacct.UsageScanJobs', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.UsageScanJobAccounts', N'U') IS NOT NULL
    OR OBJECT_ID(N'svcacct.UsageScanJobServers', N'U') IS NOT NULL
    THROW 51430, 'Service Accounts candidate 10 already applied; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;
CREATE TABLE svcacct.UsageScanJobs(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaScanJobs PRIMARY KEY,
    State varchar(16) NOT NULL CONSTRAINT CK_SaScanJobs_State CHECK (State IN ('Queued','Running','Completed','Cancelled','Failed')),
    Purpose varchar(16) NOT NULL CONSTRAINT CK_SaScanJobs_Purpose CHECK (Purpose IN ('Discovery','GmsaCheck')),
    ExpectedAccount nvarchar(100) NULL,
    AccountsJson nvarchar(max) NOT NULL CONSTRAINT CK_SaScanJobs_Accounts CHECK (ISJSON(AccountsJson) = 1),
    PlannedServers int NOT NULL CONSTRAINT CK_SaScanJobs_Planned CHECK (PlannedServers BETWEEN 1 AND 500),
    PlanSha256 char(64) NOT NULL,
    Collector varchar(16) NOT NULL CONSTRAINT CK_SaScanJobs_Collector CHECK (Collector IN ('Fake','Jea')),
    RetryOfJobId uniqueidentifier NULL CONSTRAINT FK_SaScanJobs_RetryOf REFERENCES svcacct.UsageScanJobs(Id),
    StartedBy uniqueidentifier NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    StartedAt datetimeoffset(7) NULL,
    FinishedAt datetimeoffset(7) NULL,
    CancelRequestedAt datetimeoffset(7) NULL,
    CancelRequestedBy uniqueidentifier NULL,
    FailureCode varchar(32) NULL,
    ScanId uniqueidentifier NULL CONSTRAINT FK_SaScanJobs_Scan REFERENCES svcacct.UsageScans(Id),
    LeaseId uniqueidentifier NULL,
    LeaseUntil datetimeoffset(7) NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaScanJobs_Expected CHECK ((Purpose = 'GmsaCheck' AND ExpectedAccount IS NOT NULL)
        OR (Purpose = 'Discovery' AND ExpectedAccount IS NULL)),
    CONSTRAINT CK_SaScanJobs_Finished CHECK ((State IN ('Queued','Running') AND FinishedAt IS NULL)
        OR (State IN ('Completed','Cancelled','Failed') AND FinishedAt IS NOT NULL)),
    CONSTRAINT CK_SaScanJobs_Failure CHECK ((State = 'Failed' AND FailureCode IS NOT NULL) OR (State <> 'Failed' AND FailureCode IS NULL)),
    CONSTRAINT CK_SaScanJobs_Cancel CHECK ((CancelRequestedAt IS NULL AND CancelRequestedBy IS NULL)
        OR (CancelRequestedAt IS NOT NULL AND CancelRequestedBy IS NOT NULL))
);
CREATE INDEX IX_SaScanJobs_StartedBy ON svcacct.UsageScanJobs(StartedBy, CreatedAt DESC);
CREATE TABLE svcacct.UsageScanJobAccounts(
    JobId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanJobAccounts_Job REFERENCES svcacct.UsageScanJobs(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanJobAccounts_Account REFERENCES svcacct.Accounts(Id),
    SearchedName nvarchar(100) NOT NULL,
    IsActive bit NOT NULL,
    LinkOutcome varchar(16) NULL CONSTRAINT CK_SaScanJobAccounts_Link CHECK (LinkOutcome IN ('Attached','AlreadyAttached','Unavailable','Failed','NotLinked')),
    CONSTRAINT PK_SaScanJobAccounts PRIMARY KEY (JobId, AccountId)
);
-- At most one queued or running web scan per account (ADR-0024 R2-4); the service also checks first and answers 409.
CREATE UNIQUE INDEX UX_SaScanJobAccounts_Active ON svcacct.UsageScanJobAccounts(AccountId) WHERE IsActive = 1;
CREATE INDEX IX_SaScanJobAccounts_Account ON svcacct.UsageScanJobAccounts(AccountId, JobId);
CREATE TABLE svcacct.UsageScanJobServers(
    JobId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanJobServers_Job REFERENCES svcacct.UsageScanJobs(Id),
    Ordinal int NOT NULL CONSTRAINT CK_SaScanJobServers_Ordinal CHECK (Ordinal BETWEEN 1 AND 500),
    ServerName nvarchar(255) NOT NULL,
    State varchar(16) NOT NULL CONSTRAINT CK_SaScanJobServers_State CHECK (State IN ('Pending','Connecting','Done')),
    Status varchar(16) NULL CONSTRAINT CK_SaScanJobServers_Status CHECK (Status IN ('Ok','Unreachable','NoEndpoint','Forbidden','Timeout',
        'Malformed','NotRun')),
    DocumentResult varchar(16) NULL CONSTRAINT CK_SaScanJobServers_Result CHECK (DocumentResult IN ('Success','Partial','Failed')),
    Document varbinary(max) NULL,
    ItemsJson nvarchar(max) NULL CONSTRAINT CK_SaScanJobServers_Items CHECK (ItemsJson IS NULL OR ISJSON(ItemsJson) = 1),
    FormerMatches int NULL,
    ExpectedMatches int NULL,
    RefusalCode varchar(32) NULL,
    ElapsedMs int NULL,
    FinishedAt datetimeoffset(7) NULL,
    FinishSeq int NULL,
    CONSTRAINT PK_SaScanJobServers PRIMARY KEY (JobId, ServerName),
    CONSTRAINT UQ_SaScanJobServers_Ordinal UNIQUE (JobId, Ordinal),
    CONSTRAINT CK_SaScanJobServers_Done CHECK (
        (State = 'Done' AND Status IS NOT NULL AND FinishedAt IS NOT NULL AND FinishSeq IS NOT NULL)
        OR (State <> 'Done' AND Status IS NULL AND FinishedAt IS NULL AND FinishSeq IS NULL AND Document IS NULL AND ItemsJson IS NULL)),
    -- Only a validated Ok answer keeps a document; every other status is "no information", never "not used".
    CONSTRAINT CK_SaScanJobServers_Document CHECK (
        (Status = 'Ok' AND Document IS NOT NULL AND DocumentResult IS NOT NULL AND ItemsJson IS NOT NULL AND FormerMatches IS NOT NULL)
        OR ((Status IS NULL OR Status <> 'Ok') AND Document IS NULL AND DocumentResult IS NULL AND ItemsJson IS NULL AND FormerMatches IS NULL))
);
CREATE UNIQUE INDEX UX_SaScanJobServers_Seq ON svcacct.UsageScanJobServers(JobId, FinishSeq) WHERE FinishSeq IS NOT NULL;
GO
CREATE TRIGGER svcacct.TR_SaScanJobs_Guard ON svcacct.UsageScanJobs AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) THROW 51431, 'Usage scan jobs are never deleted.', 1;
    -- A finished job is a closed record; its fixed columns never change after creation.
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.Id = d.Id
               WHERE d.State IN ('Completed','Cancelled','Failed')
                  OR i.Purpose <> d.Purpose OR ISNULL(i.ExpectedAccount, N'') <> ISNULL(d.ExpectedAccount, N'') OR i.AccountsJson <> d.AccountsJson
                  OR i.PlannedServers <> d.PlannedServers OR i.PlanSha256 <> d.PlanSha256 OR i.Collector <> d.Collector
                  OR i.StartedBy <> d.StartedBy OR i.CreatedAt <> d.CreatedAt
                  OR ISNULL(CONVERT(nvarchar(36), i.RetryOfJobId), N'') <> ISNULL(CONVERT(nvarchar(36), d.RetryOfJobId), N''))
        THROW 51431, 'A finished usage scan job or its fixed columns cannot change.', 1;
END;
GO
CREATE TRIGGER svcacct.TR_SaScanJobAccounts_Guard ON svcacct.UsageScanJobAccounts AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) THROW 51432, 'Usage scan job accounts are never deleted.', 1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.JobId = d.JobId AND i.AccountId = d.AccountId
               WHERE (d.IsActive = 0 AND i.IsActive = 1) OR i.SearchedName <> d.SearchedName
                  OR (d.LinkOutcome IS NOT NULL AND ISNULL(i.LinkOutcome, '') <> d.LinkOutcome))
        THROW 51432, 'A released account or a recorded link outcome cannot change.', 1;
END;
GO
CREATE TRIGGER svcacct.TR_SaScanJobServers_Guard ON svcacct.UsageScanJobServers AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) THROW 51433, 'Usage scan job servers are never deleted.', 1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.JobId = d.JobId AND i.ServerName = d.ServerName
               WHERE d.State = 'Done' OR i.Ordinal <> d.Ordinal)
        THROW 51433, 'A finished server result cannot change.', 1;
END;
GO
COMMIT TRANSACTION;
GO
