/*
  DISPOSABLE HARNESS DATABASE ONLY (sa-sql-harness.ps1). Proves the 033 database guards without the API (design T2, T3, T8):
  every append-only change-plan table refuses UPDATE and DELETE (51392); ChangePlans refuses DELETE and changes to fixed
  columns (51394); one approval per preview (2627); the approval trigger refuses the planner, the previewer, an editor of
  the plan and a digest or plan that does not match the preview (51393); and the API runtime role cannot DELETE (229).
  Seeds synthetic rows (never removed: the tables are append-only) with fixed synthetic identifiers.
  Run with SQLCMD -I -b; -v WithRole=1 also runs the runtime-role checks (needs SA-API and SA-006 permission scripts).
*/
SET XACT_ABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
DECLARE @planner uniqueidentifier = NEWID(), @previewer uniqueidentifier = NEWID(), @editor uniqueidentifier = NEWID(),
    @approver uniqueidentifier = NEWID(), @second uniqueidentifier = NEWID();
DECLARE @account uniqueidentifier = NEWID(), @plan uniqueidentifier = NEWID(), @bare uniqueidentifier = NEWID(), @open uniqueidentifier = NEWID(),
    @preview uniqueidentifier = NEWID(), @openPreview uniqueidentifier = NEWID(), @item uniqueidentifier = NEWID();
DECLARE @sha char(64) = REPLICATE('a', 64), @openSha char(64) = REPLICATE('b', 64), @key nvarchar(64) = N'SYN-GUARD-' + LEFT(CONVERT(nvarchar(36), NEWID()), 8);

INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, IdentityKey, IdentityState, LifecycleState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
VALUES(@account, @key, UPPER(@key), N'P:UNKNOWN|' + UPPER(@key), 'Provisional', 'Active', @now, @planner, @now, @planner);
INSERT INTO svcacct.ChangePlans(Id, Kind, Title, Status, CurrentPreviewVersion, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt) VALUES
    (@plan, 'GmsaConversion', N'Sentetik koruma planı', 'Approved', 1, @planner, @now, @approver, @now),
    (@bare, 'GmsaConversion', N'Sentetik boş plan', 'Draft', NULL, @planner, @now, @planner, @now),
    (@open, 'GmsaConversion', N'Sentetik önizlenmiş plan', 'Previewed', 1, @planner, @now, @previewer, @now);
INSERT INTO svcacct.ChangePlanAccounts(PlanId, AccountId, Action, TargetGmsaName, ChangedBy, ChangedAt) VALUES
    (@plan, @account, 'Added', N'gmsaSynGuard', @planner, @now),
    (@open, @account, 'Added', N'gmsaSynGuard', @planner, @now);
INSERT INTO svcacct.ChangePlanPreviews(Id, PlanId, Version, Sha256, ScanFreshDays, ItemCount, CreatedBy, CreatedAt) VALUES
    (@preview, @plan, 1, @sha, 7, 1, @previewer, @now),
    (@openPreview, @open, 1, @openSha, 7, 1, @previewer, @now);
INSERT INTO svcacct.ChangePlanItems(Id, PreviewId, AccountId, TargetIdentity, Flag) VALUES (@item, @preview, @account, N'gmsaSynGuard', 'NoScan');
INSERT INTO svcacct.ChangePlanItems(Id, PreviewId, AccountId, TargetIdentity, Flag) VALUES (NEWID(), @openPreview, @account, N'gmsaSynGuard', 'NoScan');
INSERT INTO svcacct.ChangePlanEvents(PlanId, Event, FromStatus, ToStatus, Actor, At) VALUES
    (@plan, 'Created', NULL, 'Draft', @planner, @now),
    (@open, 'Created', NULL, 'Draft', @planner, @now),
    (@open, 'Updated', 'Draft', 'Draft', @editor, @now);
INSERT INTO svcacct.ChangePlanApprovals(Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy, ApprovedAt)
VALUES(NEWID(), @plan, @preview, @sha, N'OCO-SYN-1', @now, DATEADD(hour, 2, @now), N'Sentetik onay', @approver, @now);
INSERT INTO svcacct.ChangeItemChecks(ItemId, State, Note, OutsideWindow, CheckedBy, CheckedAt) VALUES (@item, 'Done', NULL, 0, @planner, @now);
PRINT 'seeded synthetic change plans';

-- T8: append-only tables refuse UPDATE and DELETE (51392).
BEGIN TRY UPDATE svcacct.ChangePlanAccounts SET TargetGmsaName = N'x' WHERE PlanId = @plan; THROW 51000, 'ChangePlanAccounts UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.ChangePlanAccounts WHERE PlanId = @plan; THROW 51000, 'ChangePlanAccounts DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY UPDATE svcacct.ChangePlanPreviews SET Sha256 = REPLICATE('c', 64) WHERE Id = @preview; THROW 51000, 'ChangePlanPreviews UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY UPDATE svcacct.ChangePlanItems SET ComponentName = N'x' WHERE Id = @item; THROW 51000, 'ChangePlanItems UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.ChangeItemChecks WHERE ItemId = @item; THROW 51000, 'ChangeItemChecks DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY UPDATE svcacct.ChangeItemChecks SET State = 'Failed' WHERE ItemId = @item; THROW 51000, 'ChangeItemChecks UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY UPDATE svcacct.ChangePlanApprovals SET OcoNumber = N'OCO-X' WHERE PlanId = @plan; THROW 51000, 'ChangePlanApprovals UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.ChangePlanApprovals WHERE PlanId = @plan; THROW 51000, 'ChangePlanApprovals DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY UPDATE svcacct.ChangePlanEvents SET Actor = @approver WHERE PlanId = @plan; THROW 51000, 'ChangePlanEvents UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.ChangePlanEvents WHERE PlanId = @plan; THROW 51000, 'ChangePlanEvents DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51392 THROW; END CATCH;
PRINT 'append-only change-plan tables refuse UPDATE and DELETE (51392)';

-- T8: ChangePlans keeps Id/Kind/Title/CreatedBy/CreatedAt and is never deleted (51394); status columns may change.
BEGIN TRY UPDATE svcacct.ChangePlans SET CreatedBy = @approver WHERE Id = @bare; THROW 51000, 'ChangePlans.CreatedBy UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51394 THROW; END CATCH;
BEGIN TRY UPDATE svcacct.ChangePlans SET Title = N'Değişti' WHERE Id = @bare; THROW 51000, 'ChangePlans.Title UPDATE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51394 THROW; END CATCH;
BEGIN TRY DELETE FROM svcacct.ChangePlans WHERE Id = @bare; THROW 51000, 'ChangePlans DELETE was not refused.', 1; END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51394 THROW; END CATCH;
UPDATE svcacct.ChangePlans SET Status = 'Cancelled', UpdatedBy = @planner, UpdatedAt = SYSUTCDATETIME() WHERE Id = @bare;
IF @@ROWCOUNT <> 1 THROW 51000, 'ChangePlans status UPDATE was refused.', 1;
PRINT 'ChangePlans fixed columns and delete refused (51394); status update allowed';

-- T2: at most one approval per preview (2627), even for a different approver.
BEGIN TRY
    INSERT INTO svcacct.ChangePlanApprovals(Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy, ApprovedAt)
    VALUES(NEWID(), @plan, @preview, @sha, N'OCO-SYN-2', @now, DATEADD(hour, 2, @now), N'Tekrar', @second, @now);
    THROW 51000, 'A second approval of one preview was not refused.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 2627 THROW; END CATCH;
PRINT 'second approval of one preview refused (2627)';

-- T3/T7: the approval trigger refuses planner, previewer, editor, a foreign preview and a wrong digest (51393).
DECLARE @refused int = 0, @candidate uniqueidentifier, @candidateSha char(64), @candidatePlan uniqueidentifier, @case int = 0;
WHILE @case < 5
BEGIN
    SELECT @candidate = CASE @case WHEN 0 THEN @planner WHEN 1 THEN @previewer WHEN 2 THEN @editor ELSE @second END,
        @candidateSha = CASE @case WHEN 3 THEN REPLICATE('d', 64) ELSE @openSha END,
        @candidatePlan = CASE @case WHEN 4 THEN @plan ELSE @open END;
    BEGIN TRY
        INSERT INTO svcacct.ChangePlanApprovals(Id, PlanId, PreviewId, Sha256, OcoNumber, WindowStart, WindowEnd, Reason, ApprovedBy, ApprovedAt)
        VALUES(NEWID(), @candidatePlan, @openPreview, @candidateSha, N'OCO-SYN-3', @now, DATEADD(hour, 2, @now), N'Atlatma denemesi', @candidate, @now);
        THROW 51000, 'An approval the separation trigger must refuse was written.', 1;
    END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 51393 THROW; SET @refused += 1; END CATCH;
    SET @case += 1;
END;
IF @refused <> 5 OR EXISTS (SELECT 1 FROM svcacct.ChangePlanApprovals WHERE PreviewId = @openPreview)
    THROW 51000, 'Separation trigger left an approval row.', 1;
PRINT 'approval trigger refuses planner, previewer, editor, wrong digest and foreign preview (51393)';

IF N'$(WithRole)' = N'1'
BEGIN
    IF DATABASE_PRINCIPAL_ID(N'sa_guard_runtime') IS NULL CREATE USER sa_guard_runtime WITHOUT LOGIN;
    ALTER ROLE svcacct_api_runtime ADD MEMBER sa_guard_runtime;
    EXECUTE AS USER = N'sa_guard_runtime';
    DECLARE @failure nvarchar(200) = NULL;
    BEGIN TRY DELETE FROM svcacct.ChangePlanEvents WHERE PlanId = @plan; SET @failure = N'API role DELETE on ChangePlanEvents was not refused.'; END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 229 SET @failure = N'API role DELETE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    IF @failure IS NULL
    BEGIN
        BEGIN TRY DELETE FROM svcacct.ChangePlans WHERE Id = @bare; SET @failure = N'API role DELETE on ChangePlans was not refused.'; END TRY
        BEGIN CATCH IF ERROR_NUMBER() <> 229 SET @failure = N'API role plan DELETE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    END;
    IF @failure IS NULL
    BEGIN
        BEGIN TRY UPDATE svcacct.ChangePlans SET CreatedBy = @approver WHERE Id = @bare; SET @failure = N'API role CreatedBy UPDATE was not refused.'; END TRY
        BEGIN CATCH IF ERROR_NUMBER() <> 51394 SET @failure = N'API role CreatedBy UPDATE failed with ' + CONVERT(nvarchar(12), ERROR_NUMBER()); END CATCH;
    END;
    REVERT;
    ALTER ROLE svcacct_api_runtime DROP MEMBER sa_guard_runtime;
    DROP USER sa_guard_runtime;
    IF @failure IS NOT NULL THROW 51000, @failure, 1;
    PRINT 'API runtime role cannot DELETE (229) and cannot change fixed plan columns (51394)';
END;
