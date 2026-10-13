/* Review candidate for SA-010 (numbered 037). Requires SA-Worker-permissions.sql (role svcacct_worker_runtime) and 030.
   The Worker runs the web usage scan job: it claims and updates the job rows, then writes the ordinary 030 evidence record
   (tool = Jea) and links it to each account under the starter's identity, re-reading the starter's scope inside that write, with
   module History and audit in the same transaction. Evidence tables stay SELECT/INSERT only. Re-reading the starter's approval and
   capabilities uses the platform access tables the Worker principal already reads for its other jobs; nothing is granted on them
   here. Nothing may be deleted. No role member is assigned. */
SET XACT_ABORT ON;
IF OBJECT_ID(N'svcacct.UsageScanJobs') IS NULL OR OBJECT_ID(N'svcacct.UsageScans') IS NULL THROW 51434, 'SA-010 (037) and SA-004 (030) are required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_worker_runtime') IS NULL THROW 51434, 'Worker role is required (SA-Worker-permissions.sql).', 1;
BEGIN TRANSACTION;
GRANT SELECT, UPDATE ON OBJECT::svcacct.UsageScanJobs TO svcacct_worker_runtime;
GRANT SELECT, UPDATE ON OBJECT::svcacct.UsageScanJobAccounts TO svcacct_worker_runtime;
GRANT SELECT, UPDATE ON OBJECT::svcacct.UsageScanJobServers TO svcacct_worker_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScans TO svcacct_worker_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScanServers TO svcacct_worker_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScanItems TO svcacct_worker_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.UsageScanLinks TO svcacct_worker_runtime;
GRANT SELECT ON OBJECT::svcacct.ScopeGrants TO svcacct_worker_runtime;
GRANT SELECT ON OBJECT::svcacct.Organizations TO svcacct_worker_runtime;
GRANT SELECT ON OBJECT::svcacct.Teams TO svcacct_worker_runtime;
GRANT SELECT ON OBJECT::svcacct.Handovers TO svcacct_worker_runtime;
GRANT SELECT, INSERT ON OBJECT::svcacct.History TO svcacct_worker_runtime;
GRANT INSERT ON OBJECT::audit.AuditLog TO svcacct_worker_runtime;
COMMIT TRANSACTION;
