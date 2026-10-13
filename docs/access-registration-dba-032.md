# Access registration: DBA note for migration 032

Date: 2026-10-06. Scope: G-34. This task applies the script only to fresh synthetic
LocalDB databases. Installed TEST has not been changed. Target execution requires
the owner's separate approval and the DBA's approved recovery point/change process.
The application never executes migrations.

## Preflight (read-only)

Inventory the installed migration chain; 031 is the preceding numbered migration.
The index itself requires the reviewed access objects through 019. Do not replay
001-031 on an installed target. Confirm database identity, SQL version, collation,
available index space, log capacity and acceptable blocking window.

```sql
SELECT DB_NAME() AS DatabaseName, SERVERPROPERTY('ProductVersion') AS SqlVersion;
SELECT i.name, i.is_disabled, i.has_filter, i.filter_definition,
       c.name AS ColumnName, ic.key_ordinal, ic.is_descending_key, ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N'security.AccessRequests')
ORDER BY i.name, ic.key_ordinal, c.name;
```

Require `security.AccessRequests` and `IX_AccessRequests_StatusPage` (019).
`IX_AccessRequests_UserRequested` must be absent. Missing dependencies fail with
51380; an existing same-named index refuses replay with 51381, including a mismatched
definition. Investigate differences rather than skipping or silently repairing them.

Record access row counts, append-only history/trigger definitions and runtime grants
before/after. The change adds one nonunique, unfiltered index:
`(UserId ASC, RequestedAt DESC) INCLUDE (Status)`. The clustered request ID is already
available to the latest-request read. No row backfill, role seed, grant, constraint,
trigger, isolation-level or authorization change is made.

## Application (only after target approval)

Use a DBA DDL identity, SQLCMD mode, and the migrations working directory:

```powershell
cd <repo>\sql\migrations
sqlcmd -S <approved-server> -d <approved-database> -E -I -b -i 032-access-request-user-index.sql
```

Stop on a nonzero exit code. The schema script uses `XACT_ABORT ON` and a transaction.
There is no startup DDL, automatic migration or additional runtime permission.
This is an ordinary index build, not an online build; assess its blocking/log impact
on the actual target before choosing the change window.

## Verification and recovery

Repeat the inventory query: the new enabled index must have the exact keys/include
above and `has_filter = 0`. Existing rows, history, guards and grants must be unchanged.
Review the latest-request query plan and fresh deadlock evidence on the approved target;
LocalDB success does not establish corporate SQL readiness.

The matching binary retries only a registration transaction that SQL reports as deadlock
victim 1205, once, using a fresh connection/transaction after disposal. A second 1205,
other SQL errors and cancellation propagate. Post-commit snapshot reads, approval,
role changes and external operations are outside that retry. Serializable isolation,
UPDLOCK/HOLDLOCK, pending status and the unique pending-request barrier stay in force.
The retry is not a replacement for 032.

For binary rollback, retain the additive index; it is compatible with the older query.
Do not delete history/audit data or disable guards. Removing the index would restore
the observed scan risk and needs a separate DBA-reviewed decision/recovery plan.

## Synthetic evidence

Reproduce/verify using the new LocalDB-only harness (refuses existing database/evidence
names; retains synthetic databases for inspection):

```powershell
powershell -NoProfile -File tests/sql/access-registration/Test-AccessRegistrationSql.ps1 -DatabaseSuffix <unique-suffix> -ThroughMigration 32
```

The original red test is commit `4acbb10` with migrations through 031: eight concurrent
new identities per round, ten rounds, 73 successes and seven engine 1205 victims.
Seven deadlock graphs show RangeS-U cycles on `IX_AccessRequests_StatusPage` at the
latest-request read. A first run without the timing fixture also captured 26 graphs.
A test-only 50 ms Users-insert trigger makes overlap reproducible; it is removed in
`finally` and is never included in a migration or deployment.

Index-only verification, before the retry change: 80/80 registrations and zero graphs.
The harness records TRX, deadlock XML and a cached latest-request plan under
`artifacts/access-registration/<suffix>/`; it checks predecessor Pending/Approved/Rejected
rows and all database permissions by SHA-256 and refuses migration replay (51381).
Separate negative probes deliberately force real engine deadlocks to verify one retry
and second-victim propagation. Their expected 1205 events are not a concurrency-test
failure or evidence of recurrence in the fixed query.

Service Accounts/UI and their serial test collections are unchanged. No installed TEST,
corporate provider, sending, deployment or merge verification is claimed.
