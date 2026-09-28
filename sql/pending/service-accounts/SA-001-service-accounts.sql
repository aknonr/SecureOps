/*
  Service Accounts module — UNNUMBERED CANDIDATE (not a deployable migration).
  Lives under sql/pending so release discovery (sql/migrations, sql/schema) never picks it up.
  Codex must reserve a migration number before promotion; do not assume 025.

  Requires: security.Users and audit.AuditLog (001). Verified on a fresh isolated 001-024 database.
  Additive only: one new schema `svcacct`; no existing object, role, grant or row is changed.
  Refuses replay. Run with SQLCMD -I -b. No down script: rollback retains these objects and data.
  Runtime (candidate, least privilege, DBA assigns to the existing approved API/Worker principal):
    SELECT, INSERT, UPDATE on the mutable svcacct tables listed at the end of this file;
    SELECT, INSERT only on the append-only/immutable tables; DELETE only on ImportRows of an uncommitted batch
    (preview re-plan, trigger-guarded); no other DELETE, no DDL or db_owner.
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'security.Users') IS NULL OR OBJECT_ID(N'audit.AuditLog') IS NULL
    THROW 51300, 'Service Accounts candidate requires the reviewed 001 access/audit schema.', 1;
IF SCHEMA_ID(N'svcacct') IS NOT NULL
    THROW 51300, 'svcacct already exists; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;
GO
CREATE SCHEMA svcacct AUTHORIZATION dbo;
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

CREATE TABLE svcacct.Organizations(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaOrganizations PRIMARY KEY,
    ParentId uniqueidentifier NULL CONSTRAINT FK_SaOrganizations_Parent REFERENCES svcacct.Organizations(Id),
    Name nvarchar(200) NOT NULL,
    NormalizedName nvarchar(200) NOT NULL CONSTRAINT UQ_SaOrganizations_Name UNIQUE,
    Kind varchar(24) NOT NULL CONSTRAINT CK_SaOrganizations_Kind CHECK (Kind IN ('Directorate','Department','Group','Other')),
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaOrganizations_NotSelf CHECK (ParentId IS NULL OR ParentId <> Id)
);

CREATE TABLE svcacct.Teams(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaTeams PRIMARY KEY,
    OrganizationId uniqueidentifier NULL CONSTRAINT FK_SaTeams_Organization REFERENCES svcacct.Organizations(Id),
    Name nvarchar(200) NOT NULL,
    NormalizedName nvarchar(200) NOT NULL CONSTRAINT UQ_SaTeams_Name UNIQUE,
    Provisional bit NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL
);

CREATE TABLE svcacct.People(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaPeople PRIMARY KEY,
    DisplayName nvarchar(200) NOT NULL,
    NormalizedName nvarchar(200) NOT NULL,
    Upn nvarchar(256) NULL,
    DirectoryObjectId nvarchar(128) NULL,
    VerificationState varchar(16) NOT NULL CONSTRAINT CK_SaPeople_State CHECK (VerificationState IN ('Provisional','Verified')),
    VerifiedBy uniqueidentifier NULL,
    VerifiedAt datetimeoffset(7) NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaPeople_Verified CHECK (VerificationState = 'Provisional'
        OR (VerifiedBy IS NOT NULL AND VerifiedAt IS NOT NULL AND (Upn IS NOT NULL OR DirectoryObjectId IS NOT NULL)))
);
CREATE INDEX IX_SaPeople_NormalizedName ON svcacct.People(NormalizedName);
CREATE UNIQUE INDEX UX_SaPeople_Upn ON svcacct.People(Upn) WHERE Upn IS NOT NULL;
CREATE UNIQUE INDEX UX_SaPeople_DirectoryObject ON svcacct.People(DirectoryObjectId) WHERE DirectoryObjectId IS NOT NULL;

CREATE TABLE svcacct.PersonAliases(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaPersonAliases PRIMARY KEY,
    PersonId uniqueidentifier NOT NULL CONSTRAINT FK_SaPersonAliases_Person REFERENCES svcacct.People(Id),
    Alias nvarchar(200) NOT NULL,
    AliasNormalized nvarchar(200) NOT NULL,
    Evidence nvarchar(400) NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    CONSTRAINT UQ_SaPersonAliases UNIQUE (PersonId, AliasNormalized)
);
CREATE INDEX IX_SaPersonAliases_Alias ON svcacct.PersonAliases(AliasNormalized);

CREATE TABLE svcacct.TeamMemberships(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaTeamMemberships PRIMARY KEY,
    PersonId uniqueidentifier NOT NULL CONSTRAINT FK_SaTeamMemberships_Person REFERENCES svcacct.People(Id),
    TeamId uniqueidentifier NOT NULL CONSTRAINT FK_SaTeamMemberships_Team REFERENCES svcacct.Teams(Id),
    EffectiveFrom date NULL,
    EffectiveTo date NULL,
    Source nvarchar(200) NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    CONSTRAINT CK_SaTeamMemberships_Range CHECK (EffectiveTo IS NULL OR EffectiveFrom IS NULL OR EffectiveTo >= EffectiveFrom)
);

/* Access scope: binds an approved application user to data scope. Imported people never grant access. */
CREATE TABLE svcacct.ScopeGrants(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaScopeGrants PRIMARY KEY,
    UserId uniqueidentifier NOT NULL CONSTRAINT FK_SaScopeGrants_User REFERENCES security.Users(UserId),
    ScopeKind varchar(16) NOT NULL CONSTRAINT CK_SaScopeGrants_Kind CHECK (ScopeKind IN ('All','Organization','Team')),
    OrganizationId uniqueidentifier NULL CONSTRAINT FK_SaScopeGrants_Organization REFERENCES svcacct.Organizations(Id),
    TeamId uniqueidentifier NULL CONSTRAINT FK_SaScopeGrants_Team REFERENCES svcacct.Teams(Id),
    Reason nvarchar(400) NOT NULL,
    GrantedBy uniqueidentifier NOT NULL,
    GrantedAt datetimeoffset(7) NOT NULL,
    RevokedBy uniqueidentifier NULL,
    RevokedAt datetimeoffset(7) NULL,
    RevokeReason nvarchar(400) NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaScopeGrants_Target CHECK (
        (ScopeKind = 'All' AND OrganizationId IS NULL AND TeamId IS NULL)
        OR (ScopeKind = 'Organization' AND OrganizationId IS NOT NULL AND TeamId IS NULL)
        OR (ScopeKind = 'Team' AND TeamId IS NOT NULL AND OrganizationId IS NULL)),
    CONSTRAINT CK_SaScopeGrants_NoSelfGrant CHECK (UserId <> GrantedBy),
    CONSTRAINT CK_SaScopeGrants_Revoke CHECK ((RevokedAt IS NULL AND RevokedBy IS NULL) OR (RevokedAt IS NOT NULL AND RevokedBy IS NOT NULL AND RevokeReason IS NOT NULL))
);
CREATE INDEX IX_SaScopeGrants_User ON svcacct.ScopeGrants(UserId) WHERE RevokedAt IS NULL;

CREATE TABLE svcacct.Accounts(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaAccounts PRIMARY KEY,
    AccountName nvarchar(256) NOT NULL,
    NormalizedName nvarchar(256) NOT NULL,
    Domain nvarchar(128) NULL,
    NormalizedDomain nvarchar(128) NULL,
    Sid varchar(184) NULL,
    IdentityKey nvarchar(400) NOT NULL CONSTRAINT UQ_SaAccounts_IdentityKey UNIQUE,
    IdentityState varchar(16) NOT NULL CONSTRAINT CK_SaAccounts_IdentityState CHECK (IdentityState IN ('Provisional','Confirmed')),
    ReportOrganizationId uniqueidentifier NULL CONSTRAINT FK_SaAccounts_Organization REFERENCES svcacct.Organizations(Id),
    CurrentOwnerTeamId uniqueidentifier NULL CONSTRAINT FK_SaAccounts_OwnerTeam REFERENCES svcacct.Teams(Id),
    CurrentOwnerPersonId uniqueidentifier NULL CONSTRAINT FK_SaAccounts_OwnerPerson REFERENCES svcacct.People(Id),
    ConsumerTeamId uniqueidentifier NULL CONSTRAINT FK_SaAccounts_ConsumerTeam REFERENCES svcacct.Teams(Id),
    LifecycleState varchar(24) NOT NULL CONSTRAINT CK_SaAccounts_Lifecycle CHECK (LifecycleState IN ('Active','ClosureVerified')),
    Notes nvarchar(2000) NULL,
    LastObservedOn date NULL,
    LastObservationPresence varchar(16) NULL CONSTRAINT CK_SaAccounts_Presence CHECK (LastObservationPresence IN ('Present','NotPresent')),
    LegacyReference nvarchar(300) NULL,
    LegacyDisplayId nvarchar(32) NULL,
    MigrationKey uniqueidentifier NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaAccounts_Domain CHECK ((Domain IS NULL AND NormalizedDomain IS NULL) OR (Domain IS NOT NULL AND NormalizedDomain IS NOT NULL))
);
CREATE UNIQUE INDEX UX_SaAccounts_DomainName ON svcacct.Accounts(NormalizedDomain, NormalizedName) WHERE NormalizedDomain IS NOT NULL;
CREATE UNIQUE INDEX UX_SaAccounts_Sid ON svcacct.Accounts(Sid) WHERE Sid IS NOT NULL;
CREATE UNIQUE INDEX UX_SaAccounts_MigrationKey ON svcacct.Accounts(MigrationKey) WHERE MigrationKey IS NOT NULL;
CREATE UNIQUE INDEX UX_SaAccounts_Legacy ON svcacct.Accounts(LegacyReference) WHERE LegacyReference IS NOT NULL;
CREATE INDEX IX_SaAccounts_Name ON svcacct.Accounts(NormalizedName);
CREATE INDEX IX_SaAccounts_Scope ON svcacct.Accounts(ReportOrganizationId, CurrentOwnerTeamId);

CREATE TABLE svcacct.AccountAliases(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaAccountAliases PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaAccountAliases_Account REFERENCES svcacct.Accounts(Id),
    Alias nvarchar(256) NOT NULL,
    NormalizedAlias nvarchar(256) NOT NULL,
    Domain nvarchar(128) NULL,
    Source nvarchar(200) NOT NULL,
    ConfirmedBy uniqueidentifier NOT NULL,
    ConfirmedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT UQ_SaAccountAliases UNIQUE (AccountId, NormalizedAlias)
);

CREATE TABLE svcacct.OwnershipAssignments(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaOwnership PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaOwnership_Account REFERENCES svcacct.Accounts(Id),
    TeamId uniqueidentifier NULL CONSTRAINT FK_SaOwnership_Team REFERENCES svcacct.Teams(Id),
    PersonId uniqueidentifier NULL CONSTRAINT FK_SaOwnership_Person REFERENCES svcacct.People(Id),
    State varchar(16) NOT NULL CONSTRAINT CK_SaOwnership_State CHECK (State IN ('Proposed','Confirmed','Ended','Rejected')),
    Source nvarchar(300) NOT NULL,
    EffectiveFrom date NULL,
    EffectiveTo date NULL,
    EvidenceNote nvarchar(1000) NULL,
    ProposedBy uniqueidentifier NOT NULL,
    ProposedAt datetimeoffset(7) NOT NULL,
    DecidedBy uniqueidentifier NULL,
    DecidedAt datetimeoffset(7) NULL,
    DecisionReason nvarchar(1000) NULL,
    SourceKey nvarchar(400) NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaOwnership_Subject CHECK (TeamId IS NOT NULL OR PersonId IS NOT NULL),
    CONSTRAINT CK_SaOwnership_Decision CHECK (State = 'Proposed' OR (DecidedBy IS NOT NULL AND DecidedAt IS NOT NULL))
);
CREATE INDEX IX_SaOwnership_Account ON svcacct.OwnershipAssignments(AccountId, State);
CREATE UNIQUE INDEX UX_SaOwnership_SourceKey ON svcacct.OwnershipAssignments(SourceKey) WHERE SourceKey IS NOT NULL;
CREATE UNIQUE INDEX UX_SaOwnership_OneConfirmed ON svcacct.OwnershipAssignments(AccountId) WHERE State = 'Confirmed';

CREATE TABLE svcacct.ExternalRecords(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaExternalRecords PRIMARY KEY,
    RecordType varchar(8) NOT NULL CONSTRAINT CK_SaExternalRecords_Type CHECK (RecordType IN ('OR','OCO','JIRA','OTHER')),
    Number nvarchar(64) NOT NULL,
    NormalizedNumber nvarchar(64) NOT NULL,
    Url nvarchar(400) NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    CONSTRAINT UQ_SaExternalRecords UNIQUE (RecordType, NormalizedNumber)
);

CREATE TABLE svcacct.ExternalRecordLinks(
    ExternalRecordId uniqueidentifier NOT NULL CONSTRAINT FK_SaExternalLinks_Record REFERENCES svcacct.ExternalRecords(Id),
    EntityType varchar(16) NOT NULL CONSTRAINT CK_SaExternalLinks_Entity CHECK (EntityType IN ('Account','Request','Action','Communication')),
    EntityId uniqueidentifier NOT NULL,
    AccountId uniqueidentifier NULL CONSTRAINT FK_SaExternalLinks_Account REFERENCES svcacct.Accounts(Id),
    LinkedAt datetimeoffset(7) NOT NULL,
    LinkedBy uniqueidentifier NOT NULL,
    CONSTRAINT PK_SaExternalLinks PRIMARY KEY (ExternalRecordId, EntityType, EntityId)
);
CREATE INDEX IX_SaExternalLinks_Entity ON svcacct.ExternalRecordLinks(EntityType, EntityId);

CREATE TABLE svcacct.Handovers(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaHandovers PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaHandovers_Account REFERENCES svcacct.Accounts(Id),
    SourceTeamId uniqueidentifier NULL CONSTRAINT FK_SaHandovers_Source REFERENCES svcacct.Teams(Id),
    TargetTeamId uniqueidentifier NOT NULL CONSTRAINT FK_SaHandovers_Target REFERENCES svcacct.Teams(Id),
    ConsumerTeamId uniqueidentifier NULL CONSTRAINT FK_SaHandovers_Consumer REFERENCES svcacct.Teams(Id),
    SourceBatchId uniqueidentifier NULL,
    CohortLabel nvarchar(200) NULL,
    ProposedOn date NULL,
    Status varchar(16) NOT NULL CONSTRAINT CK_SaHandovers_Status CHECK (Status IN ('Proposed','Accepted','Rejected')),
    DecidedOn date NULL,
    DecidedBy uniqueidentifier NULL,
    DecisionNote nvarchar(1000) NULL,
    SourceNote nvarchar(1000) NULL,
    SourceKey nvarchar(400) NULL,
    LegacyReference nvarchar(300) NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaHandovers_Decision CHECK (Status = 'Proposed' OR (DecidedOn IS NOT NULL AND DecidedBy IS NOT NULL AND DecisionNote IS NOT NULL))
);
CREATE UNIQUE INDEX UX_SaHandovers_SourceKey ON svcacct.Handovers(SourceKey) WHERE SourceKey IS NOT NULL;
CREATE UNIQUE INDEX UX_SaHandovers_Legacy ON svcacct.Handovers(LegacyReference) WHERE LegacyReference IS NOT NULL;
CREATE INDEX IX_SaHandovers_Target ON svcacct.Handovers(TargetTeamId, Status);
CREATE INDEX IX_SaHandovers_Account ON svcacct.Handovers(AccountId);

CREATE TABLE svcacct.WorkRequests(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaRequests PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaRequests_Account REFERENCES svcacct.Accounts(Id),
    ActionType varchar(32) NOT NULL,
    Status varchar(16) NOT NULL CONSTRAINT CK_SaRequests_Status CHECK (Status IN ('Open','Closed')),
    CloseOutcome varchar(16) NULL CONSTRAINT CK_SaRequests_Outcome CHECK (CloseOutcome IN ('Completed','NotNeeded','Cancelled')),
    TargetTeamId uniqueidentifier NULL CONSTRAINT FK_SaRequests_TargetTeam REFERENCES svcacct.Teams(Id),
    FollowupPersonId uniqueidentifier NULL CONSTRAINT FK_SaRequests_Followup REFERENCES svcacct.People(Id),
    ContactPersonId uniqueidentifier NULL CONSTRAINT FK_SaRequests_Contact REFERENCES svcacct.People(Id),
    PlanStart date NULL,
    PlanEnd date NULL,
    PlanAnnouncedOn date NULL,
    NextFollowupOn date NULL,
    FirstSentOn date NULL,
    LastReplyOn date NULL,
    Notes nvarchar(4000) NULL,
    RelatedHandoverId uniqueidentifier NULL CONSTRAINT FK_SaRequests_Handover REFERENCES svcacct.Handovers(Id),
    SourceKey nvarchar(400) NULL,
    LegacyReference nvarchar(300) NULL,
    LegacyDisplayId nvarchar(32) NULL,
    MigrationKey uniqueidentifier NULL,
    ClosedAt datetimeoffset(7) NULL,
    ClosedBy uniqueidentifier NULL,
    CloseReason nvarchar(1000) NULL,
    ClosingActionId uniqueidentifier NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaRequests_Plan CHECK (PlanEnd IS NULL OR PlanStart IS NULL OR PlanEnd >= PlanStart),
    CONSTRAINT CK_SaRequests_Closed CHECK ((Status = 'Open' AND CloseOutcome IS NULL) OR (Status = 'Closed' AND CloseOutcome IS NOT NULL
        AND (ClosedAt IS NOT NULL OR LegacyReference IS NOT NULL)))
);
CREATE UNIQUE INDEX UX_SaRequests_SourceKey ON svcacct.WorkRequests(SourceKey) WHERE SourceKey IS NOT NULL;
CREATE UNIQUE INDEX UX_SaRequests_Legacy ON svcacct.WorkRequests(LegacyReference) WHERE LegacyReference IS NOT NULL;
CREATE INDEX IX_SaRequests_Work ON svcacct.WorkRequests(TargetTeamId, Status, NextFollowupOn);
CREATE INDEX IX_SaRequests_Account ON svcacct.WorkRequests(AccountId, Status);

CREATE TABLE svcacct.ActionEvents(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaActions PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaActions_Account REFERENCES svcacct.Accounts(Id),
    RequestId uniqueidentifier NULL CONSTRAINT FK_SaActions_Request REFERENCES svcacct.WorkRequests(Id),
    ActionType varchar(32) NOT NULL,
    Result varchar(16) NOT NULL CONSTRAINT CK_SaActions_Result CHECK (Result IN ('Planned','Performed','Verified')),
    RecordKind varchar(16) NOT NULL CONSTRAINT CK_SaActions_Kind CHECK (RecordKind IN ('Intermediate','Closure')),
    ActualOn date NULL,
    ActualAt datetimeoffset(7) NULL,
    ActualPrecision varchar(10) NOT NULL CONSTRAINT CK_SaActions_Precision CHECK (ActualPrecision IN ('Unknown','DateOnly','Instant')),
    PerformerTeamId uniqueidentifier NULL CONSTRAINT FK_SaActions_PerformerTeam REFERENCES svcacct.Teams(Id),
    PerformerPersonId uniqueidentifier NULL CONSTRAINT FK_SaActions_PerformerPerson REFERENCES svcacct.People(Id),
    EvidenceNote nvarchar(2000) NULL,
    VerifiedOn date NULL,
    VerifiedByPersonId uniqueidentifier NULL CONSTRAINT FK_SaActions_Verifier REFERENCES svcacct.People(Id),
    VerifiedByUserId uniqueidentifier NULL,
    VerificationNote nvarchar(2000) NULL,
    VerificationEvidenceId uniqueidentifier NULL,
    VoidedAt datetimeoffset(7) NULL,
    VoidedBy uniqueidentifier NULL,
    VoidReason nvarchar(1000) NULL,
    SourceNote nvarchar(2000) NULL,
    LegacyReference nvarchar(300) NULL,
    LegacyDisplayId nvarchar(32) NULL,
    MigrationKey uniqueidentifier NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaActions_VerifyOrder CHECK (VerifiedOn IS NULL OR ActualOn IS NULL OR VerifiedOn >= ActualOn),
    CONSTRAINT CK_SaActions_PrecisionDate CHECK ((ActualPrecision = 'Unknown' AND ActualOn IS NULL AND ActualAt IS NULL)
        OR (ActualPrecision = 'DateOnly' AND ActualOn IS NOT NULL AND ActualAt IS NULL)
        OR (ActualPrecision = 'Instant' AND ActualOn IS NOT NULL AND ActualAt IS NOT NULL)),
    CONSTRAINT CK_SaActions_Void CHECK ((VoidedAt IS NULL AND VoidedBy IS NULL) OR (VoidedAt IS NOT NULL AND VoidedBy IS NOT NULL AND VoidReason IS NOT NULL))
);
CREATE UNIQUE INDEX UX_SaActions_Legacy ON svcacct.ActionEvents(LegacyReference) WHERE LegacyReference IS NOT NULL;
CREATE INDEX IX_SaActions_Account ON svcacct.ActionEvents(AccountId, ActualOn);

CREATE TABLE svcacct.Communications(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaCommunications PRIMARY KEY,
    ProviderMessageId nvarchar(256) NULL,
    Direction varchar(8) NOT NULL CONSTRAINT CK_SaComms_Direction CHECK (Direction IN ('Incoming','Outgoing')),
    Kind varchar(24) NOT NULL CONSTRAINT CK_SaComms_Kind CHECK (Kind IN ('FirstRequest','Reply','Information','Reminder','Draft')),
    OccurredOn date NULL,
    OccurredAt datetimeoffset(7) NULL,
    Precision varchar(10) NOT NULL CONSTRAINT CK_SaComms_Precision CHECK (Precision IN ('Unknown','DateOnly','Instant')),
    ContactTeamId uniqueidentifier NULL CONSTRAINT FK_SaComms_Team REFERENCES svcacct.Teams(Id),
    ContactPersonId uniqueidentifier NULL CONSTRAINT FK_SaComms_Person REFERENCES svcacct.People(Id),
    Subject nvarchar(400) NULL,
    Summary nvarchar(4000) NULL,
    Link nvarchar(400) NULL,
    RecordScope varchar(8) NOT NULL CONSTRAINT CK_SaComms_Scope CHECK (RecordScope IN ('Account','Team')),
    MeaningfulReply bit NULL,
    LegacyReference nvarchar(300) NULL,
    MigrationKey uniqueidentifier NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT CK_SaComms_PrecisionDate CHECK ((Precision = 'Unknown' AND OccurredOn IS NULL AND OccurredAt IS NULL)
        OR (Precision = 'DateOnly' AND OccurredOn IS NOT NULL AND OccurredAt IS NULL)
        OR (Precision = 'Instant' AND OccurredOn IS NOT NULL AND OccurredAt IS NOT NULL))
);
CREATE UNIQUE INDEX UX_SaComms_Provider ON svcacct.Communications(ProviderMessageId) WHERE ProviderMessageId IS NOT NULL;
CREATE UNIQUE INDEX UX_SaComms_Legacy ON svcacct.Communications(LegacyReference) WHERE LegacyReference IS NOT NULL;

CREATE TABLE svcacct.CommunicationAccounts(
    CommunicationId uniqueidentifier NOT NULL CONSTRAINT FK_SaCommAccounts_Comm REFERENCES svcacct.Communications(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaCommAccounts_Account REFERENCES svcacct.Accounts(Id),
    LinkedAt datetimeoffset(7) NOT NULL,
    LinkedBy uniqueidentifier NOT NULL,
    CONSTRAINT PK_SaCommAccounts PRIMARY KEY (CommunicationId, AccountId)
);
CREATE INDEX IX_SaCommAccounts_Account ON svcacct.CommunicationAccounts(AccountId);

CREATE TABLE svcacct.Findings(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaFindings PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaFindings_Account REFERENCES svcacct.Accounts(Id),
    Server nvarchar(256) NULL,
    ComponentType nvarchar(64) NULL,
    ComponentName nvarchar(256) NULL,
    Environment nvarchar(64) NULL,
    ScanAt datetimeoffset(7) NULL,
    ScanOn date NULL,
    ScanResult varchar(16) NOT NULL CONSTRAINT CK_SaFindings_Scan CHECK (ScanResult IN ('Success','Unreachable','Failed','Partial','Unknown')),
    MatchResult varchar(16) NOT NULL CONSTRAINT CK_SaFindings_Match CHECK (MatchResult IN ('Match','NoMatch','Uncertain')),
    CoverageWindow nvarchar(400) NULL,
    EvidenceNote nvarchar(2000) NULL,
    OwningTeamId uniqueidentifier NULL CONSTRAINT FK_SaFindings_Team REFERENCES svcacct.Teams(Id),
    Status varchar(16) NOT NULL CONSTRAINT CK_SaFindings_Status CHECK (Status IN ('Open','InReview','Closed')),
    JobReference nvarchar(128) NULL,
    Notes nvarchar(2000) NULL,
    LegacyReference nvarchar(300) NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL
);
CREATE UNIQUE INDEX UX_SaFindings_Legacy ON svcacct.Findings(LegacyReference) WHERE LegacyReference IS NOT NULL;
CREATE INDEX IX_SaFindings_Account ON svcacct.Findings(AccountId, Status);

CREATE TABLE svcacct.IdentityTransitions(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaTransitions PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaTransitions_Account REFERENCES svcacct.Accounts(Id),
    HandoverId uniqueidentifier NULL CONSTRAINT FK_SaTransitions_Handover REFERENCES svcacct.Handovers(Id),
    Target varchar(16) NOT NULL CONSTRAINT CK_SaTransitions_Target CHECK (Target IN ('gMSA')),
    Suitability varchar(16) NOT NULL CONSTRAINT CK_SaTransitions_Suitability CHECK (Suitability IN ('Unknown','Review','Eligible','Ineligible')),
    DecisionNote nvarchar(2000) NULL,
    DecidedBy uniqueidentifier NULL,
    DecidedAt datetimeoffset(7) NULL,
    PlannedOn date NULL,
    CompletedActionId uniqueidentifier NULL CONSTRAINT FK_SaTransitions_Action REFERENCES svcacct.ActionEvents(Id),
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT UQ_SaTransitions UNIQUE (AccountId, Target),
    CONSTRAINT CK_SaTransitions_Decision CHECK (Suitability IN ('Unknown','Review') OR (DecisionNote IS NOT NULL AND DecidedBy IS NOT NULL))
);

CREATE TABLE svcacct.ImportBatches(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaImportBatches PRIMARY KEY,
    Profile varchar(32) NOT NULL,
    FileName nvarchar(260) NOT NULL,
    ContentType varchar(128) NOT NULL,
    Sha256 char(64) NOT NULL,
    Content varbinary(max) NOT NULL,
    SourceReportDate date NULL,
    SourceDateProvenance nvarchar(400) NOT NULL,
    DeclaredScope nvarchar(200) NULL,
    DeclaredDomain nvarchar(128) NULL,
    MappingJson nvarchar(max) NOT NULL CONSTRAINT CK_SaImportBatches_Mapping CHECK (ISJSON(MappingJson) = 1),
    MappingVersion int NOT NULL,
    PreviewVersion int NOT NULL,
    DecisionVersion int NOT NULL,
    Status varchar(16) NOT NULL CONSTRAINT CK_SaImportBatches_Status CHECK (Status IN ('Staged','Previewed','Committed','Abandoned')),
    ReplayKey char(64) NOT NULL,
    SummaryJson nvarchar(max) NULL,
    ResultJson nvarchar(max) NULL,
    CommitIdempotencyKey nvarchar(128) NULL,
    UploadedBy uniqueidentifier NOT NULL,
    UploadedAt datetimeoffset(7) NOT NULL,
    CommittedBy uniqueidentifier NULL,
    CommittedAt datetimeoffset(7) NULL,
    RowVer rowversion NOT NULL
);
CREATE UNIQUE INDEX UX_SaImportBatches_Replay ON svcacct.ImportBatches(ReplayKey) WHERE Status = 'Committed';
CREATE INDEX IX_SaImportBatches_Uploaded ON svcacct.ImportBatches(UploadedAt DESC);

CREATE TABLE svcacct.ImportRows(
    BatchId uniqueidentifier NOT NULL CONSTRAINT FK_SaImportRows_Batch REFERENCES svcacct.ImportBatches(Id),
    RowKey int NOT NULL,
    Sheet nvarchar(64) NOT NULL,
    RowNumber int NOT NULL,
    EntityKind varchar(16) NOT NULL,
    OriginalJson nvarchar(max) NOT NULL CONSTRAINT CK_SaImportRows_Original CHECK (ISJSON(OriginalJson) = 1),
    NormalizedJson nvarchar(max) NULL,
    Classification varchar(24) NOT NULL,
    MatchAccountId uniqueidentifier NULL,
    CandidatesJson nvarchar(max) NULL,
    ErrorsJson nvarchar(max) NULL,
    DiffJson nvarchar(max) NULL,
    RequiresDecision bit NOT NULL,
    Decision varchar(24) NULL,
    DecisionNote nvarchar(1000) NULL,
    DecidedBy uniqueidentifier NULL,
    DecidedAt datetimeoffset(7) NULL,
    CONSTRAINT PK_SaImportRows PRIMARY KEY (BatchId, RowKey),
    CONSTRAINT UQ_SaImportRows_Source UNIQUE (BatchId, Sheet, RowNumber, EntityKind)
);

/* Source observations are append-only facts; they never mean deletion, fix or completed work. */
CREATE TABLE svcacct.AccountObservations(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaObservations PRIMARY KEY,
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaObservations_Account REFERENCES svcacct.Accounts(Id),
    BatchId uniqueidentifier NOT NULL CONSTRAINT FK_SaObservations_Batch REFERENCES svcacct.ImportBatches(Id),
    SourceProfile varchar(32) NOT NULL,
    SourceReportDate date NULL,
    Presence varchar(16) NOT NULL CONSTRAINT CK_SaObservations_Presence CHECK (Presence IN ('Present','NotPresent')),
    PasswordLastSet datetime2(3) NULL,
    LastLogonAdOrLdap datetime2(3) NULL,
    LastLogonAd datetime2(3) NULL,
    Organization nvarchar(200) NULL,
    GroupDirectorate nvarchar(200) NULL,
    Comment nvarchar(2000) NULL,
    SourceTeam nvarchar(200) NULL,
    ConsumerTeam nvarchar(200) NULL,
    HandoverFlag nvarchar(32) NULL,
    SourceRow nvarchar(120) NULL,
    RawRowJson nvarchar(max) NULL,
    RecordedAt datetimeoffset(7) NOT NULL,
    CONSTRAINT UQ_SaObservations UNIQUE (AccountId, BatchId, SourceProfile)
);
CREATE INDEX IX_SaObservations_Account ON svcacct.AccountObservations(AccountId, SourceReportDate);

CREATE TABLE svcacct.Evidence(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaEvidence PRIMARY KEY,
    OwnerEntityType varchar(16) NOT NULL CONSTRAINT CK_SaEvidence_Owner CHECK (OwnerEntityType IN ('Account','Request','Action','Communication','Finding','Handover','Transition','Ownership')),
    OwnerEntityId uniqueidentifier NOT NULL,
    AccountId uniqueidentifier NULL CONSTRAINT FK_SaEvidence_Account REFERENCES svcacct.Accounts(Id),
    ScopeTeamId uniqueidentifier NULL CONSTRAINT FK_SaEvidence_Team REFERENCES svcacct.Teams(Id),
    FileName nvarchar(260) NOT NULL,
    ContentType varchar(128) NOT NULL,
    Sha256 char(64) NOT NULL,
    SizeBytes int NOT NULL,
    Content varbinary(max) NOT NULL,
    Label nvarchar(400) NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL,
    CONSTRAINT CK_SaEvidence_Anchor CHECK (AccountId IS NOT NULL OR ScopeTeamId IS NOT NULL)
);
CREATE INDEX IX_SaEvidence_Owner ON svcacct.Evidence(OwnerEntityType, OwnerEntityId);

CREATE TABLE svcacct.ReportSnapshots(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaSnapshots PRIMARY KEY,
    Kind varchar(16) NOT NULL CONSTRAINT CK_SaSnapshots_Kind CHECK (Kind IN ('Weekly','Manager')),
    PeriodStart date NOT NULL,
    PeriodEnd date NOT NULL,
    AsOf datetimeoffset(7) NOT NULL,
    ScopeJson nvarchar(4000) NOT NULL CONSTRAINT CK_SaSnapshots_Scope CHECK (ISJSON(ScopeJson) = 1),
    ScopeHash char(64) NOT NULL,
    MetricDefinitionVersion varchar(32) NOT NULL,
    InputWatermark nvarchar(200) NOT NULL,
    PayloadJson nvarchar(max) NOT NULL CONSTRAINT CK_SaSnapshots_Payload CHECK (ISJSON(PayloadJson) = 1),
    PayloadSha256 char(64) NOT NULL,
    Label nvarchar(200) NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    CreatedBy uniqueidentifier NOT NULL
);
CREATE INDEX IX_SaSnapshots_Period ON svcacct.ReportSnapshots(PeriodStart DESC, CreatedAt DESC);

CREATE TABLE svcacct.ReminderOutbox(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaReminders PRIMARY KEY,
    RequestId uniqueidentifier NOT NULL CONSTRAINT FK_SaReminders_Request REFERENCES svcacct.WorkRequests(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaReminders_Account REFERENCES svcacct.Accounts(Id),
    RuleCode varchar(32) NOT NULL,
    DueDate date NOT NULL,
    Channel varchar(16) NOT NULL CONSTRAINT CK_SaReminders_Channel CHECK (Channel IN ('InApp','Draft')),
    IdempotencyKey char(64) NOT NULL CONSTRAINT UQ_SaReminders_Key UNIQUE,
    TargetTeamId uniqueidentifier NULL,
    Status varchar(16) NOT NULL CONSTRAINT CK_SaReminders_Status CHECK (Status IN ('Pending','Delivered','Failed','DeadLetter','Dismissed')),
    AttemptCount int NOT NULL,
    NextAttemptAt datetimeoffset(7) NOT NULL,
    LeaseOwner nvarchar(128) NULL,
    LeaseUntil datetimeoffset(7) NULL,
    LastError nvarchar(400) NULL,
    PayloadJson nvarchar(4000) NOT NULL CONSTRAINT CK_SaReminders_Payload CHECK (ISJSON(PayloadJson) = 1),
    CreatedAt datetimeoffset(7) NOT NULL,
    DeliveredAt datetimeoffset(7) NULL,
    DismissedBy uniqueidentifier NULL,
    DismissedAt datetimeoffset(7) NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT UQ_SaReminders_Natural UNIQUE (RequestId, RuleCode, DueDate, Channel)
);
CREATE INDEX IX_SaReminders_Due ON svcacct.ReminderOutbox(Status, NextAttemptAt);

/* Business timeline with field-level before/after; append-only. Platform audit is written alongside. */
CREATE TABLE svcacct.History(
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaHistory PRIMARY KEY,
    EntityType varchar(24) NOT NULL,
    EntityId uniqueidentifier NOT NULL,
    AccountId uniqueidentifier NULL,
    Action varchar(64) NOT NULL,
    ChangesJson nvarchar(max) NULL CONSTRAINT CK_SaHistory_Changes CHECK (ChangesJson IS NULL OR ISJSON(ChangesJson) = 1),
    Reason nvarchar(1000) NULL,
    ActorUserId uniqueidentifier NOT NULL,
    CorrelationId nvarchar(128) NULL,
    OccurredAt datetimeoffset(7) NOT NULL
);
CREATE INDEX IX_SaHistory_Account ON svcacct.History(AccountId, OccurredAt DESC) WHERE AccountId IS NOT NULL;
CREATE INDEX IX_SaHistory_Entity ON svcacct.History(EntityType, EntityId);

GO
CREATE TRIGGER svcacct.TR_SaHistory_AppendOnly ON svcacct.History AFTER UPDATE, DELETE AS
BEGIN THROW 51301, 'svcacct.History is append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaObservations_AppendOnly ON svcacct.AccountObservations AFTER UPDATE, DELETE AS
BEGIN THROW 51302, 'Source observations are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaSnapshots_Immutable ON svcacct.ReportSnapshots AFTER UPDATE, DELETE AS
BEGIN THROW 51303, 'Report snapshots are immutable.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaEvidence_Immutable ON svcacct.Evidence AFTER UPDATE, DELETE AS
BEGIN THROW 51304, 'Evidence is immutable.', 1; END;
GO
/* Business records keep history: no hard delete on mutable roots either. */
CREATE TRIGGER svcacct.TR_SaBusiness_NoDelete_Accounts ON svcacct.Accounts AFTER DELETE AS
BEGIN THROW 51305, 'Service account records are never hard-deleted.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaBusiness_NoDelete_Requests ON svcacct.WorkRequests AFTER DELETE AS
BEGIN THROW 51305, 'Requests are never hard-deleted.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaBusiness_NoDelete_Actions ON svcacct.ActionEvents AFTER DELETE AS
BEGIN THROW 51305, 'Actions are voided, never hard-deleted.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaBusiness_NoDelete_Comms ON svcacct.Communications AFTER DELETE AS
BEGIN THROW 51305, 'Communications are never hard-deleted.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaBusiness_NoDelete_Imports ON svcacct.ImportBatches AFTER DELETE AS
BEGIN THROW 51305, 'Import batches are never hard-deleted.', 1; END;
GO
/* Staging rows of an uncommitted batch may be re-planned (replaced); committed rows are immutable evidence. */
CREATE TRIGGER svcacct.TR_SaImportRows_CommittedImmutable ON svcacct.ImportRows AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted d JOIN svcacct.ImportBatches b ON b.Id = d.BatchId WHERE b.Status = 'Committed')
        THROW 51306, 'Rows of a committed import are immutable.', 1;
END;
GO
COMMIT TRANSACTION;
GO
/*
  Reviewed role grants are separate: SA-API-permissions.sql and SA-Worker-permissions.sql.
  Neither assigns a corporate principal. Apply only after an approved identity/role review.
*/
