# 18 - UI Demo Shell Handoff

## 1. Purpose and Scope

This handoff records the current UI demo shell implementation for management presentation use. It is not Phase 2 production UI and does not start Phase 1 diagnostics.

## 2. Current Branch and UI Baseline

Current work is on `feature/ui-demo-shell`. The UI remains in `src/SecureOps.Ui` using Blazor Server and MudBlazor for authenticated pages.

## 3. Architecture Decisions

- `/login` is a dedicated server-rendered Razor Page with `LoginLayout`.
- Authenticated pages are Blazor Server components hosted by `_Host.cshtml`.
- Demo sign-in posts to `/demo-auth/sign-in` and uses ASP.NET Core antiforgery validation.
- Cookie sign-in/sign-out happens only in server endpoints with writable responses.
- No backend API, diagnostic, remediation, AD, PAM, LDAP, Entra, or SSO implementation is added by this UI work.

## 4. Demo Authentication Configuration Matrix

| Environment | DemoMode:Enabled | DemoMode:AllowMockAuthentication | Result |
|---|---:|---:|---|
| Development | true | true | Demo login allowed |
| Demo | true | true | Demo login allowed |
| Development/Demo | false | any | Demo login blocked |
| Development/Demo | true | false | Demo login blocked |
| Production | any | any | Demo login blocked |

Committed `appsettings.json` keeps demo auth disabled.

## 5. Routes and Role Access Matrix

| Route | Access | Notes |
|---|---|---|
| `/login` | Anonymous | Server-rendered login page, no drawer |
| `/` | Authenticated | Dashboard alias |
| `/dashboard` | Authenticated | Operations overview |
| `/identity-lookup` | `TeamLeadOrAbove` | Calls Phase 1A API client |
| `/team` | `AdminOnly` | In-memory demo access view |
| `/access-denied` | Anonymous | Safe denial page |
| `/ui-kit` | Authenticated | Visual baseline |

## 6. Identity Lookup Integration Contract

The UI uses only `IIdentityLookupApiClient` and the existing Phase 1A contract:

- `POST /api/v1/identity/lookup`
- `GET /api/v1/identity/me`
- `GET /api/v1/identity/lookup/capabilities`
- `GET /api/v1/health/identity-provider`

The safe mock account shown in the UI is `pam12356`, matching `MockIdentityDirectoryProvider`.

## 7. UI Component / Service Map

| Area | Files |
|---|---|
| Login | `Pages/Login.cshtml`, `Pages/Login.cshtml.cs`, `Pages/LoginLayout.cshtml` |
| Shell | `Shared/MainLayout.razor`, `Shared/NavMenu.razor`, `Shared/SecureOpsTheme.cs` |
| Pages | `Pages/Dashboard.razor`, `Pages/IdentityLookup.razor`, `Pages/Team.razor` |
| Demo auth | `Services/DemoCurrentUserService.cs`, `Services/DemoModeState.cs` |
| Team demo | `Services/DemoTeamAccessService.cs` |
| API client | `Services/IIdentityLookupApiClient.cs`, `Services/IdentityLookupApiClient.cs` |
| Styling | `wwwroot/css/secureops-theme.css` |

## 8. Files Added or Modified

Added:

- `docs/18-ui-demo-shell-handoff.md`
- `src/SecureOps.Ui/Pages/Login.cshtml`
- `src/SecureOps.Ui/Pages/Login.cshtml.cs`
- `src/SecureOps.Ui/Pages/LoginLayout.cshtml`
- `src/SecureOps.Ui/Pages/_ViewImports.cshtml`
- `src/SecureOps.Ui/Security/LocalReturnUrl.cs`
- `tests/SecureOps.Tests.Integration/Ui/UiDemoAuthEndpointTests.cs`

Modified:

- `README.md`
- `docs/17-ui-demo-shell.md`
- `src/SecureOps.Ui/Program.cs`
- `src/SecureOps.Ui/Properties/launchSettings.json`
- `src/SecureOps.Ui/README.md`
- `src/SecureOps.Ui/Shared/MainLayout.razor`
- `src/SecureOps.Ui/Pages/Dashboard.razor`
- `src/SecureOps.Ui/Pages/IdentityLookup.razor`
- `src/SecureOps.Ui/Pages/Team.razor`
- `src/SecureOps.Ui/wwwroot/css/secureops-theme.css`
- `tests/SecureOps.Tests.Integration/Ui/UiDemoShellTests.cs`

Removed:

- `src/SecureOps.Ui/Pages/Login.razor`

## 9. Known Defects Fixed in This Iteration

- Fixed `Headers are read-only, response has already started` caused by calling `IAntiforgery.GetAndStoreTokens` in a Blazor component lifecycle method.
- Removed authenticated drawer/navigation from the anonymous login surface.
- Replaced invented identity lookup sample account with existing safe mock account `pam12356`.
- Added safe local return URL handling for demo sign-in redirects.

## 10. How to Run the Demo Locally

The demo now runs as two local hosts. Start the API first, then the UI, in two terminals:

```powershell
# Terminal 1 - Phase 1A API in its mock-safe Demo profile
dotnet run --project src/SecureOps.Api/SecureOps.Api.csproj --launch-profile "SecureOps.Api (Demo)"

# Terminal 2 - Blazor demo UI in its Demo profile
dotnet run --project src/SecureOps.Ui/SecureOps.Ui.csproj --launch-profile "SecureOps.Ui (Demo)"
```

The API Demo profile listens on `https://localhost:5001` (and `http://localhost:5000`) with
`ASPNETCORE_ENVIRONMENT=Demo`, `DemoAuth:Enabled=true`, and an in-memory audit store. The UI Demo
profile listens on `https://localhost:63947` and points `DemoMode:ApiBaseAddress` at the API.

Open `https://localhost:63947/login`, choose `Platform Yöneticisi`, then use `/dashboard`,
`/identity-lookup` (query the safe mock account `pam12356`), and `/team`.

If Chrome shows "Güvenli değil" on the UI, run `dotnet dev-certs https --trust` once locally. No
certificate files are added to the repository.

## 11. IIS Notes

- Publish folder is only for application files.
- Keep demo auth disabled outside approved local demo use.
- UI app pool needs read/execute access to the app folder.
- Production auth remains Windows Authentication with AD group policy mapping.
- If deployed behind a load balancer, keep TLS termination, forwarded headers, and Blazor SignalR behavior in the IIS/load balancer review.

## 12. Validation Evidence

Required commands:

- `git diff --check`
- `dotnet build SecureOps.sln --no-restore /p:MSBuildEnableWorkloadResolver=false`
- `dotnet test SecureOps.sln --no-build /p:MSBuildEnableWorkloadResolver=false`
- `dotnet format SecureOps.sln --verify-no-changes --no-restore`

Current validation on 2026-06-27 (this iteration):

- `git diff --check`: passed (no whitespace errors).
- `dotnet build`: passed with 0 warnings and 0 errors.
- `dotnet test`: passed, 41 unit tests and 62 integration tests (103 total).
- `dotnet format --verify-no-changes`: passed.
- Dual-host live smoke run (see section 15.10).

## 13. Deliberately Deferred Work

- Phase 1 diagnostic MVP.
- Phase 2 production UI template migration.
- Real Windows Authentication in the demo branch.
- Real AD/PAM/LDAP/Entra/SSO integration in the UI.
- AI/RAG.
- Remediation or any command execution controls.

## 14. Exact Recommended Next Agent Prompt

Continue on `feature/ui-demo-shell`. Do not push, merge, rebase, reset, stash, clean, amend, or commit. First run `git status --short`, `git diff --check`, build, test, format, and a local demo smoke run. If all pass, summarize remaining UI presentation risks and wait for approval before any commit.

---

## 15. Iteration 2026-06-27 — Live Mock API Integration and UI Clarity Pass

### 15.1 UI/API dual-host demo topology

Two local hosts cooperate for the demo:

- **API host** (`SecureOps.Api`) runs the real Phase 1A Identity Lookup endpoint with the existing
  Mock PAM resolver and Mock AD directory provider. Demo profile: `https://localhost:5001`.
- **UI host** (`SecureOps.Ui`) is the Blazor Server demo shell. Demo profile: `https://localhost:63947`.

The UI's `IdentityLookupApiClient` calls the API over real HTTP. There is no static fallback
identity data in the UI; if the API is unavailable the UI shows a safe error state only.

### 15.2 Actual API base URL configuration key

`DemoMode:ApiBaseAddress` (UI). Committed default `https://localhost:5001/`. The UI Demo launch
profile sets `DemoMode__ApiBaseAddress` to the API Demo URL. No localhost URL is hardcoded in source.

### 15.3 Demo authentication decision

The API is normally Windows Authentication (Negotiate). No safe HTTP test/demo auth existed, so a
**narrowly scoped, non-production demo authentication bridge** was added to the API:

- Files: `src/SecureOps.Api/Security/DemoApiAuthentication.cs`,
  `src/SecureOps.Api/Security/DemoApiAuthenticationHandler.cs`.
- Activates **only** when the environment is Development or Demo **and** `DemoAuth:Enabled=true`.
  `DemoApiAuthentication.IsEnabled` returns false for Production regardless of the flag.
- Maps a fixed demo actor header (`X-SecureOps-Demo-Actor`: `platform-admin` or `team-lead`) to the
  configured RBAC role group. Unknown/missing actor → not authenticated (401). It is not a universal
  bypass; the existing `TeamLeadOrAbove` policy still decides authorization.
- The UI never forwards its own cookie. `DemoApiAuthHeaderHandler` (UI) attaches the configured
  `DemoMode:ApiDemoActor` header to API calls only in Development/Demo with demo mode enabled.
- Committed defaults keep it disabled: API `DemoAuth:Enabled=false`, UI `DemoMode:Enabled=false`.

### 15.4 Optional Alert ID / Turuncuhat Event ID behavior

Required: `Account`, `Purpose`. Optional: `AlertId` (Guid), `TuruncuhatEvtId` (string). A lookup
succeeds with neither supplied. When supplied, `TuruncuhatEvtId` is length- and character-validated
(`^[A-Za-z0-9._-]+$`, max 100); `AlertId` is a `Guid?` and is format-safe by binding. Audit records
keep the real (possibly null) values — no fabricated substitutes. The UI form moves these into a
collapsed `Olay Referansları (İsteğe Bağlı)` panel and sends them only when entered.

### 15.5 Final page and route map

| Route | Access | Notes |
|---|---|---|
| `/login` | Anonymous | Server-rendered profile selector, no drawer |
| `/` and `/dashboard` | Authenticated | `Operasyon Panosu` with module status |
| `/identity-lookup` | `TeamLeadOrAbove` | Live API lookup, optional event refs |
| `/team` | `AdminOnly` | Compact table, detail-on-select, demo-only |
| `/access-denied` | Anonymous | Safe denial |
| `/ui-kit` | Authenticated | Visual baseline |

### 15.6 Important UI services and API client flow

`IdentityLookup.razor` → `IIdentityLookupApiClient` (`IdentityLookupApiClient`) → typed `HttpClient`
(base `DemoMode:ApiBaseAddress`) → `DemoApiAuthHeaderHandler` adds the demo actor header →
`POST /api/v1/identity/lookup` on the API → demo auth bridge → `TeamLeadOrAbove` policy →
`IdentityLookupService` → Mock PAM resolver + Mock AD provider → audited response.

### 15.7 Exact local launch commands

See section 10.

### 15.8 Known local HTTPS certificate note

If Chrome shows "Güvenli değil" on the UI, run `dotnet dev-certs https --trust` once. No certificate
files are committed.

### 15.9 Files changed in this iteration

- API: `Program.cs`, `Security/DemoApiAuthentication.cs` (new), `Security/DemoApiAuthenticationHandler.cs` (new), `Validation/IdentityLookupRequestValidator.cs`, `appsettings.json`, `Properties/launchSettings.json`.
- Infrastructure: `DependencyInjection.cs` (explicit mock directory provider factory so the demo returns the mock user).
- UI: `Program.cs`, `Configuration/DemoModeOptions.cs`, `Services/DemoApiAuthHeaderHandler.cs` (new), `Auth/DemoRoles.cs` (Turkish labels), `Services/DemoCurrentUserService.cs`, `Services/DemoTeamAccessService.cs`, `appsettings.json`, `Shared/MainLayout.razor`, `Shared/NavMenu.razor`, `Shared/SecureOpsTheme.cs`, `Pages/Login.cshtml`, `Pages/LoginLayout.cshtml`, `Pages/Dashboard.razor`, `Pages/IdentityLookup.razor`, `Pages/Team.razor`, `Pages/UiKit.razor`, `Pages/AccessDenied.razor`, `App.razor`, `wwwroot/css/secureops-theme.css`.
- Tests: `Api/IdentityLookupRequestValidatorTests.cs`, `Api/DemoApiIdentityLookupTests.cs` (new), `Api/DemoApiAuthenticationGatingTests.cs` (new), `Ui/UiDemoShellTests.cs`, `Ui/UiDemoAuthEndpointTests.cs`.
- Docs: `docs/17-ui-demo-shell.md`, `docs/18-ui-demo-shell-handoff.md`, `README.md`, `src/SecureOps.Ui/README.md`.

### 15.10 Tests and smoke-run evidence

- 41 unit + 62 integration tests pass. `DemoApiIdentityLookupTests` runs the real `IdentityLookupApiClient`
  against the real API host (TestServer) in Demo mode via the demo bridge: Found, NotFound, optional
  fields, missing/unknown actor → 401.
- Live dual-host smoke run (curl + Blazor prerender): API returned the mock user for `pam12356` with
  Account+Purpose only (and with optional fields); unknown account → 404 NotFound; no actor / unknown
  actor → 401; unsafe `TuruncuhatEvtId` → 400. The live UI signed in as Platform Yöneticisi and
  prerendered `/identity-lookup`, calling the running API (provider pill `Demo API · Mock sağlayıcı`,
  green). With the API stopped, the same page showed `Demo API · bağlantı yok` and no fabricated
  identity. `/dashboard` and `/team` rendered with no remediation controls.

### 15.11 Deliberately deferred items

See section 13. Additionally deferred: browser-driven end-to-end UI lookup automation (covered here by
the in-process client/API integration test plus the live prerender connectivity check), and any real
Windows Authentication wiring for the API in this branch.

### 15.12 Exact next recommended agent prompt

> Continue on `feature/ui-demo-shell`. Do not push, merge, rebase, reset, stash, clean, amend, or
> commit. Run `git status --short`, `git diff --check`, `dotnet build`, `dotnet test`, and
> `dotnet format --verify-no-changes`. Then run the dual-host demo (API Demo profile, then UI Demo
> profile) and confirm `/identity-lookup` returns the mock `pam12356` result. If all pass, capture
> 1366x768 and 1440x900 screenshots of `/login`, `/dashboard`, `/identity-lookup`, and `/team` for the
> management review, list any residual visual-polish items, and wait for approval before any commit.

---

## 16. Iteration 2026-06-29 — Gate 1: Proven Dual-Host Connectivity

Full Gate 1 details are in `docs/19-ui-presentation-acceptance.md`. Factual results of this iteration:

- **Root cause of `Demo API · bağlantı yok`.** Three real defects, found by reproduction (not guessed):
  1. The UI's effective API address came from committed `appsettings.json` (`https://localhost:63945/`,
     the API *Development* port) because the UI was not launched with the Demo profile. With the API on
     its Demo profile nothing listens on 63945, so the probe failed with a connection error.
  2. Even with the right port, HTTPS UI → API failed: the local ASP.NET dev certificate is **not
     trusted** by .NET certificate validation on this machine (verified with a real .NET client).
  3. In the **Demo** environment the build output's static-web-assets manifest is not auto-loaded, so
     `_content/MudBlazor/MudBlazor.min.js` and `.css` returned **404**; MudBlazor JS interop
     (`MudDrawer`/`MudCollapse` `getBoundingClientRect`) then tore down the Blazor circuit on every
     authenticated page. This is why the real browser UI never reached a working lookup.
- **Fixes.**
  - Canonical `IdentityLookupApi:BaseAddress` typed options with `ValidateOnStart` and a safe
    migration from legacy `DemoMode:ApiBaseAddress`. Committed default and the UI Demo profile both
    point to the API Demo HTTP endpoint `http://localhost:5000/`.
  - `Program.cs` enables `UseStaticWebAssets()` for non-Development/non-Production hosts (Demo).
  - The eager health probe moved to a single guarded post-circuit run; the provider pill stays neutral
    until verified, never showing `bağlantı yok` from a prerender race; technical detail is logged
    server-side via `ILogger`, never surfaced in the UI.
  - `/identity-lookup` optional event references use a lightweight CSS toggle instead of
    `MudExpansionPanels`/`MudCollapse`, removing the on-render JS call.
- **Browser-driven evidence (real Chrome via puppeteer-core).** Signed in as Platform Yöneticisi;
  `/identity-lookup` pill resolved to `Demo API · Mock sağlayıcı`; `pam12356` with Account+Purpose only
  → **Bulundu / Example Admin**; `pam00000` → **bulunamadı**; with the API stopped → `ulaşılamıyor` and
  `Demo API · bağlantı yok` with no fabricated data. `_content/MudBlazor/MudBlazor.min.{js,css}` = 200.
- **Files changed this iteration.** UI: `Program.cs`, `Configuration/IdentityLookupApiOptions.cs` (new),
  `Configuration/DemoModeOptions.cs` (removed `ApiBaseAddress`), `Pages/IdentityLookup.razor`,
  `appsettings.json`, `Properties/launchSettings.json`. Tests:
  `Ui/UiIdentityLookupApiConfigTests.cs` (new), `Ui/UiDemoShellTests.cs` (logger). Docs:
  `docs/19-ui-presentation-acceptance.md` (new), `docs/18-ui-demo-shell-handoff.md`.
- **Validation.** `git diff --check` clean; `dotnet build` 0/0; `dotnet test` 41 unit + 75 integration
  pass; `dotnet format --verify-no-changes` passes; browser smoke as above.

---

## 17. Iteration 2026-06-30 — Phase 4: Visual Implementation Gate

Implements the binding visual contract `docs/20-ui-visual-design-contract.md`. No API authentication,
contracts, connectivity, launch profiles, demo bridge, or `UseStaticWebAssets()` behavior changed. The
user-owned unstaged `src/SecureOps.Ui/appsettings.json` was not touched.

### 17.1 Theme convergence and dark mode

- One source of truth: `Shared/SecureOpsTheme.cs` holds the canonical MudBlazor palette (light =
  `MudTheme.Palette` as a `PaletteLight`, dark = `PaletteDark`). `wwwroot/css/secureops-theme.css` no
  longer hard-codes a second palette; the `--so-*` semantic tokens (`--so-surface`, `--so-surface-raised`,
  `--so-text`, `--so-text-muted`, `--so-primary`, `--so-critical`, `--so-border`, `--so-focus`, plus
  `--so-success`/`--so-warning`) map onto the `--mud-palette-*` variables MudThemeProvider emits, so both
  follow the active mode. Fallback light hexes exist only for `/login`, which renders without a
  MudThemeProvider.
- Aviation-operations direction: dark navy/graphite shell, neutral surfaces, **one** controlled deep-red
  accent reserved for error/critical; the previous decorative amber accent was removed (amber remains only
  as the semantic warning color). No gradients on the primary card.
- A compact, accessible light/dark toggle lives in the authenticated app bar (`MudIconButton`, dynamic
  icon, `aria-label`/`title`). It flips `MudThemeProvider.IsDarkMode` through the Blazor circuit. **No**
  `localStorage`/`sessionStorage`/cookie/DB persistence; preference resets on a full refresh, by design.

### 17.2 Responsive shell and navigation

- `MainLayout.razor`: `MudDrawer` is now `DrawerVariant.Responsive` (`Breakpoint.Md`) with a hamburger
  toggle. Below md it is a temporary overlay; a `MudHidden`-tracked desktop flag closes it after route
  navigation on mobile only (never on desktop). Top bar compacted: env indicator (`Örnek veri · Demo`),
  user, dark toggle, and a secondary logout (icon on xs, text on sm+). The duplicate role caption was
  dropped (the demo profile display name already names the role).
- `NavMenu.razor`: `Arayüz Kiti` removed from presentation nav (the `/ui-kit` route is retained for
  development). Added future-state entries under a `Gelecek fazlar` caption: **Salt Okunur Tanılama**
  (`/diagnostics-readonly`) and **Denetim ve Uyum** (`/audit-compliance`), each with a `Planlandı` tag.
- One reusable placeholder, `Shared/FuturePhasePlaceholder.razor`, backs both future routes
  (`Pages/ReadOnlyDiagnostics.razor`, `Pages/AuditCompliance.razor`) with the exact message
  `Bu yetenek, güvenlik ve operasyon kontrolleri tamamlandıktan sonra ayrı bir fazda kullanıma açılacaktır.`
  No fabricated metrics, dates, or timelines.

### 17.3 Page redesigns

- **Login** (`Pages/Login.cshtml`): compact, app-bar/drawer-free, three one-line profiles, always-visible
  boundary text, and an indeterminate sign-in spinner (generic local SVG) shown **only** while the real
  POST to `/demo-auth/sign-in` is in flight — added via a class on submit, no button disabling (antiforgery
  preserved), no fake percentage, no artificial delay.
- **Dashboard** (`Pages/Dashboard.razor`): added the read-only operational flow strip
  `Olay bağlamı → Kimlik sorgulama → Kayıt / audit → Salt okunur tanılama` (current step emphasized, future
  muted/dashed, markers not by color alone), a left-accented (non-gradient) primary `PAM / AD Kimlik
  Sorgulama` card with the `Kimlik Sorgula` action and audit assurance copy
  `Sorgular API üzerinden kayıt altına alınır.`, and the compact capability-status list.
- **Identity Lookup** (`Pages/IdentityLookup.razor`): API integration unchanged. The render-time health
  probe was removed so the provider pill stays quiet (`Demo API`) until a real query resolves it
  (`Demo API · bağlı` / `Demo API · ulaşılamadı`); initial prompt is
  `Hesap ve sorgu amacı girerek kimlik sorgulamasını başlatın.`. Two-column desktop / single-column mobile,
  required-field markers, optional event-refs collapsed. No raw URLs/exceptions/ports surfaced.
- **Team** (`Pages/Team.razor`): `Demo Üye Ekle` is now secondary (outlined); persistent
  `Demo only — gerçek erişim veya yetki değişikliği uygulanmaz.` warning, no detail panel before selection,
  in-memory demo actions only.

### 17.4 Accessibility

- Global `:focus-visible` ring via `--so-focus`; icon-only controls (hamburger, dark toggle, logout) have
  `aria-label`; status uses text labels (not color alone); flow strip uses `role="list"`/`aria-current`.
  AA-oriented contrast in both modes.

### 17.5 Files changed this iteration

- UI added: `Shared/FuturePhasePlaceholder.razor`, `Pages/ReadOnlyDiagnostics.razor`,
  `Pages/AuditCompliance.razor`.
- UI modified: `Shared/SecureOpsTheme.cs`, `Shared/MainLayout.razor`, `Shared/NavMenu.razor`,
  `Pages/Dashboard.razor`, `Pages/IdentityLookup.razor`, `Pages/Team.razor`, `Pages/Login.cshtml`,
  `wwwroot/css/secureops-theme.css`.
- Docs: `docs/18-ui-demo-shell-handoff.md`, `docs/20-ui-visual-design-contract.md` (section 8 checklist).
- No test files needed changes; existing UI tests still assert the preserved markup.

### 17.6 Validation evidence

- `dotnet build` 0 warnings / 0 errors; `dotnet test` **41 unit + 75 integration** pass;
  `dotnet format --verify-no-changes` passes; `git diff --check` clean (files normalized to CRLF).
- Real-browser run (Chrome via puppeteer-core, dual-host Demo) at **1366×768**, **1440×900**, and **390px**:
  `/login`, `/dashboard`, `/identity-lookup`, `/team`, and the future-phase placeholder all render at all
  three widths; sign-in (Platform Yöneticisi) → `/dashboard`; `pam12356` lookup → **Bulundu / Example
  Admin** with the pill resolving to `Demo API · bağlı`; dark-mode toggle switches the body to the dark
  palette through the circuit; the mobile hamburger opens the overlay drawer; no horizontal scroll at 390px.
  Review screenshots were captured outside the repository (scratch folder); no binaries are committed.

---

## 18. Iteration 2026-06-30 — Visual Remediation & Responsive-Layout Acceptance

The §17 acceptance was reopened: a wider screenshot matrix (1024 → 3840 px, light and dark) showed the
shell had a layout defect that the earlier 3-width review missed. No API contracts, URLs, the
`IdentityLookupApi:BaseAddress`, the demo bridge, launch profiles, authentication, backend, or the
user-owned unstaged `appsettings.json` changed in this iteration.

### 18.1 Root cause of the layout failure (measured, not assumed)

- MudBlazor's `.mud-main-content` **already** applies `margin-left: var(--mud-drawer-width-left)` when the
  responsive drawer is docked (per-breakpoint `@media`) and `padding-top` for the app bar. The custom
  `.secureops-main` rule added a **second** `padding-left: calc(248px + 28px)` plus a fixed
  `max-width: 1180px` island. The result was a duplicated drawer-width offset.
- Browser measurement (puppeteer) confirmed it objectively: at every authenticated width from **1024 to
  3840 px** the page's left space exceeded its right space by **248 px** (exactly the drawer width). 390 px
  (overlay drawer) and `/login` (no drawer) were unaffected. This was the "large blank region / content
  shifted too far right" reported against §17.

### 18.2 Responsive layout system implemented

- **One offset owner.** `.secureops-main` no longer adds any drawer-width or app-bar offset; it only
  provides symmetric horizontal gutters (`clamp`) and the bottom gap. `.mud-main-content` is the single
  source of the drawer/app-bar offset.
- **Adaptive container.** `.secureops-page` replaces the fixed `max-width: 1180px` with an adaptive cap
  (1380 → 1600 → 2080 → 2680 px at ≥1920/2560/3840) and is centered within the post-drawer workspace, so it
  fills common laptop widths and grows into a bounded wide layout on 4K instead of staying a narrow island.
- **Drawer-aware breakpoints via CSS container queries.** `.secureops-page` is a query container
  (`container-type: inline-size`); the dashboard and identity-lookup grids switch columns on the **real
  available workspace**, not the raw viewport. The lookup goes two-column only at `@container (min-width:
  1040px)` and the form column is bounded (`minmax(340px, 420px)`) so fields never stretch absurdly wide.
- **Drawer dock point raised to `Breakpoint.Lg` (1280 px)** with `ClipMode.Always`: below ~1280 px the
  drawer is a temporary overlay (full-width content, hamburger), so 1024×768 lookup is single-column; the
  app bar is full-width and the drawer no longer needs a manual top pad.
- **Re-measured result:** left/right asymmetry is **0 px** and there is **no horizontal scroll** at all
  seven widths (1024/1366/1440/1920/2560/3840/390) in both light and dark.

### 18.3 Content / composition work

- **Dashboard** filled with truthful content (no fabricated metrics): full-width operational flow strip,
  a primary `PAM / AD Kimlik Sorgulama` card, and two concise lists — `Bu sürümde güvenle yapılabilenler`
  and `Kapsam dışı ve sonraki fazlar` — in the main column, with `Kabiliyet durumu` and `Demo kapsamı` in a
  supporting column (drawer-aware two-column grid; stacks on small widths).
- **Identity Lookup** result panel given a real `min-height` and a centered, structured empty state
  (`Hesap ve sorgu amacı girerek kimlik sorgulamasını başlatın.`) instead of a thin bar on a blank card.
  The provider indicator is a quiet `Demo API` context label before any query (no health badge on render).
- **Loading system.** New reusable `Shared/SecureOpsLoader.razor`: a generic local Material flight
  silhouette inside a thin rotating deep-red ring, `role="status"` + `sr-only` label `Yükleniyor`, reduced
  via `prefers-reduced-motion`. Used during the real identity lookup and (inline, button-sized) the real
  sign-in POST. A lightweight route-transition is a 160 ms content fade-in on `.secureops-page`, also
  reduced-motion-gated. No fake percentage and no artificial delay.
- **Navigation** active state refined (left-accent + soft tint, not the default flat highlight).
- **Future module pages** enriched via the shared placeholder with truthful `Mevcut durum` /
  `Güvenlik sınırı` / `Sonraki önkoşul` lines — no `yakında`-only text, no fake dates/percentages/charts.

### 18.4 Dark mode

Verified in dark across app bar, drawer, refined nav active state, cards, MudBlazor inputs, the MudTable
on `/team`, status badges, alerts, the result panel, the loader, and the placeholder pages: visible
borders, readable body/label text, and non-washed success/warning/critical statuses, with identical
responsive behavior to light mode.

### 18.5 Files changed this iteration

- UI added: `Shared/SecureOpsLoader.razor`.
- UI modified: `wwwroot/css/secureops-theme.css`, `Shared/MainLayout.razor`,
  `Shared/FuturePhasePlaceholder.razor`, `Pages/Dashboard.razor`, `Pages/IdentityLookup.razor`,
  `Pages/ReadOnlyDiagnostics.razor`, `Pages/AuditCompliance.razor`, `Pages/Login.cshtml`.
- Docs: `docs/18-ui-demo-shell-handoff.md`, `docs/20-ui-visual-design-contract.md` (§8 evidence corrected).
- No source-logic change → existing tests preserved and still pass (no test edits required).

### 18.6 Validation evidence

- `dotnet build` 0/0; `dotnet test` **41 unit + 75 integration** pass; `dotnet format --verify-no-changes`
  passes; `git diff --check` clean.
- Browser matrix (Chrome via puppeteer-core, dual-host Demo) at **1024 / 1366 / 1440 / 1920 / 2560 / 3840 /
  390 px** in **light and dark**, routes `/login`, `/dashboard`, `/identity-lookup` (before and after a
  `pam12356` lookup → **Bulundu / Example Admin**), `/team`, and a future-module placeholder: left/right
  asymmetry 0 px and no horizontal scroll on every cell; dual-host smoke (Login → Platform Yöneticisi →
  `pam12356` + Purpose → Found) passes. Screenshots stored outside the repo; no binaries committed.
- Honest residual: at very tall 4K viewports (2560/3840 × tall) the dashboard leaves lower whitespace,
  because content is kept strictly truthful (no fabricated metrics/charts to fill height). At the common
  1366–1920 widths the content fills the workspace without dead zones.
