/* Review candidate for SA-008 (numbered 035). Requires SA-API-permissions.sql (role svcacct_api_runtime).
   Only the verbs the module issues: submission events are append-only, so SELECT and INSERT; submissions also get UPDATE, but
   only on the columns an outcome or a reconciliation writes (State, ExternalId, ExternalNumber, LinkVerification,
   OutcomeReason, UpdatedBy, UpdatedAt), so the submitted content is refused by permission (230) before
   TR_SaRecordSubmissions_Guard (51403) is reached. Nothing may be deleted. Everything else the flow writes (History,
   audit.AuditLog) uses grants the role already holds. The Worker role needs nothing new. No role member is assigned. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'svcacct.ChangePlanRecordSubmissions') IS NULL OR OBJECT_ID(N'svcacct.ChangePlanRecordSubmissionEvents') IS NULL
    THROW 51401, 'SA-008 (035) is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THROW 51401, 'API role is required (SA-API-permissions.sql).', 1;
BEGIN TRANSACTION;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanRecordSubmissions TO svcacct_api_runtime;
GRANT UPDATE ON OBJECT::svcacct.ChangePlanRecordSubmissions (State, ExternalId, ExternalNumber, LinkVerification, OutcomeReason, UpdatedBy, UpdatedAt)
    TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanRecordSubmissionEvents TO svcacct_api_runtime;
COMMIT TRANSACTION;
