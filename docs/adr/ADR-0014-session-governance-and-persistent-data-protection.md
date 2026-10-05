# ADR-0014: Server-Side Session Governance and Persistent Data Protection

**Status:** Accepted for implementation
**Date:** 2026-08-23

## Context

Negotiate currently authenticates each request and application access is revalidated against the access repository, but SecureOps has no durable application-session lifecycle. Directory Explorer continuation tokens are protected by a process-local key, so they do not survive an App Pool recycle. Pilot and Production also require explicit persistent ASP.NET Core Data Protection rather than an accidental ephemeral key ring.

## Decision

- Corporate authentication remains independent: Negotiate is the interim provider and disabled-by-default OIDC readiness is the compatible replacement path. LDAP password authentication is prohibited.
- SecureOps issues a Secure, HttpOnly, SameSite=Lax, browser-session cookie containing only a Data-Protection-protected opaque session identifier. The cookie is not an authentication or authorization authority.
- Authoritative session state is server-side and records the SecureOps user, start/last-seen/absolute-expiry/end timestamps, end reason, authentication method, and access version.
- Defaults are 30 minutes idle, 12 hours absolute lifetime, and five minutes between persisted activity updates. Activity never extends the absolute expiry.
- Disabled access, changed access version, explicit logout, administrative revocation, idle expiry, and absolute expiry invalidate effective sessions. No heartbeat is audited.
- In Pilot and Production, both API and UI Data Protection must use explicitly configured persistent key rings protected at rest. Local-machine DPAPI with separate API/UI rings is the single-node option. Certificate-protected shared rings are the multi-node option. Stable application discriminators isolate API and UI protected payloads.
- Directory continuation tokens use a dedicated Data Protection purpose and retain operation, target hash, offset, and expiry binding. LDAP cookies and directory identities are never placed in the token.
- Schema changes remain DBA-owned. Runtime receives only required `SELECT`, `INSERT`, and `UPDATE`; it receives no `DELETE`, DDL, or schema ownership.

## Consequences

An App Pool recycle does not invalidate application-session cookies or continuation tokens when the same key ring is available. Negotiate behind a reverse proxy still requires a supported end-to-end authentication topology; application-session state cannot repair a load balancer that terminates or fails to pass Windows authentication.

## Amendment 1 - Session v2 revocation, 2026-10-05

Implements the owner's 2026-10-02 decision in `docs/decisions-log.md`.

- A revoked browser authentication session must end. The UI clears its server-held API
  cookies and OIDC tokens, signals its existing circuit navigation, and rejects and deletes
  the UI authentication cookie on the next HTTP request, for both interim and OIDC sign-in.
- A terminal correlation marker remains in the UI process store after credential removal.
  Requests from another tab or a replayed old UI cookie cannot recreate an API session;
  late responses cannot restore cookies or tokens. Explicit sign-in creates a new correlation.
- API validation retains invalid and terminal handles rather than deleting them and making
  a later authenticated request appear to be a first visit. Self-revocation does the same.
  The safe response header `X-SecureOps-Session-Reauthentication: required` carries no handle
  or identity and also signals successful self-revocation. UI transport additionally recognizes
  `SessionRevoked` and `SessionExpired` ProblemDetails and older API deletion signals.
- Store/audit unavailability is retryable and retains the existing API handle; it does not
  itself mean administrative revocation. Explicit logout retains its documented handle deletion.
- Revocation affects the exact application session, not the person's account, provider session,
  or separate browser. Access is still decided by the API. An idle browser observes revocation
  on its next API operation; no polling, provider logout or session-policy redesign is introduced.

The UI marker and tokens remain process-local. Persistent multi-node/recycle correlation and
actual Windows/IIS/provider/browser behavior require separate validation and owner decisions.

## Amendment 2 - Missing UI process state fails closed, 2026-10-05

Owner-approved F2 policy: losing the UI process-store entry requires explicit sign-in, not
silent continuation. There is no durable SQL browser correlation in this change.

- Only successful explicit interim/OIDC sign-in initializes a fresh active browser correlation.
  Cookie renewal and validation never initialize one. A missing correlation claim is rejected.
- A missing store lookup creates a terminal marker, rejects/deletes the existing UI cookie,
  and blocks outbound API transport from an existing circuit. An existing terminal entry cannot
  be reactivated by initialization; explicit sign-in uses a new correlation.
- Cache eviction invalidates credential material without disposing gates that in-flight requests
  may still hold. Existing Session v2 terminal-handle and credential-clearing behavior remains.
- Hosted cookie replay and API transport tests simulate cache loss and a fresh empty store;
  neither allows an old correlation to start another API session. These are deterministic hosted
  tests, not IIS recycle or browser acceptance.

F1 atomic session termination/audit follows in Amendment 3. F3 concurrent termination
results and F4 already-running validation boundaries remain open and outside this change.

## Amendment 3 - Atomic session termination and audit, 2026-10-05

Owner-approved F1 policy: logout, administrative revoke, idle/absolute expiry and terminal
bulk operations commit their SQL session changes and required append-only audit together.
The owner's 2026-10-06 correction permits audit-first sequencing for non-durable InMemory
sessions with non-atomic writers, as specified below; SQL transaction guarantees are unchanged.

- The SQL repository owns the transaction and directly inserts into the existing `audit.AuditLog`
  on the same connection/transaction as the session update. Queued audit cannot enlist and is
  not used for these terminal events. No schema, grant, audit update/delete or migration changes.
- SQL expiry sweeps and access-disable/change batches commit all affected state and terminal events
  together. Any update/audit failure rolls back the batch; the caller receives an unavailable
  result. Caller cancellation propagates and an uncommitted transaction is disposed/rolled back.
- InMemory with `IAtomicAuditWriter` prepares replacement state under its repository gate before
  publishing an atomic audit batch, then publishes state without another failing/cancellable
  operation. The local audit writer prepares all events and reserves capacity before appending
  them as one batch.
  This is process-local test behavior, not durable crash-recovery evidence.
- InMemory with non-atomic File/Queued writers prepares replacement state under the same gate,
  awaits each audit write first, and publishes session termination only after all writes succeed.
  Audit failure throws `AuditWriteUnavailableException`, returns `AuditStoreUnavailable` (503),
  and leaves all affected sessions Active. Writer-observed cancellation propagates without
  publishing state; after successful audit no new cancellable operation precedes publication.
  This supports the shipped File default without changing configuration or writer behavior.
  Rationale: InMemory sessions are a non-durable local store; they cannot share a persistent
  transaction with File/Queued audit. A later write failure can leave an append-only audit prefix
  while every session stays Active; retries can append duplicates. Queue success means accepted
  enqueue, not confirmed durable sink persistence. Neither pairing promises durable crash recovery
  or SQL-style batch rollback. SQL sessions retain genuine same-transaction state/audit atomicity.
- Audit failures are translated to safe unavailable responses; internal exceptions are retained
  but their payloads, identity and handles are not logged by the new terminal-audit error paths.
- The repository still returns whether an active row actually transitioned. Service response
  policy and requested audit on a losing single-session transition are unchanged: F3 remains open.
  Touch/validation and in-flight revocation boundaries are unchanged: F4 remains open.
- Initial session creation and its existing failed-start audit compensation are outside this F1
  termination change. The administrative list-view attempt audit remains separate from terminal
  batch audit. Explicit UI/logout cookie cleanup and API terminal-handle semantics are unchanged.

Evidence includes unit failure/success/cancellation tests, hosted API 503 behavior, and actual
isolated LocalDB update/audit failure triggers, second-insert rollback and transaction cancellation.
The full guarded Resource SQL harness also proves the existing append-only restrictions remain.
