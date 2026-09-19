# sql/

## Post-03b0c04 Catalogue Delta

Current successor source additionally requires **024-in-use-report-catalogue**
after verified 023. 024 creates one immutable metadata projection and its code
index; it neither backfills from current records nor rewrites existing receipts,
envelopes or XLSX bytes. Runtime delta is SELECT/INSERT on
`reporting.InUseReportCatalogue` for the existing approved API principal only;
no user/role assignment, UPDATE, DELETE, DDL or Worker archive ACL is added.
DBA reviews this new object through the existing deployment procedure. Do not
replay operator-reported installed 022/023, 001-021, or Hangfire schema 9.
There is no migration ledger in this repository: verify object/column/index/
trigger definitions and the target's retained DBA execution evidence read-only.
The isolated harness now creates a fresh 001-024 test database. Existing 023
installations use only the separately reviewed 024 delta in a matched successor.
Older writers are not proven compatible with lifecycle/EvidenceSheets and reporter
decision metadata: stop writes and coordinate all components before rollback;
retain the new table and audit, do not use destructive down scripts.

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
| 016 | Durable announcement source jobs and versioned operator overrides; requires 014-015. Adds `announcements.SourceJobs` (unique OwnerId/DraftId/SubmissionKey, no-delete trigger) and mutable `announcements.SourceOverrides`. Runtime delta: `GRANT SELECT, INSERT, UPDATE` on those two objects only; no DELETE or DDL grant. Hangfire's schema is provisioned separately using its published 1.8.6 installation script; runtime `Hangfire:PrepareSchema` must be false. |
| 017 | Additive SourceJobs dispatch reservation, Hangfire acknowledgment, expiring attempt identity/count and recovery index; requires 016. No new runtime object grants; no rewrite or deletion of snapshots/revisions. |
| 018 | Append-only announcement preparation snapshots; requires 017. Runtime SELECT/INSERT on announcements.Preparations only, plus existing grants. Promoted from the local unnumbered candidate. |
| 019 | Versioned persisted role bundles and bounded access paging indexes; preserves existing role IDs/permissions. Requires 018; refuses replay. |
| 020 | Immutable mail intent/bytes, one-distribution index, recovery states and append-only typed operation events. Existing OCO Source/Prepare rights become explicit; no SelfTest/Send grant. Requires 019; refuses replay. |
| 021 | Nullable original initiator, input version and authoritative closure evidence on JiraTransfers; legacy rows remain NULL. Requires 020; refuses replay. |
| 022 | Immutable server review history, durable In Use execution/artifact and append-only step evidence. Requires installed 001-021; no role assignment or legacy-data rewrite. |

### Post-rc6.22 Delta

An installation already on 001-021/Hangfire 9 applies only the reviewed **022**
successor, never the earlier scripts. See `docs/inuse-v2-upgrade-tr.md` for the
operator sequence, narrow object permissions and non-destructive rollback limits.
No new source writes are enabled. History/events need SELECT/INSERT; executions
need SELECT/INSERT/UPDATE; existing record/access reads and audit INSERT remain.
No runtime DDL, DELETE or db_owner is needed. Review/event updates and deletes,
and execution intent/artifact rewrites, are rejected by triggers. Preserve new
tables on binary rollback and suspend In Use writes from older serializers.

### Upgrade From Verified 018

Use the new delivery's **019-021 delta**, not the full reference archive. Confirm
the operator-reported 018 objects and backup before applying only missing scripts
in order through SQLCMD (`-I -b`, migrations working directory). Existing 001-018
files are unchanged; never replay them or the already provisioned Hangfire schema 9.
No application startup DDL, down migration, trigger disabling or automatic repair.

Narrow runtime delta, assigned by DBA to the existing approved principal only:
security.Roles adds INSERT/UPDATE to existing SELECT; announcements.MailCommands
needs SELECT/INSERT/UPDATE; ops.OperationEvents needs SELECT/INSERT. Existing
JiraTransfers/Users/RoleAssignments access, audit INSERT, preparation SELECT/INSERT,
source-job grants and dedicated Hangfire DML remain. No DELETE on these new
objects, audit/history mutation, schema ownership, db_owner or runtime DDL.
The same application lock already uses SQL public sp_getapplock permissions.
API requires event SELECT; Worker only appends events. Split identities can have
these grants narrowed separately after actual host inventory.

019/020 access-version changes require access revalidation. 021 does not invent
historical profile/closure facts. Old binaries are not approved writers for new
role/command/JSON contracts; suspend writes and use the paired backup/recovery plan
before rollback. Retain all additive objects, mail intents and audit evidence.

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

Use the fresh `Test-AnnouncementPreparationsSql.ps1` wrapper for 001-018,
or `Test-ResourceCatalogueSql.ps1 -IncludeAnnouncementPreparations`. Runtime needs
SELECT/INSERT on announcements.Preparations plus existing audit/access grants only.

Announcement drafts use additive 014 (separate append-only revisions). Runtime
needs SELECT/INSERT on announcements.DraftRevisions plus existing audit INSERT;
no UPDATE/DELETE/DDL. No grants are applied automatically. Retain additive data on
rollback; older binaries cannot operate this module. Do not replay 012/013.
The harness opt-in `-IncludeAnnouncementDrafts` adds 014-015 only to its fresh local DB.
`-IncludeAnnouncementSources` includes drafts and extends that inventory through 017.
It does not install Hangfire: provision that schema separately before starting either host.
Runtime Hangfire needs SELECT/INSERT/UPDATE/DELETE on its dedicated schema, not DDL.
Source apply keeps draft INSERT, source override writes and audit INSERT in one transaction.
The existing append-only audit UPDATE/DELETE restrictions remain enforced.
See [source acceptance](../docs/contracts/planned-announcement-source-acceptance.md) for tested grants,
isolated databases and integration requirements. No corporate migration was executed.

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
# Integrated reporting delta 023

Current follow-up requires `023-workflow-report-snapshots.sql` after verified 022.
It adds `OperationalRecords.SourceSynthetic` (NULL means unknown, never backfilled
as real), three immutable reporting tables and bounded-query indexes. Runtime uses
SELECT/INSERT on those tables with existing operational reads/audit INSERT; no
DDL, DELETE, grants or history rewrite is performed by application startup.
Archive receipts attest verified envelope bytes, not an earlier authorization audit.
Preserve additive data on binary rollback. Expired cuts are inaccessible after one
hour/access-version change; immutable retention is not an automatic purge policy.
See ADR-0023 and `docs/integrated-activation-tr.md`. Review installed definitions;
never replay 001-022 or Hangfire schema 9 on an existing target.
