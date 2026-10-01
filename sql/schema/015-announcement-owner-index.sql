SET XACT_ABORT ON;
IF OBJECT_ID(N'announcements.DraftRevisions') IS NULL THROW 51150, 'Reviewed migration 014 is required.', 1;
BEGIN TRANSACTION;
CREATE INDEX IX_AnnouncementDraftRevisions_OwnerLatest ON announcements.DraftRevisions(OwnerId,Id,Version DESC);
COMMIT;
