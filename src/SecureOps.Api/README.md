# SecureOps.Api

ASP.NET Core Web API. Hosted on IIS in-process.

Planned-announcement source/editor/preparation integration and local evidence:
`docs/contracts/planned-announcement-integration.md`. Optional mail routes are
implemented behind default-off deployment fences and persisted SelfTest/Send
capabilities; see `docs/contracts/planned-announcements-v1.md`. SMTP is Worker-only.

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

Planned announcement owned draft pagination, banner metadata, safe preview and authenticated .eml export:
`docs/contracts/planned-announcements-v1.md`. Default-off; connected owned-draft UI,
versioned templates (new drafts use oco-table-v3) and private asset bundles.
Mail preview/confirmation binds immutable preparation, actor, recipients and current
authorization; archive/download do not require sending. Source routes submit durable work,
read status/proposals and apply reviewed fields atomically; source collection runs only in Worker.
See `docs/contracts/planned-announcement-source-acceptance.md` for contracts and executed local evidence.
POST announcement preview is a bounded authenticated caller-input transformation;
it neither reads draft records nor saves/sends. Saved exports remain version-bound.

Phase 1A IdentityLookup is implemented here through `IdentityController`, validation, authorization, correlation ID middleware, rate-limit policy, and safe health endpoints. Phase 1 alert webhook and diagnostic orchestration endpoints are still planned.

Platform foundation implemented: strict configured forwarded-header trust, explicitly enabled authenticated Demo/Test Swagger, safe ProblemDetails, bounded bulk lookup, and capability bootstrap policies. Windows/AD and database access remain runtime-only validation work; no local API test contacts them.

Application-session middleware runs after authentication and before authorization. The Secure, HttpOnly cookie is an opaque protected handle only; authoritative lifecycle state is resolved through Infrastructure. Directory Explorer continuation tokens use the same persistent Data Protection key ring under a separate purpose. See `docs/28-session-governance-data-protection-and-sql-pilot.md`.

The API release gate verifies Active Directory runtime assemblies, dependency-manifest consistency, and publish-to-ZIP hashes; it does not attempt an AD lookup.

The complete TEST deployment configuration, first-admin bootstrap, migration order, runtime permissions, web.config delta, and rollback constraints are documented in `docs/24-api-test-deployment-readiness.md`. The generated OpenAPI snapshot and `docs/contracts/secureops-api-v1-ui-integration.md` are the frontend contract; UI agents must not infer routes or response models.

Operational Record endpoints live under `/api/v1/operational-records`. Controllers are thin and use capability policies; Jira preview performs no external write. External providers remain fake-only until approved contracts exist. See `docs/22-operational-record-jira-workflow.md`.
# Integrated workflow reporting continuation

Post-rc6.26 local recovery adds POST `/api/v1/in-use/{id}/draft-lifecycle`
with ExpectedVersion, Action (Reset/Discard/Restart) and Reason. InUse.Review and
the current approved actor are mandatory. This is not DELETE and never calls a
provider. Active execution rows block lifecycle changes transactionally. GET
`status=Discarded` explicitly lists removed local work; default queries exclude it.
Archived report access remains separately authorized and byte-preserving.
New report metadata separates EvidenceSheets from four corporate Sheets. See
`docs/post-rc626-repair-tr.md`; OpenAPI is updated with the source contract.

`/api/v1/reporting/management/workflows` captures bounded immutable SQL 023 cuts;
GET `{id}` and `{id}/export` recheck current approval, report capability, owner,
access version and module scope. OCO remains owner-only. POST is limited to three
captures per authenticated actor/minute. No discovery, SMTP or source mutation.
See `docs/adr/ADR-0023-workflow-report-snapshots.md`,
`docs/integrated-test-activation.md` and `docs/integrated-activation-tr.md`.
023 must precede this API; 001-022 are not replayed. Corporate completion readback
contracts remain separate blockers; a dashboard event is not remote acceptance.
