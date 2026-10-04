# Admin Service Accounts Operations

## Owner Decision

The 2026-10-03 owner decision grants all seven current Service Accounts operations
to the genuine persisted protected Admin: View, Work, Assign, Verify, Import,
Report and Administer. ADR-0022 records the bounded amendment. Other roles and
modules are unchanged; future capabilities are not implicitly included.

Explicit module scope is separate. Import requires Organization-level scope;
Team-only scope is insufficient. An Admin without scope can open administration,
but cannot inspect ungranted inventory or stage an import. Self-scope and
self-escalation are still denied. This source change is not a scope grant or
permission assignment to any corporate principal.

## Source And SQL

Base is the sealed .NET 8 source 0e85c9d072593c1808d6c6e94d40e3836c475136.
Changes are isolated on fix/admin-service-accounts-capabilities-20261003.
Claude's .NET 10 checkout and all existing packages/evidence are preserved.

- The reviewed in-memory Admin catalog explicitly lists the seven operations.
- [028 entry](../../sql/migrations/028-admin-service-account-operations.sql) includes
  [the atomic implementation](../../sql/schema/028-admin-service-account-operations.sql).
- 028 requires reviewed 026 and the protected Admin View/Administer baseline from 027.
- Only missing five operational capabilities are appended. Existing capabilities
  and all role/user/scope identities are preserved.
- The same access-administration lock serializes role changes. Role and affected
  access versions increment with mandatory same-transaction audit; failures roll back.
- A matching bundle rejects replay; it is not repaired at application startup.
- No runtime role membership, provider setting, integration or scheduler changes.

028 is reserved after public remote SQL inventories ended at 027; numbering was
rechecked before source publication. Existing release guards still reject
an unreviewed 028 packaging inventory; no packaging workflow is bypassed.

## Operator Boundary

Do not replay installed 022-027 or the existing API grant scripts. Target application
of 028 needs the separate owner execution approval and a recoverable backup. The
prior checksum backup is not described as VERIFYONLY-passed. After approved 028,
confirm seven Admin capabilities and audit/version outcome, then refresh /access/me.
The existing sealed API/UI already evaluate persisted module capabilities; 028
does not authorize mixing binaries, deployment or silently assigning scope.

To operate within a reviewed institution, an independent existing authorized
administrator grants that Organization scope through the supported module page.
If the sole Admin is also the sole pilot, this remains a genuine scope bootstrap
blocker, not a reason to invent an administrator or bypass self-grant checks.

Local proof and remaining target acceptance are recorded separately. Local verification
performed no target SQL, corporate permission assignment, provider activation,
live AD call, Worker run or package recreation. The owner separately authorized
source commit, publication and ancestry-preserving merge on 2026-10-03.

## Local Verification

- Pinned Windows SDK 9.0.317, net8.0 runtime 8.0.31: solution build passed,
  zero warnings/errors; repository-wide format verification passed.
- Unit suite: 1,676 passed. Integration suite: 341 passed, 61 skipped;
  two unchanged restricted-runtime-role tests excluded because no runtime roles
  were created in this task's isolated database.
- Fresh 001-028 installation and 001-027 to 028 upgrade passed. Nine SQL
  assertions cover audit-failure rollback, preserved bundles, version invalidation,
  no implicit scopes/runtime roles and replay rejection.
- Persisted Admin import preview passed within independently granted Organization
  scope. No-scope, outside-Organization, ordinary-user and self-grant denials passed.
- These are synthetic local API/SQL proofs, not installed-target acceptance or
  a new browser/desktop Excel acceptance claim. Target 028 execution and scope
  assignment remain separate operator actions.
