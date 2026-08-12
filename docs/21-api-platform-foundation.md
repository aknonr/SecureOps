# API Platform Foundation

## Runtime Configuration

Forwarded headers are disabled by default. To enable them, set `ReverseProxy:ForwardedHeaders:Enabled=true` and supply exact `ReverseProxy:ForwardedHeaders:TrustedProxyIps` values. Invalid or empty enabled configuration stops startup. Direct clients cannot establish forwarded client IP or HTTPS state.

Swagger is disabled by default. Set `Swagger:Enabled=true` only in Development, Demo, or Test. In these browser-test environments the JSON is anonymous so Swagger UI can load its definition; every API operation remains protected and Demo Swagger uses the explicit `X-SecureOps-Demo-Actor` header scheme without a prepopulated actor. Outside those environments, the JSON endpoint requires the Admin application capability and no UI is enabled.

Exact Demo/Test Swagger keys: `Swagger:Enabled`, `DemoAuth:Enabled`, `Access:DemoCompatibilityEnabled`, and optional `DemoAuth:HeaderName`. Demo compatibility requires both explicit flags and is restricted to allowed non-production environments. The default header name is `X-SecureOps-Demo-Actor`.

`IdentityLookup:Provider=ActiveDirectory` requires `IdentityLookup:DomainName`, an optional `IdentityLookup:Container`, and a positive timeout. The application pool is currently `ApplicationPoolIdentity`; AD and PAM access must be tested using that runtime identity, not an interactive administrator.

Exact AD runtime keys: mandatory `IdentityLookup:Provider=ActiveDirectory` and `IdentityLookup:DomainName`; optional `IdentityLookup:Container` and `IdentityLookup:EnableUpnLookup`; bounded settings `IdentityLookup:ProviderTimeoutSeconds`, `IdentityLookup:BulkMaxAccounts`, `IdentityLookup:Cache:Enabled`, `IdentityLookup:Cache:TtlSeconds`, and `IdentityLookup:Cache:MaxEntries`.

`PamProvider:Provider` is restricted to `Mock`. A real resolver requires an approved API/module/cmdlet, exact lookup parameter and response contract, authentication model, timeout/rate limits, and runtime identity authorization. No arbitrary PowerShell is supported.

Basic PAM-style identifiers are implemented as ordinary exact directory-account input and follow the same sAMAccountName-first path as any other account. This does not resolve the human owner of an account. Owner-resolution rules remain planned until approved directory attributes or a corporate mapping contract are available.

## Controlled Test-Server Checklist

Do not run this from local development. Under explicit approval, confirm the application runtime identity, `IdentityLookup:DomainName` and optional container, DC reachability, one approved synthetic exact lookup, NotFound behavior, timeout behavior, and TeamLead/Admin authorization. Do not use interactive administrator permissions as evidence of application access.

## Authorization Migration

Windows Integrated Authentication remains the production target. Authentication is translated to a corporate principal, then persisted application status, roles, and capabilities determine access. Exact `Access:BootstrapAdministrators` values can initialize an Admin; ordinary first-seen users remain pending. Demo authentication remains an explicit Demo/Test compatibility path. Future OIDC supplies only the principal and session handler; approval and capability authorization remain unchanged. See ADR-0010 and `docs/23-platform-access-concurrency-and-release-safety.md`.

Operation limits use `RateLimiting:{IdentityLookup|BulkIdentityLookup|OperationalRecordRefresh|JiraPreview|JiraCreate|WorkflowRetry}:PermitLimit` and `WindowSeconds`. Session policy uses `SessionSecurity:*`; command leases use `CommandIdempotency:*`.

TEST release validation must run `scripts/powershell/Test-ApiTestSwaggerReadiness.ps1` against publish output with explicit expected environment and `SwaggerEnabled=true`. Server-owned `web.config` or environment variables remain deployment inputs and are not inferred from the artifact.

## Audit SQL

`SqlAuditWriter` inserts each queued audit event independently. A batch can therefore partially persist before a later insert fails; the queue health becomes unhealthy and later fail-closed operations stop. The reviewed SQL asset supplies `audit.AuditLog`, indexes, and UPDATE/DELETE protection, plus the security access-control schema. A DBA must execute and review it; no application migration runs automatically.
