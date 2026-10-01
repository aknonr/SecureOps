/* Unnumbered review candidate for SA-002. Requires SA-API-permissions.sql (role svcacct_api_runtime).
   Only the verbs the module issues; no DELETE (both tables block hard delete). The Worker role needs nothing new. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'svcacct.AccountUsages') IS NULL OR OBJECT_ID(N'svcacct.TeamRoles') IS NULL THROW 51321, 'SA-002 is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THROW 51321, 'API role is required (SA-API-permissions.sql).', 1;
BEGIN TRANSACTION;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.AccountUsages TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.TeamRoles TO svcacct_api_runtime;
COMMIT TRANSACTION;
