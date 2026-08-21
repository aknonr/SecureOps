# SecureOps.Ui

Blazor Server + MudBlazor operations UI. Hosted on IIS in-process.

Built against the API contracts in `docs/contracts/secureops-api-v1-ui-integration.md` and
`docs/contracts/secureops-api-v1.openapi.json`. Design rules live in
`docs/25-ui-enterprise-shell.md`; UI-raised backend gaps live in
`docs/26-ui-backend-contract-gaps.md`.

## The one rule that shapes everything

**The UI holds no authorization logic.** The browser session establishes *who* the operator is; the
API decides *what* they may do.

```
cookie session (OIDC later)  →  identity
GET /api/v1/access/me        →  AccessStatus + roles + capabilities
```

`Program.cs` registers **no** role or capability policies on purpose — a cookie-derived policy set
would be a second source of truth able to disagree with the API. Pages carry `[Authorize]` only and
gate their content on capabilities from `ICurrentAccessProvider`. Hiding an action only avoids a dead
end; the API re-checks every capability on every call.

Never present a permission the API did not report, and never let the operator choose their own role.

## Layout

```
Auth/            AccessLabels        Turkish names for role and capability codes (display only)
Configuration/   options bound from appsettings
Pages/           routable pages; *.cshtml are server-rendered auth pages
Security/        LocalReturnUrl      open-redirect and sign-in-loop guard
Services/        API clients, access state, error translation, input rules
Shared/          MainLayout, NavMenu, UserMenu, SecureOpsTheme
Shared/Components/  So* primitives reused by every page
wwwroot/brand/   replaceable placeholder assets (see its README)
wwwroot/css/     secureops-theme.css — semantic tokens only, no colour literals
```

### Key services

| Type | Role |
|---|---|
| `ICurrentAccessProvider` | Circuit-scoped `/access/me`, resolved once, 60s reuse, `Changed` event |
| `UiProblemFactory` | Translates every API failure into an operator-facing `UiProblem` |
| `ApiResponseReader` | Shared success/failure handling for all API clients |
| `AccountInputRules` | Client mirror of the server's account rules — never stricter |
| `IdentityLookupResultCache` | 30s reuse of an identical lookup already on screen |
| `ISignedInUserService` | Reads the cookie principal; issues the interim principal |

### Shared components

`SoPageHeader`, `SoProblemPanel`, `SoEmptyState`, `SoStatusBadge`, `SoLoading`, `SoFieldGrid` +
`SoField`, plus the `.so-panel` CSS class. Use these rather than new one-off markup, so states look
the same everywhere.

The four state components map to distinct situations, and mixing them trains operators to misread
the real ones:

| Situation | Component |
|---|---|
| Waiting on the server | `SoLoading` (`Inline` for a region, `Block` for a whole panel) |
| Nothing to show, and that is normal | `SoEmptyState` |
| The call failed | `SoProblemPanel` |
| Steady-state condition worth labelling | `SoStatusBadge` |

`SoLoading` is always indeterminate: no call this UI makes reports progress, and a percentage would
have to be invented.

## Routes

| Route | Purpose | Requires |
|---|---|---|
| `/login` | Single-action sign-in | anonymous |
| `/signed-out` | Sign-out confirmation | anonymous |
| `/session-expired` | Lapsed session, preserves return path | anonymous |
| `/access-denied` | Authorization refusal + how to request access | anonymous |
| `/error` | Unhandled server error, request reference only | anonymous |
| `/`, `/dashboard` | Genel Bakış | authenticated |
| `/identity-lookup` | PAM / AD lookup | `Identity.Lookup` |
| `/account` | Identity and session security | authenticated |
| `/access/me` | Status, roles, grouped capabilities | authenticated |
| `/audit-compliance`, `/diagnostics-readonly` | Future-phase placeholders | authenticated |

Interim auth endpoints: `POST /auth/sign-in`, `GET /auth/sign-out`. They establish identity only.
When an identity provider is approved they become a challenge/callback pair and `/login` is unchanged.

**Do not add a nav entry before its route exists** — the menu must never offer a dead link.

## Error handling

All failures flow through one path:

```
SecureOpsApiException → UiProblem → <SoProblemPanel Problem="…" OnRetry="…" />
```

`UiProblem` carries a title, plain-language explanation, ordered next steps, `Retryable`,
`RequiresRefresh`, and the correlation ID. Mapping is keyed on the API's stable `code`, not the HTTP
status, because one status covers several operator situations. The API's `retryable` extension
overrides the UI default. Unknown codes degrade to a safe status-derived message.

Never render raw exception text, stack traces, upstream response bodies, API URLs, or ports. Add new
wording to `UiProblemFactory`, not to a page.

## Theme

`Shared/SecureOpsTheme.cs` is the single source of truth for colour, radius, and typography.
`secureops-theme.css` contains **no colour literals** — it maps `--mud-palette-*` onto semantic
`--so-*` tokens. Change colour there, not in CSS.

Navy is structural, white and neutrals carry content, and **brand red (`--so-brand`) is chrome-only**
— the app bar rule, the sign-in card rule, the app icon. Never style page content with it: inside the
content area red means a problem, and that meaning has to stay unambiguous.

Dark mode starts from the OS preference and is mirrored onto `<html>` as **`.so-dark`** by
`MainLayout`. Write dark overrides against `:root.so-dark`. **`.mud-theme-dark` does not exist in
MudBlazor 6.16** — rules targeting it compile fine and silently never apply. See
`docs/25-ui-enterprise-shell.md` §6.

Exception: the three server-rendered auth pages load without a Blazor circuit and cannot read
`--mud-palette-*`, so `.so-auth-body` declares its own light/dark values. Keep them in step with the
theme class.

## Forbidden in this project

See `.cursor/rules/060-ui-rules.mdc`. Highlights:

- No `localStorage` / `sessionStorage` — state is server-side.
- No direct DbContext injection in `.razor`; the UI calls the API.
- No direct PowerShell invocation.
- No colour literals in CSS; extend the theme.
- No leaderboards or per-operator comparison widgets ("audit is not surveillance").
- No role selection presented to the user.
- No remediation controls.

## Running locally

Two hosts. Start the API first:

```powershell
dotnet run --project src/SecureOps.Api/SecureOps.Api.csproj --launch-profile "SecureOps.Api (Demo)"
dotnet run --project src/SecureOps.Ui/SecureOps.Ui.csproj --launch-profile "SecureOps.Ui (Demo)"
```

The UI reads the API address from `IdentityLookupApi:BaseAddress` (the legacy
`DemoMode:ApiBaseAddress` key is honoured only as a migration fallback). It never forwards its own
cookie to the API; in Development/Demo, `DemoApiAuthHeaderHandler` sends the
`DemoMode:ApiDemoActor` value in `X-SecureOps-Demo-Actor`, which the API's non-production bridge maps
to an actor.

**The Demo API also needs `Access__DemoCompatibilityEnabled=true`.** Without it the demo actor is
created as `Pending`, every capability check denies, and the UI correctly shows the "awaiting
approval" state — which looks like a broken demo. This is API launch configuration and is tracked as
G-5 in `docs/26-ui-backend-contract-gaps.md`.

Identity lookup works against the Demo API as of backend commit `4adab66c` (G-1, resolved). The
seeded mock account `pam12356` returns a full record; unknown accounts still return `404
IdentityNotFound`.

Note that looking a mock user up **by UPN** still returns not-found even though capabilities reports
`supportsUpnLookup: true` — tracked as G-7 in `docs/26-ui-backend-contract-gaps.md`. Use the
sAMAccountName form when testing.

If Chrome shows "Güvenli değil", run `dotnet dev-certs https --trust` once. No certificates are
committed.

## Testing

Pure UI logic is unit-tested in `tests/SecureOps.Tests.Unit/Ui/`: error translation, account input
rules, lookup result reuse, and return-URL safety. `LocalReturnUrl` is `internal` and reachable via
the `InternalsVisibleTo` entry in the csproj.

Responsive and visual behaviour is validated in a real browser at 1440×900, 1366×768, and 390px in
both themes. See `docs/25-ui-enterprise-shell.md` §8.
