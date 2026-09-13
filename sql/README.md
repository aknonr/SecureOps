# sql/

SQL Server schema and migration scripts. Files under `schema/` are reviewed contracts, never applied by application startup. Explicit isolated SQL tests may execute them; corporate execution requires the approved DBA process.

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
| 008 | Persisted application-user profile fields |
| 009 | Bounded nullable SDM evaluation evidence on OperationalRecords and append-only workflow history; requires 001-008 |
| 010 | Shared resource categories/links, owner-scoped versioned personal preferences, and unassigned ResourceCurator role seed; requires 001-009 |
| 011 | Default-off durable source-close intent on JiraTransfers; requires 001-010; legacy transfers remain source-open |
| 012 | Independent local In Use records/refresh state; unassigned role seeds 8 InUseReviewer and 9 InUseCoordinator; requires 001-011 |
| 013 | Transactional SDM evidence CHECK replacement admits the exact positive pilot policy version; requires 001-012; no new objects, columns, roles or runtime grants |
| 014 | Independent append-only announcement draft revisions; requires 001-013 |
| 015 | Announcement owner/latest-version index only; requires 014; no JSON rewrite or new runtime grant |
| 016 | Durable announcement source jobs and versioned operator overrides; requires 014-015. Adds `announcements.SourceJobs` (unique OwnerId/DraftId/SubmissionKey, no-delete trigger) and mutable `announcements.SourceOverrides`. Runtime delta: `GRANT SELECT, INSERT, UPDATE` on those two objects only; no DELETE or DDL grant. Hangfire's own schema is a separate DBA step using the published Hangfire 1.8.6 script; the application creates it only when the isolated LocalDB facility sets `Hangfire:PrepareSchema` |

013 uses `migrations/013-sdm-pilot-policy.sql` in SQLCMD mode. Existing NULL/v1
evidence and append-only triggers remain untouched; WITH CHECK validates stored
rows. DBA DDL identity only. No runtime DDL is needed. Older writers can invalidate
positive evidence or drop additive In Use JSON metadata; downgrade is not a proven
compatible write path. Keep recovery points and suspend writes before rollback.

In Use V1 requires 012 only when using SQL persistence. Runtime delta:
`GRANT SELECT, INSERT, UPDATE ON OBJECT::ops.InUseRecords TO [approved_runtime_principal];`
and `GRANT SELECT, UPDATE ON OBJECT::ops.InUseRefresh TO [approved_runtime_principal];`.
Replace only the principal placeholder through the approved DBA process. Existing
`audit.AuditLog` INSERT, `ops.CommandExecutions` SELECT/INSERT/UPDATE and access
read grants are reused. No DELETE, DDL, db_owner, audit UPDATE/DELETE or new
scheduler grants. Role seeds assign no users; role-ID collisions fail closed.
012 is not a blind-rerun script. The application never applies migrations.
The existing isolated LocalDB harness now upgrades through 012 and includes
In Use round-trip, version conflict and transactional audit rollback tests.

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

No application startup migration or EF migration is currently enabled. Corporate SQLCMD execution belongs to the approved DBA process; the isolated local test harness below is separate.

Audit triggers and append-only enforcement live in `sql/schema/` and are applied as part of the same migration that creates the table.

The Operational Record/Jira workflow uses `schema/002-operational-record-jira-workflow.sql`. DBA review must confirm schema ownership, backup/retention, and runtime grants. The service requires only `SELECT`, `INSERT`, and `UPDATE` on the `ops` tables; it does not require `DELETE`, DDL, or migration permissions.

Application-session governance uses `schema/007-application-session-governance.sql`. Runtime requires `SELECT`, `INSERT`, and `UPDATE` on `security.ApplicationSessions` and `SELECT` on `reporting.ManagementSessionStatus`. It requires no `DELETE`, DDL, schema ownership, or migration permission.

## Test Harness

Announcement drafts use additive 014 (separate append-only revisions). Runtime
needs SELECT/INSERT on announcements.DraftRevisions plus existing audit INSERT;
no UPDATE/DELETE/DDL. No grants are applied automatically. Retain additive data on
rollback; older binaries cannot operate this module. Do not replay 012/013.
The harness opt-in `-IncludeAnnouncementDrafts` adds 014-015 only to its fresh local DB.

Resource v1 adds `scripts/powershell/Test-ResourceCatalogueSql.ps1`, an explicitly
invoked isolated LocalDB-only harness. It refuses an existing database name,
applies 001-008, inserts synthetic predecessor rows, applies 009 then 010, checks
preservation and empty catalogue defaults, and optionally runs ResourceSqlTests.
It neither installs SQL nor touches existing user databases. It uses SQLCMD -I
(QUOTED_IDENTIFIER ON), required by filtered indexes in the existing chain.
Migration 009 remains unchanged. Local execution is not corporate readiness.

010 stores bounded text and tags, ordered/visibility indexes and category/user
foreign keys. Versions must be positive. Personal JSON is capped at 240000 bytes
and must contain arrays plus the matching version; the service enforces nested
counts, IDs, ownership and default-set invariants. No link snapshots or credentials
are copied into personal JSON. Runtime needs SELECT/INSERT/UPDATE on the three
resource tables and existing INSERT on audit.AuditLog; no DELETE or DDL.
Existing records are untouched and no catalogue entries/user grants are seeded.
010 guards object/role creation but does not repair a mismatched existing schema.
The table/role batch is transactional; schema creation precedes it. Rollback
restores binaries/configuration and retains additive tables/audit, not a destructive
down migration. Archive changes only current state; audit remains append-only.

Migration 009 adds `SdmEvaluationJson nvarchar(4000) NULL` to the record and
history. SQL JSON/shape checks enforce safe evaluation defaults; existing rows
remain unevaluated. The repository commits evaluation plus history/audit in one
serializable transaction. Existing object-level grants suffice, including INSERT
on `audit.AuditLog`; no approval actor/time/reason columns or new permissions are
introduced. Apply through the DBA process before upgrading SQL-backed binaries.

SQL integration tests must use an explicitly supplied `SECUREOPS_SQL_TEST_CONNECTION` environment variable and must reject empty values. No source-controlled connection string is permitted. Offline schema contract tests do not connect to SQL Server.

## Reference

- `docs/04-domain-model.md` — full schema specification with DDL.
- `docs/08-audit-model.md` — audit table specifics and triggers.
