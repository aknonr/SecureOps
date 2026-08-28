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
| `/directory/users?account=…&tab=…` (and legacy `/identity-lookup`) | AD Kullanıcı ve Hesap Sorgulama: exact lookup plus tabbed directory evidence | Authenticated + `Identity.Lookup` |
| `/directory/groups?group=…` | AD Grup Analizi: exact group, its members, and bounded nested analysis | Authenticated + `Identity.Groups.View` |
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

## 13. Active Directory screens

Two exact-only screens, both audited on every call. Backend `f89f996` added real-world group
analysis; this milestone rebuilt the surface around it so the screens read as Windows operations
rather than as a directory browser.

| Route | Purpose |
|---|---|
| `/directory/users` (also `/identity-lookup`) | AD Kullanıcı ve Hesap Sorgulama |
| `/directory/groups` | AD Grup Analizi |

Neither accepts a filter, a wildcard, or LDAP syntax. Both take one exact target.

### Search, then result

Each screen has two states. Before a query it is a search box and nothing else. After one, the query
collapses to a single compact line and the evidence takes the full width of the page. An operator
who has already asked the question does not need the form that asked it still occupying a third of
the screen.

### The reason field is gone

The read-only directory routes no longer ask for one. `a607ac4` made `purpose` optional; this
milestone removed the field from both screens, along with the legacy alert reference and
`turuncuhatEvtId` inputs that survived from the first identity-lookup design. The query is still
recorded on every call — what changed is that the record no longer depends on the operator filling
in a box the backend does not require.

`POST /api/v1/identity/lookup` no longer receives one either. Nothing in the UI now sends a directory
purpose, and `DirectoryPurposeInput` is retained only for the write workflows described below.

Write workflows are untouched. An access decision and a session revocation still require a reason,
because those are actions taken against someone and the justification is what makes them defensible
afterwards. Looking something up is not.

### Vocabulary

Active Directory attribute names do not reach the screen. Every field is labelled in the language an
operator uses, and the technical concept gets one sentence where it appears rather than a glossary
somewhere else.

| Screen | Underlying attribute |
|---|---|
| Ad Soyad | `displayName` |
| AD Kullanıcı Adı / AD Grup Hesap Adı | `sAMAccountName` |
| Kurumsal Oturum Adı (UPN) | `userPrincipalName` |
| Üyesi Olduğu Gruplar | `memberOf` |
| Grup Türü | `groupType` category |
| Grup Kapsamı | `groupType` scope |
| Grup Sorumlusu | `managedBy` |
| AD Nesne Yolu | `distinguishedName` |
| AD Nesne Türü | `objectClass` |

Distinguished names and SIDs are the one place raw directory naming belongs. They stay folded inside
a disclosure rather than leading the overview.

Two things are deliberately **not** translated. Group scope renders as `Global`, `Universal` and
`Domain Local` because those are the words in the Windows tooling the operator will open next, and a
Turkish paraphrase would break the match. The nav section label `Active Directory` carries
`lang="en"`: under Turkish case mapping, `text-transform: uppercase` would render it
*ACTİVE DİRECTORY*.

### User screen tabs

`Genel Bilgiler` reuses the exact-lookup result. `Grup Üyelikleri`, `Hesap ve Parola Bilgileri` and
`Servis Hesabı Göstergeleri` require `Identity.Groups.View`. `Ayrıcalıklı Grup Üyelikleri` requires
`Identity.PrivilegedGroups.View` and is **absent** rather than present-and-refusing when that
capability is missing. Each tab loads on first open: every request is audited and rate-limited, and
fetching all five up front would spend an operator's quota on evidence they never asked to see.

### Three kinds of membership

The screen tells apart what Active Directory itself keeps separate, and never merges them:

- **Doğrudan Üyelik** — a backlink on `memberOf`. This is where a membership is removed.
- **Birincil Grup** — resolved from `primaryGroupID`. It is not in `memberOf` at all, and it cannot
  be removed from the group's member list; it is changed on the account. Its section carries an edge
  the others do not, because treating it like a direct membership leads to an operation that fails.
- **Dolaylı / İç İçe Üyelik** — reached through another group, and removed somewhere else entirely.

`alsoTransitivelyReachable` is a badge on the single direct row, not a second row. The same group
listed twice reads as two grants, which is exactly the confusion the split exists to prevent.

### Account and password evidence

Every field on this contract is nullable and unknown is rendered as unknown. A directory that did not
return an attribute has not said the attribute is false.

`lastLogonTimestampUtc` is the replicated attribute. It is labelled **"Son görülen oturum zamanı
(yaklaşık)"** — in the label itself, not only in a note beneath it, so the caveat survives being
copied into a ticket. It is never presented as an exact last sign-in.

### Service account indicators

Titled *göstergeler*, not a classification. The API reports what the directory objects say — object
class, SPNs, `managedBy` — and does not decide that an account is a service account; neither does the
UI.

**An account with no SPN is a fact, not a failure.** It renders as *"Bu hesap üzerinde tanımlı SPN
bulunmuyor."* — never as a directory outage, and never as an error panel. When SPN lists are
truncated server-side the screen states that the **count is authoritative and the list is not**, so
nobody counts visible rows and reports a smaller number.

When principal evidence resolves but the group counts do not, the tab keeps the evidence it has and
reports the gap in place: *"Hesap bilgileri alındı, grup sayıları alınamadı."* Losing the whole tab
because one of its two calls failed throws away a working answer.

### Privileged membership

Restrained by design. Privileged membership is a normal, expected property of an administrator
account — a fact to establish during an investigation, not an incident. A match is marked with an
accent edge, not an alarm fill; colouring every match red would make the screen useless to the people
whose job requires those memberships. The configured group set is server-owned, and a group that is
configured but missing from the directory is surfaced rather than dropped.

## 13a. Group analysis

Cost is the organising principle, because the three calls behind this screen do not cost remotely the
same thing. Group metadata returns in milliseconds. Member enumeration can take hundreds of
milliseconds, and on real groups it sometimes fails only after ten seconds. The bounded nested walk
is the expensive one.

So the screen loads in that order, and never speculatively:

| Tab | Call | When |
|---|---|---|
| Genel Bakış | `groups/lookup` | with the query |
| Doğrudan Üyeler | `groups/members` | on first open |
| İç İçe Gruplar · Tüm Etkin Üyeler · Üyesi Olduğu Gruplar | `groups/analysis` | on an explicit button |
| Üyelik Kontrolü | `principals/membership-paths` | on submit |

The walk sits behind **Analizi Çalıştır** rather than a tab click because one call produces nested
groups, effective members and parent memberships together: three tabs, one traversal. Opening a tab
is not a decision to spend it.

### A failed member list is not an outage

This is the behaviour the milestone exists to get right. A fast successful overview plus a failed
member enumeration is **not** "Active Directory is down". The overview stays exactly where it is, and
only the failing section reports its own problem, with its own retry:

> Grup bilgileri alındı ancak üye listesi tamamlanamadı.

`DirectoryProviderTimeout` gets its own words — *"Active Directory sorgusu süre sınırı içinde
tamamlanamadı"* — and never borrows *ulaşılamıyor*. A query that ran out of time and a directory that
cannot be reached send an operator to two different investigations.

### Nested navigation

A nested group opens **its own exact lookup**, and a breadcrumb rooted at `Active Directory` records
how the operator got there. The browser never builds topology and never traverses a graph; the server
returns it. Recomputing or extending any of it here would produce an answer nobody could audit.

### Partial traversal

`isComplete=false` at HTTP 200 is a bounded result, not an empty one. The rows shown are real; the
list is not finished. The banner says so and states the consequence plainly — the result must not be
used to conclude that a membership does not exist — and lists every bound reached separately, because
a depth limit, a provider result limit and a detected cycle each call for different follow-up.

### Membership check

"Üye mi?" answers the question actually asked during an authorization incident, using the server's own
proof, and gives one of exactly three verdicts:

- **Üye** — with the kind of membership, so a primary-group answer is not mistaken for a direct one.
- **Üyelik bulunamadı** — permitted **only** when the server reports no limit and no truncation
  (`DirectoryView.NegativeIsConclusive`).
- **Üyelik doğrulanamadı** — a bounded traversal. Never rendered as a negative answer.

### CSV export

Gated on `Identity.Groups.Export` from `/api/v1/access/me`, never on a role name. Without the
capability the button is not drawn. The file is produced by the server, streamed through
`window.secureOpsDownload`, and keeps the server's own filename; the browser does not build a CSV.

Partial effective-membership results are not exportable, and the server refuses them with
`DirectoryTraversalPartial` — a spreadsheet outlives the banner that qualified it.

### Group metadata and members are separate capabilities

An operator who may see that a group exists does not automatically see who is in it, and a refused
member list leaves the metadata standing. Members are direct only; a nested group appears as a member
and links to its own lookup rather than being expanded in place. Paging is forward-only because the
continuation token is — "Daha fazla yükle" appends rather than pretending to offer random access the
API does not have.

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
Active Directory
             → AD Kullanıcı ve Hesap Sorgulama   Identity.Lookup
             → AD Grup Analizi                   Identity.Groups.View
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

The Active Directory screens use the full content width once a result resolves. Their group, SPN and
member tables are the widest content in the product, and in a half-width column the trailing row
action is permanently scrolled out of reach.

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

**Contract-shaped stub for reporting, sessions, and integration status.** 61 browser checks across
complete, partial and absent evidence coverage; session listing, revoke confirmation, revoke failure
and forbidden access; integration status configured, disabled, unavailable and forbidden. Its
directory assertions were retired when the Active Directory redesign replaced that surface.

**Contract-shaped stub for the Active Directory screens.** 131 browser checks: navigation naming and
casing; the legacy route; a true 404 on an unknown path; the removed reason, alert and Turuncuhat
fields; the search-then-result collapse; every user and group label; direct, primary and transitive
membership told apart; zero, one and truncated SPN lists; principal evidence usable while graph
evidence is not; group overview independent of member enumeration; a member enumeration that times
out while the overview stands; the analysis walk behind its button; nested navigation with
breadcrumbs; the three membership-check verdicts; partial traversal and the export it refuses; export
allowed and forbidden; 404, invalid input, 403, timeout and unavailable each in their own words; a
scan for leaked LDAP, provider and exception text on every error state; four viewports (1920, 1366,
834, 390) and both themes.

**Write-workflow guard.** 2 checks confirming session revocation still demands a reason — the
boundary the optional-reason delta was careful not to cross.

This is presentation verification: the Active Directory read model needs a real domain, and the
reporting read model needs SQL Server with migrations 001–007.

Provider selection is never a user-facing setting. When the source is `Disabled`, the list renders the
ordinary service-unavailable experience with its correlation reference and never names the provider
or its configuration. Synthetic records are identifiable by their own `SYN-` source codes rather than
by any UI label claiming they are real.

**Synthetic readiness is not Turuncu Hat readiness.** See `docs/26-ui-backend-contract-gaps.md`.

## 20. Out of scope for this milestone

Read-only diagnostics and the audit/compliance view remain truthful placeholders; their navigation
entries say so rather than offering a dead link. Reporting is confined to the two management
endpoints — no per-day adoption series, no manual-effort baseline, and no time-saved figure exists to
show. The Active Directory screens are read-only throughout: no AD write, no membership change, no account
enable/disable, no password reset, no LDAP credential form, no directory-wide enumeration, no XLSX
export, and no client-side graph traversal. Backend gaps are tracked in
`docs/26-ui-backend-contract-gaps.md`.

## 21. TEST pilot operations hardening

This milestone integrates the backend hardening from commit `3347b4a` and closes the TEST usability
findings operators reported. It is an operational-usability pass, not a visual redesign.

### Browser-session-aware API transport

The API application session is a cookie. Previously each typed `HttpClient` kept its own cookie
container, and `IHttpClientFactory` pools one handler chain per client name — so a container was
neither per-browser nor per-operator. A refresh opened another application session, navigation opened
more, and the Aktif Oturumlar page filled with duplicates of one person.

The UI now runs one transport strategy for every SecureOps API client:

- `UseCookies=false` on the primary handler, so no handler owns a cookie jar.
- A protected browser-session correlation value, issued as a claim inside the encrypted UI
  authentication cookie. It is stable for a browser session, shared by that browser's tabs, and
  different in a separate or private browser.
- `ApiSessionStore` keeps one server-side `CookieContainer` per correlation value.
  `ApiSessionCookieHandler` replays it and captures `Set-Cookie`. A first-request gate serialises
  simultaneous tabs so two of them establish one application session rather than one each.
- Clients stamp the correlation value on the request; the handler removes it before the request is
  sent, so it never reaches the API.

The handle is carried, never shown: it is not rendered, logged, put in a URL, or exposed to the
browser. Requests without a correlation value still succeed — they simply do not share a jar, which
is the safe degradation. Sessions are never merged by username.

`AddSecureOpsApiClient` is the only registration path, and a unit test asserts every typed client
takes `IApiSessionContext`, because a client that skipped it would fail silently.

### Sign-out ends the API session

`GET /auth/sign-out` calls `POST /api/v1/access/logout` **before** the UI authentication sign-out,
then drops the browser session's cookie jar. If the API call fails the operator is still signed out
and the failure is logged with its safe code and correlation ID only; refusing to sign someone out
because the API was unreachable would strand them signed in. The jar is dropped either way.

### Aktif Oturumlar

Columns are Kullanıcı, Oturum Başlangıcı, Son Aktivite, Bitiş / Süre, Kimlik Doğrulama, Durum,
İşlem. Identity leads with `displayName`, falling back to `principal` then `normalizedPrincipal`, and
stops at an explicit "Kimlik bilgisi yok" rather than inventing a person. The account line is omitted
when it would repeat the name. Internal identifiers and `accessVersion` moved under **Teknik
ayrıntılar**; they are support material, not identity. `isCurrent` marks **Bu oturum**. Revocation
still requires a reason, still states that it does not disable the AD account, and still reloads the
list afterwards.

### Directory navigation

`lookupKey` is used for group details, direct members, nested navigation, user→group and parent-group
navigation, and export. Display name is rendered; the server's key is sent. A distinguished name is
never sent. After an overview resolves, subsequent calls stop replaying whatever the operator typed.

Directory screens are addressable. The account, its open tab, and the current group live in the URL,
so browser Back restores the previous account and tab, Forward works, and group and member actions
are real anchors — Ctrl+Click, middle-click, and "Yeni sekmede aç" work without the UI implementing
them. Tab switches replace rather than push, so they do not bury the previous account. No secret,
token, or purpose statement goes in a URL.

Direct members page server-side at 25/50/100 with Önceki/Sonraki over the opaque continuation token.
Effective members are windowed at the same sizes over the server's already-bounded result; the counts
and the partial-result banner always describe the whole analysis, so paging can never make a partial
answer look complete. `Analizi Çalıştır` stays explicit, and Üyelik Kontrolü remains the way to
answer "is this user in this group".

### Operational Records

Labels come from the backend's stable `presentationState` and from nothing else: `NeedsAttention` →
İnceleme Gerekiyor, `Actionable` → Jira'ya Aktarılabilir, `InProgress` → İşlemde, `Completed` →
Tamamlandı. An unrecognised category reports as unknown rather than being folded into one of the four.
The list groups on the same value instead of re-deriving its own.

A restrained step line shows the operator sequence — Kaydı İncele → Jira Taslağını Önizle → Bilgileri
Doğrula → Jira Kaydı Oluştur → Kaynak Kaydı Tamamla — positioned from durable state, so it survives a
refresh. Failures are explained operationally: a changed source, a prevented duplicate, an unverified
outcome needing reconciliation, and a Jira success whose source close failed. A persisted Jira key
stays visible wherever one exists. Fencing, idempotency, mapping versions, and correlation internals
moved under **Teknik ayrıntılar ve destek bilgisi**.

### TEST simulation

When the backend reports `simulationMode`, a prominent **TEST SİMÜLASYONU** banner states that the
operation creates no record in the real Turuncu Hat or Jira, and repeats it inside the create
confirmation where the decision is actually made. The server-supplied notice is shown in addition to
that guarantee, never instead of it. The UI never decides that a run is simulated and never simulates
anything itself; the backend's synthetic scenarios are exercised as ordinary responses.

### Management reporting readiness

`ReportingPersistenceNotConfigured` and `ReportingUnavailable` are both `503` and were both falling
through to the generic "servis yanıt vermiyor". They are now distinct: not-configured is a readiness
state — "Yönetim raporlaması henüz etkin değil" — that is non-retryable and offers no retry button,
while unavailable stays a retryable service problem. Forbidden remains an ordinary authorization
state and no-evidence keeps its honest coverage wording. No metric is invented in any of them.

### Operatör Raporu

Removed from the primary navigation. The route, the capability, and the endpoint are unchanged; it is
reached as a drill-down from the Yönetim Panosu, where the window, coverage notice, and limitations
that stop per-actor counts reading as a ranking already sit. The anti-ranking statement on the page
is unchanged and pinned by a test.
