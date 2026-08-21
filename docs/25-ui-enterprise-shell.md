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
| `/` and `/dashboard` | Genel Bakış: can I work, what can I do, what is next | Authenticated |
| `/identity-lookup` | PAM / AD lookup | Authenticated + `Identity.Lookup` |
| `/account` | Signed-in identity and session security | Authenticated |
| `/access/me` | Erişimim: status, roles, grouped capabilities | Authenticated |
| `/access/requests` | Erişim Talepleri: decision queue | Authenticated + `Access.ApproveRequests` |
| `/access/users` | Kullanıcılar: user list grouped by access state | Authenticated + `Access.ManageUsers` |
| `/access/users/{id}` | User detail, role editor, disable, request history | Authenticated + `Access.ManageUsers` |
| `/operational-records` | Operasyonel Kayıtlar: OR → Jira workspace | Authenticated + `OperationalRecords.View` |
| `/operational-records/{id}` | Source, workflow, and Jira transfer detail | Authenticated + `OperationalRecords.View` |
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

## 11. Verification, and what it proves

Two layers, and they prove different things.

**Synthetic end-to-end, against the real API** with `OperationalRecords__SourceProvider=Fake`
(Development, Demo, and Test only). Every transition is produced by the real SecureOps workflow, not
by UI state: preview advances the record, create yields a real Jira key and reaches `Completed`, a
repeat create returns the same key rather than a second issue, and the stale, closed, and missing
fixtures are each rejected by the backend with their own code and create no Jira issue. Thirty checks.

**Contract-shaped stub**, for the states the fake providers cannot produce — reconciliation required,
retry blocked, live claim contention, rate limiting, Jira provider failure. Forty checks. This proves
**presentation only**, and the report says so rather than letting a green tick imply more.

Provider selection is never a user-facing setting. When the source is `Disabled`, the list renders the
ordinary service-unavailable experience with its correlation reference and never names the provider
or its configuration. Synthetic records are identifiable by their own `SYN-` source codes rather than
by any UI label claiming they are real.

**Synthetic readiness is not Turuncu Hat readiness.** See `docs/26-ui-backend-contract-gaps.md`.

## 12. Out of scope for this milestone

The Operational Record → Jira screens are the next milestone. Their navigation entry is deliberately
absent until the routes exist, so the menu never offers a dead link. Backend gaps are tracked in
`docs/26-ui-backend-contract-gaps.md`.
