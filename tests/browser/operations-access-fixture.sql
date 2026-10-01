SET XACT_ABORT ON;
IF CONVERT(int,SERVERPROPERTY('IsLocalDB'))<>1 OR DB_NAME() NOT LIKE N'SecureOps[_]ResourcesV1[_]OcoOperations%'
    THROW 51182,'Task-owned local operations fixture only.',1;
IF EXISTS(SELECT 1 FROM security.Users WHERE CorporateIdentity LIKE 'synthetic:operations:%')
    THROW 51182,'Fixture already exists; never overwrite.',1;
BEGIN TRANSACTION;
DECLARE @people TABLE(UserId uniqueidentifier,Number int,State varchar(16));
WITH Numbers AS (SELECT 1 AS n UNION ALL SELECT n+1 FROM Numbers WHERE n<140)
INSERT INTO @people SELECT NEWID(),n,CASE WHEN n<=80 THEN 'Pending' WHEN n<=130 THEN 'Approved' ELSE 'Disabled' END
FROM Numbers OPTION(MAXRECURSION 150);
INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus,DisplayName,LoginName,Mail,Uid,ProfileUpdatedAt)
SELECT UserId,CONCAT('synthetic:operations:',Number),'oidc',State,
    CONCAT(N'Operat',NCHAR(246),N'r ',FORMAT(Number,'000')),CONCAT('ops',FORMAT(Number,'000')),
    CONCAT('ops',FORMAT(Number,'000'),'@example.invalid'),FORMAT(Number,'000000'),SYSUTCDATETIME() FROM @people;
INSERT INTO security.AccessRequests(AccessRequestId,UserId,Status,RequestedAt,DecidedAt,DecidedByCorporateIdentity,DecisionReason)
SELECT NEWID(),UserId,CASE WHEN Number<=60 THEN 'Pending' WHEN Number<=80 THEN 'Rejected' ELSE 'Approved' END,
    DATEADD(MINUTE,-Number,SYSUTCDATETIME()),CASE WHEN Number>60 THEN SYSUTCDATETIME() END,
    CASE WHEN Number>60 THEN 'synthetic:fixture-admin' END,CASE WHEN Number>60 THEN 'Synthetic decision' END FROM @people;
INSERT INTO security.RoleAssignments(RoleAssignmentId,UserId,RoleId,GrantedByCorporateIdentity)
SELECT NEWID(),p.UserId,r.RoleId,'synthetic:fixture-admin' FROM @people p JOIN security.Roles r ON
    r.RoleCode=CASE WHEN p.Number%3=0 THEN 'InUseReviewer' WHEN p.Number%3=1 THEN 'Operator' ELSE 'ReadOnly' END
WHERE p.State='Approved';
COMMIT TRANSACTION;
