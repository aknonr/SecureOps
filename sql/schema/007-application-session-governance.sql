/* Reviewed offline application-session schema. Execute only through the approved DBA migration process. */
IF OBJECT_ID(N'security.Users', N'U') IS NULL
    THROW 51000, 'Prerequisite table security.Users is missing.', 1;
GO

IF SCHEMA_ID(N'reporting') IS NULL
    THROW 51000, 'Prerequisite schema reporting is missing.', 1;
GO

IF OBJECT_ID(N'security.ApplicationSessions', N'U') IS NULL
BEGIN
    CREATE TABLE security.ApplicationSessions
    (
        SessionId uniqueidentifier NOT NULL CONSTRAINT PK_ApplicationSessions PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        LastSeenAtUtc datetimeoffset(7) NOT NULL,
        AbsoluteExpiresAtUtc datetimeoffset(7) NOT NULL,
        EndedAtUtc datetimeoffset(7) NULL,
        EndReason nvarchar(32) NULL,
        AuthenticationMethod nvarchar(64) NOT NULL,
        AccessVersion bigint NOT NULL,
        CONSTRAINT FK_ApplicationSessions_Users FOREIGN KEY (UserId) REFERENCES security.Users(UserId),
        CONSTRAINT CK_ApplicationSessions_TimeOrder CHECK
        (
            LastSeenAtUtc >= StartedAtUtc
            AND AbsoluteExpiresAtUtc > StartedAtUtc
            AND (EndedAtUtc IS NULL OR EndedAtUtc >= StartedAtUtc)
        ),
        CONSTRAINT CK_ApplicationSessions_TerminalState CHECK
        (
            (EndedAtUtc IS NULL AND EndReason IS NULL)
            OR
            (EndedAtUtc IS NOT NULL AND EndReason IN
                (N'IdleTimeout', N'AbsoluteTimeout', N'Logout', N'Revoked', N'AccessDisabled', N'AccessChanged', N'AuditFailure'))
        ),
        CONSTRAINT CK_ApplicationSessions_AuthenticationMethod CHECK (LEN(AuthenticationMethod) > 0),
        CONSTRAINT CK_ApplicationSessions_AccessVersion CHECK (AccessVersion > 0)
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ApplicationSessions_UserActive'
      AND object_id = OBJECT_ID(N'security.ApplicationSessions')
)
    CREATE INDEX IX_ApplicationSessions_UserActive
        ON security.ApplicationSessions (UserId, EndedAtUtc)
        INCLUDE (LastSeenAtUtc, AbsoluteExpiresAtUtc, AccessVersion);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ApplicationSessions_ActiveExpiry'
      AND object_id = OBJECT_ID(N'security.ApplicationSessions')
)
    CREATE INDEX IX_ApplicationSessions_ActiveExpiry
        ON security.ApplicationSessions (EndedAtUtc, AbsoluteExpiresAtUtc, LastSeenAtUtc)
        INCLUDE (SessionId, UserId, AuthenticationMethod, AccessVersion);
GO

CREATE OR ALTER VIEW reporting.ManagementSessionStatus
AS
    SELECT SessionId, StartedAtUtc, LastSeenAtUtc, AbsoluteExpiresAtUtc, EndedAtUtc, EndReason
    FROM security.ApplicationSessions;
GO
