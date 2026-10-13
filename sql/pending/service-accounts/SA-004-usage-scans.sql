/*
  Service Accounts module — candidate 4 (SA-004), numbered 030 by the module owner (ADR-0027, 2026-10-04).
  Requires 025 and 026 (svcacct.Accounts, svcacct.WorkRequests, svcacct.AccountUsages).

  Operator-run usage scans, imported as evidence (ADR-0027). Additive only: five new append-only tables; no existing table,
  constraint, row, role or grant is changed.
  - UsageScans: the uploaded file (bounded bytes + SHA-256) and its header: purpose, searched accounts, expected gMSA,
    times, counts and the uploader's run statement (where it ran and under which authority).
  - UsageScanServers: one row per PLANNED server with its result. Unreachable/NoResult carry no source status: no
    information, never "not used".
  - UsageScanItems: one row per matched component (Former = a searched account, Expected = the expected gMSA).
  - UsageScanLinks: a person attached the scan to an account (optionally through a participant's own open request).
  - UsageScanDecisions: a person turned an item into a usage, or dismissed it with a reason. Never automatic.
  Nothing here closes, frees or verifies an account. No secret column exists; the API refuses files with secret-like fields.
  Refuses replay. Run with SQLCMD -I -b. No down script: rollback retains these objects and data.
  Runtime grants: SA-004-API-permissions.sql (SELECT, INSERT only).
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL OR OBJECT_ID(N'svcacct.WorkRequests', N'U') IS NULL
    OR OBJECT_ID(N'svcacct.AccountUsages', N'U') IS NULL
    THROW 51360, 'Service Accounts candidate 4 requires 025 and 026.', 1;
IF OBJECT_ID(N'svcacct.UsageScans', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.UsageScanServers', N'U') IS NOT NULL
    OR OBJECT_ID(N'svcacct.UsageScanItems', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NOT NULL
    OR OBJECT_ID(N'svcacct.UsageScanDecisions', N'U') IS NOT NULL
    THROW 51360, 'Service Accounts candidate 4 already applied; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;

CREATE TABLE svcacct.UsageScans(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaUsageScans PRIMARY KEY,
    Sha256 char(64) NOT NULL,
    FileName nvarchar(260) NOT NULL,
    SizeBytes int NOT NULL CONSTRAINT CK_SaUsageScans_Size CHECK (SizeBytes > 0),
    Content varbinary(max) NOT NULL,
    Purpose varchar(16) NOT NULL CONSTRAINT CK_SaUsageScans_Purpose CHECK (Purpose IN ('Discovery','GmsaCheck')),
    ExpectedAccount nvarchar(100) NULL,
    AccountsJson nvarchar(max) NOT NULL CONSTRAINT CK_SaUsageScans_Accounts CHECK (ISJSON(AccountsJson) = 1),
    Tool varchar(16) NOT NULL CONSTRAINT CK_SaUsageScans_Tool CHECK (Tool IN ('Combined','Jea')),
    CombinedAt datetimeoffset(7) NOT NULL,
    FirstScannedAt datetimeoffset(7) NULL,
    LastScannedAt datetimeoffset(7) NULL,
    PlannedServers int NOT NULL CONSTRAINT CK_SaUsageScans_Planned CHECK (PlannedServers BETWEEN 1 AND 500),
    AnsweredServers int NOT NULL,
    RunStatement nvarchar(400) NOT NULL,
    UploadedBy uniqueidentifier NOT NULL,
    UploadedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT CK_SaUsageScans_Expected CHECK ((Purpose = 'GmsaCheck' AND ExpectedAccount IS NOT NULL)
        OR (Purpose = 'Discovery' AND ExpectedAccount IS NULL)),
    CONSTRAINT CK_SaUsageScans_Answered CHECK (AnsweredServers BETWEEN 0 AND PlannedServers),
    CONSTRAINT CK_SaUsageScans_Times CHECK ((AnsweredServers = 0 AND FirstScannedAt IS NULL AND LastScannedAt IS NULL)
        OR (AnsweredServers > 0 AND FirstScannedAt IS NOT NULL AND LastScannedAt >= FirstScannedAt)),
    CONSTRAINT UQ_SaUsageScans_Upload UNIQUE (UploadedBy, Sha256)
);

CREATE TABLE svcacct.UsageScanServers(
    ScanId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanServers_Scan REFERENCES svcacct.UsageScans(Id),
    ServerName nvarchar(255) NOT NULL,
    Result varchar(16) NOT NULL CONSTRAINT CK_SaScanServers_Result CHECK (Result IN ('Success','Partial','Failed','Unreachable','NoResult')),
    WindowsServices varchar(16) NULL CONSTRAINT CK_SaScanServers_Services CHECK (WindowsServices IN ('Success','Failed')),
    ScheduledTasks varchar(16) NULL CONSTRAINT CK_SaScanServers_Tasks CHECK (ScheduledTasks IN ('Success','Failed')),
    Iis varchar(16) NULL CONSTRAINT CK_SaScanServers_Iis CHECK (Iis IN ('Success','Failed','NotInstalled')),
    ScannedAt datetimeoffset(7) NULL,
    Warnings nvarchar(1000) NULL,
    CONSTRAINT PK_SaScanServers PRIMARY KEY (ScanId, ServerName),
    CONSTRAINT CK_SaScanServers_Answer CHECK (
        (Result IN ('Unreachable','NoResult') AND WindowsServices IS NULL AND ScheduledTasks IS NULL AND Iis IS NULL AND ScannedAt IS NULL)
        OR (Result IN ('Success','Partial','Failed') AND WindowsServices IS NOT NULL AND ScheduledTasks IS NOT NULL AND Iis IS NOT NULL
            AND ScannedAt IS NOT NULL))
);

CREATE TABLE svcacct.UsageScanItems(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaScanItems PRIMARY KEY,
    ScanId uniqueidentifier NOT NULL,
    ServerName nvarchar(255) NOT NULL,
    Role varchar(16) NOT NULL CONSTRAINT CK_SaScanItems_Role CHECK (Role IN ('Former','Expected')),
    MatchedAccount nvarchar(100) NOT NULL,
    ComponentType varchar(24) NOT NULL CONSTRAINT CK_SaScanItems_Type CHECK (ComponentType IN ('WindowsService','ScheduledTask','IisAppPool',
        'IisSite','IisApplication','IisVirtualDirectory')),
    ComponentName nvarchar(1024) NOT NULL,
    ConfiguredIdentity nvarchar(256) NOT NULL,
    State nvarchar(64) NULL,
    Detail nvarchar(1024) NULL,
    CONSTRAINT FK_SaScanItems_Server FOREIGN KEY (ScanId, ServerName) REFERENCES svcacct.UsageScanServers(ScanId, ServerName)
);
CREATE INDEX IX_SaScanItems_Scan ON svcacct.UsageScanItems(ScanId, Role, MatchedAccount);

CREATE TABLE svcacct.UsageScanLinks(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaScanLinks PRIMARY KEY,
    ScanId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanLinks_Scan REFERENCES svcacct.UsageScans(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanLinks_Account REFERENCES svcacct.Accounts(Id),
    MatchedAccount nvarchar(100) NOT NULL,
    RequestId uniqueidentifier NULL CONSTRAINT FK_SaScanLinks_Request REFERENCES svcacct.WorkRequests(Id),
    LinkedBy uniqueidentifier NOT NULL,
    LinkedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT UQ_SaScanLinks UNIQUE (ScanId, AccountId)
);
CREATE INDEX IX_SaScanLinks_Account ON svcacct.UsageScanLinks(AccountId, LinkedAt DESC);

CREATE TABLE svcacct.UsageScanDecisions(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaScanDecisions PRIMARY KEY,
    ItemId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanDecisions_Item REFERENCES svcacct.UsageScanItems(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaScanDecisions_Account REFERENCES svcacct.Accounts(Id),
    Decision varchar(16) NOT NULL CONSTRAINT CK_SaScanDecisions_Decision CHECK (Decision IN ('UsageRecorded','Dismissed')),
    UsageId uniqueidentifier NULL CONSTRAINT FK_SaScanDecisions_Usage REFERENCES svcacct.AccountUsages(Id),
    Reason nvarchar(1000) NULL,
    DecidedBy uniqueidentifier NOT NULL,
    DecidedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT UQ_SaScanDecisions UNIQUE (ItemId, AccountId),
    CONSTRAINT CK_SaScanDecisions_Shape CHECK ((Decision = 'UsageRecorded' AND UsageId IS NOT NULL)
        OR (Decision = 'Dismissed' AND UsageId IS NULL AND Reason IS NOT NULL))
);
CREATE INDEX IX_SaScanDecisions_Account ON svcacct.UsageScanDecisions(AccountId);
GO
CREATE TRIGGER svcacct.TR_SaUsageScans_Immutable ON svcacct.UsageScans AFTER UPDATE, DELETE AS
BEGIN THROW 51307, 'Usage scans are immutable evidence.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaScanServers_Immutable ON svcacct.UsageScanServers AFTER UPDATE, DELETE AS
BEGIN THROW 51307, 'Usage scans are immutable evidence.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaScanItems_Immutable ON svcacct.UsageScanItems AFTER UPDATE, DELETE AS
BEGIN THROW 51307, 'Usage scans are immutable evidence.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaScanLinks_Immutable ON svcacct.UsageScanLinks AFTER UPDATE, DELETE AS
BEGIN THROW 51307, 'Usage scan links are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaScanDecisions_Immutable ON svcacct.UsageScanDecisions AFTER UPDATE, DELETE AS
BEGIN THROW 51307, 'Usage scan decisions are append-only.', 1; END;
GO
COMMIT TRANSACTION;
GO
