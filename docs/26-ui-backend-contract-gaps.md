# 26 — UI Backend Contract Gaps

Findings raised by the UI layer against `docs/contracts/secureops-api-v1-ui-integration.md` and the
API as implemented on `release/api-test-20260812`. The UI made no backend change for any of these.

Each item states what the UI needs, what exists today, and what the UI does in the meantime.

| Gap | Status |
|---|---|
| G-1 — Mock directory resolves empty | ✅ **Resolved** by backend `4adab66c` |
| G-2 — No directory profile | ✅ **Resolved** by backend `78183dd` |
| G-3 — Lookup capabilities omit the account pattern | Open |
| G-4 — Session expiry indistinguishable from never-signed-in | Open |
| G-5 — Demo access bootstrap needs an undocumented setting | Open |
| G-6 — No access-request creation endpoint | Open |
| G-7 — Capabilities advertised UPN lookup the provider could not serve | ✅ **Resolved** by backend `704c32ba` |
| G-8 — No way to list users, or to read one user's access | ✅ **Resolved** by backend `78183dd` |
| G-9 — `AccessRequestInvalidState` conflates validation with concurrency | ✅ **Resolved** by backend `78183dd` |
| G-10 — Rejection is not durable | ✅ **Resolved** by backend `78183dd` |
| G-11 — Claim owner is not exposed | ✅ **Resolved** by backend `989030c3` |
| G-12 — `ReconciliationRequired` is not exposed | ✅ **Resolved** by backend `989030c3` |
| G-13 — No source provider yields records | ✅ **Resolved** by backend `989030c3` |
| G-14 — A duration statistic carries no stable key | ✅ **Resolved** by backend `b0e3b7b` |
| G-15 — Reported limitations are English prose with no code | ✅ **Resolved** by backend `b0e3b7b` |
| G-16 — Only identity lookup has daily buckets | Open — confirmed out of scope by `b0e3b7b` |
| G-17 — Zero and "no persisted history" are indistinguishable | ✅ **Resolved** by backend `b0e3b7b` |
| G-18 — UI integration contract still calls the directory purpose required | Open — documentation only, no longer reached by the UI |
| G-19 — `/access/me` gives held role codes but no role names | Open — UI shows "İş rolü" + code |
| G-20 — Access-request page has one fixed order and no per-status counts | Open — UI states the order |
| G-21 — Access-user page is ordered by `UserId` | Open — UI states the order is not by name |
| G-22 — Role definitions carry no assigned-user count | Open — count only after preview |
| G-23 — Service Accounts scope grants are not readable from access screens | Open — UI shows a scope note |
| G-24 — Provider health has no check time and no connectivity result | Open — UI labels "sınanmadı" |
| G-25 — SQL, audit-store and Worker state are not in the System Status contract | Open — UI lists them as unknown |
| G-26 — gMSA/MSA accounts cannot be looked up | Backend fixed; UI follow-up on `feature/ui-g26-managed-account-input-20261007` (not merged) — real AD validation pending |
| G-27 — Lookup purpose is unreadable in audit | Open — needs ADR-0008 / docs/27 decision |
| G-28 — No bounded way to resolve an account from a person's name | Open — needs ADR decision |
| G-29 — AD lookup does not show the Service Accounts inventory record | Open |
| G-30 — The solution builds only with a specific SDK/language combination | Repair implemented — pinned SDK/language; Windows verification below |
| G-31 — No per-owner usage signal for frequently used links | Open — needs an audit-framing decision before a contract |
| G-32 — Favourites view has no server-side paging | Open — low |
| G-34 — Concurrent first registrations can deadlock in the access store | Fixed in source and synthetic LocalDB — 032 index / one registration retry; installed TEST unchanged; module tests remain serialised |
| G-35 — Unsafe API requests lack a central CSRF guard | Locally verified repair; corporate Negotiate/IIS/F5 acceptance pending |
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

## G-2 — No directory profile — ✅ RESOLVED

**Resolved by backend commit `78183dd`.** `AccessIdentityProfileResponse` (nullable
`DisplayName`, `Account`, `Email`, `Department`, `Title`) now hangs off `CurrentAccessResponse`,
`AccessUserResponse`, and `AccessRequestResponse`, so the same model serves the signed-in user and
the administrator looking at someone else. No second identity system was introduced.

**Verified from the UI side:** the demo bridge resolves nothing, so `profile` comes back `null` for
both demo actors — which made the absent path the default case and easy to check. The UI falls back
to the principal identifier and states in words that enrichment is unavailable. It never renders a
blank field, an em dash, or an invented name. `AccessIdentityDisplayTests` pins that.

**What it was.** `GET /identity/me` returned only `(Name, IsAuthenticated, CanLookupIdentity)` and
`/access/me` added no directory attributes, so `/account` had nothing to show. Administration made it
worse: `AccessRequestResponse` identified a subject only by `CorporateIdentity` — a raw principal
string like `demo:team-lead` — so an approver granting `Admin` saw an identifier rather than a
person. That was security-relevant, not cosmetic: approving the wrong account is the mistake the
screen exists to prevent.

Using `POST /identity/lookup` to fill the gap would have been wrong — it is privileged, audited, and
rate-limited, and someone viewing their own profile should neither consume it nor need
`Identity.Lookup`. The delivered fix avoids that.

**The rule the UI keeps, now that enrichment exists.** Every field is nullable and the whole object
can be `null`. Absent means absent: the UI falls back to the principal identifier and says
enrichment is unavailable. It never renders a placeholder name, a blank identity field, or an em dash
standing in for an e-mail — on a screen that grants authority, a fabricated person is a safety
problem.

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

## G-8 — No way to list application users, or to read one user's access — ✅ RESOLVED

**Raised:** building the access administration screens (UI milestone 2A), 2026-08-21.
**Resolved by backend commit `78183dd`** — `GET /api/v1/access/users` and
`GET /api/v1/access/users/{id}`, both behind `Access.ManageUsers`, returning `AccessUserResponse`
with status, assigned roles, backend-derived capabilities, profile, latest request, full request
history, and a `version`.

**Verified from the UI side:** the role editor is now seeded from `GET /access/users/{id}` and
submits that record's `version` as `expectedVersion`. Confirmed live that opening the editor for a
user holding Lead + JiraPublisher preselects exactly those two, so the replace semantics no longer
risk silently stripping roles. Disabled users are listed as their own group.

**Severity when open:** High for administration. It was the single constraint that shaped the screen.

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

## G-9 — `AccessRequestInvalidState` conflated validation with concurrency — ✅ RESOLVED

**Raised:** building the access administration screens (UI milestone 2A), 2026-08-21.
**Resolved by backend commit `78183dd`.** Five stable codes now separate what one code used to
carry, and every mutation takes an `expectedVersion`:

| Code | HTTP | `retryable` | UI presentation |
|---|---|---|---|
| `AccessValidationFailed` | 400 | false | validation — fix the form |
| `AccessRequestAlreadyDecided` | 409 | false | lifecycle — someone already decided it |
| `AccessConcurrencyConflict` | 409 | **true** | stale — reload and look before acting |
| `AccessUserInvalidState` | 409 | false | user lifecycle — wrong state for this action |
| `AccessSelfApprovalDenied` | 403 | false | forbidden — separation of duties |

All five verified live against the Demo API.

Note on `AccessConcurrencyConflict`: `retryable: true` is accurate but means *re-attemptable after a
fresh read*, not *re-send this request*. The submitted version is stale by definition. The UI
therefore wires that action to a reload and labels the button "Güncel durumu yükle" rather than
"Tekrar dene" — the failed write is never automatically re-issued.

**Severity when open:** Medium. Correct behaviour was achievable, but only by working around the ambiguity.

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

## G-10 — Rejection was not durable — ✅ RESOLVED

**Raised:** 2026-08-21, reading the access repositories to settle the self-approval question below.
**Resolved by backend commit `78183dd`.** A rejected request stays `Rejected`, the user stays
non-authorized `Pending`, `pendingRequestId` is `null`, and revisiting the application no longer
creates a replacement. Reapplication is deliberately unsupported until an approved policy exists.

**Verified from the UI side:** rejected, then called `/access/me` twice more as that user —
`pendingRequestId` stayed `null` and the pending queue stayed empty.

### The distinction the UI must carry

This is the part that matters for anyone touching these screens. **There is no `Rejected` user
status.** A refused user keeps `AccessStatus.Pending` indefinitely, and only `latestRequest.status`
says otherwise:

| | Refused user | Genuinely waiting |
|---|---|---|
| `accessStatus` | `Pending` | `Pending` |
| `pendingRequestId` | `null` | set |
| `latestRequest.status` | `Rejected` | `Pending` |

Reading `accessStatus` alone shows a closed decision as an open task, and an administrator working
the queue would re-approve someone a colleague turned down. `AccessUserView` and
`AccessSnapshot.IsRejected` encode the combination; `AccessUserViewTests` pins it, including that a
rejection is toned Critical rather than sharing the pending Caution tone.

Confirmed on screen: with one rejected user the list reads **"Onay bekleyen 0 · Reddedilmiş 1"**, the
detail states the request was refused and that no pending request exists, and no "request again"
action is offered anywhere.

**Severity when open:** High. An approver's rejection did not hold.

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

## G-11 — The claim owner is not exposed — ✅ RESOLVED

**Raised:** building the Operational Record → Jira screens (UI milestone 2B), 2026-08-21.
**Resolved by backend commit `989030c3`** — `claimedBy` and `claimedAt` are now projected, with
`claimed` remaining the authoritative liveness flag.

**The trap the contract warns about, and how the UI handles it:** an expired claim may keep its
`claimedBy` while `claimed` is `false`. Deciding ownership from `claimedBy` alone would show a
long-lapsed claim as active and stop operators working a record nobody holds. `claimed` is therefore
checked first and `claimedBy` only distinguishes *whose* live claim it is, giving four states:

| `claimed` | `claimedBy` | Presented as |
|---|---|---|
| `false` | absent | Serbest — available |
| `false` | present | Önceki kilit sona erdi — history only, explicitly *not* owned |
| `true` | current actor | Sizde — claimed by me |
| `true` | someone else, or unknown | Başka operatörde — being processed by another operator |

Comparison is against `GET /identity/me`'s `name`, not the browser cookie: the API records the actor
as `demo:platform-admin` where the session says `platform-admin`, and comparing the cookie would
report every record as another operator's. If that call fails, the UI never reports a claim as the
current operator's — the safe fallback. Eight unit tests pin these, including the expired-with-owner
case and case-insensitive comparison.

**Severity when open:** Medium. The workflow is multi-operator by design, and ownership is the thing
operators most need to see.

**Original need:** distinguish *unclaimed*, *claimed by me*, and *claimed by another operator*. The first
and third differ in whether acting is safe; the first and second differ in whether the operator
already owns the work.

**Today:** `OperationalRecordResponse` exposes `Claimed` (bool) and `ClaimExpiresAt`. The owning
actor exists on the domain entity — `OperationalRecord.ClaimedBy`, alongside `ClaimedAt` — and the
repositories compare against it (`InMemoryOperationalRecordRepository` and
`SqlOperationalRecordRepository` both do `ClaimedBy == actor`), but it is not projected into the
response.

The UI can therefore only learn ownership by attempting an action and receiving
`OperationalRecordAlreadyClaimed`. Two operators looking at the same record see identical screens.

**Suggested (backend-owned):** add `ClaimedBy` (or a derived `ClaimedByMe` boolean, if exposing the
actor identity is not wanted) and `ClaimedAt` to `OperationalRecordResponse`. A boolean avoids any
question about surfacing colleague identity while still answering the operational question.

**UI meanwhile:** a live claim is presented as *"Başka operatörde olabilir"* — may be held by another
operator — and the detail says outright that the API does not report who holds it. The UI never
claims a record is the current operator's. That is the safe direction to be wrong in: it warns
before an action that might collide, rather than implying an exclusivity nobody promised.

---

## G-12 — `ReconciliationRequired` is not exposed — ✅ RESOLVED

**Raised:** building the Operational Record → Jira screens (UI milestone 2B), 2026-08-21.

**Resolved by backend commit `989030c3`** — `reconciliationRequired`, `retryEligible`, and
`jiraExists` are now projected, so all three can be shown before an operator tries anything.

The UI now:

- flags reconciliation in the **list** with its own badge, and in the detail with a prominent amber
  panel — no command attempt required;
- treats `reconciliationRequired` as outranking the state machine: create is blocked in *every*
  state while it is set, asserted by a test across the whole enum;
- offers retry **only** when `retryEligible` is true, and says so plainly when it is not — retry is
  never inferred from a stage or an HTTP status;
- states whether a Jira issue exists inside the reconciliation panel, because that is the first
  thing to check by hand;
- uses `jiraExists` as authoritative for duplicate safety, so a missing key cannot re-enable create.

**Severity when open:** Medium. The state was reachable and correctly enforced; it just could not be
seen until someone acted.

**UI need:** show, in the list and on the record, that a Jira create outcome is unresolved and manual
reconciliation is required — before an operator tries something.

**Today:** `OperationalRecord.ReconciliationRequired` exists on the domain entity and gates the
workflow (`JiraTransferService` returns `WorkflowConflict` with `stage: "jira-reconciliation"`), but
it is not part of `OperationalRecordResponse`. A read cannot report it.

**Suggested (backend-owned):** add `ReconciliationRequired` to `OperationalRecordResponse`. It is
already on the entity; this is a projection change.

**UI meanwhile:** the closest authoritative signal a read gives is the `CreatingJira` stage with no
issue key, and the UI treats that as unknown-outcome: a prominent amber panel, an explicit
duplicate-risk warning, the correlation id, and **no create or retry action offered**. When an action
is attempted anyway, `WorkflowConflict` + `stage: "jira-reconciliation"` is mapped to the same
dedicated presentation rather than to the generic conflict message. Both paths are unit-tested.

---

## G-13 — No source provider yields operational records — ✅ RESOLVED

**Raised:** building the Operational Record → Jira screens (UI milestone 2B), 2026-08-21.

**Resolved by backend commit `989030c3`** — `OperationalRecords:SourceProvider` is now honoured
(`Fake` in Development/Demo/Test only, `Disabled` otherwise, unimplemented providers failing closed),
and the fake source ships four deliberate fixtures: eligible, stale, closed, and missing.

Synthetic end-to-end verification is now genuinely possible and was performed — see the readiness
note below.

**Severity when open:** High for TEST readiness. The entire OR → Jira workflow was unexercisable in
any environment.

Two findings:

1. **The configured provider is never read.** `OperationalRecordsOptions.SourceProvider` exists and
   defaults to `"Fake"`, but `SecureOps.Infrastructure/DependencyInjection.cs` registers
   `services.AddSingleton<IOperationalRecordClient, FakeOperationalRecordClient>()` unconditionally.
   Unlike the identity and audit providers, no branch consults the option, so it is dead
   configuration — setting it has no effect.
2. **The fake returns nothing.** `FakeOperationalRecordClient.GetActiveAsync` returns
   `Array.Empty<OperationalRecordSourceItem>()` and `GetByIdAsync` returns `null`.

Confirmed live: `GET /api/v1/operational-records` returns `[]` against the Demo host, and there is no
route by which a record can be created — import is the only path in.

**Consequence:** no end-to-end verification of preview, create, retry, claim, freshness, or
reconciliation is possible against the real API, in Demo or Test.

**Suggested (backend-owned):** honour `SourceProvider` in registration, and supply a provider that
yields bounded sample records for non-production environments (as `MockIdentityDirectoryProvider`
does for identity). The real source client for TEST is a separate decision.

**UI meanwhile:** the empty list renders as a truthful state — *"Kaynak kayıt yok / Yapılandırılmış
kaynak şu anda aktarılacak kayıt döndürmüyor"* — distinct from a filter matching nothing and from a
load failure. State rendering for all fifteen required workflow and failure states was verified
against a contract-shaped local stub serving the committed DTO shapes and ProblemDetails codes; see
`docs/25-ui-enterprise-shell.md` §11.

---

## Readiness: synthetic ≠ real Turuncu Hat

**Synthetic OR → Jira end-to-end readiness is not real Turuncu Hat integration readiness.** The
distinction matters for release planning, so it is stated here rather than left implied.

What synthetic verification does establish: the UI drives the real SecureOps workflow correctly, and
the workflow's own state machine, idempotency, source revalidation and duplicate protection behave as
documented. Verified against the real API with `OperationalRecords__SourceProvider=Fake`: preview
advanced a record to `Previewed`; create produced a real Jira key through the fake client and reached
`Completed`; a repeat create returned the *same* key rather than a second issue; the stale fixture was
rejected with `OperationalRecordChanged` and the closed and missing fixtures with
`OperationalRecordNoLongerOpen`, none of them creating a Jira issue.

What it does **not** establish:

- **No real source.** There is still no Turuncu Hat provider. `Fake` is a fixture set, not a
  connector, and it is refused outside Development, Demo, and Test.
- **No real Jira.** `FakeJiraClient` always succeeds and is idempotent by key. Genuine Jira failure
  modes — timeouts, auth rejection, field validation, and above all an *unknown* outcome — cannot
  occur against it. Reconciliation and retry-blocked were verified by rendering, not by a real
  provider producing them.
- **No live claim contention.** With fast fake providers, commands complete before a read can observe
  a live claim, so "claimed by another operator" was verified from authoritative fields and unit
  tests rather than by two operators colliding in practice.
- **No SQL repository.** Verification ran on the in-memory repository; migration 004 was not executed.

---

## G-14 — A duration statistic carries no stable key — ✅ RESOLVED

**Endpoint:** `GET /api/v1/reporting/management/summary`
**Severity:** Low — cosmetic today, silently wrong later
**Status:** Open

`DurationStatisticsResponse` identifies each interval only by `definition`, an English sentence:

```json
{ "definition": "Workflow claim to durable Jira issue-key persistence",
  "sampleCount": 74, "minimumSeconds": 31, "averageSeconds": 264, "maximumSeconds": 3600 }
```

There is no `name`, no code, and nothing in the contract that fixes the array order — the projector
happens to emit three entries in a fixed sequence, but that is an implementation detail rather than a
promise.

**What the UI does.** Matches on the exact sentence to pick a Turkish label, and falls back to
displaying the server's own text when the sentence is not one of the three it knows. Array position
is deliberately not used: a reordering would then relabel every row without any error, which is worse
than an untranslated label.

**What would resolve it.** A stable `name` alongside `definition` — the projector already has one
internally (`ImportToPreview`, `ClaimToJiraCreated`, `ClaimToCompleted`); it is simply not serialized.

---

## G-15 — Reported limitations are English prose with no code — ✅ RESOLVED

**Endpoint:** `GET /api/v1/reporting/management/summary`
**Severity:** Low
**Status:** Open

`dataLimitations` is a list of English sentences. They are important — they are what stops a manager
reading an unmeasured zero as a measured one — but they arrive as prose with no identifier, on a
product whose entire operator-facing surface is Turkish.

**What the UI does.** Renders each sentence through a reviewed translation keyed on the exact server
text, and shows anything unrecognised verbatim. Failing towards the server's own words is the safe
direction: an English sentence on a Turkish screen is a blemish, whereas a dropped or reworded
limitation is a false statement about the data.

**What would resolve it.** A stable code per limitation, so the UI can carry the wording and the
backend can carry the fact.

---

## G-16 — Only identity lookup has daily buckets

**Endpoint:** `GET /api/v1/reporting/management/summary`
**Severity:** Medium for the brief, low for correctness
**Status:** Open — by design in ADR-0011, recorded here because it constrains the dashboard

`identityLookup.trend` is the only time series in the contract. Adoption reports four scalars
(`dailyActiveUsers`, `weeklyActiveUsers`, `monthlyActiveUsers`, `uniqueActiveUsersInWindow`), and the
Operational Record workflow reports totals for the window with no per-day breakdown at all.

**What the UI does.** Draws exactly one trend chart, from the one series that exists. Adoption is
presented as its four reported figures with a note that a per-day series is not available in this
release, and no workflow trend is drawn. Interpolating a line through numbers the server never
bucketed would be an invented metric, which ADR-0011 forbids and which nobody could reconcile against
the audit trail.

**What would resolve it.** Daily buckets for adoption and workflow transitions, in the same shape as
the identity trend.

---

## G-17 — Zero and "no persisted history" are indistinguishable — ✅ RESOLVED

**Endpoint:** `GET /api/v1/reporting/management/summary`
**Severity:** Medium
**Status:** Open — partially mitigated

Every count in the contract is a non-nullable `long` except `securityAndQuality.rateLimitEvents`.
A window that predates the reporting read model therefore returns `0` for every metric, which is
indistinguishable on the wire from a window in which nothing happened. This matters most during the
pilot, whose SQL history begins part-way through any range a manager is likely to ask for.

**What the UI does, and what it cannot do.**

- When *every* counted metric is zero, the dashboard replaces the figures with "Bu aralıkta kayıtlı
  kanıt yok" and states explicitly that this may mean the persisted history does not yet cover the
  range — not that nothing happened.
- `dataLimitations` is always on the page.
- A window with *some* activity but partial history still shows zeroes for the unmeasured parts, and
  the UI cannot mark them, because the contract gives it nothing to distinguish them by. Durations
  are the exception: `sampleCount: 0` is an explicit "not measured" and renders as such.

**What would resolve it.** Either a `coverageFromUtc` on the response — the earliest instant the read
model can answer for — or nullable counts for metrics outside that coverage.

---

---

## Round 2 resolution notes

Backend `b0e3b7b` closed three of the four gaps this UI raised, and the screens now consume the
hardened shapes:

- **G-14** — every duration carries a stable `key`. The UI keys on it and ignores `definition` text
  and array order entirely, which the contract states are presentation details.
- **G-15** — every limitation carries a stable `code`. The UI localizes on the code; `message` is
  fallback display only and drives no behaviour.
- **G-17** — both reporting routes return `coverage`. A zero is now presented as a measured zero only
  when `coverageComplete` is true, and a partial window names the boundary and the size of the
  unmeasured head rather than drawing it as no activity.
- **G-16** remains open and was explicitly left out of scope by the backend. Only
  `identityLookup.trend` is bucketed by day, so it stays the only trend drawn; no adoption or
  Operational Record/Jira series is inferred client-side.

No new contract gaps were found while building the Directory Explorer, session administration, or
integration status screens. Every field those screens needed was present, correctly nullable, and
accompanied by the traversal or truncation metadata required to present a bounded result honestly.

One backend change required a UI fix rather than a gap entry: `OperationalRecordResponse.createdAt`
became nullable, because the reviewed legacy Turuncu Hat projection supplies no source timestamp. The
record detail renders that as "kaynak sistemde tarih yok".

## G-18 — The UI integration contract still calls the directory purpose required

**Endpoint:** `POST /api/v1/directory/*`
**Severity:** Low — documentation only, no runtime effect
**Status:** Open, and no longer reached by the UI

Backend `a607ac4` made `purpose` optional on every read-only Directory Explorer route. `docs/27`,
`docs/29`, `DirectoryExplorerOptions`, and `DirectoryLookupPurpose` all say so. The UI integration
contract does not:

> Phase 2 requests are exact-only POST bodies with `account`, **required operational `purpose`**, and
> optional `refresh`

That line is now wrong, and it is the document a UI author reads first. The DTOs are unambiguous —
every `Purpose` parameter defaults to `null` — so the UI follows the code and the phase docs.

**What would resolve it.** Correcting that sentence in
`docs/contracts/secureops-api-v1-ui-integration.md`. Backend-owned, so it is recorded here rather
than edited.

**Update after the Active Directory redesign.** The UI no longer sends `purpose` on any directory
route, or on `POST /api/v1/identity/lookup` — the field was removed from both screens rather than
kept as an optional input nobody filled in. So the stale sentence can no longer mislead a UI author
about what to send. It can still mislead a *backend* reader about what the routes accept, which is
why the gap stays open rather than being closed by a UI change.

---

## G-19 — `/access/me` gives held role codes but no role names

**Endpoint:** `GET /api/v1/access/me`
**Severity:** Medium — the signed-in user cannot read the name of an administrator-defined role
**Status:** Open (raised by the 2026-10-01 access/management UI pass)

`roles` holds codes only. Fixed roles have UI labels, but a business role created on Rol tanımları
has a generated code (`Business_…`) and its name is readable only through `GET /access/roles`, which
requires an access-administration capability.

**What the UI does.** Erişimim and Genel Bakış show such a role as **İş rolü**, with the code as a
tooltip/inline code, and point to the capability list for what it grants. No name is guessed.

**What would resolve it.** `name` (and optionally `purpose`) for each held role on `/access/me`,
e.g. `heldRoles: [{ code, name, purpose }]`, without exposing other roles.

---

## G-20 — Access-request page has one fixed order and no per-status counts

**Endpoint:** `GET /api/v1/access/requests/page`
**Severity:** Medium when requests accumulate
**Status:** Open

Rows are ordered `RequestedAt DESC`; there is no sort parameter, so the longest-waiting pending
requests are on the last page. The response total covers only the requested status, and each page
read writes an `AccessRequestsViewed` audit event, so the UI does not issue extra reads to count the
other tabs.

**What the UI does.** States "en yeni talep üstte; en uzun bekleyenler son sayfadadır", shows the
waiting time from `requestedAt`, labels pending requests older than 7 days, and keeps the pager above
the list. It does not re-sort one page.

**What would resolve it.** A `sort=oldest|newest` parameter (oldest first as the pending default) and
`counts: { pending, approved, rejected }` from the same snapshot.

---

## G-21 — Access-user page is ordered by `UserId`

**Endpoint:** `GET /api/v1/access/users/page`
**Severity:** Low–medium
**Status:** Open

`ORDER BY u.UserId` is stable but meaningless to a person. **What the UI does.** Says the order is set
by the server and is not by name or last sign-in. **What would resolve it.** A sort by display
name/account and by `lastAuthenticatedAt`.

---

## G-22 — Role definitions carry no assigned-user count

**Endpoint:** `GET /api/v1/access/roles`
**Severity:** Low
**Status:** Open

`AccessRoleDefinition` has no count of users holding the role; only a preview returns
`affectedUsers`. **What the UI does.** Shows action and module counts per role and nothing about
holders until a preview. **What would resolve it.** `assignedUsers` on each definition, from the same
read.

---

## G-23 — Service Accounts scope grants are not readable from access screens

**Endpoints:** `GET /api/v1/access/me`, `GET /api/v1/access/users/{id}`
**Severity:** Medium — a `ServiceAccounts.View` holder without a scope grant sees an empty module
**Status:** Open

Role capabilities and module scope grants are separate; scope is administered inside Servis
Hesapları. **What the UI does.** Explains on Rol tanımları and Erişimim that scope is granted
separately and is not shown there. **What would resolve it.** A read-only scope summary
(team/organization names and kind) on the user detail and on `/access/me`.

---

## G-24 — Provider health has no check time and no connectivity result

**Endpoints:** `GET /api/v1/health/enterprise-integrations`, `GET /api/v1/health/identity-provider`
**Severity:** Medium — "Configured" is easy to read as healthy
**Status:** Open

For Turuncu Hat and Jira, `Configured` means a provider is selected and the API process has recorded
no failed call since it started; `Unavailable` means the last call failed. No timestamps are
returned. The identity endpoint always returns `Configured` and tracks no failures.

**What the UI does.** Shows **Yapılandırıldı · sınanmadı** in an informational (not positive) tone,
explains each state, shows the page's own read time in UTC, shows simulation/read-only/test-provider
notices from the response, and renders an unreadable provider as **Okunamadı** instead of hiding it.

**What would resolve it.** Per provider `lastSuccessAt`, `lastFailureAt` and process start time, and
failure tracking for the identity provider. An explicit, authorized connectivity check would be a
separate decision.

---

## G-25 — SQL, audit-store and Worker state are not in the System Status contract

**Endpoints:** `GET /api/v1/health/persistence`, `GET /api/v1/health/audit-store` (exist in the API)
**Severity:** Medium for an operator asking "is the platform up"
**Status:** Open — needs a contract decision before the UI uses them

These routes exist but are not described in `docs/contracts/secureops-api-v1-ui-integration.md`,
are authenticated-only outside Development rather than Admin-scoped, and Worker/Hangfire state appears
only as heartbeat evidence inside `GET /api/v1/diagnostics/operations`. **What the UI does.** System
Status lists SQL Server, audit store, Hangfire/Worker and SMTP under "Bu sayfada durumu
gösterilmeyenler" as unknown. **What would resolve it.** A documented, Admin-authorized status
contract (state, observed-at, safe reason code) for these components.

---

## G-26 — gMSA/MSA accounts cannot be looked up

**Endpoints:** `POST /api/v1/identity/lookup`, `POST /api/v1/directory/principals/*`
**Severity:** High for the Service Accounts work (gMSA transition)
**Status:** Backend fixed (2026-10-07); UI follow-up and non-sensitive real AD validation pending

**Original defect (2026-10-01).**

`IdentityLookup:AllowedAccountPattern` (`^[a-zA-Z0-9._@-]+$`) and the Directory Explorer input
pattern (`^[a-zA-Z0-9._@ -]+$`) reject `$`, but every gMSA/MSA `sAMAccountName` ends in `$`.
Lookups also use `UserPrincipal.FindByIdentity`; gMSA objects derive from the `computer` class, so
that query is not expected to return them. `AccountTypeEvidence` can report
`GroupManagedServiceAccount`, but that path is probably unreachable. The group pattern likewise
rejects Turkish letters, parentheses and `&`, which real group names may contain.

**What would resolve it.** Accept a single trailing `$`; resolve gMSA/MSA by exact `sAMAccountName`
with an objectClass-bound search (or `ComputerPrincipal`); optionally return who may retrieve the
managed password (`msDS-GroupMSAMembership`, resolved names) as evidence. Revisit the group pattern
against the corporate naming standard. The UI would label the type from the existing evidence field.

**Implemented backend scope (2026-10-07).** Both input guards accept only one trailing `$`
on a nonempty sAMAccountName; wildcard/LDAP/bulk inputs and managed-account UPN forms remain
rejected. Identity lookup and all Directory Explorer principal paths resolve managed accounts
by exact sAMAccountName with only `msDS-GroupManagedServiceAccount` / `msDS-ManagedServiceAccount`
classes. A fixed LDAP metadata projection excludes all password/secret attributes; no result
DirectoryEntry is hydrated. Search is bounded, referrals disabled, result name/class verified,
and ambiguous results fail closed. Direct/primary membership handling and ordinary user/UPN
lookup behavior are preserved. `AccountTypeEvidence` is returned by service evidence and
additively by identity lookup. Mock seeds: `syn.gmsa$`, `syn.msa$`.

**Remaining.** UI files were not changed: the UI owner must update
`src/SecureOps.Ui/Services/AccountInputRules.cs:130` to accept the same trailing suffix.
`DirectoryView.cs:305` and `SoDirectoryService.razor:88` already label the directory evidence;
the identity overview must also display `user.accountTypeEvidence`. A non-sensitive real AD gMSA/MSA
under the configured domain/container still needs separate authorized validation. Existing
runtime `AllowedAccountPattern` overrides must be updated; no live settings were changed.
Turkish/parenthesis/ampersand group-name support is a separate naming-policy question.
Optional password-retriever names (`msDS-GroupMSAMembership`) are deferred and not read.

**UI follow-up (2026-10-07, branch `feature/ui-g26-managed-account-input-20261007`, not merged).** `AccountInputRules`
mirrors `IdentityProviderInputGuard.HasSafeDollarSuffix`: one `$`, last, after a nonempty name, never with `@`; any other
`$` gets its own message. The identity overview (`DirectoryUser.razor`, Genel tab) shows "Dizin nesne türü" from
`accountTypeEvidence` through the existing `DirectoryView.AccountTypeEvidenceLabel`. Still open: real AD validation and
runtime `AllowedAccountPattern` overrides (above). Local Demo (Mock identity provider, own ports, 2026-10-07): `gm$sa`
refused in the field with the new message; `syn.gmsa$` → "Grup yönetilen servis hesabı nesnesi (gMSA)", `syn.msa$` →
"Yönetilen servis hesabı nesnesi (MSA)", `pam12356` → "Standart kullanıcı nesnesi"; 390 px dark and light without
horizontal scroll. Input was set by page script because the browser pane was hidden (no keyboard check this time).

**Local verification (Windows, SDK 9.0.317).** Solution build: 0 warnings/errors.
Unit suite: 1,946 passed, 1 Windows machine-dependent collector test skipped.
Selected identity/directory/bulk identity/OpenAPI integration suite: 88 passed, 0 skipped.
Coverage includes dollar boundaries (also with permissive configuration), wildcard/LDAP
rejection, exact object-class/result projection, gMSA/MSA found/not-found, ordinary user/UPN
regression, and HTTP execution through every principal route using synthetic Mock data.
No real AD, IIS, corporate system or database validation was performed.
Repository-wide `dotnet format SecureOps.sln --verify-no-changes --no-restore` and
`git diff --check` passed. UI source files and SQL assets were not changed.

---

## G-27 — Lookup purpose is unreadable in audit

**Endpoints:** identity and directory read routes
**Severity:** Medium — audit cannot answer "why was this looked up"
**Status:** Open — needs an ADR-0008 / `docs/27` decision

Purpose is optional and audited only as hash + length; the UI no longer asks for it. Comparable
tools record a readable reason: CyberArk audits a `Reason` and optional ticketing system/ticket ID;
Entra PIM can require justification and a ticket number shown in audit. KVKK md. 4 asks processing
to be for a specific purpose and proportionate.

**Proposal.** A structured purpose instead of free text: a reason code (e.g. Olay/Alarm, Değişiklik,
Erişim talebi, Servis hesabı envanteri, Diğer) plus an optional validated reference (OR number or
Jira key), both stored in clear; free text stays out of audit. Required for member enumeration,
privileged analysis and export; optional for a basic lookup. The UI would add one compact selector
and prefill the reference when navigating from an operational record.

---

## G-28 — No bounded way to resolve an account from a person's name

**Endpoints:** none today
**Severity:** Medium — operators often know a display name, not the `sAMAccountName`
**Status:** Open — conflicts with ADR-0008 "no broad search"; needs an explicit decision

**Proposal.** A disambiguation step rather than people search: AD ambiguous name resolution (ANR)
with at least 3 characters, at most 10 results, returning only display name, `sAMAccountName` and
object type; selecting one runs the existing exact lookup. Separate capability, rate limit and
audit (target hash, result count).

---

## G-29 — AD lookup does not show the Service Accounts inventory record

**Endpoints:** `POST /api/v1/directory/principals/service-evidence` and the Service Accounts API
**Severity:** Medium
**Status:** Open

AD evidence and the inventory (owner, team, handover, gMSA plan) are on separate screens with no
link. **What would resolve it.** A scope-checked "inventory match" for an exact account (record id,
owning team, status) returned only when the caller holds `ServiceAccounts.View` and scope; the UI
would show a link to the record.

---

## G-30 — The solution builds only with a specific SDK/language combination

**Severity:** High for continuity (bus factor 1)
**Status:** Repair implemented on PR #3; historical reproductions below remain labelled.

**Current policy (2026-10-02).** `global.json` requires SDK **9.0.317** exactly,
`rollForward: disable`, no prerelease. This is the installed Windows build SDK,
not a runtime upgrade: every project still targets **net8.0**, with **C# 12.0**
and analyzer level **9.0** explicitly selected in Directory.Build.props. Install
that reviewed SDK on build agents; fail if absent rather than silently selecting
SDK 8/10. SDK updates require an explicit reviewed pin change and regression.
SDK selection and target runtime are independent:
[Microsoft SDK selection](https://learn.microsoft.com/en-us/dotnet/core/versions/selection),
[global.json matching](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).

The Jira review-only labels now use an explicit read-only array (empty for
installation/retirement, cloned server labels otherwise); no C# 14 collection
conversion is required. Parser regression uses `Enumerable.Reverse(rows)`
explicitly, never an instance/span Reverse that could return void.
DPAPI persistence cases are WindowsFact/WindowsTheory only; ephemeral antiforgery
and portable fail-closed validation continue on every OS. Windows never skips
these DPAPI cases. Unrelated Linux PowerShell/Skia/path failures are NOT skipped.
Windows PR baseline on SDK 9.0.317 built with zero warnings/errors; that does not
invalidate the retained SDK 8/10 Linux reproductions or count as repaired-source
acceptance. Exact new-source test evidence is recorded in the canonical register.

With SDK 8, `JiraIssueDraftService.cs` does not compile (C# 14 collection-expression conversion). With
SDK 10 and `LangVersion=latest` (C# 14), `InUseServiceItemParserTests` does not compile
(`Reverse()` binds to the span overload). `global.json` pins `8.0.100` with
`rollForward: latestMajor`, so the result depends on the machine. Three `UiPersistentDataProtectionTests`
fail rather than skip off Windows. **What would resolve it.** Pin one SDK and language version, fix
the two sources, and skip the DPAPI tests on non-Windows.

**Reproduction, 2026-10-01 (Linux, Ubuntu-packaged SDKs, no `LangVersion` override).** Both failures
are in files the UI branch does not change; they reproduce identically on master `214690e`.

- SDK 8.0.131 (`global.json` pinned to it with `rollForward: disable`), `dotnet build SecureOps.sln`:
  `src/SecureOps.Infrastructure/OperationalRecords/JiraIssueDraftService.cs(204,80): error CS7036:
  There is no argument given that corresponds to the required parameter 'list' of
  'ReadOnlyCollection<string>.ReadOnlyCollection(IList<string>)'`.
- SDK 10.0.112 (selected by the committed `global.json`), `dotnet build SecureOps.sln`:
  `tests/SecureOps.Tests.Unit/InUse/InUseServiceItemParserTests.cs(43,68): error CS0023: Operator '.'
  cannot be applied to operand of type 'void'`. All `src/` projects, including `SecureOps.Ui`, build
  with 0 warnings and 0 errors.
- Off-Windows test failures, identical on master and the UI branch once line 43 is changed (in a
  throw-away copy only) to `Enumerable.Reverse(rows)`: unit 4 (`ApiReleasePackagingContractTests` x2,
  missing `src/SecureOps.Api/obj/project.assets.json`; `SccmFailureEvidenceTests`, PowerShell
  execution policy unsupported; `AuditConfigurationValidatorTests.Validate_WhenProductionFailOpen_Throws`,
  publish-directory message precedes the expected one); integration 12 (DPAPI "requires Windows" x5,
  SkiaSharp native library x7). These are Codex-owned.

---

## G-31 — No per-owner usage signal for frequently used links

**Endpoints:** `GET /api/v1/resources/links`, `GET /api/v1/resources/me`
**Severity:** Low–medium — an operator looking for "the link I use every shift" has search and Favorilerim only
**Status:** Open (raised by the 2026-10-01 shift set journey pass)

The catalogue query has no "favourites first" or "recently opened" ordering, and the API records no per-owner
opening history. **What the UI does.** Points to Favorilerim and personal groups for frequently used links and
invents no usage order. **What would resolve it.** Either a `favouritesFirst` sort on the catalogue query, or —
only after an explicit decision that frames it as a personal convenience, not employee activity monitoring — a
bounded, owner-only "recently opened" list.

---

## G-32 — Favourites view has no server-side paging

**Endpoint:** `GET /api/v1/resources/me`
**Severity:** Low
**Status:** Open

Favorilerim filters and pages the bounded personal projection (at most 200 favourites) in the UI, while Tüm
bağlantılar pages on the server. Both show the same range and page wording. **What would resolve it.** A
`favouritesOnly` parameter on `GET /api/v1/resources/links` so both views share one paging path.

---

## G-34 — Concurrent first registrations can deadlock in the access store

**Code:** `SqlAccessRepository.EnsureUserAsync` (`src/SecureOps.Infrastructure/Access/SqlAccessRepository.cs`, latest-request read)
**Severity:** Medium (a first sign-in can fail once under concurrency; no data is corrupted)
**Status:** Fixed in source and synthetic LocalDB on `fix/access-user-registration-deadlock-20261006`.
Installed TEST has not been upgraded; the Service Accounts module and its serial test collections are unchanged.

**Observed (2026-10-06, local LocalDB, synthetic data).** `ServiceAccountPersistedAccessSqlTests` failed once in a full
integration run (6 s instead of about 1 s, i.e. the SQL Server deadlock-detection interval). Reproduced under contention:
`SqlException 1205 … chosen as the deadlock victim` thrown from `EnsureUserAsync` (stack: `SqlAccessRepository.cs` line 102,
called from the test's `ApprovedAsync`) while another class registered users at the same time.

**Confirmed local cause (2026-10-06).** Inside the registration transaction,
`SELECT TOP (1) AccessRequestId FROM security.AccessRequests WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId ORDER BY RequestedAt DESC`
has no index on `UserId` for all statuses (only the filtered `UX_SecurityAccessRequests_Pending` and
`IX_AccessRequests_StatusPage (Status, RequestedAt, AccessRequestId)`), so it scans and takes range locks well beyond the
user's rows. Captured engine graphs show RangeS-U lock cycles on `IX_AccessRequests_StatusPage` at this exact read,
including cycles between concurrent scans before request insertion. The original scan hypothesis is confirmed;
the earlier insertion/conversion explanation was more specific than the evidence supported.

**What would resolve it (platform owner).** An index on `security.AccessRequests (UserId, RequestedAt DESC)` (numbered
migration), and/or one retry of the whole registration transaction on 1205. **Module side (done, test only):** the module
classes that call `EnsureUserAsync` run in one serial xUnit collection so the module suite does not trip over it; no
retry was added that could hide the defect.

**Original requested scope (implemented by the Access owner).** In `SqlAccessRepository.EnsureUserAsync`
(`src/SecureOps.Infrastructure/Access/SqlAccessRepository.cs`), two concurrent first registrations can deadlock (SQL error 1205)
on `SELECT TOP (1) … FROM security.AccessRequests WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId ORDER BY RequestedAt DESC`,
because no index covers `UserId` for all statuses. Add the next numbered migration with
`CREATE INDEX IX_AccessRequests_UserRequested ON security.AccessRequests (UserId, RequestedAt DESC) INCLUDE (Status)` (additive,
refuses replay, DBA guide note), and make the whole registration transaction retry once on 1205 (nothing else retried); add
a SQL integration test that runs at least 8 concurrent `EnsureUserAsync` calls for distinct new identities against a
disposable database and expects every call to succeed with exactly one pending request per user. Synthetic data only; do not
apply to the installed TEST system; do not change the Service Accounts module (its tests already run serially for G-34).

**Local evidence.** The original red test (`4acbb10`, 001-031) ran ten rounds of eight concurrent new identities:
73 succeeded, seven failed with 1205, and seven matching deadlock graphs were captured. A test-only 50 ms user-insert
delay ensures overlap; it is always removed. Migration 032 alone, before adding the retry, passed 80/80 with zero graphs.
The registration transaction now retries once on 1205 after connection/transaction disposal; post-commit reads and
other operations remain outside the retry. SQL probes verify one real victim recovers, a second victim propagates,
and a non-deadlock SQL error is not retried. Existing Pending/no-capability and durable rejection guards remain.
See [DBA note](access-registration-dba-032.md) for preflight, replay refusal, unchanged row/permission hashes,
reproduction commands and target approval requirements. Full validation results are recorded in the task's acceptance note.

---

## G-35 — Unsafe API requests lack a central CSRF guard

**Raised:** 2026-10-07, two independent reviews of PR #18.
**Owner:** Platform/API; minimal shared UI transport update authorized by the owner.

Windows Negotiate can automatically authenticate cross-origin CORS-simple forms.
There is no central Origin/antiforgery check before API form binding, including
Service Accounts usage-scan uploads. Authentication/capability checks alone do
not prevent this request from executing as the user.

**Decision:** [ADR-0029](adr/ADR-0029-api-csrf-origin-guard.md). All unsafe methods
require `X-SecureOps-Csrf: 1`; browser source headers must match explicitly
configured `ApiCsrf:AllowedOrigins`, and cross-site Fetch Metadata is denied.
The server-side Blazor API client supplies the header centrally, without inventing
Origin or forwarding browser headers. Scripts must adopt the same contract.

**Status:** Design recorded before code in `7fee54c`. Central API guard and
minimal shared UI transport implemented and locally verified. Release build 0/0,
full format verification, 1918 unit passes (1 skip), 356 integration passes
(117 skips), all 99 unsafe OpenAPI operations, real loopback cross-origin multipart
403 and interactive UI writes passed. See
[local evidence and limitations](api-csrf-local-acceptance-20261007.md).
Real Negotiate/IIS/F5 and deployment origin configuration remain unverified.
The no-write assertion concerns business/provider state, excluding required denial
audit and existing authentication/access/session lifecycle writes. Installed TEST,
corporate systems, provider writes, merge and deployment remain outside scope.

## Note: enums cross the wire as numbers

Not a gap. Recorded here because it caught out a test double, and now **formally frozen by the v1
contract** in backend `689e757c`: both enums carry explicit numeric assignments, the UI-integration
contract lists every value, and contract tests pin them. The documented rule is that the values must
not be renumbered or reordered, and that a string representation would require an explicitly
versioned API contract rather than a silent change.

The practical consequence is unchanged: any stub, fixture, or client written against these DTOs must
send integers, not names.

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

Confirmed against `f89f996` while building the Active Directory redesign, and recorded so nobody
re-opens the investigation:

- `membershipKind` reaches the membership-path response, not only the memberships response. It
  travels through `DirectoryMembershipGraph` into `MapPath`, so the membership check can answer
  **Birincil Grup** rather than flattening a primary-group membership into a direct one. No contract
  change was needed for the three-verdict check.
- `POST /api/v1/directory/groups/analysis` returns nested groups, effective members and parent
  memberships from a single bounded traversal, which is why the UI spends one call for three tabs.
- `POST /api/v1/directory/groups/export` refuses a partial effective-membership export with
  `DirectoryTraversalPartial` (422). The UI hides the button as well, but the server is the authority.
- `DirectoryProviderTimeout` is a distinct code from `DirectoryProviderUnavailable`. Both were being
  rendered with the same "unreachable" wording; that was a UI defect and is corrected.

**No new backend gaps were found in this milestone.**
