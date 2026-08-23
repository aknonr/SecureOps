# sql/

SQL Server schema and migration scripts. Files under `schema/` are reviewed offline contracts; they are not executed by the application, tests, or deployment process.

## Structure

```
sql/
├── schema/     # DDL for tables, views, triggers, stored procedures
└── migrations/ # Versioned migration scripts (V001, V002, ...)
```

## Current Reviewed Migrations

| Order | Description |
|---|---|
| 001 | Audit and application access schemas, tables, indexes, role seeds, and append-only/self-approval triggers |
| 002 | Operational Record, Jira correlation, and append-only workflow history |
| 003 | Access status/authentication fields, claim/freshness metadata, JiraPublisher/Auditor roles, and durable command executions |
| 004 | Explicit access-user and access-request mutation versions for stale-write rejection |
| 005 | Management reporting views and supporting indexes |
| 006 | Preserve unknown Operational Record source creation timestamps as NULL |
| 007 | Authoritative application sessions, lifecycle indexes, and limited reporting view |

The files are SQLCMD entrypoints and must run in exact order. Migrations 001 and 002 are not idempotent; 003 is only partially guarded; 004-007 guard or replace their objects. No down scripts or migration-history table exists. See `docs/24-api-test-deployment-readiness.md` before DBA execution.

Current reviewed offline assets additionally include `002-operational-record-jira-workflow.sql`, which creates `ops.OperationalRecords`, `ops.JiraTransfers`, and append-only workflow history.

## Phase 3+

| Phase | Tables added |
|---|---|
| 3 | `dbo.NotificationLog` |
| 4 | optional `EntryHash`/`PrevEntryHash` columns on `audit.AuditLog` |
| 5 | `dbo.ExpectedLocalAdmins`, `dbo.LocalAdminInventory` |
| 6 | `dbo.AnalysisRules`, `dbo.AnalysisRuleResults`, analysis views |
| 7 | `audit.AiAuditLog`, append-only trigger |
| 8 | `dbo.RemediationCatalog`, `dbo.RemediationRequests`, `dbo.RemediationExecutions` |

## Migration Tool

No application startup migration or EF migration is currently enabled. The reviewed SQLCMD assets are executed only by the approved DBA process.

Audit triggers and append-only enforcement live in `sql/schema/` and are applied as part of the same migration that creates the table.

The Operational Record/Jira workflow uses `schema/002-operational-record-jira-workflow.sql`. DBA review must confirm schema ownership, backup/retention, and runtime grants. The service requires only `SELECT`, `INSERT`, and `UPDATE` on the `ops` tables; it does not require `DELETE`, DDL, or migration permissions.

Application-session governance uses `schema/007-application-session-governance.sql`. Runtime requires `SELECT`, `INSERT`, and `UPDATE` on `security.ApplicationSessions` and `SELECT` on `reporting.ManagementSessionStatus`. It requires no `DELETE`, DDL, schema ownership, or migration permission.

## Test Harness

SQL integration tests must use an explicitly supplied `SECUREOPS_SQL_TEST_CONNECTION` environment variable and must reject empty values. No source-controlled connection string is permitted. Offline schema contract tests do not connect to SQL Server.

## Reference

- `docs/04-domain-model.md` — full schema specification with DDL.
- `docs/08-audit-model.md` — audit table specifics and triggers.
