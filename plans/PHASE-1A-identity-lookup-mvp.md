# Phase 1A — Identity Lookup / PAM AD User Lookup

**Duration:** 1–2 weeks
**Goal:** Provide a backend-only, read-only lookup that resolves one exact PAM account or AD username to operational Active Directory identity fields for incident response verification.

**Current status:** Backend implementation and hardening are complete in code, with unit/integration coverage. Real AD smoke testing with an approved read-only account remains pending.

The bounded read-only Directory Explorer extension is defined by ADR-0013 and `docs/27-read-only-directory-explorer.md`. It remains exact-input, purpose-bound, capability-protected, non-recursive, and read-only.

Session governance and persistent Data Protection for this backend surface are defined by ADR-0014 and `docs/28-session-governance-data-protection-and-sql-pilot.md`; they do not broaden exact-account or Directory Explorer query behavior.

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
6. Audit entries for requested, succeeded, not-found, rejected, failed, and forbidden lookup attempts.
7. Rate limiting on `POST /api/v1/identity/lookup`.
8. Safe metadata endpoints: `GET /api/v1/identity/me`, `GET /api/v1/identity/lookup/capabilities`, `GET /api/v1/health/audit-store`, and `GET /api/v1/health/identity-provider`.
9. Unit tests for normalization, provider behavior, authorization metadata, validation audit, rate-limit metadata, and audit behavior.

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
| P1A-T09 | Production hardening pass | 4h | Swagger auth, fail-closed audit, rate limit, provider guard |

**Total Phase 1A effort:** approximately 20–30 hours.

## Security Rules

- One account value per request.
- Wildcards, comma/semicolon-separated lists, whitespace-separated lists, LDAP filters, and raw distinguished names are rejected.
- Returned fields are limited to display name, account name, UPN, mail, department, title, manager display name, enabled/locked state, and source.
- Group membership, SID, distinguished name, phone, address, password metadata, and raw LDAP attributes are not returned.
- Audit stores query metadata and matched account identifier only; it does not store returned personal detail fields.
- `POST /api/v1/identity/lookup` is the only endpoint that accepts an account value. Account values are never accepted in URL paths or query strings.
- If audit writing is unavailable, lookup fails closed before AD/PAM provider access.
- Provider implementations repeat max-length and exact-input validation even after controller/service validation.
- User-facing failure messages are generic; detailed exception data stays in internal logs with correlation ID.
- Development/Test audit persistence uses a configurable audit directory such as `D:\SecureOps\Audit`; audit files must not be written under the application publish directory.
- Technical application logs and audit logs are separate. Technical logs diagnose the application; audit logs are append-only operational evidence.
- Production must not use `InMemory` audit. Production must run fail-closed and should move to SQL Server audit when the database is available.
- Persistent audit writes use a bounded queue. If a background file/SQL sink fails after accepting an event, audit-store health becomes `Unhealthy`; with fail-closed enabled, later lookups stop before AD provider access until audit writes recover.

## API Surface and Future UI Calls

The future UI calls these Phase 1A endpoints:

| Method | Endpoint | UI use |
|---|---|---|
| POST | `/api/v1/identity/lookup` | TeamLead/Admin lookup form submit |
| GET | `/api/v1/identity/me` | Show current caller capability |
| GET | `/api/v1/identity/lookup/capabilities` | Render validation limits, allowed fields, and effective provider behavior |
| GET | `/api/v1/health/audit-store` | Admin/system health page |
| GET | `/api/v1/health/identity-provider` | Admin/system health page |

`POST /api/v1/identity/lookup` is the only endpoint that accepts account input.

## Error Codes

| errorCode | HTTP | Meaning |
|---|---:|---|
| `InvalidRequestBody` | 400 | Request body is missing or malformed |
| `PurposeRequired` | 400 | Purpose/context is missing or invalid |
| `InvalidIdentityLookupRequest` | 400 | A non-account/non-purpose request field failed validation |
| `EmptyAccount` | 400 | Account is missing after normalization |
| `AccountTooLong` | 400 | Account exceeds configured max length |
| `BulkLookupRejected` | 400 | Input contains bulk separators or multiple accounts |
| `SearchPatternRejected` | 400 | Input contains wildcard, LDAP filter, DN, or search-style characters |
| `AccountPatternRejected` | 400 | Input is outside the configured allow-list |
| `RateLimitExceeded` | 429 | Per-user endpoint rate limit exceeded |
| `AuditUnavailable` | 503 | Audit queue/store cannot accept the required audit event |
| `DirectoryProviderTimeout` | 503 | Identity provider exceeded configured timeout |
| `ProviderUnavailable` | 503 | Identity provider failed |

## Production Hardening Checklist

- [x] Swagger/OpenAPI declares Windows Integrated Authentication.
- [x] Swagger is available without auth only in Development; non-development Swagger routes require authentication.
- [x] Non-development API endpoints require authentication by fallback policy unless explicitly overridden.
- [x] `POST /api/v1/identity/lookup` is protected by `TeamLeadOrAbove`.
- [x] Operator-only and Auditor-only users are denied by policy tests.
- [x] Validation failures write `IdentityLookupRejected` before the service is called.
- [x] Authorization denials for lookup write `IdentityLookupForbidden`.
- [x] Lookup request audit failure prevents resolver/AD provider access.
- [x] Successful lookup response is suppressed if success audit cannot be written.
- [x] Rate limiting is applied to the lookup POST endpoint.
- [x] Rate limiting partitions by authenticated user + endpoint, not by IP only.
- [x] `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Correlation-ID` are supported.
- [x] File audit uses a bounded queue and background worker for persistent stores.
- [x] Background audit sink failure marks audit health unhealthy and fail-closes later lookup attempts.
- [x] Swagger/Postman examples use placeholders only, not real account names or personal data.
- [ ] Real AD smoke test completed with a read-only test account in the production-like test environment.

## Test/UAT Checklist

- Configure a test IIS site with a dedicated app pool identity or approved service account.
- Keep the application publish folder read/execute only; do not write audit files under the publish directory.
- Configure `Audit:Provider=File`, `Audit:FailClosed=true`, and `Audit:File:Directory` to a dedicated folder such as `D:\SecureOps\Audit`.
- Grant the app pool identity write/create/append/read on the audit folder and technical log folder only.
- Confirm non-development Swagger requires authentication.
- Confirm TeamLead/Admin users can call `POST /api/v1/identity/lookup`.
- Confirm Operator-only and Auditor-only users cannot call lookup.
- Confirm invalid wildcard, bulk, LDAP-filter-like, raw DN, too-long, and empty-purpose inputs return 400 and are audited as `IdentityLookupRejected`.
- Confirm audit store outage returns `AuditUnavailable` before AD access.
- Confirm AD timeout returns `DirectoryProviderTimeout` and is audited as `IdentityLookupProviderTimeout`.
- Confirm health endpoints do not disclose connection strings, file paths, real accounts, or personal details.
- Confirm audit records do not store returned personal detail fields.
- Complete one real AD smoke test with an approved read-only account before production readiness is claimed.

## Swagger Security Model

Swagger is a development and operator-test aid, not a public surface. In Development, Swagger UI can be opened locally for fast iteration. In non-development environments, the Swagger JSON endpoint is mapped with authorization, and the API declares the `WindowsAuth` security scheme using Negotiate/Windows Integrated Authentication.

Swagger examples must use placeholders such as `sample-admin` and `EVT-00000 incident response verification`. Real PAM account values, real personal names, real email addresses, and production incident IDs must not be embedded in OpenAPI examples or screenshots.

## Exit Criteria

- [ ] Endpoint is callable from Swagger/Postman.
- [ ] TeamLead/Admin authorization is enforced.
- [ ] AD lookup uses read-only APIs only.
- [ ] Mock PAM resolver remains default until real PAM access is approved.
- [ ] All lookup outcomes are audited.
- [ ] Audit fail-closed behavior is verified.
- [ ] Lookup endpoint rate limiting is verified.
- [ ] Unit tests pass.

## Hand-off to Phase 1

Phase 1 diagnostic work starts after Phase 1A is complete or explicitly deferred by management. Identity lookup remains backend-only until Phase 2 or later UI work.
