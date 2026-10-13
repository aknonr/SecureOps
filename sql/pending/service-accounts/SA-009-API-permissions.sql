/* Review candidate for SA-009 (numbered 036). Requires SA-API-permissions.sql (role svcacct_api_runtime).
   Only the verbs the module issues: event records are append-only, so SELECT and INSERT; nothing may be updated or deleted.
   The records an event produces use grants the role already holds (Communications, ActionEvents, ExternalRecords,
   WorkRequests, OwnershipAssignments, History, audit.AuditLog). The Worker role needs nothing new. No role member is assigned. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'svcacct.AccountEvents') IS NULL OR OBJECT_ID(N'svcacct.AccountEventItems') IS NULL THROW 51412, 'SA-009 (036) is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THROW 51412, 'API role is required (SA-API-permissions.sql).', 1;
BEGIN TRANSACTION;
GRANT SELECT, INSERT ON OBJECT::svcacct.AccountEvents TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.AccountEventItems TO svcacct_api_runtime;
COMMIT TRANSACTION;
