# ADR-0026: One-time first scope grant for Service Accounts

**Status:** Accepted by the project owner (2026-10-03) as a module setup rule. **Corporate security review and
deployment approval are not claimed.** The schema change is an unnumbered candidate (`SA-003`); Codex numbers and
applies it.
**Date:** 2026-10-03
**Decision makers:** project owner. Related: ADR-0017 (one-time OIDC first-admin bootstrap).

## Context

Service Accounts separates **capabilities** (what a user may do, from versioned role bundles) from **data scope** (which
organizations' accounts a user sees, `svcacct.ScopeGrants`). A user may never grant scope to themself; the service refuses
it and migration 025 enforces it with `CK_SaScopeGrants_NoSelfGrant (UserId <> GrantedBy)`.

On the installed system migrations 024–028 are applied, the protected Admin holds all seven module capabilities, and no
scope grant exists. With a single module administrator nobody can make the first grant, so import and reports stay
closed. Asking a colleague to grant scope to the designer of the system is a workaround, not a rule.

## Decision

- A caller with `ServiceAccounts.Administer` may grant **"All" scope to themself exactly once**, and only while the module
  has **never** had a scope grant (active or revoked). Reason is mandatory; module history records `Bootstrapped` and
  the audit log `ServiceAccount.ScopeBootstrapped`.
- After that, every grant again needs a different grantor; the bootstrap never reopens, even if the bootstrap grant or
  every other grant is revoked later (revoked rows are kept and still close it).
- Enforced in three layers:
  1. Service: Administer capability, reason, and the repository result.
  2. Repository: one serializable transaction holding the `SecureOps.ServiceAccounts.ScopeGrants.v1` application lock
     (also taken by ordinary grants, which prevents a range-lock deadlock), refuses when any grant row exists.
  3. Schema (`SA-003`): `IsBootstrap bit NOT NULL DEFAULT 0`; the self-grant check allows `UserId = GrantedBy` only for
     `IsBootstrap = 1 AND ScopeKind = 'All'`; filtered unique index `UX_SaScopeGrants_OneBootstrap` allows one such row.
- Before `SA-003` is applied the endpoint answers `bootstrapSchema` and nothing changes; ordinary grants keep working.
- Endpoints: `GET /api/v1/service-accounts/scope-grants/bootstrap` (state), `POST .../scope-grants/bootstrap`
  (`{ "reason": "..." }`), both `Administer`.
- The scope form lists approved application users (`GET .../scope-grants/candidates`, `Administer`) instead of a typed
  identity. It reads the application's own user list; no directory search, and it grants nothing by itself.

## Consequences

- A single administrator can start the module without weakening the rule afterwards.
- The first grant is self-granted by design; the audit row and history make this visible to reviewers.
- Rollback of `SA-003`: the original check can be restored only while no bootstrap row exists.

## Rejected alternatives

- **Configuration switch (like `BootstrapAdmin`)**: needs a deployment-time change and an operator to remember to turn it
  off; the database guard is simpler and cannot be left open.
- **Granting scope automatically with the Admin role**: mixes capabilities and data scope again.
- **Removing the self-grant rule**: loses the separation of duties for every later grant.
