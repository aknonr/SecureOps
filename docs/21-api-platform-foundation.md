# API Platform Foundation

## Runtime Configuration

Forwarded headers are disabled by default. To enable them, set `ReverseProxy:ForwardedHeaders:Enabled=true` and supply exact `ReverseProxy:ForwardedHeaders:TrustedProxyIps` values. Invalid or empty enabled configuration stops startup. Direct clients cannot establish forwarded client IP or HTTPS state.

Swagger is disabled by default. Set `Swagger:Enabled=true` only in Development, Demo, or Test. Demo Swagger uses the explicit `X-SecureOps-Demo-Actor` header scheme and never prepopulates an actor. Outside those environments, the JSON endpoint requires the configured Admin group and no UI is enabled.

`IdentityLookup:Provider=ActiveDirectory` requires `IdentityLookup:DomainName`, an optional `IdentityLookup:Container`, and a positive timeout. The application pool is currently `ApplicationPoolIdentity`; AD and PAM access must be tested using that runtime identity, not an interactive administrator.

`PamProvider:Provider` is restricted to `Mock`. A real resolver requires an approved API/module/cmdlet, exact lookup parameter and response contract, authentication model, timeout/rate limits, and runtime identity authorization. No arbitrary PowerShell is supported.

Basic PAM-style identifiers are implemented as ordinary exact directory-account input and follow the same sAMAccountName-first path as any other account. This does not resolve the human owner of an account. Owner-resolution rules remain planned until approved directory attributes or a corporate mapping contract are available.

## Controlled Test-Server Checklist

Do not run this from local development. Under explicit approval, confirm the application runtime identity, `IdentityLookup:DomainName` and optional container, DC reachability, one approved synthetic exact lookup, NotFound behavior, timeout behavior, and TeamLead/Admin authorization. Do not use interactive administrator permissions as evidence of application access.

## Authorization Migration

Windows Integrated Authentication remains the production target. The configured AD Admin group is the bootstrap administrator path. It can approve pending database access requests and assign application roles. Demo authentication remains limited to explicit Development/Demo configuration. A later OIDC provider should emit the same approved role/capability model.

## Audit SQL

`SqlAuditWriter` inserts each queued audit event independently. A batch can therefore partially persist before a later insert fails; the queue health becomes unhealthy and later fail-closed operations stop. The reviewed SQL asset supplies `audit.AuditLog`, indexes, and UPDATE/DELETE protection, plus the security access-control schema. A DBA must execute and review it; no application migration runs automatically.
