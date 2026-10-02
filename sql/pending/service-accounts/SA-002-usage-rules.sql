/*
  Service Accounts module — UNNUMBERED CANDIDATE 2 (not a deployable migration).
  Requires SA-001 (schema svcacct). Codex reserves the migration number before promotion.

  Additive only: two new tables in svcacct; no existing object, role, grant or row is changed.
  - AccountUsages: where an account is used (knowledge-base rule input), entered by people. Never deleted: a usage is
    removed with a reason; a rule exception records who accepted it and why.
  - TeamRoles: module dictionary that tags teams as SQL teams (accounts evaluated as gMSA) and names the single gMSA
    executing team. Configuration, not authority: it grants no access.
  Refuses replay. Run with SQLCMD -I -b. No down script: rollback retains these objects and data.
  Runtime grants: SA-002-API-permissions.sql.
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF SCHEMA_ID(N'svcacct') IS NULL OR OBJECT_ID(N'svcacct.Accounts') IS NULL
    THROW 51320, 'Service Accounts candidate 2 requires SA-001.', 1;
IF OBJECT_ID(N'svcacct.AccountUsages') IS NOT NULL OR OBJECT_ID(N'svcacct.TeamRoles') IS NOT NULL
    THROW 51320, 'Service Accounts candidate 2 already applied; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;

CREATE TABLE svcacct.AccountUsages(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaUsages PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaUsages_Account REFERENCES svcacct.Accounts(Id),
    UsageKind varchar(24) NOT NULL CONSTRAINT CK_SaUsages_Kind CHECK (UsageKind IN ('Database','FileShare','ScheduledTask','WindowsService',
        'UncApplication','IisVirtualDirectory','IisAppPool','Other')),
    DatabaseEngine varchar(16) NULL CONSTRAINT CK_SaUsages_Engine CHECK (DatabaseEngine IN ('Unknown','SqlServer','Oracle','Other')),
    NeedVerified bit NULL,
    Server nvarchar(256) NULL,
    Component nvarchar(256) NULL,
    Notes nvarchar(1000) NULL,
    Source varchar(16) NOT NULL CONSTRAINT CK_SaUsages_Source CHECK (Source IN ('Manual')),
    ExceptionReason nvarchar(1000) NULL,
    ExceptionBy uniqueidentifier NULL,
    ExceptionAt datetimeoffset(7) NULL,
    RemovedAt datetimeoffset(7) NULL,
    RemovedBy uniqueidentifier NULL,
    RemovedReason nvarchar(1000) NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaUsages_EngineOnlyDatabase CHECK ((UsageKind = 'Database' AND DatabaseEngine IS NOT NULL) OR (UsageKind <> 'Database' AND DatabaseEngine IS NULL)),
    CONSTRAINT CK_SaUsages_NeedOnlyService CHECK (UsageKind = 'WindowsService' OR NeedVerified IS NULL),
    CONSTRAINT CK_SaUsages_Exception CHECK ((ExceptionReason IS NULL AND ExceptionBy IS NULL AND ExceptionAt IS NULL)
        OR (ExceptionReason IS NOT NULL AND ExceptionBy IS NOT NULL AND ExceptionAt IS NOT NULL)),
    CONSTRAINT CK_SaUsages_Removed CHECK ((RemovedAt IS NULL AND RemovedBy IS NULL AND RemovedReason IS NULL)
        OR (RemovedAt IS NOT NULL AND RemovedBy IS NOT NULL AND RemovedReason IS NOT NULL))
);
CREATE INDEX IX_SaUsages_Account ON svcacct.AccountUsages(AccountId) WHERE RemovedAt IS NULL;

CREATE TABLE svcacct.TeamRoles(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaTeamRoles PRIMARY KEY,
    TeamId uniqueidentifier NOT NULL CONSTRAINT FK_SaTeamRoles_Team REFERENCES svcacct.Teams(Id),
    Role varchar(24) NOT NULL CONSTRAINT CK_SaTeamRoles_Role CHECK (Role IN ('SqlTeam','GmsaExecutor')),
    Reason nvarchar(1000) NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    RevokedAt datetimeoffset(7) NULL,
    RevokedBy uniqueidentifier NULL,
    RevokedReason nvarchar(1000) NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaTeamRoles_Revoked CHECK ((RevokedAt IS NULL AND RevokedBy IS NULL AND RevokedReason IS NULL)
        OR (RevokedAt IS NOT NULL AND RevokedBy IS NOT NULL AND RevokedReason IS NOT NULL))
);
CREATE UNIQUE INDEX UX_SaTeamRoles_Active ON svcacct.TeamRoles(TeamId, Role) WHERE RevokedAt IS NULL;
CREATE UNIQUE INDEX UX_SaTeamRoles_OneExecutor ON svcacct.TeamRoles(Role) WHERE Role = 'GmsaExecutor' AND RevokedAt IS NULL;
GO
CREATE TRIGGER svcacct.TR_SaUsages_NoDelete ON svcacct.AccountUsages AFTER DELETE AS
BEGIN THROW 51305, 'Usages are removed with a reason, never hard-deleted.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaTeamRoles_NoDelete ON svcacct.TeamRoles AFTER DELETE AS
BEGIN THROW 51305, 'Team roles are revoked with a reason, never hard-deleted.', 1; END;
GO
COMMIT TRANSACTION;
GO
