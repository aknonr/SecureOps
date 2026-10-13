/*
  Service Accounts module — candidate 8 (SA-008), numbered 035 by the module owner (reserved 2026-10-12 for OR-2).
  Requires 033 and 034 (svcacct.ChangePlans, svcacct.ChangePlanPreviews, the 'ChangePlan' evidence owner type).

  OR submissions from an approved gMSA change plan (docs/adr/ADR-0030-service-account-plan-or-creation.md, PROPOSED; OR-2 is
  built with a Fake adapter only and the configuration default is Disabled). One row per explicit "open OR" attempt for one
  account of one plan. The row is written in state Sending BEFORE any remote call, with the exact content that is sent, so a
  crash or an unclear answer is never lost and never retried automatically. Additive only: two new tables; no existing
  table, constraint, row, role or grant changes (OcoNumber on ChangePlanApprovals is left as it is; ADR-0030 §10 is open).
  - ChangePlanRecordSubmissions: plan, account, the approved preview the content was built from, the client idempotency key
    (unique per account), the server fingerprint (SHA-256 of the canonical submission), provider, environment, the ITSM
    requester id and the exact short description and description. Only State, ExternalId, ExternalNumber, LinkVerification,
    OutcomeReason and UpdatedAt/By may change, and only along the allowed transitions (TR_SaRecordSubmissions_Guard):
      Sending          -> NotSent | Created | PartiallyCreated | OutcomeUnknown | Linked | ConfirmedNotCreated
      OutcomeUnknown   -> Linked | ConfirmedNotCreated
      PartiallyCreated -> Linked
    ExternalId and ExternalNumber may only be filled, never changed or cleared. Rows are never deleted (51403, 51404).
  - UX_SaRecordSubmissions_Active: at most one submission per (plan, account) that is not NotSent or ConfirmedNotCreated, so a
    second "open OR" for the same account is refused by the database even if the API is bypassed.
  - UX_SaRecordSubmissions_Number: one OR number is held by at most one live submission.
  - ChangePlanRecordSubmissionEvents: every state change with its actor and, for a person's reconciliation, the reason.
    Append-only (51402).
  Coded text columns accept only their exact spelling (binary collation, no trailing spaces), because the database collation
  is case-insensitive. No credential, session, URL, remote body or exception text column exists. Refuses replay (51400). Run
  with SQLCMD -I -b. No down script: rollback retains these objects and data (DBA-035-TR.md). Runtime grants:
  SA-008-API-permissions.sql (SELECT, INSERT; UPDATE only on the changeable submission columns).
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NULL OR OBJECT_ID(N'svcacct.ChangePlanPreviews', N'U') IS NULL
    OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner'
                   AND definition LIKE N'%''ChangePlan''%')
    THROW 51400, 'Service Accounts candidate 8 requires 033 and 034.', 1;
IF OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions', N'U') IS NOT NULL OR OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents', N'U') IS NOT NULL
    THROW 51400, 'Service Accounts candidate 8 already applied; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;

CREATE TABLE svcacct.ChangePlanRecordSubmissions(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_SaRecordSubmissions PRIMARY KEY,
    PlanId uniqueidentifier NOT NULL CONSTRAINT FK_SaRecordSubmissions_Plan REFERENCES svcacct.ChangePlans(Id),
    AccountId uniqueidentifier NOT NULL CONSTRAINT FK_SaRecordSubmissions_Account REFERENCES svcacct.Accounts(Id),
    PreviewId uniqueidentifier NOT NULL CONSTRAINT FK_SaRecordSubmissions_Preview REFERENCES svcacct.ChangePlanPreviews(Id),
    IdempotencyKey uniqueidentifier NOT NULL,
    Fingerprint char(64) NOT NULL CONSTRAINT CK_SaRecordSubmissions_Fingerprint CHECK (Fingerprint COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'),
    Provider varchar(16) NOT NULL CONSTRAINT CK_SaRecordSubmissions_Provider CHECK (Provider COLLATE Latin1_General_BIN2 IN ('Fake','TuruncuHat')
        AND DATALENGTH(Provider) = LEN(Provider)),
    RecordKind varchar(8) NOT NULL CONSTRAINT CK_SaRecordSubmissions_Kind CHECK (RecordKind COLLATE Latin1_General_BIN2 IN ('OR')
        AND DATALENGTH(RecordKind) = LEN(RecordKind)),
    Environment nvarchar(64) NOT NULL CONSTRAINT CK_SaRecordSubmissions_Environment CHECK (LEN(Environment) > 0),
    RequesterExternalId nvarchar(64) NOT NULL CONSTRAINT CK_SaRecordSubmissions_Requester CHECK (LEN(RequesterExternalId) > 0),
    ShortDescription nvarchar(400) NOT NULL CONSTRAINT CK_SaRecordSubmissions_Short CHECK (LEN(ShortDescription) > 0),
    Description nvarchar(4000) NOT NULL CONSTRAINT CK_SaRecordSubmissions_Description CHECK (LEN(Description) > 0),
    State varchar(24) NOT NULL CONSTRAINT CK_SaRecordSubmissions_State CHECK (State COLLATE Latin1_General_BIN2 IN ('Sending','NotSent','Created',
        'PartiallyCreated','OutcomeUnknown','Linked','ConfirmedNotCreated') AND DATALENGTH(State) = LEN(State)),
    ExternalId nvarchar(64) NULL,
    ExternalNumber nvarchar(64) NULL,
    LinkVerification varchar(16) NULL CONSTRAINT CK_SaRecordSubmissions_Verification CHECK (LinkVerification COLLATE Latin1_General_BIN2 IN ('Found',
        'NotVerified') AND DATALENGTH(LinkVerification) = LEN(LinkVerification)),
    OutcomeReason varchar(32) NULL CONSTRAINT CK_SaRecordSubmissions_Reason CHECK (OutcomeReason COLLATE Latin1_General_BIN2 IN ('NotConfigured',
        'RequesterLocationMissing','AuthenticationFailed','Timeout','Unavailable','Rejected','InvalidResponse','NumberNotReadBack','PropertyFailed',
        'DispatchInterrupted') AND DATALENGTH(OutcomeReason) = LEN(OutcomeReason)),
    CreatedBy uniqueidentifier NOT NULL,
    CreatedAt datetimeoffset(7) NOT NULL,
    UpdatedBy uniqueidentifier NOT NULL,
    UpdatedAt datetimeoffset(7) NOT NULL,
    RowVer rowversion NOT NULL,
    CONSTRAINT UQ_SaRecordSubmissions_Key UNIQUE (AccountId, IdempotencyKey),
    -- What each state must and must not carry: Created has the record id and number; Linked has a number and its check
    -- result; PartiallyCreated has the record id; the states without a remote record carry no number and no link check.
    CONSTRAINT CK_SaRecordSubmissions_Shape CHECK (
        (State = 'Created' AND ExternalId IS NOT NULL AND ExternalNumber IS NOT NULL AND LinkVerification IS NULL)
        OR (State = 'Linked' AND ExternalNumber IS NOT NULL AND LinkVerification IS NOT NULL)
        OR (State = 'PartiallyCreated' AND ExternalId IS NOT NULL AND LinkVerification IS NULL)
        OR (State IN ('Sending','NotSent','OutcomeUnknown','ConfirmedNotCreated') AND ExternalNumber IS NULL AND LinkVerification IS NULL))
);
CREATE UNIQUE INDEX UX_SaRecordSubmissions_Active ON svcacct.ChangePlanRecordSubmissions(PlanId, AccountId)
    WHERE State IN ('Sending','Created','PartiallyCreated','OutcomeUnknown','Linked');
CREATE UNIQUE INDEX UX_SaRecordSubmissions_Number ON svcacct.ChangePlanRecordSubmissions(ExternalNumber)
    WHERE ExternalNumber IS NOT NULL AND State IN ('Created','PartiallyCreated','Linked');
CREATE INDEX IX_SaRecordSubmissions_Plan ON svcacct.ChangePlanRecordSubmissions(PlanId, AccountId, CreatedAt);

CREATE TABLE svcacct.ChangePlanRecordSubmissionEvents(
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaRecordSubmissionEvents PRIMARY KEY,
    SubmissionId uniqueidentifier NOT NULL CONSTRAINT FK_SaRecordSubmissionEvents_Submission REFERENCES svcacct.ChangePlanRecordSubmissions(Id),
    Event varchar(24) NOT NULL CONSTRAINT CK_SaRecordSubmissionEvents_Event CHECK (Event COLLATE Latin1_General_BIN2 IN ('Started','NotSent','Created',
        'PartiallyCreated','OutcomeUnknown','Linked','ConfirmedNotCreated') AND DATALENGTH(Event) = LEN(Event)),
    FromState varchar(24) NULL,
    ToState varchar(24) NOT NULL,
    ReasonCode varchar(32) NULL,
    ExternalNumber nvarchar(64) NULL,
    Reason nvarchar(1000) NULL,
    Actor uniqueidentifier NOT NULL,
    At datetimeoffset(7) NOT NULL
);
CREATE INDEX IX_SaRecordSubmissionEvents_Submission ON svcacct.ChangePlanRecordSubmissionEvents(SubmissionId, Id);
GO
CREATE TRIGGER svcacct.TR_SaRecordSubmissionEvents_AppendOnly ON svcacct.ChangePlanRecordSubmissionEvents AFTER UPDATE, DELETE AS
BEGIN THROW 51402, 'OR submission events are append-only.', 1; END;
GO
CREATE TRIGGER svcacct.TR_SaRecordSubmissions_Guard ON svcacct.ChangePlanRecordSubmissions AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    -- Fixed columns compare exactly: the database collation ignores letter case and trailing spaces, and datetimeoffset
    -- equality ignores the offset, so text compares in a binary collation plus DATALENGTH and time with its offset.
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.Id = d.Id))
        OR EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
                   WHERE i.PlanId <> d.PlanId OR i.AccountId <> d.AccountId OR i.PreviewId <> d.PreviewId OR i.IdempotencyKey <> d.IdempotencyKey
                      OR i.Fingerprint COLLATE Latin1_General_BIN2 <> d.Fingerprint COLLATE Latin1_General_BIN2
                      OR i.Provider COLLATE Latin1_General_BIN2 <> d.Provider COLLATE Latin1_General_BIN2
                      OR i.RecordKind COLLATE Latin1_General_BIN2 <> d.RecordKind COLLATE Latin1_General_BIN2
                      OR i.Environment COLLATE Latin1_General_BIN2 <> d.Environment COLLATE Latin1_General_BIN2 OR DATALENGTH(i.Environment) <> DATALENGTH(d.Environment)
                      OR i.RequesterExternalId COLLATE Latin1_General_BIN2 <> d.RequesterExternalId COLLATE Latin1_General_BIN2
                      OR DATALENGTH(i.RequesterExternalId) <> DATALENGTH(d.RequesterExternalId)
                      OR i.ShortDescription COLLATE Latin1_General_BIN2 <> d.ShortDescription COLLATE Latin1_General_BIN2
                      OR DATALENGTH(i.ShortDescription) <> DATALENGTH(d.ShortDescription)
                      OR i.Description COLLATE Latin1_General_BIN2 <> d.Description COLLATE Latin1_General_BIN2 OR DATALENGTH(i.Description) <> DATALENGTH(d.Description)
                      OR i.CreatedBy <> d.CreatedBy OR i.CreatedAt <> d.CreatedAt OR DATEPART(TZOFFSET, i.CreatedAt) <> DATEPART(TZOFFSET, d.CreatedAt))
        THROW 51403, 'OR submissions are never deleted; the submitted content is fixed.', 1;
    -- Allowed transitions only; a stored record id or number is never changed or cleared.
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE NOT ((d.State = 'Sending' AND i.State IN ('NotSent','Created','PartiallyCreated','OutcomeUnknown','Linked','ConfirmedNotCreated'))
                       OR (d.State = 'OutcomeUnknown' AND i.State IN ('Linked','ConfirmedNotCreated'))
                       OR (d.State = 'PartiallyCreated' AND i.State = 'Linked'))
                  OR (d.ExternalId IS NOT NULL AND (i.ExternalId IS NULL OR i.ExternalId COLLATE Latin1_General_BIN2 <> d.ExternalId COLLATE Latin1_General_BIN2
                      OR DATALENGTH(i.ExternalId) <> DATALENGTH(d.ExternalId)))
                  OR (d.ExternalNumber IS NOT NULL AND (i.ExternalNumber IS NULL OR i.ExternalNumber COLLATE Latin1_General_BIN2 <> d.ExternalNumber COLLATE Latin1_General_BIN2
                      OR DATALENGTH(i.ExternalNumber) <> DATALENGTH(d.ExternalNumber))))
        THROW 51404, 'OR submission state change refused: not an allowed transition, or a stored record id or number would change.', 1;
END;
GO
COMMIT TRANSACTION;
GO
