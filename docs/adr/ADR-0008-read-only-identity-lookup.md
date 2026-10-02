# ADR-0008 — Read-Only Identity Lookup

**Status:** Accepted  
**Date:** 2026-06-18  
**Decision makers:** Project owner; production rollout subject to security review

## Context

Operators sometimes see a PAM-style account name during an incident and must manually connect to shared tooling, query AD or PAM-related data, identify the human-readable account owner, and then continue coordination. This is slow during shift work and weakens incident response evidence.

The project already uses Active Directory for Windows Authentication and RBAC. The new capability must not become a people search tool, must not modify AD or PAM, and must preserve the project's anti-surveillance framing.

## Decision

SecureOps will add **Phase 1A Identity Lookup / PAM AD User Lookup** as a backend-only helper before the read-only diagnostic MVP.

The first implementation:

- Exposes `POST /api/v1/identity/lookup`.
- Requires `TeamLeadOrAbove`.
- Accepts one exact account value and an optional bounded purpose/context. Omitted or whitespace purpose is valid for a read-only lookup and no default reason is fabricated.
- Normalizes the account by configuration.
- Rejects wildcard, bulk, LDAP-filter, and search-style input.
- Resolves directly against Active Directory with read-only APIs.
- Keeps a mock PAM account resolver interface for later BeyondTrust metadata and Phase 4 correlation.
- Audits every lookup request and outcome.
- Keeps `POST /api/v1/identity/lookup` as the only endpoint that accepts account input; no account value is accepted in URL paths or query strings.
- Fails closed when audit writing is unavailable, before provider access.
- Applies provider-level input validation and exact-match checks in addition to controller/service validation.
- Applies rate limiting to the lookup POST endpoint.
- Allows only safe metadata endpoints that do not accept account input or return personal AD data.
- Allows Development/Test file audit persistence through a bounded background queue, with files written to a configurable audit folder outside the application publish directory.
- Keeps SQL Server audit as the production target while allowing File audit as a transitional persistent store before the database is available.
- Uses `ConnectionStrings:SecureOpsDb` for the future SQL audit store.
- Distinguishes directory timeout from generic provider failure with `DirectoryProviderTimeout` and `IdentityLookupProviderTimeout`.

## Consequences

- This delivers a small, visible operational win without waiting for Turuncuhat or BeyondTrust API approvals.
- The feature introduces personal data exposure, so the field set is deliberately limited and access is restricted.
- Audit availability becomes a hard dependency for lookup, by design.
- Request threads do not perform file audit IO; they only enqueue bounded audit events.
- If a background persistent audit sink fails after accepting an event, audit health becomes unhealthy and later fail-closed lookups are blocked before provider access until audit writes recover.
- Swagger/OpenAPI is authenticated outside Development and uses placeholder-only examples.
- Real BeyondTrust/PAM API lookup remains deferred until stakeholder approval and API contract details are available.
- This does not replace Phase 4 PAM session correlation; it prepares a reusable identity-resolution hook for it.

## Rejected Alternatives

### Operator-wide access

Rejected for first release. It maximizes convenience but broadens personal data exposure too early.

### PAM-first lookup

Rejected for first release. BeyondTrust API permissions, versions, and account metadata shape are still stakeholder-dependent.

### Broad wildcard search

Rejected. It creates unnecessary directory browsing risk and conflicts with the purpose-limited incident-response workflow.

## References

- `docs/05-security-model.md`
- `docs/06-integrations.md`
- `docs/08-audit-model.md`
- `plans/PHASE-1A-identity-lookup-mvp.md`

## Amendment (2026-10-01)

ADR-0025 adds one bounded, prefix-only name search inside the Service Accounts module (3+ letters, at most 10 results,
three returned fields, `Identity.Lookup` plus module View, same rate limit, name-free audit). Exact lookup described
above is unchanged and remains the only platform-wide identity endpoint. Broad wildcard search stays rejected.
