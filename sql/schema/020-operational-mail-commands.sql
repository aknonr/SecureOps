SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF COL_LENGTH(N'security.Roles',N'CapabilitiesJson') IS NULL THROW 51200,'Migration 019 required.',1;
IF OBJECT_ID(N'announcements.MailCommands') IS NOT NULL OR OBJECT_ID(N'ops.OperationEvents') IS NOT NULL
    THROW 51200,'020 objects already exist; compare definitions instead of replay.',1;
BEGIN TRANSACTION;
CREATE TABLE announcements.MailCommands (
    CommandId uniqueidentifier NOT NULL PRIMARY KEY,
    PreparationId uniqueidentifier NOT NULL REFERENCES announcements.Preparations(Id),
    DraftId uniqueidentifier NOT NULL,
    OwnerId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    Kind varchar(16) NOT NULL CHECK(Kind IN ('SelfTest','Send')),
    Version bigint NOT NULL CHECK(Version>0),
    State varchar(16) NOT NULL CHECK(State IN ('Queued','Dispatching','Accepted','Partial','Failed','Unknown','Denied')),
    CreatedAt datetimeoffset(7) NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    LeaseExpiresAt datetimeoffset(7) NULL,
    ExecutionToken uniqueidentifier NULL,
    IntentJson nvarchar(max) NOT NULL CHECK(ISJSON(IntentJson)=1 AND DATALENGTH(IntentJson)<=131072),
    MessageBytes varbinary(max) NOT NULL CHECK(DATALENGTH(MessageBytes) BETWEEN 1 AND 3000000),
    OutcomeJson nvarchar(max) NULL CHECK(OutcomeJson IS NULL OR (ISJSON(OutcomeJson)=1 AND DATALENGTH(OutcomeJson)<=131072))
);
CREATE UNIQUE INDEX UX_MailCommands_OneDistribution ON announcements.MailCommands(DraftId) WHERE Kind='Send';
CREATE INDEX IX_MailCommands_Recovery ON announcements.MailCommands(State,UpdatedAt,CommandId) INCLUDE(LeaseExpiresAt);
CREATE INDEX IX_MailCommands_Owner ON announcements.MailCommands(OwnerId,DraftId,CreatedAt DESC,CommandId) INCLUDE(State,Kind,Version);
CREATE TABLE ops.OperationEvents (
    EventId uniqueidentifier NOT NULL PRIMARY KEY,
    CommandId uniqueidentifier NOT NULL,
    RecordType varchar(32) NOT NULL,
    RecordId nvarchar(128) NOT NULL,
    ActorId uniqueidentifier NOT NULL REFERENCES security.Users(UserId),
    Action varchar(64) NOT NULL,
    Outcome varchar(32) NOT NULL,
    OccurredAt datetimeoffset(7) NOT NULL,
    EvidenceJson nvarchar(max) NOT NULL CHECK(ISJSON(EvidenceJson)=1 AND DATALENGTH(EvidenceJson)<=65536)
);
CREATE INDEX IX_OperationEvents_Command ON ops.OperationEvents(CommandId,OccurredAt,EventId);
CREATE INDEX IX_OperationEvents_Record ON ops.OperationEvents(RecordType,RecordId,OccurredAt DESC,EventId);
-- Preserve existing implicit draft/source/preparation rights as explicit actions.
-- SelfTest and Send are deliberately not granted to any role.
UPDATE security.Roles SET CapabilitiesJson=JSON_MODIFY(JSON_MODIFY(CapabilitiesJson,
    'append $','Announcements.Source'),'append $','Announcements.Prepare'),Version=Version+1
WHERE EXISTS(SELECT 1 FROM OPENJSON(CapabilitiesJson) WHERE value='Announcements.Drafts');
UPDATE u SET AccessVersion=AccessVersion+1 FROM security.Users u WHERE EXISTS(
    SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
    CROSS APPLY OPENJSON(r.CapabilitiesJson) c WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value='Announcements.Drafts');
GO
CREATE TRIGGER ops.TR_OperationEvents_AppendOnly ON ops.OperationEvents INSTEAD OF UPDATE,DELETE AS
BEGIN THROW 51201,'Operational evidence is append-only.',1; END;
GO
CREATE TRIGGER announcements.TR_MailCommands_NoDelete ON announcements.MailCommands INSTEAD OF DELETE AS
BEGIN THROW 51202,'Mail commands are durable evidence.',1; END;
GO
CREATE TRIGGER announcements.TR_MailCommands_ImmutableIntent ON announcements.MailCommands AFTER UPDATE AS
BEGIN
    IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.CommandId=d.CommandId WHERE
        i.PreparationId<>d.PreparationId OR i.DraftId<>d.DraftId OR i.OwnerId<>d.OwnerId OR i.Kind<>d.Kind OR i.CreatedAt<>d.CreatedAt
        OR CONVERT(varbinary(max),i.IntentJson)<>CONVERT(varbinary(max),d.IntentJson) OR i.MessageBytes<>d.MessageBytes)
        THROW 51203,'Confirmed mail intent and bytes are immutable.',1;
END;
GO
COMMIT TRANSACTION;
