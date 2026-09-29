/* Unnumbered review candidate. DBA assigns the approved API principal to this role separately. */
SET XACT_ABORT ON;
IF SCHEMA_ID(N'svcacct') IS NULL THROW 51310, 'Service Accounts schema is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NOT NULL THROW 51310, 'API role exists; compare grants, do not replay.', 1;
BEGIN TRANSACTION;
CREATE ROLE svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.Organizations TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.Teams TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.People TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.ScopeGrants TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.Accounts TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.OwnershipAssignments TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.Handovers TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.WorkRequests TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.ActionEvents TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.Communications TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.Findings TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.IdentityTransitions TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.ImportBatches TO svcacct_api_runtime;
GRANT SELECT, INSERT, DELETE ON OBJECT::svcacct.ImportRows TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.ReminderOutbox TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.PersonAliases TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.AccountAliases TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ExternalRecords TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ExternalRecordLinks TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.CommunicationAccounts TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.AccountObservations TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.Evidence TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ReportSnapshots TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.History TO svcacct_api_runtime;
GRANT SELECT ON OBJECT::security.Users TO svcacct_api_runtime;
GRANT INSERT ON OBJECT::audit.AuditLog TO svcacct_api_runtime;
COMMIT TRANSACTION;
/* DELETE is limited to uncommitted ImportRows by TR_SaImportRows_CommittedImmutable.
   Least privilege (2026-09-29): only verbs the module issues are granted; svcacct.TeamMemberships is not used by the
   runtime and gets no grant. sp_getapplock (write gate) needs only public. */
