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

Dark mode is built as an elevation ladder (`#0C1421` ground → `#141F31` panels → `#101A2A` drawer)
rather than one flat surface, avoids pure black and pure white to reduce halation, and lightens the
accent because a mid-navy loses contrast on a dark ground. Body text is ~13.5:1 and secondary text
~5.6:1 against their own surface.

The three server-rendered auth pages declare their own light/dark values, because they load without
a Blazor circuit and cannot read `--mud-palette-*`. Keep them in step with the theme class.

## 7. Branding

Placeholders live in `wwwroot/brand/` with replacement instructions in `wwwroot/brand/README.md`.
`secureops-mark.svg` exposes an `id="mark"` symbol referenced by the app bar and the auth pages;
`favicon.svg` is standalone with baked colours because favicons receive no CSS. Approved corporate
assets replace files in place — no page markup changes. Nothing here is a corporate logo.

## 8. Responsive validation

Validated in real Chrome (puppeteer-core) against a live API, at **1440×900, 1366×768, and 390px**,
in **light and dark**, across `/login`, `/signed-out`, `/session-expired`, `/dashboard`,
`/identity-lookup`, `/access/me`, and `/account` — 42 captures. Measured result: **no horizontal
scroll on any page at any width in either theme**.

The stylesheet uses two breakpoints in total (1024px for two-column regions, 599px for compact
spacing); everything else reflows through auto-fit grids and `flex-wrap`.

## 9. Out of scope for this milestone

Access administration screens (`/access/requests`, `/access/users`) and the Operational Record → Jira
screens are the next two milestones. Navigation entries for both are deliberately absent until their
routes exist, so the menu never offers a dead link. Backend gaps are tracked in
`docs/26-ui-backend-contract-gaps.md`.
