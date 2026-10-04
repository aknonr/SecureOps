/* Review candidate for SA-004 (numbered 030, ADR-0027). Requires SA-API-permissions.sql (role svcacct_api_runtime).
   Only the verbs the module issues: scans, links and decisions are append-only, so only SELECT and INSERT are granted;
   the Worker role needs nothing new. No role member is assigned. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'svcacct.UsageScans') IS NULL OR OBJECT_ID(N'svcacct.UsageScanDecisions') IS NULL THROW 51361, 'SA-004 (030) is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THROW 51361, 'API role is required (SA-API-permissions.sql).', 1;
BEGIN TRANSACTION;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScans TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScanServers TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScanItems TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScanLinks TO svcacct_api_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScanDecisions TO svcacct_api_runtime;
COMMIT TRANSACTION;
