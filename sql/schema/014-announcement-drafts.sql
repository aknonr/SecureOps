SET XACT_ABORT ON;
IF OBJECT_ID(N'ops.InUseRecords') IS NULL THROW 51140, 'Apply reviewed predecessor migrations first.', 1;
IF SCHEMA_ID(N'announcements') IS NULL EXEC(N'CREATE SCHEMA announcements AUTHORIZATION dbo;');
GO
BEGIN TRANSACTION;
IF OBJECT_ID(N'announcements.DraftRevisions') IS NOT NULL THROW 51141, 'Refuse existing announcement schema.', 1;
CREATE TABLE announcements.DraftRevisions (
    Id uniqueidentifier NOT NULL,
    Version bigint NOT NULL CHECK (Version > 0),
    OwnerId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    DocumentJson nvarchar(max) NOT NULL CHECK (ISJSON(DocumentJson)=1 AND DATALENGTH(DocumentJson)<=262144),
    CONSTRAINT PK_AnnouncementDraftRevisions PRIMARY KEY (Id,Version)
);
GO
CREATE TRIGGER announcements.TR_DraftRevisions_AppendOnly ON announcements.DraftRevisions
INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51142, 'Announcement revisions are append-only.', 1; END;
GO
COMMIT;
