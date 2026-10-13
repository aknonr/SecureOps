/*
  DISPOSABLE HARNESS DATABASE ONLY (sa-sql-harness.ps1). Proves the 035 database guards without the API (ADR-0030 §3, OR-2):
  submission events are append-only (51402); a submission is never deleted and its content (fingerprint, texts, requester,
  environment, key) never changes, also case-only and trailing-space changes (51403); only the allowed state transitions pass
  and a stored record id or number never changes (51404); one live submission per (plan, account) and one live holder per OR
  number (2601); one idempotency key per account (2627); coded columns take only their exact spelling and each state its shape
  (547); and the API runtime role cannot DELETE (229) and may UPDATE only the outcome columns (230).
  Seeds synthetic rows (never removed: submissions cannot be deleted) with synthetic placeholder numbers (OR-SYN-...).
  Run with SQLCMD -I -b; -v WithRole=1 also runs the runtime-role checks (needs SA-API and SA-008 permission scripts).
*/
SET XACT_ABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME(), @actor uniqueidentifier = NEWID(), @account uniqueidentifier = NEWID(), @other uniqueidentifier = NEWID(),
    @plan uniqueidentifier = NEWID(), @preview uniqueidentifier = NEWID(), @live uniqueidentifier = NEWID(), @done uniqueidentifier = NEWID(),
    @closed uniqueidentifier = NEWID(), @unknown uniqueidentifier = NEWID(), @key uniqueidentifier = NEWID(), @fp char(64) = REPLICATE('a', 64),
    @name nvarchar(64) = N'SYN-OR-' + LEFT(CONVERT(nvarchar(36), NEWID()), 8), @otherName nvarchar(64) = N'SYN-OR2-' + LEFT(CONVERT(nvarchar(36), NEWID()), 8),
    @number nvarchar(64) = N'OR-SYN-' + LEFT(CONVERT(nvarchar(36), NEWID()), 8);

INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, IdentityKey, IdentityState, LifecycleState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy) VALUES
    (@account, @name, UPPER(@name), N'P:UNKNOWN|' + UPPER(@name), 'Provisional', 'Active', @now, @actor, @now, @actor),
    (@other, @otherName, UPPER(@otherName), N'P:UNKNOWN|' + UPPER(@otherName), 'Provisional', 'Active', @now, @actor, @now, @actor);
INSERT INTO svcacct.ChangePlans(Id, Kind, Title, Status, CurrentPreviewVersion, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
VALUES(@plan, 'GmsaConversion', N'Sentetik OR planı', 'Approved', 1, @actor, @now, @actor, @now);
INSERT INTO svcacct.ChangePlanPreviews(Id, PlanId, Version, Sha256, ScanFreshDays, ItemCount, CreatedBy, CreatedAt)
VALUES(@preview, @plan, 1, REPLICATE('b', 64), 7, 2, @actor, @now);

-- @done: Created for @account; @closed: NotSent for @account (so a second live one is possible only after it); @unknown: OutcomeUnknown for @other.
INSERT INTO svcacct.ChangePlanRecordSubmissions(Id, PlanId, AccountId, PreviewId, IdempotencyKey, Fingerprint, Provider, RecordKind, Environment,
    RequesterExternalId, ShortDescription, Description, State, ExternalId, ExternalNumber, LinkVerification, OutcomeReason, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
VALUES
    (@closed, @plan, @account, @preview, NEWID(), @fp, 'Fake', 'OR', N'Test', N'920001', N'gmsa service account talebi(gmsaSyn)', N'Sentetik açıklama',
        'NotSent', NULL, NULL, NULL, 'NotConfigured', @actor, @now, @actor, @now),
    (@done, @plan, @account, @preview, @key, @fp, 'Fake', 'OR', N'Test', N'920001', N'gmsa service account talebi(gmsaSyn)', N'Sentetik açıklama',
        'Created', N'900001', @number, NULL, NULL, @actor, @now, @actor, @now),
    (@unknown, @plan, @other, @preview, NEWID(), @fp, 'Fake', 'OR', N'Test', N'920001', N'gmsa service account talebi(gmsaSyn2)', N'Sentetik açıklama',
        'OutcomeUnknown', NULL, NULL, NULL, 'Timeout', @actor, @now, @actor, @now);
INSERT INTO svcacct.ChangePlanRecordSubmissionEvents(SubmissionId, Event, FromState, ToState, Actor, At)
VALUES (@done, 'Started', NULL, 'Sending', @actor, @now), (@done, 'Created', 'Sending', 'Created', @actor, @now);
PRINT 'seeded synthetic OR submissions (Created, NotSent, OutcomeUnknown) and two events';

-- Append-only events (51402).
BEGIN TRY UPDATE svcacct.ChangePlanRecordSubmissionEvents SET Reason = N'x' WHERE SubmissionId = @done; THROW 51000, 'Submission event UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51402 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.ChangePlanRecordSubmissionEvents WHERE SubmissionId = @done; THROW 51000, 'Submission event DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51402 THROW; END CATCH;
PRINT 'submission events refuse UPDATE and DELETE (51402)';

-- Submissions: no delete, fixed content compared exactly (51403). @closed has no events, so DELETE reaches the trigger, not the foreign key.
BEGIN TRY DELETE FROM svcacct.ChangePlanRecordSubmissions WHERE Id = @closed; THROW 51000, 'Submission DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51403 THROW; END CATCH;
DECLARE @case int = 0;
WHILE @case < 7
BEGIN
    BEGIN TRY
        UPDATE svcacct.ChangePlanRecordSubmissions SET
            Description = CASE @case WHEN 0 THEN Description + N' ' WHEN 1 THEN UPPER(Description) ELSE Description END,
            ShortDescription = CASE @case WHEN 2 THEN ShortDescription + N'x' ELSE ShortDescription END,
            RequesterExternalId = CASE @case WHEN 3 THEN N'920004' ELSE RequesterExternalId END,
            Environment = CASE @case WHEN 4 THEN N'test' ELSE Environment END,
            Fingerprint = CASE @case WHEN 5 THEN REPLICATE('c', 64) ELSE Fingerprint END,
            IdempotencyKey = CASE @case WHEN 6 THEN NEWID() ELSE IdempotencyKey END
        WHERE Id = @done;
        THROW 51000, 'A submission content change was not refused.', 1;
    END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 51403 THROW; END CATCH;
    SET @case += 1;
END;
IF NOT EXISTS (SELECT 1 FROM svcacct.ChangePlanRecordSubmissions WHERE Id = @closed)
    OR NOT EXISTS (SELECT 1 FROM svcacct.ChangePlanRecordSubmissions WHERE Id = @done AND State = 'Created' AND Description = N'Sentetik açıklama' COLLATE Latin1_General_BIN2)
    THROW 51000, 'A submission row changed.', 1;
PRINT 'submissions refuse DELETE and content changes, also case-only and trailing-space ones (51403)';

-- Transitions (51404): final states stay final; a stored number never changes.
SET @case = 0;
WHILE @case < 4
BEGIN
    BEGIN TRY
        IF @case = 0 UPDATE svcacct.ChangePlanRecordSubmissions SET State = 'OutcomeUnknown', ExternalNumber = NULL, ExternalId = NULL WHERE Id = @done;
        IF @case = 1 UPDATE svcacct.ChangePlanRecordSubmissions SET State = 'Sending', OutcomeReason = NULL WHERE Id = @closed;
        IF @case = 2 UPDATE svcacct.ChangePlanRecordSubmissions SET State = 'Created', ExternalId = N'900001', ExternalNumber = N'OR-SYN-X' WHERE Id = @unknown;
        IF @case = 3 UPDATE svcacct.ChangePlanRecordSubmissions SET ExternalNumber = @number + N'9' WHERE Id = @done;
        THROW 51000, 'A refused submission transition was accepted.', 1;
    END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 51404 THROW; END CATCH;
    SET @case += 1;
END;
PRINT 'only allowed transitions pass; Created, NotSent stay final; OutcomeUnknown cannot become Created; a stored number never changes (51404)';

-- One live submission per (plan, account) and one live holder per number (2601); one key per account (2627).
BEGIN TRY
    INSERT INTO svcacct.ChangePlanRecordSubmissions(Id, PlanId, AccountId, PreviewId, IdempotencyKey, Fingerprint, Provider, RecordKind, Environment,
        RequesterExternalId, ShortDescription, Description, State, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
    VALUES (NEWID(), @plan, @account, @preview, NEWID(), @fp, 'Fake', 'OR', N'Test', N'920001', N'x', N'x', 'Sending', @actor, @now, @actor, @now);
    THROW 51000, 'A second live submission for one plan account was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 2601 THROW; END CATCH;
BEGIN TRY
    UPDATE svcacct.ChangePlanRecordSubmissions SET State = 'Linked', ExternalNumber = @number, LinkVerification = 'NotVerified' WHERE Id = @unknown;
    THROW 51000, 'One OR number linked to two live submissions was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 2601 THROW; END CATCH;
BEGIN TRY
    INSERT INTO svcacct.ChangePlanRecordSubmissions(Id, PlanId, AccountId, PreviewId, IdempotencyKey, Fingerprint, Provider, RecordKind, Environment,
        RequesterExternalId, ShortDescription, Description, State, OutcomeReason, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
    VALUES (NEWID(), @plan, @account, @preview, @key, @fp, 'Fake', 'OR', N'Test', N'920001', N'x', N'x', 'NotSent', 'NotConfigured', @actor, @now, @actor, @now);
    THROW 51000, 'A repeated idempotency key was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 2627 THROW; END CATCH;
PRINT 'one live submission per plan account and per OR number (2601); one idempotency key per account (2627)';

-- Exact spelling and state shape (547).
DECLARE @refused int = 0;
SET @case = 0;
WHILE @case < 6
BEGIN
    BEGIN TRY
        INSERT INTO svcacct.ChangePlanRecordSubmissions(Id, PlanId, AccountId, PreviewId, IdempotencyKey, Fingerprint, Provider, RecordKind, Environment,
            RequesterExternalId, ShortDescription, Description, State, ExternalId, ExternalNumber, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
        VALUES (NEWID(), @plan, @other, @preview, NEWID(), CASE @case WHEN 4 THEN REPLICATE('A', 64) ELSE @fp END,
            CASE @case WHEN 0 THEN 'fake' WHEN 1 THEN 'Fake ' ELSE 'Fake' END, 'OR', N'Test', N'920001', N'x', N'x',
            CASE @case WHEN 2 THEN 'notsent' WHEN 3 THEN 'Created' WHEN 5 THEN 'Belirsiz' ELSE 'NotSent' END,
            NULL, NULL, @actor, @now, @actor, @now);
        THROW 51000, 'An invalid submission shape was accepted.', 1;
    END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 547 THROW; SET @refused += 1; END CATCH;
    SET @case += 1;
END;
IF @refused <> 6 THROW 51000, 'Submission checks refused the wrong number of rows.', 1;
BEGIN TRY
    INSERT INTO svcacct.ChangePlanRecordSubmissionEvents(SubmissionId, Event, FromState, ToState, Actor, At) VALUES (@done, 'started', NULL, 'Sending', @actor, @now);
    THROW 51000, 'A case variant event was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 547 THROW; END CATCH;
PRINT 'case variants, trailing spaces, unknown states, upper-case fingerprint and a Created row without its number refused (547)';

-- Allowed transitions pass (reconciliation of the OutcomeUnknown row by a person).
UPDATE svcacct.ChangePlanRecordSubmissions SET State = 'ConfirmedNotCreated', UpdatedAt = @now WHERE Id = @unknown;
IF NOT EXISTS (SELECT 1 FROM svcacct.ChangePlanRecordSubmissions WHERE Id = @unknown AND State = 'ConfirmedNotCreated')
    THROW 51000, 'An allowed transition did not apply.', 1;
PRINT 'OutcomeUnknown -> ConfirmedNotCreated accepted';

IF N'$(WithRole)' = N'1'
BEGIN
    IF DATABASE_PRINCIPAL_ID(N'sa_guard_runtime') IS NULL CREATE USER sa_guard_runtime WITHOUT LOGIN;
    ALTER ROLE svcacct_api_runtime ADD MEMBER sa_guard_runtime;
    EXECUTE AS USER = N'sa_guard_runtime';
    DECLARE @failure nvarchar(200) = NULL;
    BEGIN TRY DELETE FROM svcacct.ChangePlanRecordSubmissions WHERE Id = @closed; SET @failure = N'API role DELETE on submissions was not refused.'; END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 229 SET @failure = N'API role DELETE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    IF @failure IS NULL
    BEGIN
        BEGIN TRY UPDATE svcacct.ChangePlanRecordSubmissions SET Description = N'x' WHERE Id = @done; SET @failure = N'API role content UPDATE was not refused.'; END TRY
        BEGIN CATCH IF ERROR_NUMBER() <> 230 SET @failure = N'API role content UPDATE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    END;
    IF @failure IS NULL
    BEGIN
        BEGIN TRY DELETE FROM svcacct.ChangePlanRecordSubmissionEvents WHERE SubmissionId = @done; SET @failure = N'API role DELETE on events was not refused.'; END TRY
        BEGIN CATCH IF ERROR_NUMBER() <> 229 SET @failure = N'API role event DELETE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    END;
    -- Everything the OR flow issues is granted (History and audit use the module's existing grants).
    IF @failure IS NULL AND EXISTS (SELECT 1 FROM (VALUES
        (N'svcacct.ChangePlanRecordSubmissions', N'SELECT'), (N'svcacct.ChangePlanRecordSubmissions', N'INSERT'),
        (N'svcacct.ChangePlanRecordSubmissionEvents', N'SELECT'), (N'svcacct.ChangePlanRecordSubmissionEvents', N'INSERT'),
        (N'svcacct.ChangePlans', N'SELECT'), (N'svcacct.ChangePlanAccounts', N'SELECT'), (N'svcacct.ChangePlanPreviews', N'SELECT'),
        (N'svcacct.ChangePlanItems', N'SELECT'), (N'svcacct.History', N'INSERT'), (N'audit.AuditLog', N'INSERT')
        ) p(ObjectName, Permission) WHERE HAS_PERMS_BY_NAME(p.ObjectName, 'OBJECT', p.Permission) <> 1)
        SET @failure = N'API role lacks a permission the OR flow uses.';
    IF @failure IS NULL AND EXISTS (SELECT 1 FROM (VALUES (N'State'), (N'ExternalId'), (N'ExternalNumber'), (N'LinkVerification'), (N'OutcomeReason'),
        (N'UpdatedBy'), (N'UpdatedAt')) c(ColumnName)
        WHERE HAS_PERMS_BY_NAME(N'svcacct.ChangePlanRecordSubmissions', 'OBJECT', 'UPDATE', c.ColumnName, 'COLUMN') <> 1)
        SET @failure = N'API role cannot UPDATE an outcome column.';
    IF @failure IS NULL AND (HAS_PERMS_BY_NAME(N'svcacct.ChangePlanRecordSubmissions', 'OBJECT', 'DELETE') = 1
        OR HAS_PERMS_BY_NAME(N'svcacct.ChangePlanRecordSubmissionEvents', 'OBJECT', 'DELETE') = 1
        OR HAS_PERMS_BY_NAME(N'svcacct.ChangePlanRecordSubmissionEvents', 'OBJECT', 'UPDATE') = 1)
        SET @failure = N'API role may DELETE an OR table or UPDATE the events.';
    REVERT;
    ALTER ROLE svcacct_api_runtime DROP MEMBER sa_guard_runtime;
    DROP USER sa_guard_runtime;
    IF @failure IS NOT NULL THROW 51000, @failure, 1;
    PRINT 'API runtime role holds every verb the OR flow uses, cannot DELETE (229) and may UPDATE only the outcome columns (230)';
END;
