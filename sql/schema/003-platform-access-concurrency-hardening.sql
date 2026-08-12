/* Reviewed offline upgrade contract. Execute only through the approved DBA migration process. */
IF COL_LENGTH(N'security.Users', N'AuthenticationSource') IS NULL
    ALTER TABLE security.Users ADD AuthenticationSource nvarchar(64) NOT NULL
        CONSTRAINT DF_SecurityUsers_AuthenticationSource DEFAULT N'negotiate';
IF COL_LENGTH(N'security.Users', N'AccessStatus') IS NULL
    ALTER TABLE security.Users ADD AccessStatus nvarchar(16) NOT NULL
        CONSTRAINT DF_SecurityUsers_AccessStatus DEFAULT N'Pending';
IF COL_LENGTH(N'security.Users', N'LastAuthenticatedAt') IS NULL
    ALTER TABLE security.Users ADD LastAuthenticatedAt datetimeoffset(7) NOT NULL
        CONSTRAINT DF_SecurityUsers_LastAuthenticatedAt DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'security.Users', N'DisabledAt') IS NULL
    ALTER TABLE security.Users ADD DisabledAt datetimeoffset(7) NULL;
IF COL_LENGTH(N'security.Users', N'DisabledByCorporateIdentity') IS NULL
    ALTER TABLE security.Users ADD DisabledByCorporateIdentity nvarchar(256) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_SecurityUsers_AccessStatus')
    ALTER TABLE security.Users ADD CONSTRAINT CK_SecurityUsers_AccessStatus
        CHECK (AccessStatus IN (N'Pending', N'Approved', N'Disabled'));
GO

IF NOT EXISTS (SELECT 1 FROM security.Roles WHERE RoleCode = N'JiraPublisher')
    INSERT INTO security.Roles (RoleId, RoleCode) VALUES (5, N'JiraPublisher');
IF NOT EXISTS (SELECT 1 FROM security.Roles WHERE RoleCode = N'Auditor')
    INSERT INTO security.Roles (RoleId, RoleCode) VALUES (6, N'Auditor');
GO

IF COL_LENGTH(N'ops.OperationalRecords', N'SourceConcurrencyToken') IS NULL
    ALTER TABLE ops.OperationalRecords ADD SourceConcurrencyToken nvarchar(256) NOT NULL
        CONSTRAINT DF_OperationalRecords_SourceConcurrencyToken DEFAULT N'legacy:unvalidated';
IF COL_LENGTH(N'ops.OperationalRecords', N'LastSourceValidationAt') IS NULL
    ALTER TABLE ops.OperationalRecords ADD LastSourceValidationAt datetimeoffset(7) NULL;
IF COL_LENGTH(N'ops.OperationalRecords', N'ClaimedBy') IS NULL
    ALTER TABLE ops.OperationalRecords ADD ClaimedBy nvarchar(256) NULL;
IF COL_LENGTH(N'ops.OperationalRecords', N'ClaimedAt') IS NULL
    ALTER TABLE ops.OperationalRecords ADD ClaimedAt datetimeoffset(7) NULL;
IF COL_LENGTH(N'ops.OperationalRecords', N'ClaimExpiresAt') IS NULL
    ALTER TABLE ops.OperationalRecords ADD ClaimExpiresAt datetimeoffset(7) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OperationalRecords_Claim')
    ALTER TABLE ops.OperationalRecords ADD CONSTRAINT CK_OperationalRecords_Claim CHECK
    (
        (ClaimedBy IS NULL AND ClaimedAt IS NULL AND ClaimExpiresAt IS NULL)
        OR
        (ClaimedBy IS NOT NULL AND ClaimedAt IS NOT NULL AND ClaimExpiresAt > ClaimedAt)
    );
GO

IF OBJECT_ID(N'ops.CommandExecutions', N'U') IS NULL
BEGIN
    CREATE TABLE ops.CommandExecutions
    (
        CommandExecutionId uniqueidentifier NOT NULL CONSTRAINT PK_CommandExecutions PRIMARY KEY,
        CommandName nvarchar(128) NOT NULL,
        TargetId nvarchar(128) NOT NULL,
        IdempotencyKey nvarchar(256) NOT NULL,
        Actor nvarchar(256) NOT NULL,
        Status nvarchar(16) NOT NULL,
        ExecutionToken uniqueidentifier NOT NULL,
        ErrorCode nvarchar(128) NULL,
        StartedAt datetimeoffset(7) NOT NULL,
        LeaseExpiresAt datetimeoffset(7) NOT NULL,
        CompletedAt datetimeoffset(7) NULL,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT UQ_CommandExecutions_Scope UNIQUE (CommandName, TargetId, IdempotencyKey),
        CONSTRAINT CK_CommandExecutions_Status CHECK (Status IN (N'InProgress', N'Completed', N'Failed')),
        CONSTRAINT CK_CommandExecutions_Lease CHECK (LeaseExpiresAt > StartedAt)
    );
    CREATE INDEX IX_CommandExecutions_Lease ON ops.CommandExecutions (Status, LeaseExpiresAt);
END;
GO

IF COL_LENGTH(N'ops.CommandExecutions', N'ExecutionToken') IS NULL
    ALTER TABLE ops.CommandExecutions ADD ExecutionToken uniqueidentifier NOT NULL
        CONSTRAINT DF_CommandExecutions_ExecutionToken DEFAULT NEWID();
GO
