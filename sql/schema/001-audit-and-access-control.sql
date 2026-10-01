/* Reviewed offline SQL contract. Execute only through the approved DBA migration process. */
CREATE SCHEMA audit;
GO
CREATE SCHEMA security;
GO

CREATE TABLE audit.AuditLog
(
    AuditLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    OccurredAt datetimeoffset(7) NOT NULL,
    Actor nvarchar(256) NOT NULL,
    Action nvarchar(128) NOT NULL,
    AlertId uniqueidentifier NULL,
    ServerName nvarchar(255) NULL,
    CorrelationId nvarchar(256) NULL,
    DetailsJson nvarchar(max) NULL,
    SourceIp nvarchar(64) NULL
);
GO
CREATE INDEX IX_AuditLog_OccurredAt ON audit.AuditLog (OccurredAt DESC);
CREATE INDEX IX_AuditLog_CorrelationId ON audit.AuditLog (CorrelationId) WHERE CorrelationId IS NOT NULL;
GO
CREATE TRIGGER audit.TR_AuditLog_AppendOnly ON audit.AuditLog AFTER UPDATE, DELETE AS
BEGIN
    THROW 51000, 'audit.AuditLog is append-only.', 1;
END;
GO

CREATE TABLE security.Users
(
    UserId uniqueidentifier NOT NULL CONSTRAINT PK_SecurityUsers PRIMARY KEY DEFAULT NEWID(),
    CorporateIdentity nvarchar(256) NOT NULL,
    FirstAuthenticatedAt datetimeoffset(7) NOT NULL CONSTRAINT DF_SecurityUsers_FirstAuthenticatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_SecurityUsers_CorporateIdentity UNIQUE (CorporateIdentity)
);
GO
CREATE TABLE security.Roles
(
    RoleId smallint NOT NULL CONSTRAINT PK_SecurityRoles PRIMARY KEY,
    RoleCode nvarchar(64) NOT NULL CONSTRAINT UQ_SecurityRoles_RoleCode UNIQUE,
    IsSeeded bit NOT NULL CONSTRAINT DF_SecurityRoles_IsSeeded DEFAULT 1
);
GO
INSERT INTO security.Roles (RoleId, RoleCode) VALUES (1, 'Admin'), (2, 'Lead'), (3, 'Operator'), (4, 'ReadOnly');
GO
CREATE TABLE security.RoleAssignments
(
    RoleAssignmentId uniqueidentifier NOT NULL CONSTRAINT PK_SecurityRoleAssignments PRIMARY KEY DEFAULT NEWID(),
    UserId uniqueidentifier NOT NULL CONSTRAINT FK_SecurityRoleAssignments_User REFERENCES security.Users(UserId),
    RoleId smallint NOT NULL CONSTRAINT FK_SecurityRoleAssignments_Role REFERENCES security.Roles(RoleId),
    GrantedAt datetimeoffset(7) NOT NULL CONSTRAINT DF_SecurityRoleAssignments_GrantedAt DEFAULT SYSUTCDATETIME(),
    GrantedByCorporateIdentity nvarchar(256) NOT NULL,
    RevokedAt datetimeoffset(7) NULL,
    RevokedByCorporateIdentity nvarchar(256) NULL
);
GO
CREATE UNIQUE INDEX UX_SecurityRoleAssignments_Active ON security.RoleAssignments (UserId, RoleId) WHERE RevokedAt IS NULL;
GO
CREATE TABLE security.AccessRequests
(
    AccessRequestId uniqueidentifier NOT NULL CONSTRAINT PK_SecurityAccessRequests PRIMARY KEY DEFAULT NEWID(),
    UserId uniqueidentifier NOT NULL CONSTRAINT FK_SecurityAccessRequests_User REFERENCES security.Users(UserId),
    Status nvarchar(16) NOT NULL CONSTRAINT CK_SecurityAccessRequests_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected')),
    RequestedAt datetimeoffset(7) NOT NULL CONSTRAINT DF_SecurityAccessRequests_RequestedAt DEFAULT SYSUTCDATETIME(),
    DecidedAt datetimeoffset(7) NULL,
    DecidedByCorporateIdentity nvarchar(256) NULL,
    DecisionReason nvarchar(500) NULL
);
GO
CREATE UNIQUE INDEX UX_SecurityAccessRequests_Pending ON security.AccessRequests (UserId) WHERE Status = 'Pending';
GO
CREATE TABLE security.AccessRequestHistory
(
    AccessRequestHistoryId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SecurityAccessRequestHistory PRIMARY KEY,
    AccessRequestId uniqueidentifier NOT NULL,
    Status nvarchar(16) NOT NULL,
    ChangedAt datetimeoffset(7) NOT NULL CONSTRAINT DF_SecurityAccessRequestHistory_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedByCorporateIdentity nvarchar(256) NULL
);
GO
CREATE TRIGGER security.TR_AccessRequests_NoSelfApproval ON security.AccessRequests AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN security.Users u ON u.UserId = i.UserId WHERE i.Status = 'Approved' AND i.DecidedByCorporateIdentity = u.CorporateIdentity)
        THROW 51001, 'Self-approval is not allowed.', 1;

    INSERT INTO security.AccessRequestHistory (AccessRequestId, Status, ChangedByCorporateIdentity)
        SELECT AccessRequestId, Status, DecidedByCorporateIdentity FROM inserted;
END;
GO
CREATE TRIGGER security.TR_AccessRequestHistory_AppendOnly ON security.AccessRequestHistory AFTER UPDATE, DELETE AS
BEGIN
    THROW 51002, 'security.AccessRequestHistory is append-only.', 1;
END;
GO
