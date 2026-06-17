# Phase 1A — Identity Lookup / PAM AD User Lookup

**Duration:** 1–2 weeks
**Goal:** Provide a backend-only, read-only lookup that resolves one exact PAM account or AD username to operational Active Directory identity fields for incident response verification.

## Critical Framing

This feature helps an authorized lead/admin answer "which approved account is this?" while handling an incident. It is **not** a people search tool and **not** a performance-monitoring feature.

Canonical wording:

> "Privileged account identity lookup for incident response verification. Process support, not personnel monitoring."

## Pre-Conditions

- [ ] Phase 0 stakeholder package acknowledges the new AD read-only data flow.
- [ ] Bilgi Guvenligi and Siber Guvenlik are informed before production use.
- [ ] A service account or app pool identity with read-only AD lookup capability is available.
- [ ] No BeyondTrust/PAM API dependency is required for first release.

## Deliverables

1. `POST /api/v1/identity/lookup`.
2. Request/response DTOs and JSON schema.
3. Config-based account normalization.
4. Read-only AD provider.
5. Mock PAM account resolver hook for later BeyondTrust metadata and Phase 4 correlation.
6. Audit entries for requested, succeeded, not-found, and failed lookups.
7. Unit tests for normalization, provider behavior, authorization metadata, and audit behavior.

## Task Breakdown

| # | Task | Estimate | Deliverable |
|---|---:|---|
| P1A-T01 | Add ADR-0008 and update roadmap/security/integration/audit docs | 4h | Docs current |
| P1A-T02 | Add lookup contracts and schema examples | 3h | Swagger/Postman payloads |
| P1A-T03 | Implement account normalizer | 3h | Exact lookup only; wildcard/bulk rejected |
| P1A-T04 | Implement identity lookup service + mock PAM resolver hook | 5h | Correlation-ready design |
| P1A-T05 | Implement read-only AD provider | 5h | AD query by exact sAMAccountName/UPN |
| P1A-T06 | Add API endpoint and authorization | 3h | TeamLead/Admin only |
| P1A-T07 | Add audit writer integration | 3h | Privileged reads audited |
| P1A-T08 | Add unit tests | 6h | Core behavior covered |

**Total Phase 1A effort:** approximately 20–30 hours.

## Security Rules

- One account value per request.
- Wildcards, comma/semicolon-separated lists, whitespace-separated lists, LDAP filters, and raw distinguished names are rejected.
- Returned fields are limited to display name, account name, UPN, mail, department, title, manager display name, enabled/locked state, and source.
- Group membership, SID, distinguished name, phone, address, password metadata, and raw LDAP attributes are not returned.
- Audit stores query metadata and matched account identifier only; it does not store returned personal detail fields.

## Exit Criteria

- [ ] Endpoint is callable from Swagger/Postman.
- [ ] TeamLead/Admin authorization is enforced.
- [ ] AD lookup uses read-only APIs only.
- [ ] Mock PAM resolver remains default until real PAM access is approved.
- [ ] All lookup outcomes are audited.
- [ ] Unit tests pass.

## Hand-off to Phase 1

Phase 1 diagnostic work starts after Phase 1A is complete or explicitly deferred by management. Identity lookup remains backend-only until Phase 2 or later UI work.
