SET XACT_ABORT ON;
IF OBJECT_ID(N'announcements.DraftRevisions') IS NULL THROW 51160, 'Reviewed migrations 014-015 are required.', 1;
IF OBJECT_ID(N'ops.CommandExecutions') IS NULL THROW 51161, 'Reviewed migration 003 is required.', 1;
GO
BEGIN TRANSACTION;
IF OBJECT_ID(N'announcements.SourceJobs') IS NOT NULL THROW 51162, 'Refuse existing announcement source schema.', 1;

-- Durable owner-scoped source jobs. State lives here, not in the job queue, so a Worker or API
-- restart never erases an outcome. SnapshotJson is written exactly once on a terminal transition.
CREATE TABLE announcements.SourceJobs (
    JobId uniqueidentifier NOT NULL CONSTRAINT PK_AnnouncementSourceJobs PRIMARY KEY,
    OwnerId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    DraftId uniqueidentifier NOT NULL,
    Profile nvarchar(32) NOT NULL,
    OcoReference nvarchar(64) NOT NULL,
    SubmissionKey nvarchar(128) NOT NULL,
    State nvarchar(16) NOT NULL CONSTRAINT CK_AnnouncementSourceJobs_State
        CHECK (State IN (N'Queued', N'Running', N'Succeeded', N'Partial', N'Failed')),
    ErrorCode nvarchar(64) NULL,
    SnapshotJson nvarchar(max) NULL CONSTRAINT CK_AnnouncementSourceJobs_Snapshot
        CHECK (SnapshotJson IS NULL OR (ISJSON(SnapshotJson) = 1 AND DATALENGTH(SnapshotJson) <= 1048576)),
    SubmittedAt datetimeoffset NOT NULL,
    UpdatedAt datetimeoffset NOT NULL
);
GO
-- One accepted job per owner, draft and submission key: a repeated click returns the existing job.
CREATE UNIQUE INDEX UX_AnnouncementSourceJobs_Submission
    ON announcements.SourceJobs(OwnerId, DraftId, SubmissionKey);
CREATE INDEX IX_AnnouncementSourceJobs_OwnerDraftLatest
    ON announcements.SourceJobs(OwnerId, DraftId, SubmittedAt DESC);
GO
-- Source evidence is operational audit material: state may advance, rows are never removed.
CREATE TRIGGER announcements.TR_SourceJobs_NoDelete ON announcements.SourceJobs
INSTEAD OF DELETE AS BEGIN THROW 51163, 'Announcement source jobs are retained.', 1; END;
GO
-- Operator decisions, deliberately separate from any source snapshot. Version guards stale writes.
CREATE TABLE announcements.SourceOverrides (
    DraftId uniqueidentifier NOT NULL,
    OwnerId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    Version bigint NOT NULL CONSTRAINT CK_AnnouncementSourceOverrides_Version CHECK (Version > 0),
    OverridesJson nvarchar(max) NOT NULL CONSTRAINT CK_AnnouncementSourceOverrides_Json
        CHECK (ISJSON(OverridesJson) = 1 AND DATALENGTH(OverridesJson) <= 65536),
    AppliedJobId uniqueidentifier NULL REFERENCES announcements.SourceJobs(JobId),
    AppliedCapturedAt datetimeoffset NULL,
    UpdatedAt datetimeoffset NOT NULL,
    CONSTRAINT PK_AnnouncementSourceOverrides PRIMARY KEY (DraftId, OwnerId)
);
COMMIT;
