/* Unnumbered review candidate. DBA assigns the approved Worker principal to this role separately.
   Existing Hangfire permissions are independent and are not changed here. */
SET XACT_ABORT ON;
IF SCHEMA_ID(N'svcacct') IS NULL THROW 51311, 'Service Accounts schema is required.', 1;
IF DATABASE_PRINCIPAL_ID(N'svcacct_worker_runtime') IS NOT NULL THROW 51311, 'Worker role exists; compare grants, do not replay.', 1;
BEGIN TRANSACTION;
CREATE ROLE svcacct_worker_runtime;
GRANT SELECT ON OBJECT::svcacct.Accounts TO svcacct_worker_runtime;
GRANT SELECT ON OBJECT::svcacct.WorkRequests TO svcacct_worker_runtime;
GRANT SELECT, INSERT, UPDATE ON OBJECT::svcacct.ReminderOutbox TO svcacct_worker_runtime;
COMMIT TRANSACTION;
