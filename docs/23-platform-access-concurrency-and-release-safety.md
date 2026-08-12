# Platform Access, Concurrency, and Release Safety

## Access and Session Boundary

Negotiate authenticates the current corporate principal. `ICorporatePrincipalResolver` translates authentication data into a provider-neutral identifier; `IApplicationAccessService` then resolves `Pending`, `Approved`, or `Disabled` status, roles, and capabilities. New users are pending. `GET /api/v1/access/me` is available to authenticated users so a future UI can show that state. Approval, rejection, role replacement, and disable endpoints require explicit access capabilities.

The first Admin is an exact principal supplied through `Access:BootstrapAdministrators`; its automatic approval uses a system actor and audits both approval and role assignment. An empty database without that configured principal or the explicit Demo/Test compatibility bootstrap is a lockout condition. Remove the configured bootstrap after redundant persisted Admin assignments exist. Full deployment details are in `docs/24-api-test-deployment-readiness.md`.

`SessionSecurity` records policy for the current Negotiate and future OIDC boundary: `IdleTimeoutMinutes`, `AbsoluteLifetimeHours`, `SecureCookie`, `HttpOnly`, `SameSite`, and `RevalidateAccessOnEveryRequest`. Negotiate currently has no application-issued cookie or application logout token. Logout is provider-managed and audited. Future OIDC must enforce the configured idle/absolute limits, Secure and HttpOnly cookies, an approved SameSite mode, provider logout, and access-status revalidation. Secure cookie policy must not be weakened.

OIDC authority, client ID, scopes, redirects, claims, signing, and logout are external inputs. No OIDC handler is enabled. The reserved `Access:OidcIssuerClaimType` and `Access:OidcSubjectClaimType` seam produces an opaque stable principal identifier when an approved handler supplies both claims.

## Capability Matrix

Roles are application records, not direct AD-group grants. `Admin` has all implemented capabilities. `Lead` has identity lookup, Operational Record view/create/retry/diagnostics, team view, and diagnostics. `Operator` can view records and create previews. `JiraPublisher` can view/preview/create/retry. `Auditor` can view audit and Operational Record diagnostics. `ReadOnly` can only view Operational Records.

## Reliability Controls

Jira create/retry accepts optional `Idempotency-Key`; absent keys use a deterministic actor, command, and target digest. SQL `ops.CommandExecutions` preserves completed/failed/in-progress state across restarts. A separate bounded Operational Record claim records actor and expiry. Expired claims can be recovered. An expired source-close stage can resume because the Jira key is already durable. Interrupted `CreatingJira` remains fail-closed for reconciliation because remote outcome is unknown; automatic recovery requires a future Jira idempotency contract.

Immediately before Jira create and source close, `IOperationalRecordClient.GetByIdAsync` must return an existing open record whose explicit version token, or deterministic bounded-state hash, matches the imported token. Without native source ETag/conditional update support, a small check-to-write race remains and must be resolved by the future adapter contract.

Identity exact reads use an optional bounded in-process TTL cache and single-flight provider call keyed only by normalized exact account. Exceptions are not cached. Aggregate hit/miss/provider/coalesced counters contain no account labels. Authorization is evaluated before cache access.

Named fixed-window rate policies partition by authenticated actor plus operation: identity lookup, bulk lookup, Operational Record refresh, Jira preview, Jira create, and retry. Rejection is safe RFC ProblemDetails with correlation data. Rate limiting does not replace idempotency.

## Swagger TEST Release Gate

TEST requires `ASPNETCORE_ENVIRONMENT=Test` or `Demo`, `Swagger__Enabled=true`, explicit Demo authentication settings when that compatibility path is used, and the three Swashbuckle runtime assemblies represented in `SecureOps.Api.deps.json`. Run `scripts/powershell/Test-ApiTestSwaggerReadiness.ps1` against the publish directory with explicit environment and Swagger inputs. The gate validates files, dependency manifest, and `net8.0`; it does not modify server configuration. Production Swagger UI remains disabled and JSON remains Admin-capability protected when enabled.

`scripts/release/Test-ApiReleasePayload.ps1` additionally rejects source/PDB/test/log payloads, scans text configuration for credential-like assignments, and scans all files for caller-supplied personal-path markers before packaging.

## Persistence

The application never runs SQL migrations. DBA review/execution of migrations 001-003 is required before selecting SQL access or Operational Record persistence. Runtime needs `SELECT`, `INSERT`, and `UPDATE` on the required `security`, `ops`, and `audit` tables; no DDL or DELETE permission is required.
