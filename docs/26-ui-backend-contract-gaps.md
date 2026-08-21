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
| G-8 — No way to list users, or to read one user's access | Open (new) |
| G-9 — `AccessRequestInvalidState` conflates validation with concurrency | Open (new) |
| G-10 — Rejection is not durable; rejected users re-enter the queue | Open (new) |
| `AccessSelfApprovalDenied` | ✅ Verified working — precedence explains the earlier observation |

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

### Extension: the same gap now blocks access administration

Access administration (UI milestone 2A) needs directory attributes for **another** user, which is a
strictly larger ask than the caller's own profile. `AccessRequestResponse` identifies a subject only
by `CorporateIdentity` — a raw principal string such as `demo:team-lead` or, in production, an OIDC
subject or `DOMAIN\account`.

An approver deciding whether to grant `Admin` sees an opaque identifier, not a person. That is a
security-relevant weakness, not only a cosmetic one: approving the wrong account is exactly the
mistake this screen exists to prevent.

| Field | Screen | Required? | Why the current contract is insufficient |
|---|---|---|---|
| `DisplayName` | `/access/requests` list + detail; confirmation dialog | **Required** | The approver must be able to tell *who* they are granting authority to. `CorporateIdentity` is a principal string, not a name. |
| `Mail` | detail | Optional | Lets the approver verify out of band before deciding. |
| `Department` | detail | Optional | Supports "does this person's team need this role" without a second system. |
| `Title` | detail | Optional | Same. |
| `Manager` | detail | Optional | Approval policy often follows the reporting line. |

**Suggested:** add a nullable `DisplayName` (at minimum) to `AccessRequestResponse`, or expose the
directory attributes through the `GET /access/users/{id}` read model proposed in G-8. Resolving it
per request at decision time would also work, provided it does not consume the audited
`Identity.Lookup` path.

**UI meanwhile:** the detail panel shows `CorporateIdentity` and the user id, and states in plain
language that name, e-mail, and department are not provided by the API. The confirmation dialog
names the same identifier, so an approver is never shown a fabricated person.

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

### UX limitations this causes

Confirmed while building the administration screens. No endpoint was invented; these are the costs
of the implicit flow, stated so the trade-off is a decision rather than an oversight:

- **The requester cannot say why.** `AccessRequestResponse.DecisionReason` records the *approver's*
  reason. There is no field for the requester's justification, so the approver decides on an
  identifier and a timestamp alone. Combined with G-2, the approval screen shows neither who the
  person is nor why they asked.
- **A request is created by merely visiting.** Any authenticated principal calling `/access/me` —
  which the shell does on every page load — creates a pending request. The queue therefore fills
  with anyone who opened the application, not only those who deliberately asked. The UI cannot
  distinguish the two.
- ~~A rejected user cannot re-apply through the UI.~~ **Corrected 2026-08-21:** the opposite is
  true, and it is worse. See G-10.

**Suggested (backend-owned):** if `POST /api/v1/access/requests` is added, accept a requester-supplied
reason and return the created request. That closes this and the first half of G-2's approval problem
together.

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

## G-8 — No way to list application users, or to read one user's access

**Raised:** building the access administration screens (UI milestone 2A), 2026-08-21.

**Severity:** High for administration. It is the single constraint that shapes what that screen can be.

**UI need:** an administrator asked for "approved users", "disabled users", and "user access
details" needs to enumerate users and read one user's current status, roles, and capabilities.

**Today:** the access surface is exactly seven endpoints, and none of them reads another user:

| Endpoint | Returns |
|---|---|
| `GET /access/me` | **the caller's** access only |
| `GET /access/requests?status=` | `AccessRequestResponse[]` — id, userId, corporateIdentity, status, timestamps, decision reason |
| `POST /access/requests/{id}/approve` \| `/reject` | the decided request |
| `PUT /access/users/{id}/roles` | that user's `CurrentAccessResponse` — **only as the result of a write** |
| `POST /access/users/{id}/disable` | same |

Three consequences follow, and all three are visible in the shipped UI:

1. **There is no user directory.** Users are discoverable only as the subject of an access request,
   so there is no `/access/users` route and no "Kullanıcılar ve Roller" nav entry. Adding one would
   mean inventing an endpoint.
2. **"Disabled users" cannot be listed at all.** Disabling changes the *user's* `AccessStatus`; the
   request's status stays `Approved`. Nothing exposes users by access status.
3. **Roles cannot be read before they are replaced.** `PUT .../roles` is a replace — "Administrative
   replacement of active application roles" — and omitted roles are removed. An administrator
   therefore edits a set they cannot see. The dialog states plainly that this is a replace and that
   current roles are unknown, rather than preselecting a guess that would silently strip roles.

**Suggested (backend-owned):** `GET /api/v1/access/users` with a status filter, and
`GET /api/v1/access/users/{id}` returning the same `CurrentAccessResponse` shape the write endpoints
already return. Both are read models over data the service already has, and neither changes an
existing route.

**UI meanwhile:** the workspace is a request queue with a detail panel. After any write it renders
the authoritative `CurrentAccessResponse` the API returned, which is the only moment another user's
roles and capabilities are knowable.

---

## G-9 — `AccessRequestInvalidState` conflates validation with concurrency

**Raised:** building the access administration screens (UI milestone 2A), 2026-08-21.

**Severity:** Medium. Correct behaviour is achievable, but only by working around the ambiguity.

**UI need:** requirement 6 asks for distinct UX for a validation error and a concurrency conflict.
They call for opposite responses — *fix what you typed* versus *reload, the world moved on* — so the
UI has to tell them apart.

**Today:** `409 AccessRequestInvalidState` is returned for all of these, with nothing in the body to
separate them:

| Cause | What it really is |
|---|---|
| blank or whitespace `reason` | validation |
| empty role list on approve | validation |
| unknown role code | validation |
| `?status=` value outside the known set | validation |
| request already decided by another administrator | **concurrency conflict** |
| disabling an already-disabled user | state conflict |
| assigning roles to a disabled user | state conflict |

Observed against the Demo API — all seven return byte-identical ProblemDetails apart from the
correlation id.

Also noted: `AccessSelfApprovalDenied` exists in `OperationalErrorCodes` and the contract lists it
for approve/reject, but a self-approval attempt in testing returned `AccessRequestInvalidState`. The
test was inconclusive — the only request belonging to the administrator was already decided, so the
already-decided check may simply have run first. Flagged for Codex to confirm rather than asserted
as a defect.

**Suggested (backend-owned):** separate codes for the input cases (for example
`AccessDecisionInvalidInput`) from the state cases, or add a discriminator to the ProblemDetails
extensions. A `422` for validation and `409` for state conflicts would also be sufficient.

**UI meanwhile:** `Services/AccessDecisionRules.cs` mirrors the server's input rules — reason
non-empty and ≤500 characters, one to sixteen roles, all from the server's own
`AccessRoleCatalog.RoleCodes` — and blocks submission before a request is sent. That removes the
validation cases from the wire, so a 409 that still arrives is in practice a genuine state conflict
and can be presented as "another administrator changed this; the list has been refreshed". The rules
are unit-tested against the catalog so they cannot silently drift from the server's.

This is a workaround, not a fix: it depends on the UI's copy of the rules staying in step.

---

## G-10 — Rejection is not durable: a rejected user re-enters the queue on next page load

**Raised:** 2026-08-21, reading the access repositories to settle the self-approval question below.
**Not a UI issue** — recorded here because the UI surfaced it and cannot correct it.

**Severity:** High. An approver's rejection does not hold.

Deciding a request sets the *request* to `Rejected` but returns the **user** to `AccessStatus.Pending`:

```csharp
// InMemoryAccessRepository.DecideRequestAsync
user = user with { Status = decision == AccessRequestStatus.Approved
    ? AccessStatus.Approved
    : AccessStatus.Pending, Roles = nextRoles };
```

`SqlAccessRepository.DecideRequestAsync` writes the same value. Then, on that user's next request:

```csharp
// EnsureUserAsync
if (createRequest && pending is null && _users[userId].Status == AccessStatus.Pending)
{
    // creates a brand-new pending request
}
```

The rejected user has no *pending* request (theirs is `Rejected`) and is still `Pending`, so a fresh
request is created. `/access/me` is called on every page load, so **rejection survives only until the
rejected person next opens the application.**

Consequences:

- The pending queue cannot be durably cleared. A rejected user reappears indefinitely.
- An approver reviewing the queue sees a new request with no decision history attached, and nothing
  indicates this identity was already refused. Combined with G-2 — where the subject is an opaque
  principal string — an approver can readily approve someone a colleague rejected an hour earlier.
- There is no terminal state for "refused". `AccessStatus` has `Pending`, `Approved`, `Disabled`;
  none of them means rejected.

**Suggested (backend-owned):** either leave a rejected user in a state that does not re-trigger
auto-creation (a `Rejected`/`Denied` `AccessStatus`, or suppressing auto-creation when the most
recent decided request was a rejection), or make rejection set `Disabled` if that is the intended
equivalent. This is a state-model decision, not a UI one.

**UI meanwhile:** nothing to do. The queue renders what the API returns. The UI cannot distinguish a
first-time request from a re-created one, because `AccessRequestResponse` carries no prior-decision
history.

---

## AccessSelfApprovalDenied — verified, working as designed

**Resolved 2026-08-21 by reading both repository implementations.** Recorded here because the earlier
observation was reported as inconclusive and should not be chased as a defect.

Earlier, approving the acting administrator's own request returned `AccessRequestInvalidState` rather
than `AccessSelfApprovalDenied`. The precedence explains it — both `InMemoryAccessRepository` and
`SqlAccessRepository` check in this order:

1. request or user missing → `NotFound`
2. **`request.Status != Pending` → `InvalidState`**
3. approving one's own request → `SelfApprovalDenied`

The request used in that test was the administrator's own bootstrap request, already `Approved`, so
it stopped at (2). For a genuinely **pending** request owned by the acting administrator, (3) is
reached and `AccessSelfApprovalDenied` is returned.

The ordering is also defensible on its own terms: an already-decided request cannot be self-approved
either, and lifecycle state is the more fundamental objection.

Note for whoever tests this: the scenario cannot be reproduced through the demo API surface, because
the only way to become an approved administrator (bootstrap or demo compatibility) also decides that
administrator's own request immediately. It needs a service- or repository-level test that creates a
pending request for an already-approved administrator.

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
