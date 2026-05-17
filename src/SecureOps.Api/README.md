# SecureOps.Api

ASP.NET Core Web API. Hosted on IIS in-process.

## Responsibilities

- Receive monitoring webhooks at `/api/v1/alerts/webhook` (HMAC-signed).
- Expose REST endpoints for the UI.
- Enforce authorization (policies in `SecureOps.Shared.Auth.Policies`).
- Validate and normalize inbound payloads (FluentValidation).
- Enqueue diagnostic jobs via Hangfire.

## Does NOT

- Execute PowerShell.
- Run long-running operations inline.
- Render HTML.
- Access SQL directly (uses Infrastructure services).

## Dependencies

- `SecureOps.Domain`
- `SecureOps.Shared`
- `SecureOps.Infrastructure`

## Phase

Populated incrementally starting Phase 1. `Program.cs` and first controller (Health) in Phase 1 Sprint 1.
