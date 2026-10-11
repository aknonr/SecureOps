/*
  DISPOSABLE HARNESS DATABASE ONLY (sa-sql-harness.ps1). Proves migration 034 (design T13) without the API: evidence may
  now be owned by a change plan ('ChangePlan'); every other unknown owner type, including 'ChangePlanItem', is still
  refused (547); the replaced check is trusted (validated WITH CHECK); evidence stays immutable (51304).
  Seeds one synthetic account and one evidence row (never removed: Evidence is immutable). Run with SQLCMD -I -b.
*/
SET XACT_ABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME(), @actor uniqueidentifier = NEWID(), @account uniqueidentifier = NEWID(),
    @plan uniqueidentifier = NEWID(), @key nvarchar(64) = N'SYN-EVID-' + LEFT(CONVERT(nvarchar(36), NEWID()), 8);

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner'
           AND (is_not_trusted = 1 OR is_disabled = 1))
    THROW 51000, 'CK_SaEvidence_Owner is not trusted after 034.', 1;
PRINT 'CK_SaEvidence_Owner is trusted';

INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, IdentityKey, IdentityState, LifecycleState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
VALUES(@account, @key, UPPER(@key), N'P:UNKNOWN|' + UPPER(@key), 'Provisional', 'Active', @now, @actor, @now, @actor);
INSERT INTO svcacct.Evidence(Id, OwnerEntityType, OwnerEntityId, AccountId, FileName, ContentType, Sha256, SizeBytes, Content, CreatedAt, CreatedBy)
VALUES(NEWID(), 'ChangePlan', @plan, @account, N'sentetik-plan-kaniti.txt', 'text/plain', REPLICATE('a', 64), 4, 0x53594E31, @now, @actor);
PRINT 'evidence owned by a change plan accepted';

DECLARE @refused int = 0, @case int = 0, @owner varchar(16);
WHILE @case < 2
BEGIN
    SET @owner = CASE @case WHEN 0 THEN 'ChangePlanItem' ELSE 'Plan' END;
    BEGIN TRY
        INSERT INTO svcacct.Evidence(Id, OwnerEntityType, OwnerEntityId, AccountId, FileName, ContentType, Sha256, SizeBytes, Content, CreatedAt, CreatedBy)
        VALUES(NEWID(), @owner, @plan, @account, N'sentetik.txt', 'text/plain', REPLICATE('b', 64), 4, 0x53594E32, @now, @actor);
        THROW 51000, 'An evidence owner type outside the check was accepted.', 1;
    END TRY
    BEGIN CATCH IF ERROR_NUMBER() <> 547 THROW; SET @refused += 1; END CATCH;
    SET @case += 1;
END;
IF @refused <> 2 THROW 51000, 'Owner check refused the wrong number of rows.', 1;
PRINT 'unknown owner types (ChangePlanItem, Plan) still refused (547)';

BEGIN TRY
    UPDATE svcacct.Evidence SET OwnerEntityType = 'Account' WHERE OwnerEntityId = @plan;
    THROW 51000, 'Plan evidence UPDATE was not refused.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51304 THROW; END CATCH;
IF NOT EXISTS (SELECT 1 FROM svcacct.Evidence WHERE OwnerEntityId = @plan AND OwnerEntityType = 'ChangePlan')
    THROW 51000, 'Plan evidence changed.', 1;
PRINT 'plan evidence stays immutable (51304)';
