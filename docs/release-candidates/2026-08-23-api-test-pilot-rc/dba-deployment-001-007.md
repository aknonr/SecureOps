# SecureOps DBA Deployment Bundle 001-007

## Scope

This is the offline DBA contract for the SecureOps API TEST/Pilot release candidate. The application and deployment package never execute these files. Use SQLCMD mode and an approved DBA migration identity. Runtime identity is `DOMAIN\WASAST_YONETIM`.

## Immutable Order and Dependencies

| Order | SQLCMD entrypoint | Dependency | Result |
|---|---|---|---|
| 001 | `sql/migrations/001-audit-and-access-control.sql` | Empty target object names | `audit` and `security`; audit/access tables, roles 1-4, indexes, append-only and no-self-approval triggers |
| 002 | `sql/migrations/002-operational-record-jira-workflow.sql` | 001 is operationally required before application activation | `ops`; Operational Record, Jira transfer, history tables and constraints |
| 003 | `sql/migrations/003-platform-access-concurrency-hardening.sql` | 001 and 002 | Access/authentication columns, roles 5-6, source claim/freshness columns, `ops.CommandExecutions` |
| 004 | `sql/migrations/004-access-read-model-and-versioning.sql` | 001 and 003 | Access mutation-version columns |
| 005 | `sql/migrations/005-management-reporting-read-model.sql` | 001 and 002 | `reporting` schema, three runtime reporting views, supporting indexes |
| 006 | `sql/migrations/006-operational-record-source-created-at-nullable.sql` | 002 | Ensures `SourceCreatedAt` remains nullable; no-op on a fresh 002 schema |
| 007 | `sql/migrations/007-application-session-governance.sql` | 001, 004, and 005 | `security.ApplicationSessions`, lifecycle indexes, limited session status view |

The sequence is exactly 001-007. No migration 008 exists or is authorized. Each entrypoint includes exactly its matching `sql/schema/NNN-*.sql` file.

## Runtime Object Compatibility

| Current runtime component | Required objects |
|---|---|
| SQL audit | `audit.AuditLog` |
| Application access | `security.Users`, `Roles`, `RoleAssignments`, `AccessRequests`, `AccessRequestHistory` |
| Application sessions | `security.ApplicationSessions` |
| Operational Record/Jira workflow | `ops.OperationalRecords`, `JiraTransfers`, `OperationalRecordWorkflowHistory`, `CommandExecutions` |
| Management reporting | `reporting.ManagementAuditEvents`, `ManagementWorkflowEvents`, `ManagementOperationalStatus` |

Offline contract tests and source review confirm every object referenced by the current SQL repositories is produced by 001-007. `reporting.ManagementSessionStatus` is created for bounded future/reporting use but is not read by current runtime code and therefore receives no runtime grant in this candidate.

## Preflight

1. Confirm the target database and collation are approved and take a recoverable backup/recovery point.
2. Inventory all `audit`, `security`, `ops`, and `reporting` schemas, tables, views, triggers, constraints, indexes, and seeded role IDs/codes.
3. Confirm 001 and 002 object names do not already exist. They are not re-runnable.
4. Confirm roles 1-6 do not conflict with existing IDs/codes. Migration 003 is only partially guarded.
5. Review lock/log impact from non-null backfills and indexes in 003-005 and 007.
6. Run each entrypoint separately in SQLCMD mode, stop on the first error, and verify its objects before continuing.
7. Do not re-run a partially failed batch without a DBA-authored corrective plan.

## Recovery

There is no migration-history table, encompassing transaction, down migration, or automatic rollback. A failure after `GO` can leave partial state. Application rollback restores prior binaries/configuration while retaining additive database objects. Database recovery uses only the approved backup/restore or DBA-authored corrective migration process. Never delete audit/history data as rollback.

## Runtime Grants

Review and execute `runtime-grants-WASAST_YONETIM.sql` only after all seven migrations verify successfully and the Windows login/database user already exist. The file grants only current object-level access. It grants no `DELETE`, DDL, schema ownership, `db_owner`, or `db_ddladmin`.

DBA must also verify same-owner view/trigger ownership chaining, append-only triggers, TLS validation, Windows Integrated Security, and that the runtime principal is not a member of broad database roles.
