/*
  DISPOSABLE HARNESS DATABASE ONLY (sa-sql-harness.ps1). Proves migration 036 (EVENT-GUIDANCE-DESIGN.md §5.6, §8 E5) without
  the API: both event tables are append-only (51411); one idempotency key per account (2627); coded columns accept only their
  exact spelling and the date shape (547); no free-text column exists. Seeds one synthetic account, two events and one item (never
  removed: the tables are append-only). Run with SQLCMD -I -b; -v WithRole=1 also runs the runtime-role checks (needs
  SA-API-permissions.sql and SA-009-API-permissions.sql).
*/
SET XACT_ABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME(), @actor uniqueidentifier = NEWID(), @account uniqueidentifier = NEWID(),
    @event uniqueidentifier = NEWID(), @bare uniqueidentifier = NEWID(), @key uniqueidentifier = NEWID(), @digest char(64) = REPLICATE('a', 64),
    @name nvarchar(64) = N'SYN-EVENT-' + LEFT(CONVERT(nvarchar(36), NEWID()), 8);

INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, IdentityKey, IdentityState, LifecycleState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
VALUES(@account, @name, UPPER(@name), N'P:UNKNOWN|' + UPPER(@name), 'Provisional', 'Active', @now, @actor, @now, @actor);
INSERT INTO svcacct.AccountEvents(Id, AccountId, EventKind, OccurredOn, DatePrecision, Channel, IdempotencyKey, DraftDigest, CreatedBy, CreatedAt)
VALUES(@event, @account, 'PasswordChanged', '2026-10-01', 'DateOnly', 'Mail', @key, @digest, @actor, @now),
    (@bare, @account, 'Other', NULL, 'Unknown', 'Phone', NEWID(), @digest, @actor, @now);
INSERT INTO svcacct.AccountEventItems(EventId, ItemKind, EntityType, EntityId, Operation) VALUES (@event, 'Action', 'Action', NEWID(), 'Create');
PRINT 'seeded two synthetic account events (one without items, so DELETE reaches the trigger, not the foreign key)';

-- No body, sender, name, address or other free-text column (design §5.6).
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id IN (OBJECT_ID(N'svcacct.AccountEvents'), OBJECT_ID(N'svcacct.AccountEventItems')) AND t.name IN (N'nvarchar', N'nchar', N'ntext', N'text', N'xml', N'varbinary'))
    THROW 51000, 'An account event table has a free-text or binary column.', 1;
PRINT 'no free-text or binary column on the event tables';

-- Append-only (51411).
BEGIN TRY UPDATE svcacct.AccountEvents SET Channel = 'Phone' WHERE Id = @event; THROW 51000, 'AccountEvents UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51411 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.AccountEvents WHERE Id = @bare; THROW 51000, 'AccountEvents DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51411 THROW; END CATCH;
BEGIN TRY UPDATE svcacct.AccountEventItems SET Operation = 'Link' WHERE EventId = @event; THROW 51000, 'AccountEventItems UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51411 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.AccountEventItems WHERE EventId = @event; THROW 51000, 'AccountEventItems DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51411 THROW; END CATCH;
IF NOT EXISTS (SELECT 1 FROM svcacct.AccountEvents WHERE Id = @event AND Channel = 'Mail') OR NOT EXISTS (SELECT 1 FROM svcacct.AccountEvents WHERE Id = @bare) OR NOT EXISTS (SELECT 1 FROM svcacct.AccountEventItems WHERE EventId = @event AND Operation = 'Create')
    THROW 51000, 'An account event row changed.', 1;
PRINT 'event tables refuse UPDATE and DELETE (51411)';

-- E5: one idempotency key per account (2627).
BEGIN TRY
    INSERT INTO svcacct.AccountEvents(Id, AccountId, EventKind, OccurredOn, DatePrecision, Channel, IdempotencyKey, DraftDigest, CreatedBy, CreatedAt)
    VALUES(NEWID(), @account, 'Other', NULL, 'Unknown', 'Other', @key, REPLICATE('b', 64), @actor, @now);
    THROW 51000, 'A repeated idempotency key was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 2627 THROW; END CATCH;
PRINT 'repeated idempotency key refused (2627)';

-- Exact spelling and shape (547): letter case, trailing space, unknown kind, date shape, upper-case digest.
DECLARE @refused int = 0, @case int = 0;
WHILE @case < 6
BEGIN
    BEGIN TRY
        INSERT INTO svcacct.AccountEvents(Id, AccountId, EventKind, OccurredOn, DatePrecision, Channel, IdempotencyKey, DraftDigest, CreatedBy, CreatedAt)
        VALUES(NEWID(), @account,
            CASE @case WHEN 0 THEN 'passwordchanged' WHEN 1 THEN 'Other ' WHEN 2 THEN 'Verified' ELSE 'Other' END,
            CASE @case WHEN 3 THEN NULL WHEN 4 THEN '2026-10-01' ELSE NULL END,
            CASE @case WHEN 3 THEN 'DateOnly' ELSE 'Unknown' END,
            'Mail', NEWID(), CASE @case WHEN 5 THEN REPLICATE('A', 64) ELSE REPLICATE('c', 64) END, @actor, @now);
        THROW 51000, 'An invalid account event shape was accepted.', 1;
    END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 547 THROW; SET @refused += 1; END CATCH;
    SET @case += 1;
END;
IF @refused <> 6 THROW 51000, 'Event checks refused the wrong number of rows.', 1;
BEGIN TRY
    INSERT INTO svcacct.AccountEventItems(EventId, ItemKind, EntityType, EntityId, Operation) VALUES (@event, 'action', 'Action', NEWID(), 'Create');
    THROW 51000, 'A case variant item kind was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 547 THROW; END CATCH;
PRINT 'case variants, trailing spaces, unknown kinds, date shape and upper-case digest refused (547)';

IF N'$(WithRole)' = N'1'
BEGIN
    IF DATABASE_PRINCIPAL_ID(N'sa_guard_runtime') IS NULL CREATE USER sa_guard_runtime WITHOUT LOGIN;
    ALTER ROLE svcacct_api_runtime ADD MEMBER sa_guard_runtime;
    EXECUTE AS USER = N'sa_guard_runtime';
    DECLARE @failure nvarchar(200) = NULL;
    BEGIN TRY DELETE FROM svcacct.AccountEvents WHERE Id = @bare; SET @failure = N'API role DELETE on AccountEvents was not refused.'; END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 229 SET @failure = N'API role DELETE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    IF @failure IS NULL
    BEGIN
        BEGIN TRY UPDATE svcacct.AccountEventItems SET Operation = 'Link' WHERE EventId = @event; SET @failure = N'API role UPDATE on AccountEventItems was not refused.'; END TRY
        BEGIN CATCH IF ERROR_NUMBER() <> 229 SET @failure = N'API role UPDATE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    END;
    -- Everything the event commit issues is granted (the produced records use the module's existing grants).
    IF @failure IS NULL AND EXISTS (SELECT 1 FROM (VALUES
        (N'svcacct.AccountEvents', N'SELECT'), (N'svcacct.AccountEvents', N'INSERT'), (N'svcacct.AccountEventItems', N'SELECT'),
        (N'svcacct.AccountEventItems', N'INSERT'), (N'svcacct.Accounts', N'SELECT'), (N'svcacct.Accounts', N'UPDATE'),
        (N'svcacct.WorkRequests', N'SELECT'), (N'svcacct.WorkRequests', N'INSERT'), (N'svcacct.WorkRequests', N'UPDATE'),
        (N'svcacct.ActionEvents', N'SELECT'), (N'svcacct.ActionEvents', N'INSERT'), (N'svcacct.Communications', N'SELECT'),
        (N'svcacct.Communications', N'INSERT'), (N'svcacct.CommunicationAccounts', N'SELECT'), (N'svcacct.CommunicationAccounts', N'INSERT'),
        (N'svcacct.ExternalRecords', N'SELECT'), (N'svcacct.ExternalRecords', N'INSERT'), (N'svcacct.ExternalRecordLinks', N'SELECT'),
        (N'svcacct.ExternalRecordLinks', N'INSERT'), (N'svcacct.OwnershipAssignments', N'INSERT'), (N'svcacct.History', N'INSERT'),
        (N'audit.AuditLog', N'INSERT')
        ) p(ObjectName, Permission) WHERE HAS_PERMS_BY_NAME(p.ObjectName, 'OBJECT', p.Permission) <> 1)
        SET @failure = N'API role lacks a permission the event commit uses.';
    IF @failure IS NULL AND EXISTS (SELECT 1 FROM (VALUES (N'svcacct.AccountEvents'), (N'svcacct.AccountEventItems')) p(ObjectName)
        WHERE HAS_PERMS_BY_NAME(p.ObjectName, 'OBJECT', 'DELETE') = 1 OR HAS_PERMS_BY_NAME(p.ObjectName, 'OBJECT', 'UPDATE') = 1)
        SET @failure = N'API role may UPDATE or DELETE an event table.';
    REVERT;
    ALTER ROLE svcacct_api_runtime DROP MEMBER sa_guard_runtime;
    DROP USER sa_guard_runtime;
    IF @failure IS NOT NULL THROW 51000, @failure, 1;
    PRINT 'API runtime role holds every verb the event commit uses and cannot UPDATE or DELETE the event tables (229)';
END;
