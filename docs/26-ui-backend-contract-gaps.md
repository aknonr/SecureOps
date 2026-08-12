# 26 — UI Backend Contract Gaps

Findings raised by the UI layer against `docs/contracts/secureops-api-v1-ui-integration.md` and the
API as implemented on `release/api-test-20260812`. The UI made no backend change for any of these.

Each item states what the UI needs, what exists today, and what the UI does in the meantime.

---

## G-1 — Mock identity directory resolves to an empty user set (defect)

**Severity:** High. Identity lookup can never return a result in Development, Demo, or Test.

`SecureOps.Infrastructure/DependencyInjection.cs:54` registers:

```csharp
services.AddSingleton<IIdentityDirectoryProvider, MockIdentityDirectoryProvider>();
```

`MockIdentityDirectoryProvider` has three public constructors. .NET's activator selects the greediest
one it can satisfy, which is:

```csharp
MockIdentityDirectoryProvider(IEnumerable<DirectoryUserRecord> users, IOptions<IdentityLookupOptions> options)
```

`IEnumerable<T>` always resolves in Microsoft DI — to an **empty** sequence when no `DirectoryUserRecord`
is registered. The seeded `DefaultUsers()` overload is therefore never used, and `_users` is empty.

**Observed:** `POST /api/v1/identity/lookup` with `{"account":"pam12356"}` against the Demo host
returns `404 IdentityNotFound`, although `pam12356` ("Example Admin") is the documented default mock
user.

**Suggested fix (backend):** register the provider with an explicit factory, e.g.
`services.AddSingleton<IIdentityDirectoryProvider>(sp => new MockIdentityDirectoryProvider(sp.GetRequiredService<IOptions<IdentityLookupOptions>>()));`
or register the default `DirectoryUserRecord` set so the greedy constructor receives it.

**UI meanwhile:** nothing to do — the UI renders the 404 correctly as a not-found state with guidance.
The found-state rendering was verified against a local stub returning the documented contract shape.

---

## G-2 — No directory profile for the signed-in user

**UI need:** `/account` was asked to show display name, e-mail, department, and title.

**Today:** `GET /api/v1/identity/me` returns only `(Name, IsAuthenticated, CanLookupIdentity)`.
`GET /api/v1/access/me` adds status, roles, capabilities, auth source, and session policy. Neither
returns directory attributes for the caller.

Using `POST /api/v1/identity/lookup` for this would be wrong: it is a privileged, audited,
rate-limited lookup requiring `Identity.Lookup`, and a user viewing their own profile should not
consume it or need that capability.

**Suggested:** extend `CurrentIdentityResponse` with `DisplayName`, `Mail`, `Department`, and `Title`
(nullable, populated from the authenticated principal or a self-scoped directory read), or add
`GET /api/v1/identity/me/profile`.

**UI meanwhile:** `/account` shows a clearly labelled "Dizin profiliniz henüz kullanılamıyor" panel
rather than four blank fields. `DisplayName` falls back to the account name; no placeholder person is
invented.

---

## G-3 — Lookup capabilities do not expose the account pattern

**UI need:** mirror server validation exactly, so the form is never stricter than the endpoint.

**Today:** `IdentityLookupCapabilitiesResponse` exposes `MaxAccountLength`, `SupportsUpnLookup`,
`ReturnedFields`, `RejectedInputClasses`, and `RateLimitPolicy` — but not
`IdentityLookup:AllowedAccountPattern`, nor the forbidden-character set, nor `StripDomainPrefix`.

**Suggested:** add `AllowedAccountPattern` and `StripDomainPrefix` to the capabilities response.

**UI meanwhile:** `AccountInputRules` mirrors the committed defaults (`^[a-zA-Z0-9._@-]+$`, the
wildcard/LDAP character set, domain-prefix stripping) as UI constants, documented as a mirror. It
fails **open**: if the server pattern is ever narrowed, the server rejects and the UI shows the
server's message. Length and UPN support are read live from capabilities.

---

## G-4 — Session expiry is not distinguishable from "never signed in"

**UI need:** show `/session-expired` when a session lapses mid-task, and `/login` when the user was
never authenticated. The two need different copy, and expiry must preserve the return path.

**Today:** cookie middleware redirects both to `LoginPath`. The UI can only tell them apart when an
API call returns `401` inside an established circuit.

**UI meanwhile:** a `401` from any API call sets `UiProblem.RequiresSignIn`, and the page navigates to
`/session-expired?returnUrl=…`. A cookie that expires between requests still lands on `/login`.
Acceptable now; worth revisiting with OIDC, where the provider's `exp` is known.

---

## G-5 — Demo access bootstrap requires an undocumented setting

Running the UI against the Demo API needs **`Access__DemoCompatibilityEnabled=true`** in addition to
`DemoAuth__Enabled=true`. Without it the demo actor is created as `Pending`, every capability check
denies, and the UI correctly shows the "awaiting approval" state — which looks like a broken demo.

`src/SecureOps.Api/Properties/launchSettings.json` (`SecureOps.Api (Demo)`) sets `DemoAuth__Enabled`
and `Audit__Provider` but not `Access__DemoCompatibilityEnabled`.

**Suggested (backend-owned):** add it to the Demo launch profile, or document it in the API README.
Not changed here — the UI does not own API launch configuration.

---

## G-6 — No access-request creation endpoint

**UI need:** an explicit "request access" action for a `Pending` or unprovisioned user.

**Today:** `GET /api/v1/access/me` auto-provisions and creates a pending request as a side effect
(`AccessOptions.AutoCreateRequest`, default `true`). There is no explicit create endpoint, and the
UI-integration contract lists none.

This works, but the UI cannot offer "request access" as a deliberate action, nor let the user supply
a justification for the approver.

**Suggested:** confirm the implicit flow is intended, or add
`POST /api/v1/access/requests` accepting a reason.

**UI meanwhile:** the pending state explains that the request is already recorded and shows the
pending request ID for the operator to quote.

---

## Confirmed working as documented

Verified live against the Demo API during this milestone:

- `GET /api/v1/access/me` — shape, `AccessStatus`, roles, capabilities, and session policy match the
  contract exactly.
- ProblemDetails extensions `code`, `correlationId`, `traceId`, `stage`, and `retryable` are present
  on `401`, `404`, and `503` responses.
- `POST /api/v1/identity/lookup` reports not-found as ProblemDetails `IdentityNotFound` (404), **not**
  as a response body. The earlier UI client treated 404 as a success body; that was corrected.
- Role codes `Admin`, `Lead`, `Operator`, `JiraPublisher`, `Auditor`, `ReadOnly` and the capability
  identifiers in `SecureOps.Shared.Auth.Capabilities` match what `/access/me` returns.
