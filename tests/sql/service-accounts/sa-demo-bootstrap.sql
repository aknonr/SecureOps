/*
  LOCAL DEMO / TEST FIXTURE ONLY — never part of release discovery or a corporate database.
  Gives one already-approved demo user the first 'All' scope so the module administration page can grant the rest
  (the API refuses self-grants, so the very first scope cannot be granted through the UI).
  Usage: sqlcmd ... -v Identity="demo:platform-admin" -i sa-demo-bootstrap.sql
  The capability to use the module still comes only from the user's persisted role actions.
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DECLARE @identity nvarchar(256) = N'$(Identity)';
DECLARE @user uniqueidentifier = (SELECT UserId FROM security.Users WHERE CorporateIdentity = @identity AND AccessStatus = 'Approved');
IF @user IS NULL THROW 51000, 'Approved demo user not found; sign in once first.', 1;
IF @identity NOT LIKE N'demo:%' THROW 51000, 'Only demo identities may be bootstrapped by this fixture.', 1;
IF EXISTS (SELECT 1 FROM svcacct.ScopeGrants WHERE UserId = @user AND RevokedAt IS NULL AND ScopeKind = 'All')
BEGIN
    PRINT 'Scope already present; nothing changed.';
    RETURN;
END;
BEGIN TRANSACTION;
-- The schema forbids self-grants, so the fixture records a fixed synthetic grantor rather than the user.
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME(), @id uniqueidentifier = NEWID(), @fixture uniqueidentifier = '5A000000-0000-4000-8000-00000000DE70';
INSERT INTO svcacct.ScopeGrants(Id, UserId, ScopeKind, Reason, GrantedBy, GrantedAt)
VALUES(@id, @user, 'All', N'Yerel demo başlangıç kapsamı', @fixture, @now);
INSERT INTO svcacct.History(EntityType, EntityId, AccountId, Action, ChangesJson, Reason, ActorUserId, CorrelationId, OccurredAt)
VALUES('ScopeGrant', @id, NULL, 'Granted', N'{"Kind":"All","Fixture":"sa-demo-bootstrap"}', N'Yerel demo başlangıç kapsamı', @fixture, 'sa-demo-bootstrap', @now);
INSERT INTO audit.AuditLog(OccurredAt, Actor, Action, CorrelationId, DetailsJson)
VALUES(@now, 'fixture:sa-demo-bootstrap', 'ServiceAccount.ScopeGranted', 'sa-demo-bootstrap', N'{"Kind":"All","Fixture":"sa-demo-bootstrap","TargetUserId":"' + CONVERT(nvarchar(36), @user) + N'"}');
COMMIT TRANSACTION;
PRINT 'Demo scope granted.';
