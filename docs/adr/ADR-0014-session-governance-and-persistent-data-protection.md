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
