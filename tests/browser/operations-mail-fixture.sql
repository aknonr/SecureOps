SET XACT_ABORT ON;
IF CONVERT(int,SERVERPROPERTY('IsLocalDB'))<>1 OR DB_NAME() NOT LIKE N'SecureOps[_]ResourcesV1[_]OcoOperations%'
    THROW 51182,'Task-owned loopback mail fixture only.',1;
IF EXISTS(SELECT 1 FROM security.Roles WHERE RoleCode='SyntheticMailBrowser')
    THROW 51182,'Mail fixture already exists; use a fresh database.',1;
BEGIN TRANSACTION;
DECLARE @actor uniqueidentifier=(SELECT UserId FROM security.Users WHERE CorporateIdentity='demo:platform-admin');
IF @actor IS NULL THROW 51182,'Authenticate the synthetic administrator first.',1;
INSERT INTO security.Roles(RoleId,RoleCode,DisplayName,Purpose,Version,CapabilitiesJson)
SELECT MAX(RoleId)+1,'SyntheticMailBrowser','Local mail acceptance','Synthetic loopback only',1,
    '["Announcements.SelfTest","Announcements.Send"]' FROM security.Roles;
INSERT INTO security.RoleAssignments(RoleAssignmentId,UserId,RoleId,GrantedByCorporateIdentity)
SELECT NEWID(),@actor,RoleId,'synthetic:fixture-admin' FROM security.Roles WHERE RoleCode='SyntheticMailBrowser';
UPDATE security.Users SET Mail='actor@example.invalid',DisplayName='Synthetic initiating operator',
    LoginName='synthetic.operator',ProfileUpdatedAt=SYSUTCDATETIME(),AccessVersion=AccessVersion+1 WHERE UserId=@actor;
COMMIT TRANSACTION;
