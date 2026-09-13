SET XACT_ABORT ON;
IF OBJECT_ID(N'announcements.SourceJobs') IS NULL THROW 51170, 'Migration 016 is required.', 1;
IF COL_LENGTH(N'announcements.SourceJobs', N'AttemptId') IS NOT NULL THROW 51171, 'Refuse existing source recovery schema.', 1;
BEGIN TRANSACTION;
ALTER TABLE announcements.SourceJobs ADD
    AttemptId uniqueidentifier NULL,
    AttemptCount int NOT NULL CONSTRAINT DF_SourceJobs_AttemptCount DEFAULT 0,
    LeaseUntil datetimeoffset NULL,
    DispatchAfter datetimeoffset NOT NULL CONSTRAINT DF_SourceJobs_DispatchAfter DEFAULT SYSUTCDATETIME(),
    HangfireJobId nvarchar(64) NULL;
GO
ALTER TABLE announcements.SourceJobs ADD CONSTRAINT CK_SourceJobs_AttemptCount CHECK (AttemptCount >= 0);
CREATE INDEX IX_SourceJobs_Recovery ON announcements.SourceJobs(State, DispatchAfter)
    INCLUDE (LeaseUntil);
COMMIT;
