# ADR-0016: Corporate OIDC Readiness

**Status:** Accepted; activation pending
**Date:** 2026-09-01

## Context

The corporate browser endpoint will use OpenID Connect, while SecureOps persisted access must remain the only application authorization authority. The corporate IdP authority, token audience, client-authentication method, claim release, and single-logout support are deployment inputs that are not yet fully approved.

## Decision

OIDC is disabled by default and is enabled only through server-owned `Oidc` configuration. Authority (the exact expected issuer) and MetadataAddress are separate mandatory HTTPS values because corporate discovery uses a non-standard plural path. The UI uses Authorization Code flow with runtime-selected PKCE, secure correlation/nonce cookies, and the fixed local callback paths `/signin-oidc` and `/signout-callback-oidc`. Authorization challenges use a 32-byte cryptographically random Base64URL nonce through the middleware protocol validator, preserving its protected nonce cookie and exact ID-token comparison, and suppress IdentityModel client telemetry so the request contains only the approved corporate parameters (plus PKCE parameters when explicitly enabled). Corporate JSON token redemption is selected at runtime without replacing middleware state, nonce, signature, issuer, or audience validation. Access, refresh, and ID tokens remain only in the server-side browser-session store and are not persisted in the browser authentication ticket.

The API validates the bearer token against provider metadata, issuer, signature, audience, and lifetime. UI and API then discard the raw claim set and retain only bounded normalized values: provider, issuer, subject, opaque stable identifier, login name, display name, mail, uid, and optional role evidence. Tokens, authorization codes, and raw claim payloads are not application-log fields.

Stable identity is SHA-256 over exact `issuer + sub`; `loginname` is the server-side operator name used by exact Jira reporter resolution. `uygulama-role` is evidence only and is never a role claim. Authentication success enters the existing SecureOps access gate, where unknown identities are pending and receive no capabilities.

Access tokens are refreshed once per server operation when near expiry; refresh failure removes token state and requires reauthentication. Optional UserInfo retrieval validates `sub` and imports only explicitly configured reviewed claims. Logout ends the API application session, removes the server-side token/cookie jar, signs out the local UI cookie, and invokes discovery-based provider sign-out only when `Oidc:EnableRemoteSignOut=true`. Existing exact HTTPS-offload trust and persistent Data Protection requirements remain unchanged. Demo authentication remains independently configurable during the readiness period.

## Consequences

- Enabling incomplete or unsafe OIDC configuration stops startup.
- The user's LDAP/Jira password is never requested or handled.
- Client secrets, when the approved method requires one, remain runtime configuration and are not source defaults.
- Access tokens must contain the reviewed stable identity claims needed by the API, including `iss`, `sub`, and `loginname` for Jira reporter mapping.
- Corporate metadata, client authentication, access-token audience/claim release, and logout support must be confirmed before activation.
- No database migration is required.
