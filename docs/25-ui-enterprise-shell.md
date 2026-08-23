# 25 — UI Enterprise Shell

Supersedes the presentation-demo direction in `docs/17-ui-demo-shell.md`,
`docs/18-ui-demo-shell-handoff.md`, and **sections 1–5 and 8 of
`docs/20-ui-visual-design-contract.md`**. Sections 6, 7, and 9 of doc 20 (responsive acceptance,
branding strategy, IIS awareness) remain in force and are extended here.

This milestone converts the presentation shell into an internal operations UI built directly on the
API contracts in `docs/contracts/secureops-api-v1-ui-integration.md`.

## 1. What changed in direction

Doc 20 §3 required a three-profile demo picker on `/login`, and §5 required a permanent
"Demo only" banner on the team page. Both were correct for a presentation shell and are wrong for
the product this is becoming. The superseding rules:

| Old direction (doc 20) | New direction |
|---|---|
| `/login` offers Platform Yöneticisi / Takım Lideri / Salt Okunur profiles | `/login` offers one sign-in action; no role or profile choice is ever presented |
| Role comes from the chosen cookie profile | Roles and capabilities come from `GET /api/v1/access/me` |
| "Demo ortamıdır…" boundary text on the login card | One quiet environment marker in the app bar, shown on any non-Production host |
| Team page mutates in-memory demo state behind a "Demo only" warning | Page removed; real access administration screens replace it (next milestone) |
| Top-right shows the role caption | Top-right shows the signed-in account with an account menu |

**Rule:** the UI must never present a permission it did not receive from the API, and never let the
operator choose their own authority.

## 2. Authorization model

The UI holds no authorization logic. `SecureOps.Ui/Program.cs` registers **no** role or capability
policies, deliberately: a cookie-derived policy set would be a second source of truth able to
disagree with the API.

```
browser session (cookie today, OIDC later)  →  "who is this"
GET /api/v1/access/me                       →  "what may they do"
```

- `ICurrentAccessProvider` resolves `/access/me` once per circuit (60-second reuse) and shares it
  with the layout, navigation, and pages.
- Pages carry `[Authorize]` only. Capability gating happens in the body, so a missing permission
  produces an explanation rather than a redirect to a dead end.
- Hiding an action is a courtesy that avoids dead ends. The API re-checks every capability.

`AccessStatus` drives three whole-page states — `Approved`, `Pending`, `Disabled` — each rendered
explicitly. A pending user sees why they are waiting, not an empty dashboard.

## 3. Routes

| Route | Purpose | Auth |
|---|---|---|
| `/login` | Single-action sign-in, `returnUrl` preserved | Anonymous |
| `/signed-out` | Confirms a completed sign-out | Anonymous |
| `/session-expired` | Session lapsed; returns to where the operator was | Anonymous |
| `/access-denied` | Authorization refusal, with a route to request access | Anonymous |
| `/error` | Unhandled server error, request reference only | Anonymous |
| `/` and `/dashboard` | Yönetim Panosu with `Reporting.ManagementView`; otherwise the operator Genel Bakış | Authenticated |
| `/directory/users` (and legacy `/identity-lookup`) | Kullanıcı Sorgulama: exact lookup plus tabbed directory evidence | Authenticated + `Identity.Lookup` |
| `/directory/groups` | Grup Sorgulama: exact group and its direct members | Authenticated + `Identity.Groups.View` |
| `/admin/sessions` | Aktif Oturumlar: application-session administration | Authenticated + `Access.ManageUsers` |
| `/admin/system-status` | Sistem Durumu: enterprise integration state | Authenticated + `Access.ManageUsers` |
| `/account` | Signed-in identity and session security | Authenticated |
| `/access/me` | Erişimim: status, roles, grouped capabilities | Authenticated |
| `/access/requests` | Erişim Talepleri: decision queue | Authenticated + `Access.ApproveRequests` |
| `/access/users` | Kullanıcılar: user list grouped by access state | Authenticated + `Access.ManageUsers` |
| `/access/users/{id}` | User detail, role editor, disable, request history | Authenticated + `Access.ManageUsers` |
| `/operational-records` | Operasyonel Kayıtlar: OR → Jira workspace | Authenticated + `OperationalRecords.View` |
| `/operational-records/{id}` | Source, workflow, and Jira transfer detail | Authenticated + `OperationalRecords.View` |
| `/reporting/operators` | Operatör Raporu: paginated per-operator usage | Authenticated + `Reporting.ManagementView` |
| `/audit-compliance`, `/diagnostics-readonly` | Truthful future-phase placeholders | Authenticated |

Interim endpoints `POST /auth/sign-in` and `GET /auth/sign-out` replace the former `/demo-auth/*`
pair. They establish identity only and grant no application authority. When an identity provider is
approved they become a challenge/callback pair; the login page keeps its shape.

## 4. Error presentation

Every backend failure is translated once, by `UiProblemFactory`, into a `UiProblem` carrying a
title, a plain-language explanation, ordered next steps, a retry flag, and the correlation ID.
`SoProblemPanel` renders it identically on every screen.

- Keyed on the stable API `code`, not the HTTP status — one status covers several distinct operator
  situations (a 409 may mean "someone else claimed this" or "the source record changed").
- The API's `retryable` extension overrides the UI default: the server is the authority on whether a
  durable command may be re-issued.
- `RequiresRefresh` marks states where authoritative server state must be reloaded before acting.
- Unknown codes degrade to a safe status-derived message, so a backend code added later never leaks
  raw API text.
- The reader also accepts the legacy `ApiErrorResponse` shape, so unmigrated endpoints still classify.
- Raw exceptions, stack traces, upstream bodies, API URLs, and ports never reach the UI.

## 5. Identity lookup behaviour

- Validation mirrors `IdentityAccountNormalizer` and is **never stricter than the server**:
  `DOMAIN\account` is accepted and previewed as normalized; UPN shapes are accepted; only multiple
  accounts, wildcard/LDAP characters, and out-of-allow-list characters are blocked client-side.
- `MaxAccountLength` and `SupportsUpnLookup` come from `GET /identity/lookup/capabilities`, with
  documented defaults if that call fails.
- Identical resubmits inside 30 seconds reuse the displayed result instead of spending a rate-limit
  slot and writing another audit entry. Reuse is always stated on screen, and **Yenile** always
  forces a real call. Purpose and event references are part of the reuse signature, because a
  different purpose is a different audited action. Failures are never cached.

## 6. Theme and tokens

`Shared/SecureOpsTheme.cs` is the single source of truth for colour, radius, and typography, closing
the divergence doc 20 §7 called out. `wwwroot/css/secureops-theme.css` defines **no colour literals**;
it maps `--mud-palette-*` onto semantic `--so-*` tokens, so MudBlazor components and custom markup
cannot drift.

### Palette: red, white, dark navy, neutral

Navy is the structural colour (app bar, drawer, headings, primary actions), white and the neutral
greys carry content, and red is the brand accent.

**Red is confined to the chrome** — the rule beneath the app bar, the rule above the sign-in card,
and the application icon. It never appears inside page content. That is what makes it compatible
with red's other job in an operations tool: within the content area red means a problem and nothing
else, so an operator can never mistake a status colour for decoration. Red as the *primary action*
colour was considered and rejected for exactly that collision.

The brand colour lives in the MudBlazor `Tertiary` slot (`#B81D2B` light, `#E05263` dark) so the
single-source rule holds; CSS reads it as `--so-brand`. No component uses `Color.Tertiary`.

> **Amends doc 20 §7**, which reserved red for "errors/critical only". That rule was written when
> the product had no identity of its own. The constraint behind it — red must never become
> ambiguous — is preserved by the chrome/content split above rather than by banning red outright.

### Dark mode

Built as an elevation ladder (`#0C1421` ground → `#141F31` panels → `#101A2A` drawer) rather than one
flat surface, avoiding pure black and pure white to reduce halation, and lightening the accent
because a mid-navy loses contrast on a dark ground. Body text is ~13.5:1 and secondary text ~5.6:1
against their own surface.

Dark mode **starts from the operating system preference** (`MudThemeProvider.GetSystemPreference()`
on first render). The project forbids `localStorage`, so a manual choice cannot outlive the circuit;
without this an operator on a dark desktop would face a white flash and re-toggle on every sign-in
and every reconnect. The account-menu toggle still overrides it for the rest of the session.

Two things a palette swap cannot express — tint strength and shadow depth — need to be stronger on a
dark ground. MudBlazor 6.16 signals dark mode **only** by rewriting `--mud-palette-*` on `:root`; it
emits no class, and CSS cannot branch on a variable's value. `MainLayout` therefore mirrors the
active palette onto `<html>` as **`.so-dark`**, and the overrides hook onto `:root.so-dark`. It is
set on the document element, not on `MudLayout`, because MudBlazor renders popovers into a container
at the end of `body`, outside the layout tree.

> Do not write rules against `.mud-theme-dark`. That class is never emitted in 6.16, and rules
> targeting it silently do nothing — which is precisely how the dark tints and shadows were lost
> until a computed-style check caught it.

The three server-rendered auth pages declare their own light/dark values, because they load without
a Blazor circuit and cannot read `--mud-palette-*`. Keep them in step with the theme class.

## 7. Branding

Assets live in `wwwroot/brand/` with replacement instructions in `wwwroot/brand/README.md`.
`secureops-mark.svg` exposes an `id="mark"` symbol referenced by the app bar and the auth pages and
inherits `currentColor`, so it follows the theme. Approved corporate assets replace files in place —
no page markup changes.

The application icon (`favicon.svg`, plus `favicon.ico` at 16/32/48 px) is a **red plate with a white
shield-and-check glyph**. It is standalone with baked colours because favicons receive no CSS and
cannot resolve a cross-file `<use>`. The 16 px ICO entry drops the shield and keeps only the check:
at that size a 2 px stroke closes into a blob, and the plate already carries the identity. The ICO is
committed and regenerated by `src/SecureOps.Ui/build/make-favicon.ps1` — deliberately **not** part of
the build, and it must be re-run by hand if the brand colour changes.

Nothing here is a corporate logo, wordmark, or third-party asset; everything is drawn locally. The
red is an aviation-adjacent enterprise crimson chosen for this product, not sampled from a protected
brand palette.

## 8. Responsive validation

Validated in real Chrome (puppeteer-core) against a live API, at **1440×900, 1366×768, and 390px**,
in **light and dark**, across `/login`, `/signed-out`, `/session-expired`, `/dashboard`,
`/identity-lookup`, `/access/me`, and `/account` — 42 captures. Measured result: **no horizontal
scroll on any page at any width in either theme**.

The stylesheet uses two breakpoints in total (1024px for two-column regions, 599px for compact
spacing); everything else reflows through auto-fit grids and `flex-wrap`.

## 9. Access administration (milestone 2A)

`/access/requests` is a request queue with a detail panel. It is **not** a user directory, and that
is a contract consequence rather than a design preference: no endpoint lists application users or
reads one user's access record (G-8), so a user is reachable only as the subject of a request.
`/access/users` therefore does not exist, and no nav entry points at one.

Rules specific to this screen:

- **Roles are chosen by an administrator, never by the user.** Nothing in the operator-facing UI
  offers a role choice for oneself.
- **Assigned roles and effective capabilities are shown separately**, and capabilities are only ever
  rendered from an API response. Role descriptions in the picker are written guidance, explicitly
  labelled as such; the UI never computes what a role grants.
- **Every write is confirmed** through a dialog naming the affected user, the action, and its
  consequence — including that role assignment is a *replace*, so omitted roles are removed. The
  dialog captures intent only; the page owns the call and disables all actions before the first
  await, which is what makes double submission unexpressible.
- **After any write, authoritative state is re-read.** The result panel renders the returned
  `CurrentAccessResponse`, not the submitted values, so server-side normalisation is visible.
- **Conflicts are never swallowed.** A failed write still triggers a list reload, so the action's
  problem and the list's problem are held in separate fields; sharing one let the reload clear the
  conflict before it rendered.

Client-side validation (`AccessDecisionRules`) is load-bearing here rather than cosmetic: the API
answers both malformed input and "already decided by someone else" with the same
`AccessRequestInvalidState` (G-9). Rejecting the input cases before they reach the wire is what makes
a returned 409 safe to present as a genuine concurrency conflict.

## 10. Operational Record → Jira (milestone 2B)

The workspace separates **source record**, **SecureOps workflow**, and **Jira transfer** into three
panels. Collapsing them into one status is what allows an operator to read "failed" on a record whose
Jira issue already exists and then create a second one.

Rules specific to this screen:

- **An existing Jira issue key blocks create in every state.** Checked before any per-state rule and
  asserted across the whole state machine by test.
- **`OperationalRecordCloseFailed` is a partial success, not a failure.** Jira exists; only the source
  close is outstanding. Retry may resume it; create is never offered.
- **`reconciliationRequired` marks an unknown outcome.** The workflow contract settles an unknown
  Jira outcome as `JiraCreateFailed` with `reconciliationRequired = true`, so the flag is the signal
  rather than any one stage; `CreatingJira` without an issue key is kept as a secondary signal for
  the window before the flag is set. Prominent amber panel, explicit duplicate-risk warning,
  correlation id, create blocked in every state, and retry only when `retryEligible` is also true.
- **Idempotency is the backend's.** No `Idempotency-Key` is sent, so the API's deterministic
  actor+command+record key applies and a repeat after refresh collapses onto the same command.
- **Retry follows authoritative state, not HTTP status.**
- **No polling.** The list endpoint is a rate-limited source refresh; refresh is deliberate, plus an
  automatic re-read after every write.

Red stays reserved for genuinely critical conditions. Stale, claimed, pending, and reconciliation
states use warning or informational tones.

## 11. Management reporting

Backend-authoritative, from two endpoints and nothing else:

| Endpoint | Used by |
|---|---|
| `GET /api/v1/reporting/management/summary` | Yönetim Panosu |
| `GET /api/v1/reporting/management/operators` | Operatör Raporu |

**The UI computes no metric.** Every figure on screen is a value the API sent. Bar widths and the
daily stack heights are proportions of those same counts — a way of drawing a number, not a new
measurement. No rates, no percentages, no totals the server did not report, and no "time saved":
ADR-0011 requires every figure to be explainable from persisted evidence, and a browser-derived
number would have no audit trail behind it.

### Authorization

Gated on the `Reporting.ManagementView` capability reported by `GET /api/v1/access/me`, never on a
role name and never on which operational screens happen to be reachable. Being able to run an
identity lookup or move a record to Jira says nothing about entitlement to cross-user analytics.

`/dashboard` serves two boards behind one route: the management report for capability holders, the
operator board for everyone else. Hiding is a courtesy only — both reporting endpoints re-check the
capability and audit every privileged read.

### Windows

Presets `today`, `7d`, `30d`, plus a custom range. `ReportingWindowSelection` mirrors the server's
rules (`ReportingWindowResolver`) so a range the API would reject is explained next to the date
fields instead of after a round-trip; the server re-validates regardless. Dates are **UTC calendar
days**, stated on the control, because the backend aggregates on UTC day boundaries and reading a
picked date as local would shift every bucket by the machine's offset. The end day is inclusive for
the operator and exclusive on the wire, clamped to now when that bound has not yet arrived. Custom
ranges are capped at 92 days, the same as the server.

The window shown beside the tabs is the one the API **resolved and echoed**, not the one requested.

### Unmeasured is not zero

The two states mean opposite things to a manager, and the report keeps them apart:

| Contract signal | Rendered as |
|---|---|
| `securityAndQuality.rateLimitEvents` is `null` | "Yeterli geçmiş veri yok" |
| A duration with `sampleCount: 0` and null min/avg/max | "Yeterli geçmiş veri yok" |
| Every counted metric is zero across the window | "Bu aralıkta kayıtlı kanıt yok" — with the explicit note not to read it as zero |
| `dataLimitations` | Always shown, in the server's order |

Limitation sentences are rendered through a reviewed translation keyed on the exact server text.
Anything unrecognised is shown verbatim: an untranslated sentence is a blemish, a paraphrased
limitation is a false statement about the data.

### Attention areas

Each item is one backend count with a threshold of "greater than zero" — nothing is scored, weighted,
or combined, so a manager who clicks through finds exactly the number quoted. Prevention counts
(duplicate create stopped, source changed, source closed) are listed but toned as **guards**, not
faults: a stopped duplicate is a safety mechanism working, and presenting it beside failures would
teach the reader to treat a correct outcome as an incident.

### Operator report

Operational analytics, not employee scoring, and the difference is on the page rather than in this
document. No rank numbers, no leader board, no derived per-person rates, no superlatives. The API
orders rows by recorded operation volume and the page says so plainly, together with the statement
that an operation count does not measure effort, difficulty, working hours, or contribution.

Pagination is server-side (`page`, `pageSize`, capped at 100 by the API); the client never holds the
full set, and the server's returned `page` is treated as authoritative. The endpoint performs no
directory enrichment, so the actor is the persisted corporate principal and the page says that too.

### Charts

`identityLookup.trend` is the **only** true time series in the v1 contract — the backend buckets
terminal lookup outcomes by UTC day. Every other reported figure is a point-in-time aggregate, so
nothing else is drawn as a trend: a line interpolated through numbers the server never bucketed would
be an invented metric. Adoption is therefore shown as its four reported figures rather than as a
usage curve, and the panel says a per-day adoption series is not available in this release.

Drawn with CSS heights rather than a chart library, so it follows the same `--so-*` tokens as
everything else and works in both themes. Exact values stay available in a table, which is also what
a screen reader gets.

## 12. Sign-in visual treatment

`/login`, `/signed-out`, and `/session-expired` share `LoginLayout` and render without a Blazor
circuit, so they stay reachable when no circuit can be established.

Above 900px the layout splits: an original wireframe route network on a dark navy field, and the
sign-in card. The artwork is inline SVG — a great-circle grid, three route arcs of which one carries
the brand accent, and two swept lines — authored for this project. No airline artwork, livery, or
logotype is reproduced. It is inline rather than an image so it inherits the palette, scales without a
second asset request, and can be `aria-hidden` as the decoration it is.

The panel is a dark navy field in **both** themes: it is brand surface rather than reading surface,
and flipping it to white in light mode would make the product a different page twice a day. The card
follows the viewer's theme normally.

Below 900px the panel is removed rather than stacked, because on a phone it would push the only
action on the page below the fold. The card carries its own compact lockup for that case, which is
hidden when the panel is present so the brand does not appear twice.

Other decisions: the primary action reads **"Oturum aç"**; authentication stays redirect-based and
provider-agnostic, so the form action changes and the page does not when OIDC replaces the interim
endpoint; there is no username, password, or role selection; and the environment marker is gated on
`ShowEnvironmentMarker`, so Demo and Test name themselves and Production does not.

## 13. Directory Explorer

Two exact-only screens, both audited on every call.

| Route | Purpose |
|---|---|
| `/directory/users` (also `/identity-lookup`) | Kullanıcı Sorgulama — one account, then its evidence in tabs |
| `/directory/groups` | Grup Sorgulama — one group and its direct members |

Both take an exact target and an **optional** description. Neither accepts a filter, a wildcard, or
LDAP syntax.

Every request is audited and rate-limited, so each tab loads **on first open**, not when the lookup
returns: fetching all five sections up front would spend an operator's quota on evidence they never
asked to see. Directory calls use the `normalizedAccount` the server resolved when there is one, and
the exact account the operator typed otherwise — the endpoints normalize server-side either way.

### The purpose is optional

Since backend `a607ac4`, `purpose` is optional on every read-only `/api/v1/directory/*` route.
Omitted, null, empty, and whitespace all mean the same thing, and the query is recorded either way —
what the operator now chooses is whether to add context to that record, not whether the record
exists. Both screens label the field **Açıklama** with the helper *"Opsiyonel — sorgu amacını
belirtmek isterseniz ekleyebilirsiniz"*, carry no required marker, and drop a step in label and
helper weight so the field reads as an offer rather than a demand.

`DirectoryPurposeInput` mirrors the server's `DirectoryLookupPurpose`: blank collapses to `null`
rather than to an empty string, a supplied value is trimmed **before** its length is measured, the
bound is 256 characters (`DirectoryExplorer:MaxPurposeLength`), and control characters are refused.
The server re-validates all of it; this exists so the operator sees the problem beside the field.

Purpose text is not a cache key and not a rate-limit partition — the backend excludes it from both,
so editing it cannot bypass either. The UI adds no throttling of its own: rate limiting is the
server's, and a 429 renders through the ordinary `RateLimitExceeded` experience.

**One exception, and it is not a directory route.** The `Genel` tab is backed by
`POST /api/v1/identity/lookup`, whose contract still requires a purpose. Rather than gate the whole
screen on that, the requirement is raised inside the tab that has it: the other four tabs work with
no reason at all, and `Genel` asks for one when the operator wants identity fields. No default is
invented to fill the gap — a purpose the operator never wrote would be a fabricated audit entry.

Write workflows are untouched. An access decision and a session revocation still require a reason,
because those are actions taken against someone and the justification is what makes them defensible
afterwards. Looking something up is not.

Nothing traverses a graph in the browser. Direct groups, nested groups, membership paths, and
privileged evidence are all computed server-side; recomputing or extending any of it here would
produce an answer nobody could audit.

### Tabs

`Genel` reuses the existing exact-lookup result. `Gruplar`, `Hesap Sağlığı` and `Servis / SPN`
require `Identity.Groups.View`. `Yetkili Üyelikler` requires `Identity.PrivilegedGroups.View` and is
**absent** rather than present-and-refusing when that capability is missing.

### Direct and nested membership

The contract returns two sets and the screen never merges them. A direct membership is granted on the
group itself; a nested one comes through another group and is removed somewhere else entirely.
Presenting them as one list is what makes an operator revoke the wrong thing, so each list carries
that instruction explicitly.

`alsoTransitivelyReachable` is a badge on the single direct row, not a second row. The same group
listed twice reads as two grants, which is exactly the confusion the split exists to prevent. Nested
rows additionally show `minimumDepth`.

### Membership path

"Nasıl üye?" answers the question actually asked during an authorization incident, using the server's
own proof. A one-group chain is a direct membership and is styled distinctly from a nested one.
Multiple returned paths are all rendered — an operator removing only the first would not remove the
membership — and `pathsTruncated` says so in as many words.

**A bounded traversal is never a negative answer.** `isMember=false` may be shown as "üye değil" only
when the server reports no limit and no truncation (`DirectoryView.NegativeIsConclusive`). Otherwise
the screen says the traversal was incomplete and that the result must not drive an authorization
decision. Every reached bound is listed separately — a depth limit and a provider result limit call
for different follow-up, and a detected cycle is a directory finding in its own right.

### Account health

Every field on this contract is nullable and unknown is rendered as unknown. A directory that did not
return an attribute has not said the attribute is false.

`lastLogonTimestampUtc` is the replicated attribute. It is labelled **"Son görülen oturum zamanı
(yaklaşık)"** — in the label itself, not only in a note beneath it, so the caveat survives being
copied into a ticket. It is never presented as an exact last sign-in.

### Service evidence

Titled *göstergeler*, not a classification. The API reports what the directory objects say — object
class, SPNs, `managedBy` — and does not decide that an account is a service account; neither does the
UI. SPN lists are bounded server-side, and when truncated the screen states that the **count is
authoritative and the list is not**, so nobody counts visible rows and reports a smaller number.

### Privileged membership

Restrained by design. Privileged membership is a normal, expected property of an administrator
account — a fact to establish during an investigation, not an incident. A match is marked with an
accent edge, not an alarm fill; colouring every match red would make the screen useless to the people
whose job requires those memberships. The configured group set is server-owned, and a group that is
configured but missing from the directory is surfaced rather than dropped.

### Group lookup

Metadata and members are separate capabilities and separate calls, so an operator who may see that a
group exists does not automatically see who is in it; a refused member list leaves the metadata
standing. Members are direct only. A nested group appears as a member and links to **its own exact
lookup** rather than being expanded in place. Paging is forward-only because the continuation token
is — "Daha fazla yükle" appends rather than pretending to offer random access the API does not have.

## 14. Application sessions

| Route | Purpose | Capability |
|---|---|---|
| `/admin/sessions` | Aktif Oturumlar: server-side session administration | `Access.ManageUsers` |
| `/account` | "Oturumum" panel for the caller's own session | Authenticated |

Only what the contract exposes: identifiers and timestamps. No IP address, no device, no user agent,
and above all **no session handle** — the opaque `__Host-SecureOps.ApplicationSession` cookie is the
credential, and a screen that displayed it would turn a diagnostic view into a way to impersonate
people. The identity column is the persisted user id; this endpoint performs no directory enrichment,
and no name is fetched from elsewhere to fill the gap.

Revocation requires confirmation and a reason. The dialog states the consequence before the button —
the operator's next request is unauthenticated and work in progress can be interrupted — and states
that it **does not disable the account**, which prevents the opposite mistake. A failed revoke is kept
separate from the list problem so it survives the reload that follows; an already-ended session is
reported as a stale-view conflict, because that is usually what it is.

Idle and absolute limits are shown on `/account` from `SessionPolicyResponse`, which the API supplies.
They are not hard-coded here.

## 15. Integration status

`/admin/system-status` reads `GET /api/v1/health/enterprise-integrations`, which is Admin-only. The
page shows provider name, configured selection, and a status word — never a URL, credential, account,
or remote payload.

`Devre dışı` is toned neutral, not as a fault: a provider deliberately switched off is a configuration
decision, and colouring it red sends operators chasing a non-problem. An unrecognised status renders
as itself with a neutral tone rather than being coloured green by a default branch.

Three providers are reported, so three appear. The database, background jobs, and the audit store have
no entry on this endpoint, and the page says so explicitly instead of inventing tiles that would put a
green light next to something nobody checked.

## 16. Reporting contract hardening (G-14, G-15, G-17)

| Was | Now |
|---|---|
| Duration matched on English `definition` text | Matched on stable `key`: `importToPreview`, `claimToJiraCreation`, `claimToCompletion` |
| Limitations were English prose | Keyed on `code`; `message` is fallback only, never behaviour |
| Zero was indistinguishable from absent history | `coverage` separates a measured zero from an unmeasured one |

Duration labels ignore `definition` and array position entirely; the contract states both are
presentation details, so a reworded definition or a reordered array must not change what a row means.
An unknown key falls back to the server's own definition text.

Coverage drives three distinct states, and the verdict is taken from `coverageComplete` rather than
recomputed from the dates:

- **Complete** — a quiet line confirms that a zero on the page is a measured zero.
- **Partial** — a notice names the boundary and how many days of the requested window have no
  evidence ("istenen 30 günün ilk 20 günü"), and says that part must be read as unmeasured, not zero.
- **None** — no persisted reportable evidence exists for the window at all.

The empty-window state now distinguishes *no activity* from *no records*: with complete coverage it
says the window is a measured zero; without, it says evidence is missing and must not be read as zero.

`G-16 remains open.` Only `identityLookup.trend` is bucketed by day, so it remains the only trend
drawn. No adoption or Operational Record/Jira series was inferred.

Session-governance aggregates (`sessionGovernance`) are surfaced as their own dashboard panel, with a
link into `/admin/sessions` for holders of `Access.ManageUsers`.

## 17. Navigation

Capability-driven throughout, and a group heading appears only when at least one of its entries does:

```
Genel Bakış / Yönetim Panosu
Operasyon    → Operasyonel Kayıtlar          OperationalRecords.View
Dizin        → Kullanıcı Sorgulama           Identity.Lookup
             → Grup Sorgulama                Identity.Groups.View
Raporlama    → Yönetim Panosu                Reporting.ManagementView
             → Operatör Raporu               Reporting.ManagementView
Yönetim      → Erişim Talepleri              Access.ApproveRequests
             → Kullanıcılar                  Access.ManageUsers
             → Aktif Oturumlar               Access.ManageUsers
             → Sistem Durumu                 Access.ManageUsers
Erişim       → Erişimim                      authenticated
```

An account with an Admin-sounding role but without `Access.ManageUsers` gets no administration group.
The API would refuse those screens anyway, and offering them is a dead end that reads as a
permissions bug.

## 18. Layout notes worth keeping

Two defects found by the responsive checks, both worth recording because they recur:

- `display: flex` on a `<th>` takes the cell out of the table box model, and the table then stops
  containing its own width. The flex container must be a wrapper inside the cell.
- A wide table inside `overflow-x: auto` scrolls correctly, but its overflow still propagated to
  `.mud-layout` and grew the document. `.mud-layout { overflow-x: clip }` stops that. `clip` rather
  than `hidden`: it creates no scroll container, and because `.mud-layout` is only `position:
  relative` it does not become a containing block for the fixed app bar or drawer.

The Directory user page collapses its two-column split once a principal resolves. Its group, SPN, and
privileged tables are the widest content in the product, and in a half-width column the trailing
"Nasıl üye?" action is permanently scrolled out of reach.

## 19. Verification, and what it proves

Three layers, and they prove different things.

**Synthetic end-to-end, against the real API** with `OperationalRecords__SourceProvider=Fake`
(Development, Demo, and Test only). Every transition is produced by the real SecureOps workflow, not
by UI state: preview advances the record, create yields a real Jira key and reaches `Completed`, a
repeat create returns the same key rather than a second issue, and the stale, closed, and missing
fixtures are each rejected by the backend with their own code and create no Jira issue. Thirty checks.

**Contract-shaped stub**, for the states the fake providers cannot produce — reconciliation required,
retry blocked, live claim contention, rate limiting, Jira provider failure. Forty checks. This proves
**presentation only**, and the report says so rather than letting a green tick imply more.

**Contract-shaped stub for reporting, directory, sessions, and integration status.** 117 browser
checks across complete, partial and absent evidence coverage; direct, nested, multi-path, truncated,
conclusively-negative and traversal-limited membership; zero, one, many and truncated SPN lists;
populated and entirely-unknown account health; privileged membership allowed and forbidden; group
member paging and mixed member types; session listing, revoke confirmation, revoke failure and
forbidden access; integration status configured, disabled, unavailable and forbidden; four viewports
(1920, 1366, 834, 390) and both themes. This is presentation verification: the Directory Explorer
read model needs a real domain, and the reporting read model needs SQL Server with migrations 001–007.

Provider selection is never a user-facing setting. When the source is `Disabled`, the list renders the
ordinary service-unavailable experience with its correlation reference and never names the provider
or its configuration. Synthetic records are identifiable by their own `SYN-` source codes rather than
by any UI label claiming they are real.

**Synthetic readiness is not Turuncu Hat readiness.** See `docs/26-ui-backend-contract-gaps.md`.

## 20. Out of scope for this milestone

Read-only diagnostics and the audit/compliance view remain truthful placeholders; their navigation
entries say so rather than offering a dead link. Reporting is confined to the two management
endpoints — no per-day adoption series, no manual-effort baseline, and no time-saved figure exists to
show. The Directory Explorer is read-only throughout: no AD write, no membership change, no password
entry, and no client-side graph traversal. Backend gaps are tracked in
`docs/26-ui-backend-contract-gaps.md`.
