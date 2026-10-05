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
