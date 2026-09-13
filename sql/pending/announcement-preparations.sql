-- Unnumbered candidate, outside reviewed schema/migration pairs; isolated test application only.
SET XACT_ABORT ON;
IF OBJECT_ID(N'announcements.DraftRevisions') IS NULL THROW 51170, 'Announcement drafts required.', 1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'announcements.Preparations') IS NOT NULL THROW 51171, 'Refuse existing preparation storage.', 1;
CREATE TABLE announcements.Preparations (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OwnerId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    DraftId uniqueidentifier NOT NULL,
    DraftVersion bigint NOT NULL,
    PreparedAt datetimeoffset NOT NULL,
    PreparedBy nvarchar(256) NOT NULL,
    Subject nvarchar(200) NOT NULL,
    DocumentJson nvarchar(max) NOT NULL CHECK(ISJSON(DocumentJson)=1 AND DATALENGTH(DocumentJson)<=24000000),
    FOREIGN KEY(DraftId,DraftVersion) REFERENCES announcements.DraftRevisions(Id,Version)
);
CREATE INDEX IX_Preparations_Owner ON announcements.Preparations(OwnerId,PreparedAt DESC,Id)
    INCLUDE(Subject,DraftVersion,PreparedBy);
GO
CREATE TRIGGER announcements.TR_Preparations_AppendOnly ON announcements.Preparations
INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51172, 'Preparations are append-only.', 1; END;
GO
COMMIT;
