# ADR-0017: One-Time OIDC First-Admin Bootstrap

**Status:** Accepted
**Date:** 2026-09-02

## Context

Real OIDC activation needs one initial SecureOps administrator without treating authentication as authorization or relying on Demo actors. A configuration-only switch is insufficient because leaving it enabled, or revoking the last Admin, must not make another identity eligible. The installed schema is limited to migrations 001-007.

## Decision

The server-owned `BootstrapAdmin` section is disabled by default and contains one exact login name and one exact HTTPS issuer. Enabling it requires OIDC, SQL access/session persistence, SQL fail-closed audit, automatic creation of the pending access request, and disabled Demo compatibility. Legacy `Access:BootstrapAdministrators` configuration is rejected.

Only a normalized, validated OIDC principal containing issuer, subject, stable identifier, and the configured login-name claim can enter the gate. Issuer and stable identity comparisons are ordinal case-sensitive. Login-name comparison follows the existing corporate account policy: exact ordinal case-insensitive comparison with no wildcard or fuzzy fallback. OIDC role evidence never grants SecureOps access.

The SQL bootstrap operation uses a serializable transaction and an update lock on the unique canonical Admin role row. While holding that lock it checks `security.RoleAssignments` without filtering revoked rows. If any Admin assignment has ever existed, bootstrap is permanently ineligible. Otherwise it validates the pending OIDC user/request, approves the request, inserts one Admin assignment, and inserts `FirstAdminBootstrapped`, `AccessApproved`, and `RoleAssigned` audit rows before committing. Any SQL or audit failure rolls back the transaction and returns a generic persistence failure.

The bootstrap mechanism itself requires no additional schema beyond migrations 001-007, which already preserve revoked role-assignment rows, provide the unique canonical role, enforce one active user/role assignment, preserve request history, and protect audit rows from update/delete. The later migration 008 adds only OIDC profile metadata and does not participate in bootstrap eligibility or authorization. Runtime access has no DELETE permission.

## Consequences

- OIDC authentication alone still leaves unknown users pending.
- Two concurrent eligible logins serialize and at most one Admin assignment commits.
- Revoking or disabling the only Admin does not reopen bootstrap.
- Disabling `BootstrapAdmin:Enabled` after success is required operational cleanup but is not the one-time security boundary.
- Any historical Admin assignment, including a prior synthetic Demo assignment in the same database, intentionally blocks bootstrap and must be checked before activation.
- Demo `platform-admin` remains available only through the explicit Demo authentication compatibility path.
