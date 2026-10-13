/*
  Service Accounts module — candidate 9 (SA-009), numbered 036 by the module owner (2026-10-13; 035 and the SA-008 name are left to OR-2).
  Requires 025 (svcacct.Accounts). Event guidance (docs/service-accounts/EVENT-GUIDANCE-DESIGN.md §5.6).

  "What happened?" events: one person-reported development on an account (the owner replied, a password was reported changed,
  ...) is recorded in one transaction as the module's existing records (communication, action report, references, request
  update/creation/close, ownership proposal). These two tables only remember that event and which records it produced:
  - AccountEvents: the event header. (AccountId, IdempotencyKey) is unique, so a repeated commit cannot record the event
    twice; DraftDigest is the API's SHA-256 of the canonical draft and tells a replay from a different body under the same key.
  - AccountEventItems: the records the event created, updated or linked (entity type and id only).
  No mail body, sender, name, address, attachment or free-text column exists. Both tables are append-only (51411). Coded text
  columns accept only their exact spelling (binary collation, no trailing spaces), because the database collation is
  case-insensitive. Additive only: no existing table, constraint, row, role or grant changes. Refuses replay (51410). Run with
  SQLCMD -I -b. No down script: rollback retains these objects and data. Runtime grants: SA-009-API-permissions.sql (SELECT, INSERT).
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.Accounts', N'U') IS NULL
    THROW 51410, 'Service Accounts candidate 9 requires 025.', 1;
IF OBJECT_ID(N'svcacct.AccountEvents', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.AccountEventItems', N'U') IS NOT NULL
    THROW 51410, 'Service Accounts candidate 9 already applied; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;

CREATE TABLE svcacct.AccountEvents(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaAccountEvents PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaAccountEvents_Account REFERENCES svcacct.Accounts(Id),
    EventKind varchar(24) NOT NULL CONSTRAINT CK_SaAccountEvents_Kind CHECK (EventKind COLLATE Latin1_General_BIN2 IN ('OwnerReplied',
        'PasswordChanged','AccountDeleted','GmsaConverted','OwnerChanged','ServiceMoved','Other') AND DATALENGTH(EventKind) = LEN(EventKind)),
    OccurredOn date NULL,
    DatePrecision varchar(8) NOT NULL CONSTRAINT CK_SaAccountEvents_Precision CHECK (DatePrecision COLLATE Latin1_General_BIN2 IN ('DateOnly',
        'Unknown') AND DATALENGTH(DatePrecision) = LEN(DatePrecision)),
    Channel varchar(8) NOT NULL CONSTRAINT CK_SaAccountEvents_Channel CHECK (Channel COLLATE Latin1_General_BIN2 IN ('Mail','Phone','Meeting',
        'Other') AND DATALENGTH(Channel) = LEN(Channel)),
    IdempotencyKey uniqueidentifier NOT NULL,
    DraftDigest char(64) NOT NULL CONSTRAINT CK_SaAccountEvents_Digest CHECK (DraftDigest COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'),
    CreatedBy uniqueidentifier NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT CK_SaAccountEvents_Date CHECK ((DatePrecision = 'DateOnly' AND OccurredOn IS NOT NULL) OR (DatePrecision = 'Unknown' AND OccurredOn IS NULL)),
    CONSTRAINT UQ_SaAccountEvents_Key UNIQUE (AccountId, IdempotencyKey)
);
CREATE INDEX IX_SaAccountEvents_Account ON svcacct.AccountEvents(AccountId, CreatedAt DESC);

CREATE TABLE svcacct.AccountEventItems(
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaAccountEventItems PRIMARY KEY,
    EventId uniqueidentifier NOT NULL CONSTRAINT FK_SaAccountEventItems_Event REFERENCES svcacct.AccountEvents(Id),
    ItemKind varchar(24) NOT NULL CONSTRAINT CK_SaAccountEventItems_Kind CHECK (ItemKind COLLATE Latin1_General_BIN2 IN ('Communication','Action',
        'Reference','RequestUpdate','RequestCreate','OwnershipProposal','RequestClose') AND DATALENGTH(ItemKind) = LEN(ItemKind)),
    EntityType varchar(16) NOT NULL CONSTRAINT CK_SaAccountEventItems_Entity CHECK (EntityType COLLATE Latin1_General_BIN2 IN ('Communication',
        'Action','ExternalRecord','Request','Ownership') AND DATALENGTH(EntityType) = LEN(EntityType)),
    EntityId uniqueidentifier NOT NULL,
    Operation varchar(8) NOT NULL CONSTRAINT CK_SaAccountEventItems_Operation CHECK (Operation COLLATE Latin1_General_BIN2 IN ('Create','Update',
        'Link') AND DATALENGTH(Operation) = LEN(Operation)),
    CONSTRAINT UQ_SaAccountEventItems_Item UNIQUE (EventId, ItemKind, EntityId)
);
CREATE INDEX IX_SaAccountEventItems_Entity ON svcacct.AccountEventItems(EntityType, EntityId);
GO
CREATE TRIGGER svcacct.TR_SaAccountEvents_AppendOnly ON svcacct.AccountEvents AFTER UPDATE, DELETE AS
BEGIN THROW 51411, 'Account event records are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaAccountEventItems_AppendOnly ON svcacct.AccountEventItems AFTER UPDATE, DELETE AS
BEGIN THROW 51411, 'Account event records are append-only.', 1; END;
GO
COMMIT TRANSACTION;
GO
