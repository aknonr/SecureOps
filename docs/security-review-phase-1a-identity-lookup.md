# Phase 1A Identity Lookup Security Review Note

## Scope

Phase 1A adds a backend-only privileged account identity lookup for incident response verification. It is not a people search feature and not an operator performance feature.

## API Surface

- `POST /api/v1/identity/lookup` is the only endpoint that accepts account input.
- `GET /api/v1/identity/me` returns only current authentication status and whether the caller can use lookup.
- `GET /api/v1/identity/lookup/capabilities` returns only safe capability metadata.
- `GET /api/v1/health/audit-store` returns safe audit-store status without paths or connection strings.
- `GET /api/v1/health/identity-provider` returns provider configuration status only; it does not query AD.
- No `GET` lookup by account exists because account values must not appear in URLs, browser history, proxy logs, or IIS access logs.

## Security Decisions

- Authorization: lookup requires `TeamLeadOrAbove`; Operator-only and Auditor-only users are denied.
- Authentication: non-development API and Swagger routes require authentication by fallback policy.
- Swagger: OpenAPI declares Windows Integrated Authentication / Negotiate. Examples must use placeholders only.
- Audit: lookup writes `IdentityLookupRequested`, `IdentityLookupSucceeded`, `IdentityLookupNotFound`, `IdentityLookupRejected`, `IdentityLookupFailed`, `IdentityLookupProviderTimeout`, and `IdentityLookupForbidden`.
- Fail closed: if audit writing is unavailable, lookup does not query AD/PAM and does not return personal details.
- Input safety: controller validation, service normalization, and provider-level guards all reject wildcard, LDAP-filter, bulk, too-long, and outside-allow-list input.
- Provider behavior: AD lookup is exact-match only by `sAMAccountName`; exact UPN fallback is allowed only for UPN-shaped input.
- Data minimization: response contains only display name, account name, UPN, mail, department, title, manager display name, enabled state, locked state, and source.
- Audit minimization: audit stores normalized/matched account identifiers, purpose/context, source IP, correlation ID, and status; it does not store returned personal detail fields.
- File audit: Development/Test can use `Audit.Provider=File`, writing JSONL to a configurable directory such as `D:\SecureOps\Audit`. The audit directory must not be inside the publish folder.
- Queueing: persistent audit providers use a bounded in-memory queue. Request threads enqueue only; file IO runs in a background worker. Queue full + fail-closed prevents AD/PAM provider access.
- Rate limiting: `POST /api/v1/identity/lookup` uses the `IdentityLookup` rate-limit policy partitioned by authenticated user + endpoint, not IP only.
- Forwarded headers: `X-Forwarded-For` and `X-Forwarded-Proto` are supported for load balancer deployments. `X-Correlation-ID` is accepted when it matches the safe format.

## Audit Storage Behavior

| Environment | Allowed provider | Required behavior |
|---|---|---|
| Development | `InMemory` or `File` | SQL Server is not required; file audit writes to configured audit folder |
| Test/UAT | `File` or `SqlServer` | Persistent audit preferred; file audit must be outside publish folder |
| Production | `File` transitional or `SqlServer` target | `InMemory` forbidden; `Audit.FailClosed=true`; SQL uses `ConnectionStrings:SecureOpsDb` |

Technical application logs and audit logs are different stores. Technical logs are for troubleshooting. Audit logs are operational evidence and must record who queried which normalized account, for what purpose, with what result, without storing returned personal detail fields.

If a persistent audit event is accepted into the bounded queue but the background file/SQL sink later fails, the audit-store health endpoint reports `Unhealthy` with `lastErrorCode=AuditSinkUnavailable`. With `Audit.FailClosed=true`, subsequent identity lookups stop before AD provider access until audit writes recover.

## App Pool Permissions

The IIS app pool identity should have:
- Application publish folder: read and execute only; no audit or technical log writes to the publish folder.
- Audit folder, for example `D:\SecureOps\Audit`: create files, write, append, and read as required for audit operations.
- Technical log folder, for example `D:\SecureOps\Logs`: create files, write, append, and read as required for application logs.
- No Delete permission in Phase 1A unless a later approved retention job requires it for a separate identity.
- No broad local admin, Domain Admin, or unrelated file-system grant from this feature.

## Load Balancer and Windows Authentication Notes

- If TLS terminates at a load balancer, forward `X-Forwarded-Proto=https` and restrict trusted proxy sources at the hosting/network layer.
- Forward `X-Forwarded-For` so audit `sourceIp` reflects the caller chain instead of only the load balancer.
- Windows Authentication behind a load balancer requires SPN/Kerberos planning. NTLM may require connection affinity depending on the proxy path.
- Sticky sessions are not required by IdentityLookup itself because the endpoint is stateless, but may be required by Windows Auth/NTLM or later Blazor Server UI hosting.

## Error Taxonomy

| errorCode | HTTP | Safe message class |
|---|---:|---|
| `InvalidRequestBody` | 400 | Body missing/malformed |
| `PurposeRequired` | 400 | Purpose/context required |
| `InvalidIdentityLookupRequest` | 400 | Other request validation failure |
| `EmptyAccount` | 400 | Account missing |
| `AccountTooLong` | 400 | Account too long |
| `BulkLookupRejected` | 400 | Bulk input rejected |
| `SearchPatternRejected` | 400 | Wildcard/LDAP/DN/search input rejected |
| `AccountPatternRejected` | 400 | Outside allow-list |
| `RateLimitExceeded` | 429 | User endpoint rate limit exceeded |
| `AuditUnavailable` | 503 | Audit cannot accept the required event |
| `DirectoryProviderTimeout` | 503 | AD provider exceeded configured timeout |
| `ProviderUnavailable` | 503 | AD provider failed |

## Framework and NuGet Security Note

The solution targets `net8.0` with central package management in `Directory.Packages.props` and SDK roll-forward controlled by `global.json`. Package versions must be checked with `dotnet list SecureOps.sln package --outdated` and `dotnet list SecureOps.sln package --vulnerable` during hardening reviews. Do not introduce packages just for Phase 1A audit persistence; the file queue uses built-in .NET channels and hosted services.

Current hardening status: .NET 8-compatible patch-level package updates have been applied where available. `dotnet list SecureOps.sln package --outdated --highest-patch` reports no remaining patch-level updates, and `dotnet list SecureOps.sln package --vulnerable` reports no vulnerable packages. Remaining `--outdated` entries are major/minor upgrade decisions such as .NET 10 package lines, newer Serilog major versions, MudBlazor major upgrades, and newer test framework majors; these should be reviewed as a separate dependency upgrade batch so the `net8.0` target and `global.json` intent are not changed accidentally.

## Production Checklist

- [x] Authenticated Swagger in non-development.
- [x] Placeholder-only Swagger/Postman examples.
- [x] TeamLead/Admin authorization path tested.
- [x] Operator-only and Auditor-only denial tested.
- [x] Validation failure audit tested.
- [x] Audit fail-closed before provider access tested.
- [x] Provider-level input guard tested.
- [x] Returned response field set tested.
- [x] Audit personal-detail minimization tested.
- [x] File audit sink writes to configured directory.
- [x] Queue full fail-closed behavior tested.
- [x] Authenticated user + endpoint rate partition tested.
- [x] Correlation ID middleware tested.
- [x] Package outdated/vulnerable checks reviewed and recorded.
- [ ] Real AD smoke test with a read-only test account in the test environment.
- [ ] Security team confirms AD read-only account/app-pool permission boundary.

## Test/UAT Checklist

- Deploy API to a test IIS site using a dedicated app pool identity.
- Confirm application publish folder has read/execute only for the app pool identity.
- Configure `Audit:Provider=File`, `Audit:FailClosed=true`, and `Audit:File:Directory` outside the publish folder, for example `D:\SecureOps\Audit`.
- Confirm the app pool identity can create and append audit files in the audit folder and technical logs in the technical log folder.
- Confirm Swagger requires authentication in the non-development test environment.
- Confirm TeamLead/Admin can call `POST /api/v1/identity/lookup` from Swagger/Postman with fake or approved test accounts only.
- Confirm Operator-only and Auditor-only users receive 403 and `IdentityLookupForbidden` is audited.
- Confirm invalid wildcard, bulk, LDAP-filter-like, raw DN, too-long, and empty-purpose requests return 400 and write `IdentityLookupRejected`.
- Confirm audit queue/sink failure causes `AuditUnavailable` before AD access.
- Confirm directory timeout returns `DirectoryProviderTimeout` and writes `IdentityLookupProviderTimeout`.
- Confirm `GET /api/v1/health/audit-store` and `GET /api/v1/health/identity-provider` do not expose paths, connection strings, real accounts, or personal details.
- Confirm returned identity fields are limited to the approved response field set.
- Confirm audit JSONL records do not contain display name, mail, department, title, manager display name, group membership, SID, DN, phone, address, password metadata, or raw LDAP attributes.

## Remaining Risks

- Real Active Directory behavior still needs a smoke test with an approved read-only account.
- BeyondTrust/PAM metadata integration remains mocked until stakeholder approval and API contract details are available.
- Rate-limit values are conservative defaults and should be tuned after pilot usage.
