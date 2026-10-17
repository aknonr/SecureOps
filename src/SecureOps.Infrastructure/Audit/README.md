# Audit Infrastructure

Codex-owned append-only operational audit implementation. `IAuditWriter` supports InMemory, File, and SqlServer providers selected by `Audit:Provider`; persistent stores use `QueuedAuditWriter`. `SqlAuditWriter` uses `ConnectionStrings:SecureOpsDb` and the reviewed `audit.AuditLog` contract.

Queued writes await durable acknowledgement after the sink successfully writes the batch. Sink failure, shutdown, or cancellation/30-second acknowledgement timeout fails the waiting caller with `AuditWriteUnavailableException`. Full queues retain the configured fail-closed or explicit non-production drop behavior. Sink failures mark the store unhealthy and log a Critical entry.

File is for Development/test only and is rejected in Production: it does not enforce the retention floor of at least 36 months. The file sink rolls files by date/size but never deletes audit evidence; `RetainedFileCountLimit` is an obsolete, ignored compatibility setting. Production requires SqlServer and externally governed retention of at least 36 months.

With SQL sessions and `Audit:Provider=SqlServer`, terminal transitions and their audit inserts commit in one SQL transaction. Only transitioned sessions produce terminal audit rows. Other providers retain update-then-audit ordering with one winning transition; audit failure denies the caller but cannot roll back that non-atomic update. Losing callers re-read the persisted terminal outcome.

Local tests use synthetic InMemory/File stores by default. SQL session tests are opt-in and restricted to the isolated Resource V1 LocalDB facility. SQL schema execution, service permissions, retention, and runtime fail-closed validation require DBA and controlled-server approval.

Operational Record/Jira workflow actions use the same `IAuditWriter` boundary. Audit payloads contain workflow identifiers, state/result, correlation ID, and the Jira issue key only when available; requester details and integration credentials are excluded.

Configured first-admin approval emits `UserFirstSeen`, `AccessRequested`, `AccessApproved`, and `RoleAssigned` audit actions. SQL TEST selection requires migration 001, Integrated Security, fail-closed queue settings, and only `INSERT` permission on `audit.AuditLog` for the runtime identity.
