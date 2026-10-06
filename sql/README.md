# sql/

## Access Registration Index 032, 2026-10-06

Current numbered inventory is 001-032. `032-access-request-user-index.sql` adds
`IX_AccessRequests_UserRequested (UserId, RequestedAt DESC) INCLUDE (Status)` for
the serializable latest-request read. It requires access objects through 019,
refuses replay (51381), and changes no rows, guards or runtime grants. Only fresh
synthetic LocalDB was used for this task; installed TEST was not upgraded.
See [DBA preflight and recovery](../docs/access-registration-dba-032.md) and
`tests/sql/access-registration/Test-AccessRegistrationSql.ps1` for the regression
harness, captured deadlock/plan evidence and predecessor-preservation checks.

## Service Account Requested gMSA Name 031, 2026-10-05

Inventory is 001-031. **031-service-account-requested-gmsa-name** requires reviewed 025 and 030 and includes the retained
`pending/service-accounts/SA-005-requested-gmsa-name.sql` unchanged: two nullable columns, `WorkRequests.RequestedGmsaName`
and `IdentityTransitions.RequestedGmsaName` (`nvarchar(256) NULL`, no default), so existing rows are not rewritten and stay
NULL. No grant changes: `svcacct_api_runtime` already has table-level SELECT, INSERT, UPDATE on both tables; the Worker role
needs nothing new. Replay is refused (51370). Binaries with this code keep working before 031: names show as unavailable,
writes without a name succeed and a write with a name is refused with `gmsaNameColumnsMissing`. 031 was reserved after
checking every published branch (all end at 030 or earlier). Apply only after separate owner approval, first on a copy of
the installed database. Rollback: older binaries ignore the columns; keep them and their values.

## Service Account Usage Scans 030, 2026-10-04

Inventory is 001-030. **030-service-account-usage-scans** (ADR-0027) requires reviewed 025/026 and includes the retained
`pending/service-accounts/SA-004-usage-scans.sql` unchanged: five new append-only tables (`UsageScans`,
`UsageScanServers`, `UsageScanItems`, `UsageScanLinks`, `UsageScanDecisions`) with triggers refusing UPDATE/DELETE
(error 51307). No existing table, constraint, row, role or grant changes; no data is added. One transaction; replay is
refused (51360). Runtime grants are separate in `pending/service-accounts/SA-004-API-permissions.sql`: SELECT and INSERT
on the five tables for `svcacct_api_runtime` only; the Worker role gets nothing.

030 was reserved after checking every published branch (all end at 029 or earlier) and depends on no other unapplied
script. Binaries with the usage-scan code keep working before 030: the account detail reports scans as unavailable and an
upload is refused with `scanTablesMissing`; nothing else changes. Apply only after separate owner approval, first on a copy
of the installed database. Rollback: stop uploads (older binaries never touch the tables); keep the tables, rows and audit.

## Service Accounts Scope Bootstrap 029, 2026-10-03

Inventory is 001-029. **029-service-account-scope-bootstrap** (ADR-0026) requires reviewed 025/026
and includes the retained `pending/service-accounts/SA-003-scope-bootstrap.sql` unchanged: it adds
`svcacct.ScopeGrants.IsBootstrap` (default 0, existing rows stay 0), re-creates
`CK_SaScopeGrants_NoSelfGrant` WITH CHECK so a self-grant is allowed only for the single "All"
bootstrap row, and adds `UX_SaScopeGrants_OneBootstrap`. One transaction; replay is refused.
No data, runtime grant, role member or scope is added; the API role already inserts into
`svcacct.ScopeGrants`. Installed binaries without the bootstrap code keep working (their inserts
take the default 0 and the old rule); new binaries before 029 report `bootstrapSchema` and change nothing.

029 was reserved after checking every published branch (all end at 028 or earlier). It does not
depend on 027/028 and touches none of their objects. Do not replay installed 001-028. Apply only
after separate owner approval, first on a copy of the installed database. Rollback: the original
check can be restored only while no bootstrap row exists; otherwise keep the column, index and row.
The isolated harness applies SA-003 itself only when run with `-ThroughMigration` below 29.

## Admin Operations Successor, 2026-10-03

Isolated .NET 8 successor inventory is 001-028. **028-admin-service-account-operations**
requires the reviewed 026/027 baseline and adds only missing Work/Assign/Verify/Import/Report
capabilities to protected seeded Admin. It preserves unrelated capabilities, increments
role and assigned-user access versions and requires atomic audit. Matching bundles
reject replay. No runtime SQL grant, membership or module scope is added.

028 is reserved after checking remote master and the published .NET 10 heads
(all end at 027); numbering was rechecked before source publication.
Keep prior packages and installed 022-027 unchanged. The sealed 0e85c9d review workflow
does not package this successor: do not bypass its existing exact-inventory guards.
Rollback retains additive capabilities/versions/audit; any removal is a separate
audited owner-reviewed amendment, never version reset or audit deletion.

## Combined Source 026 Delta, 2026-10-02

The current inventory is 001-027. **027-admin-service-account-navigation** extends only the
protected Admin application bundle with module View/Administer, advances role/access versions
and commits required audit atomically. It changes no SQL runtime grants or module scopes.
Apply only after separate owner approval; do not replay an already matching bundle.

**026-service-account-usage-rules** requires reviewed 025 and
includes the retained `pending/service-accounts/SA-002-usage-rules.sql`. It atomically creates
`svcacct.AccountUsages` and `svcacct.TeamRoles` and their delete-protection triggers; replay is refused.
The separate `SA-002-API-permissions.sql` grants only SELECT/INSERT/UPDATE on those two tables to
`svcacct_api_runtime`; no new Worker grant or role member is assigned. SQLCMD working directory
is `sql/migrations`. Apply only missing, individually approved scripts, never replay installed 001-025.
Source promotion is not target execution approval. On rollback retain 026 objects/data and audit;
stop module writes and coordinate matching binaries before restoring a prior version. No down script.

## Post-03b0c04 Catalogue Delta

Current successor source additionally requires **024-in-use-report-catalogue**
after verified 023. 024 creates one immutable metadata projection and its code
index; it neither backfills from current records nor rewrites existing receipts,
envelopes or XLSX bytes. Runtime delta is SELECT/INSERT on
`reporting.InUseReportCatalogue` for the existing approved API principal only;
no user/role assignment, UPDATE, DELETE, DDL or Worker archive ACL is added.
The SQL execution operator reviews this new object through the approved change
procedure. Do not
replay operator-reported installed 022/023, 001-021, or Hangfire schema 9.
There is no migration ledger in this repository: verify object/column/index/
trigger definitions and effective API permissions read-only. Retain historical
execution logs/change notes if they exist; otherwise mark them unavailable.
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
└── migrations/ # Numbered migration scripts (001-…, same numbers as schema/)
```

## Current Reviewed Migrations

The local combined review candidate additionally reserves **025-service-accounts**.
Its SQLCMD wrapper requires 023/024 and includes the unchanged reviewed
`pending/service-accounts/SA-001-service-accounts.sql` from the migrations working
directory. Ship that include file with the wrapper; never execute only the wrapper
without its dependency. API/Worker permission scripts remain separate and assign
no principal. Observed TEST 022/023 is not replayed: the conditional sequence is
024, verify/stop, then separately approved 025 and reviewed role scripts.
Number reservation is local, not a target installation or shared-branch promotion.

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
| 023 | Immutable workflow snapshots/facts/archive receipts and nullable synthetic-origin evidence; requires 022. Runtime SELECT/INSERT on the reporting objects; no inferred historical origin. |
| 024 | Immutable In Use report catalogue metadata linked to verified archive receipts; requires 023. Runtime SELECT/INSERT on reporting.InUseReportCatalogue for the API only; no backfill, user assignment or archive rewrite. |

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

The files are SQLCMD entrypoints and must run in exact order. Migrations 001 and 002 are not idempotent; 003 is only partially guarded; 004-007 guard or replace their objects. No down scripts or migration-history table exists. See the current operator guide before approved SQL execution.

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

No application startup migration or EF migration is currently enabled. Corporate SQLCMD execution belongs to the named SQL execution operator under the approved change process; the isolated local test harness below is separate.

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
