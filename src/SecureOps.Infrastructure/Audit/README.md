# Audit Infrastructure

Codex-owned append-only operational audit implementation. `IAuditWriter` supports InMemory, File, and SqlServer providers selected by `Audit:Provider`; persistent stores use `QueuedAuditWriter`. `SqlAuditWriter` uses `ConnectionStrings:SecureOpsDb` and the reviewed `audit.AuditLog` contract.

Local tests use InMemory or offline SQL-asset checks only. No connection string, database call, or production audit store is used by default. SQL schema execution, service permissions, retention, and runtime fail-closed validation require DBA and controlled-server approval.
