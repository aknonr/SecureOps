# 26 — UI Backend Contract Gaps

Findings raised by the UI layer against `docs/contracts/secureops-api-v1-ui-integration.md` and the
API as implemented on `release/api-test-20260812`. The UI made no backend change for any of these.

Each item states what the UI needs, what exists today, and what the UI does in the meantime.

| Gap | Status |
|---|---|
| G-1 — Mock directory resolves empty | ✅ **Resolved** by backend `4adab66c` |
| G-2 — No directory profile for the signed-in user | Open |
| G-3 — Lookup capabilities omit the account pattern | Open |
| G-4 — Session expiry indistinguishable from never-signed-in | Open |
| G-5 — Demo access bootstrap needs an undocumented setting | Open |
| G-6 — No access-request creation endpoint | Open |
| G-7 — Capabilities advertised UPN lookup the provider could not serve | ✅ **Resolved** by backend `704c32ba` |

---

## G-1 — Mock identity directory resolves to an empty user set (defect) — ✅ RESOLVED

**Resolved by backend commit `4adab66c5766303045d1a5867ccda2f25ec6ebc1`**
("fix(identity): make mock provider activation deterministic") on `release/api-test-20260812`,
merged into `feature/ui-enterprise-shell`.

**Severity when open:** High. Identity lookup could never return a result in Development, Demo, or Test.

`SecureOps.Infrastructure/DependencyInjection.cs` registered:

```csharp
services.AddSingleton<IIdentityDirectoryProvider, MockIdentityDirectoryProvider>();
```

`MockIdentityDirectoryProvider` has three public constructors. .NET's activator selected the greediest
one it could satisfy:

```csharp
MockIdentityDirectoryProvider(IEnumerable<DirectoryUserRecord> users, IOptions<IdentityLookupOptions> options)
```

`IEnumerable<T>` always resolves in Microsoft DI — to an **empty** sequence when no
`DirectoryUserRecord` is registered. The seeded `DefaultUsers()` overload was therefore never used.

**The fix** replaces type-based activation with an explicit factory that selects the options-only
constructor, so the seeded set is used and constructor choice can no longer drift:

```csharp
services.AddSingleton<IIdentityDirectoryProvider>(serviceProvider =>
    new MockIdentityDirectoryProvider(
        serviceProvider.GetRequiredService<IOptions<IdentityLookupOptions>>()));
```

It sits in the `else` branch, so `ActiveDirectoryIdentityDirectoryProvider` is untouched.

**Verified from the UI side** against the running Demo API after the merge:

| Check | Result |
|---|---|
| `POST /identity/lookup` `{"account":"pam12356"}` | `200` `Found`, full record, `source: "Mock"` |
| Unknown account | `404` `IdentityNotFound` with `code`/`correlationId`/`stage`/`retryable` intact |
| `CONTOSO\pam12356` | `200`, `normalizedAccount: "pam12356"` — prefix stripping unchanged |
| UI found state, light and dark | All nine directory fields render; no API URL or exception text leaks |

No UI change was required. The found-state rendering, previously verifiable only against a local
stub, is now confirmed against the real API.

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

## G-7 — Capabilities advertised UPN lookup the provider could not serve — ✅ RESOLVED

**Raised:** while verifying the G-1 fix, 2026-08-21.
**Resolved by backend commit `704c32ba627fb9352556b4138e4f79de27cecc2e`**
("fix(identity): report effective UPN lookup capability") on `release/api-test-20260812`, merged into
`feature/ui-enterprise-shell`.

**Severity when open:** Medium. `GET /identity/lookup/capabilities` returned
`"supportsUpnLookup": true`, but looking a mock user up *by their own UPN* returned not-found:

```
POST /identity/lookup  {"account":"pam12356@contoso.local"}   →  404 IdentityNotFound
POST /identity/lookup  {"account":"pam12356"}                 →  200 Found
                             ("userPrincipalName": "pam12356@contoso.local")
```

The UI reads that flag to decide whether to accept a UPN-shaped account, so the contract promised a
capability the active provider did not implement. An operator pasting a UPN out of an alert — the
natural thing to do — got "not found" for an account that demonstrably existed.

Not a regression from `4adab66c`: G-1 was masking it by making every lookup fail.

**The fix** takes the second of the two options raised here — `supportsUpnLookup` now reports the
**effective capability of the active provider** rather than a configuration flag, and
`MockIdentityDirectoryProvider` gained exact UPN matching gated on `EnableUpnLookup`.
`ActiveDirectoryIdentityDirectoryProvider` remains exact sAMAccountName first with an optional exact
UPN fallback. This is the more durable answer, because the flag now stays honest per environment even
if the AD provider's UPN support differs from the mock's.

**Verified from the UI side** by running the Demo API in both configurations — the point being that
the flag must agree with behaviour in *both* directions, not merely be `true`:

| `IdentityLookup:EnableUpnLookup` | `supportsUpnLookup` | UPN lookup | sAMAccountName |
|---|---|---|---|
| default (enabled) | `true` | **200 Found**, `normalizedAccount: pam12356@contoso.local` | 200 Found |
| `false` | `false` | 404 `IdentityNotFound` | 200 Found |

Exact-match semantics are intact in both: `CONTOSO\pam12356` still normalizes to `pam12356` (200),
a genuinely unknown account still returns 404 `IdentityNotFound`, the partial `pam` returns 404
rather than prefix-matching, and the wildcard `pam*` is rejected as 400 `InvalidIdentityInput`.

**UI:** no change required. `AccountInputRules` already blocks wildcard and LDAP characters
client-side (submit stays disabled, no request is issued), and the UPN found state renders correctly
with the normalized UPN shown. The UI performs no wildcard, prefix, or fuzzy matching of its own.

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

Re-verified after merging `4adab66c`:

- `POST /api/v1/identity/lookup` returns `200 Found` for the seeded mock users, and the response
  shape matches `IdentityLookupResponse` field for field — `displayName`, `samAccountName`,
  `userPrincipalName`, `mail`, `department`, `title`, `managerDisplayName`, `enabled`, `locked`.
- `normalizedAccount` still strips a `DOMAIN\` prefix.
- Genuinely unknown accounts still return `404 IdentityNotFound`, so the fix did not turn the
  not-found path into a false positive.
- `GET /identity/lookup/capabilities` is unchanged: `maxAccountLength: 128`, the same nine
  `returnedFields`, and the same six `rejectedInputClasses`. The UI needed no contract change.
