# SecureOps.Api

ASP.NET Core Web API. Hosted on IIS in-process.

`ResourcesController` exposes `/api/v1/resources` for shared catalogue management
and caller-owned favourites/shift sets. Destination URLs are never fetched.
The canonical UI contract documents version conflicts and filtered set resolution.

## Responsibilities

- Receive monitoring webhooks at `/api/v1/alerts/webhook` (HMAC-signed).
- Expose REST endpoints for the UI.
- Enforce authorization (policies in `SecureOps.Shared.Auth.Policies`).
- Validate and normalize inbound payloads (FluentValidation).
- Enqueue diagnostic jobs via Hangfire.
- Expose Phase 1A IdentityLookup endpoints and safe metadata/health endpoints.
- Expose typed Operational Record query, Jira preview, explicit create, and retry endpoints.
- Enforce provider-neutral server-side application sessions and persistent Data Protection startup validation.

## Does NOT

- Execute PowerShell.
- Run long-running operations inline.
- Render HTML.
- Access SQL directly (uses Infrastructure services).

## Dependencies

- `SecureOps.Domain`
- `SecureOps.Shared`
- `SecureOps.Infrastructure`

## Current state

Phase 1A IdentityLookup is implemented here through `IdentityController`, validation, authorization, correlation ID middleware, rate-limit policy, and safe health endpoints. Phase 1 alert webhook and diagnostic orchestration endpoints are still planned.

Platform foundation implemented: strict configured forwarded-header trust, explicitly enabled authenticated Demo/Test Swagger, safe ProblemDetails, bounded bulk lookup, and capability bootstrap policies. Windows/AD and database access remain runtime-only validation work; no local API test contacts them.

Application-session middleware runs after authentication and before authorization. The Secure, HttpOnly cookie is an opaque protected handle only; authoritative lifecycle state is resolved through Infrastructure. Directory Explorer continuation tokens use the same persistent Data Protection key ring under a separate purpose. See `docs/28-session-governance-data-protection-and-sql-pilot.md`.

The API release gate verifies Active Directory runtime assemblies, dependency-manifest consistency, and publish-to-ZIP hashes; it does not attempt an AD lookup.

The complete TEST deployment configuration, first-admin bootstrap, migration order, runtime permissions, web.config delta, and rollback constraints are documented in `docs/24-api-test-deployment-readiness.md`. The generated OpenAPI snapshot and `docs/contracts/secureops-api-v1-ui-integration.md` are the frontend contract; UI agents must not infer routes or response models.

Operational Record endpoints live under `/api/v1/operational-records`. Controllers are thin and use capability policies; Jira preview performs no external write. External providers remain fake-only until approved contracts exist. See `docs/22-operational-record-jira-workflow.md`.
