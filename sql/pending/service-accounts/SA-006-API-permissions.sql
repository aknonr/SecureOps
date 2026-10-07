/* Review candidate for SA-006 (numbered 033). Requires SA-API-permissions.sql (role svcacct_api_runtime).
   Only the verbs the module issues: plan records are append-only, so SELECT and INSERT; ChangePlans also gets UPDATE for
   its status columns (TR_SaChangePlans_Fixed refuses any other column change). Nothing may be deleted. The Worker role needs
   nothing new. No role member is assigned. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'svcacct.ChangePlans') IS NULL OR OBJECT_ID(N'svcacct.ChangePlanEvents') IS NULL THROW 51391, 'SA-006 (033) is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THROW 51391, 'API role is required (SA-API-permissions.sql).', 1;
BEGIN TRANSACTION;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.ChangePlans TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanAccounts TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanPreviews TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanItems TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanApprovals TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangeItemChecks TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.ChangePlanEvents TO svcacct_api_runtime;
COMMIT TRANSACTION;
