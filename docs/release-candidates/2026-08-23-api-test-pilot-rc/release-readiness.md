# SecureOps API TEST/Pilot Release Candidate Readiness

## Milestone Ancestry

All milestones are direct ancestors in one linear chain:

| Milestone | Commit |
|---|---|
| Management reporting | `0289da8457bfd4a068d09f0363a5477a67262681` |
| Enterprise Turuncu Hat/Jira adapters | `8d412676b73d97e5b2071a1998896d38faaf2683` |
| Directory Explorer Phase 1 | `f57b934df15e102f56f77264d1a4b1a3d900d831` |
| Application session governance and persistent Data Protection | `ea025f5e972f75a0abd8b5c5cb388989ab901dd7` |
| Real Jira/Turuncu Hat evidence hardening | `d87347242ed8faed9bad2b3ab02c440bf81304d9` |

## Release Gate Status

**BLOCKED FOR DEPLOYMENT.** The package may be built and reviewed, but Pilot deployment must not start until every external blocker below is closed by the responsible infrastructure owner.

1. SQL TLS certificate trust is not valid with `Encrypt=True;TrustServerCertificate=False`.
2. `MSSQLSvc` SPNs were not found for `secureops-mssql-test.thynet.thy.com:3406` or `vtmlistener36.thynet.thy.com:3406`.
3. Windows Integrated Security for `DOMAIN\WASAST_YONETIM` is therefore not proven.
4. The IIS API Application Pool currently uses `ApplicationPoolIdentity`, not `DOMAIN\WASAST_YONETIM`.
5. IIS Windows Authentication is currently disabled and Anonymous Authentication is enabled.

Do not weaken TLS, use `TrustServerCertificate=True`, add SQL credentials, or work around Windows authentication in application code.

Real Turuncu Hat/Jira activation remains separately blocked by the sanitized response samples listed in `docs/integrations/turuncu-hat-jira-contract-gaps.md`. Keep both providers disabled for the initial Pilot gate.

## Exact TEST/Pilot Deployment Order

1. Verify the API ZIP SHA256, payload manifest, release commit, and that controlled configuration/SQL/PDB/source/test/log files are absent from the runtime ZIP.
2. Close and evidence all five infrastructure blockers above. Do not continue on partial evidence.
3. Confirm an approved database backup/recovery point and complete the DBA preflight in `dba-deployment-001-007.md`.
4. Execute SQLCMD migrations 001, 002, 003, 004, 005, 006, and 007 separately, in that exact order, using the DBA migration identity.
5. Verify schemas, tables, columns, constraints, role seeds, indexes, triggers, and reporting views after each migration.
6. Confirm the Windows login/database user for `DOMAIN\WASAST_YONETIM`; review and apply the grant-only runtime script; verify no broad database-role membership.
7. Provision the server-owned Data Protection key-ring directory outside the deployment path and grant only the App Pool identity the required create/read/write access.
8. Back up the existing API payload and server-owned `web.config`/`appsettings*.json`. Preserve those files during replacement.
9. Apply the reviewed IIS identity/authentication and runtime-configuration manifest through the authorized IIS owner. Keep Demo authentication disabled.
10. Replace only the application payload, preserving relative paths. Start/recycle only in the approved change window.
11. Run authenticated read-only smoke tests: health, audit-store health, identity-provider health, access/bootstrap, current session, exact AD identity lookup, bounded Directory Explorer, and management reporting.
12. Verify append-only audit/session evidence and SQL connection encryption from controlled server telemetry. Do not enable Turuncu Hat/Jira until its separate contract gate is approved.
13. On failure, restore prior binaries/configuration. Use only the DBA-approved database recovery plan; never drop audit/history data.

## Package Policy

The runtime ZIP excludes `web.config`, `appsettings*.json`, SQL, secrets, PDBs, source, tests, logs, `bin`, and `obj`. The separate DBA ZIP contains only migrations 001-007, their matching schemas, the grant-only script, this DBA contract, and a SHA256 manifest.
