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
- Accepts one exact account value and a non-empty purpose/context.
- Normalizes the account by configuration.
- Rejects wildcard, bulk, LDAP-filter, and search-style input.
- Resolves directly against Active Directory with read-only APIs.
- Keeps a mock PAM account resolver interface for later BeyondTrust metadata and Phase 4 correlation.
- Audits every lookup request and outcome.

## Consequences

- This delivers a small, visible operational win without waiting for Turuncuhat or BeyondTrust API approvals.
- The feature introduces personal data exposure, so the field set is deliberately limited and access is restricted.
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
