SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'svcacct.TeamRoles', N'U') IS NULL
    OR COL_LENGTH(N'security.Roles', N'CapabilitiesJson') IS NULL
    THROW 51330, 'Migration 026 and reviewed role bundles are required.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int;
    EXEC @lock = sys.sp_getapplock @Resource=N'SecureOps.Access.Administration.v1',
        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @lock < 0 THROW 51330, 'Access administration lock unavailable.', 1;

    DECLARE @roleId smallint, @previous nvarchar(4000), @version bigint;
    SELECT @roleId=RoleId, @previous=CapabilitiesJson, @version=Version
        FROM security.Roles WITH (UPDLOCK, HOLDLOCK)
        WHERE RoleCode=N'Admin' AND IsProtected=1 AND IsSeeded=1;
    IF @roleId IS NULL OR ISJSON(@previous)<>1
        THROW 51330, 'Reviewed protected Admin bundle required.', 1;
    IF EXISTS (SELECT 1 FROM OPENJSON(@previous) WHERE value=N'ServiceAccounts.View')
        AND EXISTS (SELECT 1 FROM OPENJSON(@previous) WHERE value=N'ServiceAccounts.Administer')
        THROW 51330, '027 already satisfied; compare bundle and audit, do not replay.', 1;

    DECLARE @next nvarchar(max)=@previous;
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@next) WHERE value=N'ServiceAccounts.View')
        SET @next=JSON_MODIFY(@next, 'append $', N'ServiceAccounts.View');
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@next) WHERE value=N'ServiceAccounts.Administer')
        SET @next=JSON_MODIFY(@next, 'append $', N'ServiceAccounts.Administer');
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
            @affected AS affectedUsers, N'Applied' AS outcome
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    INSERT INTO audit.AuditLog(OccurredAt, Actor, Action, CorrelationId, DetailsJson)
        VALUES(SYSUTCDATETIME(), ORIGINAL_LOGIN(), N'AccessRoleDefinitionChanged',
            N'migration:027:admin-service-account-navigation', @details);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
