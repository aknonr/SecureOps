/* Review candidate for SA-008 (numbered 037). Requires SA-API-permissions.sql (role svcacct_api_runtime).
   Only the verbs the API issues: it creates jobs (INSERT), reads them, records a cancel request, releases the accounts of a job
   whose Worker lease expired and marks a job that could not be queued (UPDATE). Nothing may be deleted. No role member is assigned. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'svcacct.UsageScanJobs') IS NULL OR OBJECT_ID(N'svcacct.UsageScanJobServers') IS NULL THROW 51414, 'SA-008 (037) is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_api_runtime') IS NULL THROW 51414, 'API role is required (SA-API-permissions.sql).', 1;
BEGIN TRANSACTION;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.UsageScanJobs TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.UsageScanJobAccounts TO svcacct_api_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.UsageScanJobServers TO svcacct_api_runtime;
COMMIT TRANSACTION;
