# ADR-0029: Central API CSRF Origin Guard

**Status:** Accepted for this scoped implementation; corporate deployment review pending
**Date:** 2026-10-07

## Context

With OIDC disabled, the API uses Windows Negotiate. Browser-supplied credentials
can authenticate a cross-origin CORS-simple multipart/form POST. CORS alone does
not prevent the server from executing that request. This affects all unsafe API
methods, including Service Accounts `POST /api/v1/service-accounts/usage-scans`.
The API currently has no central CSRF check. Authentication and capability
authorization remain necessary but do not establish the request's intent.

## Options

| Option | Benefit | Limitation |
|---|---|---|
| Origin/Referer plus Sec-Fetch-Site | Rejects browser cross-site requests before form binding; exact origins work behind offload | Headers can be absent in server clients and older/privacy-restricted browsers; absence must not silently allow writes |
| Required custom request header | CORS-simple forms cannot supply it; works with server HttpClient and scripts without cookies/tokens | Depends on keeping credentialed CORS closed; the constant is not authentication and must never bypass source-origin checks |
| Signed double-submit cookie/token | Binds browser request intent to a token; avoids naive unsigned cookie injection | Adds token issuance, cookie scope and per-session state to the UI's server-to-server transport; disproportionate for the current API-only call path |

## Decision

Combine a required non-secret `X-SecureOps-Csrf: 1` header with exact source-origin
validation and Fetch Metadata checks. Protect every method except GET, HEAD and
OPTIONS, independently of route, content type, authentication provider or module.
There is no disable switch and no endpoint exception.

- Reject `Sec-Fetch-Site: cross-site`, including requests with an allowed Origin
  or the custom header. Reject duplicate, malformed and unknown metadata values.
- Require exactly one custom header with value `1` for every unsafe request.
- If Origin exists, require a single valid HTTP(S) origin in
  `ApiCsrf:AllowedOrigins`. `null`, multiple origins, wildcard, credentials, paths,
  query and fragment are rejected. Referer cannot rescue an invalid Origin.
- Otherwise validate the origin portion of Referer against the same list. Do not
  persist its path/query. Same-site is not an implicit trust grant.
- Fetch Metadata without either source header is rejected. A request with no
  Origin, Referer or Fetch Metadata is an explicit non-browser caller and still
  requires the custom header, authentication and existing authorization.
- Missing configuration means an empty browser-origin allow-list. Invalid
  configured origins stop startup. No origin is inferred from Host, scheme,
  X-Forwarded-Host or arbitrary forwarded headers.
- Check after authorization and before rate limiting, model binding or any body
  read. Return 403 ProblemDetails (`ApiCsrfRejected`, stage `csrf`, retryable false).
  Audit `ApiCsrfRejected` with authenticated actor, matched route template, method,
  bounded reason code and correlation ID. Never log body, headers, tokens, cookie,
  raw Origin/Referer, route parameter values or query string.
- If the denial audit cannot be stored, return 503 `AuditStoreUnavailable` and
  still do not execute the endpoint. Do not log an exception's secret-bearing text.

The existing authentication/access/session pipeline may register an identity,
create/touch a session, or write its security audit before this post-authorization
guard. "No write on rejection" means no endpoint/business/provider mutation or
upload processing; it excludes the required denial audit and those pre-existing
security lifecycle operations. Moving or weakening session/authorization checks
is outside this repair. Verification must state this distinction explicitly.

## UI and non-browser transport

Blazor Server handles browser events on its server and calls the API using the
shared `AddSecureOpsApiClient` helper in `src/SecureOps.Ui/Program.cs`. It does not
forward the browser's UI cookie, Origin or Sec-Fetch-Site. Demo identity, OIDC
bearer and the server-side API session jar already use the shared handler chain.
Add the custom header once in that helper in a separate minimal UI commit. Do not
add a fake Origin or change Service Accounts module code, Razor or CSS.

Worker jobs currently run Infrastructure services, rather than these HTTP routes.
Any Worker HTTP caller, operator script, CLI or future adapter must supply the
same custom header on unsafe requests. A headerless non-browser unsafe request
gets 403; GET/HEAD/OPTIONS keep their existing behavior. The header does not grant
identity, roles or capabilities. Existing test clients must adopt this contract,
not disable the guard in Test/Demo. Swagger documents it on each unsafe operation.

## IIS / F5 HTTPS offload and configuration

Origin describes the browser's initiating public origin (for example
`https://ui.example.invalid`), not the backend HTTP hop. F5/IIS must preserve Origin,
Referer and Sec-Fetch-*; stripping them cannot make a simple form pass because the
custom header is still mandatory. Configure exact public UI/API browser origins,
including non-default ports. Server-side UI calls do not need an origin entry.
Existing trusted-proxy validation and Windows authentication topology are unchanged.

Example process/deployment configuration (synthetic only):

```text
ApiCsrf__AllowedOrigins__0=https://ui.example.invalid
ApiCsrf__AllowedOrigins__1=https://api.example.invalid
```

An allow-list entry is not a CORS permission. This change enables no CORS policy.
Any future credentialed CORS change requires review of this ADR and exact origins;
wildcard/reflective credentialed CORS must not be introduced. Rollout must update
all unsafe API callers before installing the guarded API.

## Verification and remaining gates

Use pinned SDK 9.0.317 with process-local PATH/DOTNET_ROOT. Verify clean build,
repository-wide format, unit/integration suites, OpenAPI snapshot/route inventory,
cross-origin multipart rejection without business writes, allow-listed same-origin,
Referer fallback, cross-site metadata, malformed headers, headerless client denial,
authorized UI transport, safe methods and audit failure. Replay a browser form
against loopback Demo with synthetic data when available. Demo authentication is
not evidence of real browser Negotiate or IIS/F5 behavior. Corporate origin values,
header preservation and actual Negotiate/IIS acceptance remain deployment gates.

## References

- [OWASP CSRF Prevention](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html): custom headers, explicit origins, Fetch Metadata and signed double-submit tradeoffs.
- ADR-0010, ADR-0014, ADR-0016; `docs/05-security-model.md`.
