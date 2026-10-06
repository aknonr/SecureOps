# API CSRF Request Contract

Decision: [ADR-0029](../adr/ADR-0029-api-csrf-origin-guard.md), G-35.

All API methods except GET, HEAD and OPTIONS require exactly
`X-SecureOps-Csrf: 1`. This fixed value is public request intent, not a credential.
All existing authentication, application sessions, roles, capabilities and module
scopes remain required. There is no Test/Demo exemption or disable switch.

| Caller | Unsafe request behavior |
|---|---|
| Blazor Server | Shared server HttpClient adds the header; no browser Origin/Fetch Metadata is forwarded |
| Browser with Origin | Header plus one exact `ApiCsrf:AllowedOrigins` entry required |
| Browser with Referer only | Header plus an allowed Referer origin required; path/query are not audited |
| Browser with cross-site metadata | Always 403, even with the header and an allowed Origin |
| Fetch Metadata but no source origin | 403; caller cannot claim browser trust without a configured source |
| Worker/CLI/script without source headers | Header required; otherwise 403 |
| Safe methods | No CSRF check; existing route/authentication behavior retained |

Invalid/duplicate source or intent headers fail closed. An invalid Origin is not
rescued by Referer. `same-site` is not trusted unless its source origin is explicitly
allowed. Headerless forms, including multipart, cannot execute the endpoint.
The current master usage-scan upload is
`POST /api/v1/service-accounts/accounts/{id}/usage-scans`; the global check also
covers future routes such as PR #18's standalone `/usage-scans` without module edits.

Configure the API process, using only deployment-approved exact public origins:

```text
ApiCsrf__AllowedOrigins__0=https://ui.example.invalid
ApiCsrf__AllowedOrigins__1=https://api.example.invalid:8443
```

The default list is empty. Values contain scheme, host and optional non-default
port, with no trailing slash, path, credentials, wildcard, query or fragment.
Invalid configuration stops startup without printing the rejected value.
No origin is inferred from backend HTTP, Host or forwarded host/protocol headers.
IIS/F5 must preserve the public browser source headers; no IIS/F5 change is made by
this implementation. The server-to-server UI hop needs no allow-list entry.
No CORS policy is enabled. Review any future credentialed CORS change against this
contract; an origin allow-list entry alone grants no CORS access.

Rejection returns `application/problem+json`, status 403, `code=ApiCsrfRejected`,
`stage=csrf`, `retryable=false`, and correlation/trace ID. It does not echo a header,
form field or origin. Audit action `ApiCsrfRejected` records actor and correlation
ID with `route`, `method`, `reason`, `resultStatus=Rejected`. `route` is the matched
template, not parameter values; unmatched requests use `(unmatched)`.
Reasons: `CrossSite`, `FetchMetadataInvalid`, `IntentHeaderMissingOrInvalid`,
`SourceOriginMissing`, `SourceOriginInvalid`, `SourceOriginNotAllowed`.
Audit failure returns 503 `AuditStoreUnavailable`, without endpoint execution.
The denial is not also recorded as an authorization failure.

No business, provider or upload writes occur on CSRF denial. Required rejection
audit and pre-existing identity registration/session lifecycle writes are excluded
from this statement because they occur before the post-authorization guard.

Deploy client-header updates before the guarded API. Operators must update scripts
and any HTTP adapters; Worker jobs currently call Infrastructure directly. Do not
work around 403 by disabling identity, permissions, session checks or origin checks.
The OpenAPI snapshot documents the required header on every unsafe operation.
Corporate Negotiate/IIS/F5 replay and approved public origin values remain separate
deployment gates; loopback Demo proves only local application behavior.
