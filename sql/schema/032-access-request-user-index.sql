SET XACT_ABORT ON;
IF OBJECT_ID(N'security.AccessRequests', N'U') IS NULL
    OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'security.AccessRequests') AND name = N'IX_AccessRequests_StatusPage')
    THROW 51380, 'Access request user index 032 requires reviewed access migrations through 019.', 1;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'security.AccessRequests') AND name = N'IX_AccessRequests_UserRequested')
    THROW 51381, 'Access request user index 032 already applied; refuse replay.', 1;

BEGIN TRANSACTION;
CREATE INDEX IX_AccessRequests_UserRequested
    ON security.AccessRequests (UserId, RequestedAt DESC) INCLUDE (Status);
COMMIT TRANSACTION;
