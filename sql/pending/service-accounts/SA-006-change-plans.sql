/*
  Service Accounts module — candidate 6 (SA-006), numbered 033 by the module owner (2026-10-07).
  Requires 025, 030 and 031 (svcacct.Accounts, svcacct.WorkRequests, svcacct.UsageScanLinks, requested gMSA name).

  gMSA conversion change plans (docs/service-accounts/CHANGE-PLAN-DESIGN.md). The system keeps the plan, its preview, the
  approval and the person's checklist; it never connects to a server and no secret column exists. Additive only: seven
  new tables; no existing table, constraint, row, role or grant is changed.
  - ChangePlans: the plan header. Only Status, CurrentPreviewVersion and UpdatedAt/By may change (RowVer follows); Id,
    Kind, Title, CreatedBy and CreatedAt are fixed and no plan is deleted (TR_SaChangePlans_Fixed, 51394).
  - ChangePlanAccounts: every add, remove and rename of an account is a new row; the current list is each account's
    latest row (highest Id) that is not Removed.
  - ChangePlanPreviews / ChangePlanItems: one preview version per plan and its rows from the accounts' latest Discovery
    scans; Sha256 is computed by the API from the rows (canonical order) and is what an approval binds to.
  - ChangePlanApprovals: at most one approval per preview (UQ_SaChangePlanApprovals_Preview). The approver is never the
    planner, the person who produced the preview or anyone who changed the plan's account list, and the approval must
    carry the preview's own plan and digest (TR_SaChangePlanApprovals_Separation, 51393) — enforced here even when the
    API is bypassed.
  - ChangeItemChecks: the person's own statement per preview row (latest row wins); not a verification.
  - ChangePlanEvents: every status change and edit with its actor.
  All tables except ChangePlans are append-only (51392). Refuses replay. Run with SQLCMD -I -b. No down script: rollback
  retains these objects and data. Runtime grants: SA-006-API-permissions.sql (SELECT, INSERT; UPDATE on ChangePlans only).
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL OR OBJECT_ID(N'svcacct.WorkRequests', N'U') IS NULL
    OR OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NULL OR COL_LENGTH(N'svcacct.WorkRequests', N'RequestedGmsaName') IS NULL
    THROW 51390, 'Service Accounts candidate 6 requires 025, 030 and 031.', 1;
IF OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.ChangePlanAccounts', N'U') IS NOT NULL
    OR OBJECT_ID(N'svcacct.ChangePlanPreviews', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.ChangePlanItems', N'U') IS NOT NULL
    OR OBJECT_ID(N'svcacct.ChangePlanApprovals', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.ChangeItemChecks', N'U') IS NOT NULL
    OR OBJECT_ID(N'svcacct.ChangePlanEvents', N'U') IS NOT NULL
    THROW 51390, 'Service Accounts candidate 6 already applied; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;

CREATE TABLE svcacct.ChangePlans(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaChangePlans PRIMARY KEY,
    Kind varchar(24) NOT NULL CONSTRAINT CK_SaChangePlans_Kind CHECK (Kind IN ('GmsaConversion')),
    Title nvarchar(200) NOT NULL CONSTRAINT CK_SaChangePlans_Title CHECK (LEN(Title) > 0),
    Status varchar(16) NOT NULL CONSTRAINT CK_SaChangePlans_Status CHECK (Status IN ('Draft','Previewed','Approved','InProgress','Completed','Cancelled')),
    CurrentPreviewVersion int NULL,
    CreatedBy uniqueidentifier NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaChangePlans_Preview CHECK ((Status = 'Draft' AND CurrentPreviewVersion IS NULL)
        OR (Status IN ('Previewed','Approved','InProgress','Completed') AND CurrentPreviewVersion IS NOT NULL)
        OR Status = 'Cancelled')
);
CREATE INDEX IX_SaChangePlans_Status ON svcacct.ChangePlans(Status, UpdatedAt DESC);

CREATE TABLE svcacct.ChangePlanAccounts(
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaChangePlanAccounts PRIMARY KEY,
    PlanId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanAccounts_Plan REFERENCES svcacct.ChangePlans(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanAccounts_Account REFERENCES svcacct.Accounts(Id),
    Action varchar(16) NOT NULL CONSTRAINT CK_SaChangePlanAccounts_Action CHECK (Action IN ('Added','Removed','Renamed')),
    TargetGmsaName nvarchar(256) NOT NULL CONSTRAINT CK_SaChangePlanAccounts_Name CHECK (LEN(TargetGmsaName) > 0),
    RequestId uniqueidentifier NULL CONSTRAINT FK_SaChangePlanAccounts_Request REFERENCES svcacct.WorkRequests(Id),
    ChangedBy uniqueidentifier NOT NULL,
    ChangedAt datetimeoffset(7) NOT NULL
);
CREATE INDEX IX_SaChangePlanAccounts_Plan ON svcacct.ChangePlanAccounts(PlanId, AccountId, Id);
CREATE INDEX IX_SaChangePlanAccounts_Account ON svcacct.ChangePlanAccounts(AccountId, PlanId, Id);

CREATE TABLE svcacct.ChangePlanPreviews(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaChangePlanPreviews PRIMARY KEY,
    PlanId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanPreviews_Plan REFERENCES svcacct.ChangePlans(Id),
    Version int NOT NULL CONSTRAINT CK_SaChangePlanPreviews_Version CHECK (Version >= 1),
    Sha256 char(64) NOT NULL,
    ScanFreshDays int NOT NULL CONSTRAINT CK_SaChangePlanPreviews_Fresh CHECK (ScanFreshDays BETWEEN 1 AND 365),
    ItemCount int NOT NULL CONSTRAINT CK_SaChangePlanPreviews_Items CHECK (ItemCount >= 1),
    CreatedBy uniqueidentifier NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT UQ_SaChangePlanPreviews_Version UNIQUE (PlanId, Version)
);

CREATE TABLE svcacct.ChangePlanItems(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaChangePlanItems PRIMARY KEY,
    PreviewId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanItems_Preview REFERENCES svcacct.ChangePlanPreviews(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanItems_Account REFERENCES svcacct.Accounts(Id),
    ScanLinkId uniqueidentifier NULL CONSTRAINT FK_SaChangePlanItems_ScanLink REFERENCES svcacct.UsageScanLinks(Id),
    ServerName nvarchar(255) NULL,
    ComponentType varchar(24) NULL CONSTRAINT CK_SaChangePlanItems_Type CHECK (ComponentType IN ('WindowsService','ScheduledTask','IisAppPool',
        'IisSite','IisApplication','IisVirtualDirectory')),
    ComponentName nvarchar(1024) NULL,
    CurrentIdentity nvarchar(256) NULL,
    TargetIdentity nvarchar(256) NOT NULL,
    Flag varchar(16) NOT NULL CONSTRAINT CK_SaChangePlanItems_Flag CHECK (Flag IN ('Ok','StaleScan','NoScan','NotCovered','ManualOnly','NameTooLong')),
    ScanAt datetimeoffset(7) NULL,
    CONSTRAINT CK_SaChangePlanItems_NoScan CHECK ((Flag = 'NoScan' AND ScanLinkId IS NULL AND ServerName IS NULL AND ComponentType IS NULL)
        OR (Flag <> 'NoScan' AND ScanLinkId IS NOT NULL AND ServerName IS NOT NULL))
);
CREATE INDEX IX_SaChangePlanItems_Preview ON svcacct.ChangePlanItems(PreviewId, AccountId);

CREATE TABLE svcacct.ChangePlanApprovals(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaChangePlanApprovals PRIMARY KEY,
    PlanId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanApprovals_Plan REFERENCES svcacct.ChangePlans(Id),
    PreviewId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanApprovals_Preview REFERENCES svcacct.ChangePlanPreviews(Id),
    Sha256 char(64) NOT NULL,
    OcoNumber nvarchar(64) NOT NULL CONSTRAINT CK_SaChangePlanApprovals_Oco CHECK (LEN(OcoNumber) > 0),
    WindowStart datetimeoffset(7) NOT NULL,
    WindowEnd datetimeoffset(7) NOT NULL,
    Reason nvarchar(1000) NOT NULL CONSTRAINT CK_SaChangePlanApprovals_Reason CHECK (LEN(Reason) > 0),
    ApprovedBy uniqueidentifier NOT NULL,
    ApprovedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT CK_SaChangePlanApprovals_Window CHECK (WindowEnd > WindowStart),
    CONSTRAINT UQ_SaChangePlanApprovals_Preview UNIQUE (PreviewId)
);
CREATE INDEX IX_SaChangePlanApprovals_Plan ON svcacct.ChangePlanApprovals(PlanId);

CREATE TABLE svcacct.ChangeItemChecks(
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaChangeItemChecks PRIMARY KEY,
    ItemId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangeItemChecks_Item REFERENCES svcacct.ChangePlanItems(Id),
    State varchar(16) NOT NULL CONSTRAINT CK_SaChangeItemChecks_State CHECK (State IN ('Done','Skipped','Failed','RolledBack')),
    Note nvarchar(1000) NULL,
    OutsideWindow bit NOT NULL,
    CheckedBy uniqueidentifier NOT NULL,
    CheckedAt datetimeoffset(7) NOT NULL
);
CREATE INDEX IX_SaChangeItemChecks_Item ON svcacct.ChangeItemChecks(ItemId, Id);

CREATE TABLE svcacct.ChangePlanEvents(
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaChangePlanEvents PRIMARY KEY,
    PlanId uniqueidentifier NOT NULL CONSTRAINT FK_SaChangePlanEvents_Plan REFERENCES svcacct.ChangePlans(Id),
    Event varchar(24) NOT NULL CONSTRAINT CK_SaChangePlanEvents_Event CHECK (Event IN ('Created','Updated','Previewed','Approved','Started',
        'Checked','Completed','Cancelled')),
    FromStatus varchar(16) NULL,
    ToStatus varchar(16) NOT NULL,
    PreviewVersion int NULL,
    Reason nvarchar(1000) NULL,
    Actor uniqueidentifier NOT NULL,
    At datetimeoffset(7) NOT NULL
);
CREATE INDEX IX_SaChangePlanEvents_Plan ON svcacct.ChangePlanEvents(PlanId, Event, Actor);
GO
CREATE TRIGGER svcacct.TR_SaChangePlans_Fixed ON svcacct.ChangePlans AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.Id = d.Id))
        OR EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
                   WHERE i.Kind <> d.Kind OR i.Title <> d.Title OR i.CreatedBy <> d.CreatedBy OR i.CreatedAt <> d.CreatedAt)
        THROW 51394, 'Change plans are never deleted; only status, preview version and update stamp may change.', 1;
END;
GO
CREATE TRIGGER svcacct.TR_SaChangePlanAccounts_AppendOnly ON svcacct.ChangePlanAccounts AFTER UPDATE, DELETE AS
BEGIN THROW 51392, 'Change plan records are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaChangePlanPreviews_AppendOnly ON svcacct.ChangePlanPreviews AFTER UPDATE, DELETE AS
BEGIN THROW 51392, 'Change plan records are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaChangePlanItems_AppendOnly ON svcacct.ChangePlanItems AFTER UPDATE, DELETE AS
BEGIN THROW 51392, 'Change plan records are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaChangePlanApprovals_AppendOnly ON svcacct.ChangePlanApprovals AFTER UPDATE, DELETE AS
BEGIN THROW 51392, 'Change plan records are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaChangeItemChecks_AppendOnly ON svcacct.ChangeItemChecks AFTER UPDATE, DELETE AS
BEGIN THROW 51392, 'Change plan records are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaChangePlanEvents_AppendOnly ON svcacct.ChangePlanEvents AFTER UPDATE, DELETE AS
BEGIN THROW 51392, 'Change plan records are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaChangePlanApprovals_Separation ON svcacct.ChangePlanApprovals AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    -- The approval must belong to its preview's plan and carry that preview's digest, and the approver is none of: the
    -- planner, the person who produced the preview, anyone who changed the plan's account list (design T2, T3).
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN svcacct.ChangePlans p ON p.Id = i.PlanId
               JOIN svcacct.ChangePlanPreviews v ON v.Id = i.PreviewId
               WHERE v.PlanId <> i.PlanId OR v.Sha256 <> i.Sha256 OR i.ApprovedBy = p.CreatedBy OR i.ApprovedBy = v.CreatedBy
                  OR EXISTS (SELECT 1 FROM svcacct.ChangePlanEvents e WHERE e.PlanId = i.PlanId AND e.Event = 'Updated' AND e.Actor = i.ApprovedBy))
        THROW 51393, 'Change plan approval refused: approver separation or preview binding.', 1;
END;
GO
COMMIT TRANSACTION;
GO
