SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'svcacct.TeamRoles', N'U') IS NULL
    OR COL_LENGTH(N'security.Roles', N'CapabilitiesJson') IS NULL
    THROW 51340, 'Reviewed 026/027 role bundle baseline required.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int;
    EXEC @lock = sys.sp_getapplock @Resource=N'SecureOps.Access.Administration.v1',
        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @lock < 0 THROW 51340, 'Access administration lock unavailable.', 1;

    DECLARE @roleId smallint, @previous nvarchar(4000), @version bigint;
    SELECT @roleId=RoleId, @previous=CapabilitiesJson, @version=Version
        FROM security.Roles WITH (UPDLOCK, HOLDLOCK)
        WHERE RoleCode=N'Admin' AND IsProtected=1 AND IsSeeded=1;
    IF @roleId IS NULL OR ISJSON(@previous)<>1 OR LEFT(LTRIM(@previous),1)<>N'['
        THROW 51340, 'Reviewed protected Admin capability array required.', 1;
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@previous) WHERE value=N'ServiceAccounts.View')
        OR NOT EXISTS (SELECT 1 FROM OPENJSON(@previous) WHERE value=N'ServiceAccounts.Administer')
        THROW 51340, '027 Admin navigation baseline required; do not skip it.', 1;

    DECLARE @required table(Code nvarchar(80) NOT NULL PRIMARY KEY);
    INSERT INTO @required VALUES
        (N'ServiceAccounts.Work'), (N'ServiceAccounts.Assign'),
        (N'ServiceAccounts.Verify'), (N'ServiceAccounts.Import'), (N'ServiceAccounts.Report');
    IF NOT EXISTS (SELECT 1 FROM @required r WHERE NOT EXISTS (
        SELECT 1 FROM OPENJSON(@previous) p WHERE p.value COLLATE DATABASE_DEFAULT=r.Code))
        THROW 51340, '028 already satisfied; compare bundle and audit, do not replay.', 1;

    DECLARE @next nvarchar(max)=@previous;
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@next) WHERE value=N'ServiceAccounts.Work')
        SET @next=JSON_MODIFY(@next, 'append $', N'ServiceAccounts.Work');
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@next) WHERE value=N'ServiceAccounts.Assign')
        SET @next=JSON_MODIFY(@next, 'append $', N'ServiceAccounts.Assign');
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@next) WHERE value=N'ServiceAccounts.Verify')
        SET @next=JSON_MODIFY(@next, 'append $', N'ServiceAccounts.Verify');
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@next) WHERE value=N'ServiceAccounts.Import')
        SET @next=JSON_MODIFY(@next, 'append $', N'ServiceAccounts.Import');
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@next) WHERE value=N'ServiceAccounts.Report')
        SET @next=JSON_MODIFY(@next, 'append $', N'ServiceAccounts.Report');

    UPDATE security.Roles SET CapabilitiesJson=@next, Version=Version+1 WHERE RoleId=@roleId;
    UPDATE u SET AccessVersion=AccessVersion+1
        FROM security.Users u WHERE EXISTS (
            SELECT 1 FROM security.RoleAssignments a
            WHERE a.UserId=u.UserId AND a.RoleId=@roleId AND a.RevokedAt IS NULL);
    DECLARE @affected int=@@ROWCOUNT;
    DECLARE @details nvarchar(max)=(
        SELECT 1 AS schemaVersion, N'Migration' AS actorKind, N'Admin' AS roleCode,
            @version AS previousVersion, @version+1 AS nextVersion,
            JSON_QUERY(@previous) AS previousCapabilities, JSON_QUERY(@next) AS nextCapabilities,
            @affected AS affectedUsers, N'Applied' AS outcome,
            N'2026-10-03 finite Service Accounts Admin operations' AS ownerDecision
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    INSERT INTO audit.AuditLog(OccurredAt, Actor, Action, CorrelationId, DetailsJson)
        VALUES(SYSUTCDATETIME(), ORIGINAL_LOGIN(), N'AccessRoleDefinitionChanged',
            N'migration:028:admin-service-account-operations', @details);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
