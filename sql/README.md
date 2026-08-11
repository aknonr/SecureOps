# sql/

SQL Server schema and migration scripts. `schema/001-audit-and-access-control.sql` is a reviewed offline contract; it is not executed by the application, tests, or deployment process.

## Structure

```
sql/
├── schema/     # DDL for tables, views, triggers, stored procedures
└── migrations/ # Versioned migration scripts (V001, V002, ...)
```

## First Migrations (Phase 1)

| Version | Description |
|---|---|
| V001 | Create `dbo.Servers`, `dbo.Alerts`, `dbo.AlertEvents`, indexes |
| V002 | Create `dbo.DiagnosticJobs`, `dbo.DiagnosticResults`, indexes |
| V003 | Create `audit` schema, `audit.AuditLog`, append-only trigger |
| V004 | Create `security.Users`, `security.Roles`, `security.RoleAssignments`, `security.AccessRequests` |
| V005 | Hangfire schema (managed by Hangfire library) |

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

EF Core migrations for `dbo.*` (Phase 1 onward).

Hangfire schema is created automatically by `UseSqlServerStorage` at Worker startup; no manual migration needed.

Audit triggers and append-only enforcement live in `sql/schema/` and are applied as part of the same migration that creates the table.

## Test Harness

SQL integration tests must use an explicitly supplied `SECUREOPS_SQL_TEST_CONNECTION` environment variable and must reject empty values. No source-controlled connection string is permitted. Offline schema contract tests do not connect to SQL Server.

## Reference

- `docs/04-domain-model.md` — full schema specification with DDL.
- `docs/08-audit-model.md` — audit table specifics and triggers.
