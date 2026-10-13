/*
  DISPOSABLE HARNESS DATABASE ONLY (sa-sql-harness.ps1). Proves migration 037 (web usage scan jobs) without the API or Worker:
  at most one active job per account (2601), a released account cannot be re-activated and a recorded link outcome cannot change
  (51432), a finished server row cannot change (51433), a finished job cannot change (51431), rows are never deleted, and only an
  Ok server keeps a document (547). Seeds one synthetic account and two jobs (never removed: rows are never deleted).
  Run with SQLCMD -I -b.
*/
SET XACT_ABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME(), @actor uniqueidentifier = NEWID(), @account uniqueidentifier = NEWID(),
    @job1 uniqueidentifier = NEWID(), @job2 uniqueidentifier = NEWID(), @key nvarchar(64) = N'SYN-JOB-' + LEFT(CONVERT(nvarchar(36), NEWID()), 8),
    @sha char(64) = REPLICATE('c', 64);

INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, IdentityKey, IdentityState, LifecycleState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
VALUES(@account, @key, UPPER(@key), N'P:UNKNOWN|' + UPPER(@key), 'Provisional', 'Active', @now, @actor, @now, @actor);
INSERT INTO svcacct.UsageScanJobs(Id, State, Purpose, ExpectedAccount, AccountsJson, PlannedServers, PlanSha256, Collector, StartedBy, CreatedAt)
VALUES(@job1, 'Queued', 'Discovery', NULL, N'["svc_syn"]', 2, @sha, 'Fake', @actor, @now),
      (@job2, 'Queued', 'Discovery', NULL, N'["svc_syn"]', 1, @sha, 'Fake', @actor, @now);
INSERT INTO svcacct.UsageScanJobAccounts(JobId, AccountId, SearchedName, IsActive) VALUES(@job1, @account, N'svc_syn', 1);
INSERT INTO svcacct.UsageScanJobServers(JobId, Ordinal, ServerName, State) VALUES(@job1, 1, N'syn-app-01', 'Pending'), (@job1, 2, N'syn-app-02', 'Pending');
PRINT 'job, account and servers inserted';

BEGIN TRY
    INSERT INTO svcacct.UsageScanJobAccounts(JobId, AccountId, SearchedName, IsActive) VALUES(@job2, @account, N'svc_syn', 1);
    THROW 51000, 'A second active job for the same account was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 2601 THROW; END CATCH;
PRINT 'second active job for one account refused (2601)';

BEGIN TRY
    UPDATE svcacct.UsageScanJobServers SET State = 'Done', Status = 'Unreachable', FinishedAt = @now, FinishSeq = 1, Document = 0x7B7D
    WHERE JobId = @job1 AND Ordinal = 1;
    THROW 51000, 'A document on a server that was not read was accepted.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 547 THROW; END CATCH;
PRINT 'document without Ok refused (547)';

UPDATE svcacct.UsageScanJobServers SET State = 'Done', Status = 'Unreachable', FinishedAt = @now, FinishSeq = 1 WHERE JobId = @job1 AND Ordinal = 1;
BEGIN TRY
    UPDATE svcacct.UsageScanJobServers SET Status = 'Ok' WHERE JobId = @job1 AND Ordinal = 1;
    THROW 51000, 'A finished server row changed.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51433 THROW; END CATCH;
PRINT 'finished server row stays (51433)';

UPDATE svcacct.UsageScanJobAccounts SET IsActive = 0, LinkOutcome = 'NotLinked' WHERE JobId = @job1;
INSERT INTO svcacct.UsageScanJobAccounts(JobId, AccountId, SearchedName, IsActive) VALUES(@job2, @account, N'svc_syn', 1);
PRINT 'released account may start a new job';
BEGIN TRY
    UPDATE svcacct.UsageScanJobAccounts SET IsActive = 1 WHERE JobId = @job1;
    THROW 51000, 'A released account was re-activated.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() NOT IN (51432, 2601) THROW; END CATCH;
BEGIN TRY
    UPDATE svcacct.UsageScanJobAccounts SET LinkOutcome = 'Attached' WHERE JobId = @job1;
    THROW 51000, 'A recorded link outcome changed.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51432 THROW; END CATCH;
PRINT 're-activation and link outcome change refused (51432)';

UPDATE svcacct.UsageScanJobs SET State = 'Failed', FailureCode = 'workerLost', FinishedAt = @now WHERE Id = @job1;
BEGIN TRY
    UPDATE svcacct.UsageScanJobs SET State = 'Completed', FailureCode = NULL WHERE Id = @job1;
    THROW 51000, 'A finished job changed.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51431 THROW; END CATCH;
BEGIN TRY
    UPDATE svcacct.UsageScanJobs SET PlannedServers = 3 WHERE Id = @job2;
    THROW 51000, 'A fixed job column changed.', 1;
END TRY
BEGIN CATCH IF ERROR_NUMBER() <> 51431 THROW; END CATCH;
PRINT 'finished job and fixed columns stay (51431)';

DECLARE @refused int = 0;
BEGIN TRY DELETE FROM svcacct.UsageScanJobServers WHERE JobId = @job1; END TRY BEGIN CATCH IF ERROR_NUMBER() <> 51433 THROW; SET @refused += 1; END CATCH;
BEGIN TRY DELETE FROM svcacct.UsageScanJobAccounts WHERE JobId = @job1; END TRY BEGIN CATCH IF ERROR_NUMBER() <> 51432 THROW; SET @refused += 1; END CATCH;
BEGIN TRY DELETE FROM svcacct.UsageScanJobs WHERE Id = @job2; END TRY BEGIN CATCH IF ERROR_NUMBER() NOT IN (51431, 547) THROW; SET @refused += 1; END CATCH;
IF @refused <> 3 THROW 51000, 'A job row was deleted.', 1;
PRINT 'job rows are never deleted';
