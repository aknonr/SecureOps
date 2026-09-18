# ADR-0023: Authorized workflow report snapshots

Status: implementation decision within the owner's integrated TEST continuation.

Use additive SQL 023 for bounded report snapshots and verified archive receipts.
Existing management reporting routes/definitions remain compatible. New workflow
facts are materialized once under serializable isolation, then SQL-aggregated and
paged. Snapshot owner/access version is checked before each read or export. No new
cross-owner announcement permission is implied by management access. Synthetic
data is excluded by default. No employee ranking or inferred inbox delivery.

Current backlog and period transitions have separate definitions; immutable report,
logical send, source job and distinct record are different counting units. Counts
and export derive from the same retained facts. A refresh creates a new snapshot;
it does not rewrite the previous cut or trigger any external effect. Readiness is
observed separately and labelled with its observation time.

Archive authorization precedes disk commit and is not proof of success. Record a
receipt only after verified immutable envelope commit/read; re-download repairs a
missing receipt without regenerating bytes. Historical receipt coverage remains
explicit. SQL/file operations are not a distributed transaction.

No runtime DDL or corporate migration execution. Retain additive tables on rollback;
older code cannot provide these metrics. Snapshots are short-lived for access, but
retention/deletion remains an explicit DBA policy rather than application cleanup.
