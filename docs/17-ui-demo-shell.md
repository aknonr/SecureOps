# 17 - UI Demo Shell

This document defines the local UI demonstration shell added before Phase 2 production UI work.

## Purpose

The UI demo shell gives management and team leads a visual, clickable SecureOps experience while the backend MVP phases continue. It is a presentation track, not a production phase.

It must not change the approved backend scope:

- Phase 1A identity lookup remains backend-first and read-only.
- Phase 1 diagnostics remain not started on `master`.
- No remediation, write operations, AI/RAG, PAM session automation, or diagnostic execution is introduced by the UI shell.

## Framework Boundary

The UI remains the existing `SecureOps.Ui` project:

- Blazor Server.
- MudBlazor.
- Standard Blazor `_Host.cshtml` for authenticated pages.
- A dedicated server-rendered Razor Page is allowed only for `/login`, because antiforgery token generation and cookie sign-in must occur before the HTTP response starts.
- No MVC feature area.
- No React, Angular, npm frontend, or second web host.

## Routes

| Route | Purpose | Access |
|---|---|---|
| `/login` | Demo profile selector | Anonymous |
| `/` | Dashboard alias | Authenticated |
| `/dashboard` | Dashboard | Authenticated |
| `/identity-lookup` | Phase 1A identity lookup form | `TeamLeadOrAbove` |
| `/team` | In-memory team demo view | `AdminOnly` |
| `/access-denied` | Safe authorization failure page | Anonymous |
| `/ui-kit` | Visual baseline | Authenticated |

## Demo Authentication

Committed defaults:

```json
{
  "DemoMode": {
    "Enabled": false,
    "AllowMockAuthentication": false,
    "ApiDemoActor": "platform-admin",
    "ApiDemoActorHeader": "X-SecureOps-Demo-Actor"
  },
  "IdentityLookupApi": {
    "BaseAddress": "http://localhost:5000/"
  }
}
```

The API base address is the canonical `IdentityLookupApi:BaseAddress` key (legacy
`DemoMode:ApiBaseAddress` is read only as a migration fallback). See
`docs/19-ui-presentation-acceptance.md` for the dual-host topology and configuration matrix.

Cookie-backed demo authentication is effective only when all are true:

- Environment is `Development` or `Demo`.
- `DemoMode:Enabled=true`.
- `DemoMode:AllowMockAuthentication=true`.

The sign-in form posts to `/demo-auth/sign-in` and uses ASP.NET Core antiforgery tokens. Sign-out is handled by the authenticated server endpoint `/demo-auth/sign-out`. These endpoints are for the UI shell only and must not be used as a production authentication model.

### API demo authentication bridge

The UI calls the real Phase 1A API over HTTP. The API is normally Windows Authentication (Negotiate), so a narrowly scoped, non-production demo bridge lets the demo run without forwarding UI cookies:

- API: `DemoApiAuthentication` / `DemoApiAuthenticationHandler`, active only when the environment is `Development` or `Demo` **and** `DemoAuth:Enabled=true`. It never activates in Production, maps only the fixed demo actors `platform-admin` and `team-lead` to the configured RBAC role group, rejects unknown actors, and leaves the `TeamLeadOrAbove` authorization policy in force.
- UI: `DemoApiAuthHeaderHandler` adds the `DemoMode:ApiDemoActor` header (`X-SecureOps-Demo-Actor`) to API calls only in Development/Demo with demo mode enabled. The UI authentication cookie is never sent to the API.
- Committed defaults keep both disabled (`DemoAuth:Enabled=false`, `DemoMode:Enabled=false`).

`/login` must not be implemented as an interactive Blazor component that calls `IAntiforgery.GetAndStoreTokens`, writes headers, or performs cookie sign-in from component lifecycle methods. The login surface uses a dedicated `LoginLayout` without the authenticated drawer.

Sign-out is reached from the authenticated shell and redirects back to `/login`.

Production authentication remains Windows Authentication with AD-group policy mapping as documented in `docs/05-security-model.md`.

## Identity Lookup UI Contract

The identity lookup page uses `IIdentityLookupApiClient` and the existing shared contracts:

- `IdentityLookupRequest`
- `IdentityLookupResponse`
- `IdentityLookupUserDto`
- `ApiErrorResponse`

The only account-input API route used by the client is:

```http
POST /api/v1/identity/lookup
```

No GET lookup-by-account route is added. Account values must not appear in URLs, browser history, proxy logs, or IIS access logs.

`Account` and `Purpose` are required. `AlertId` and `TuruncuhatEvtId` are optional event context: a lookup succeeds without them, and when supplied they are still validated for length and safe characters. The UI keeps them in a collapsed `Olay Referansları (İsteğe Bağlı)` section and sends them only when entered.

The UI displays only fields already approved by the Phase 1A API contract. It does not display SID, DN, group membership, password metadata, phone, address, raw LDAP attributes, or remediation controls.

The safe mock account shown in the UI is `pam12356`, matching the existing Phase 1A mock directory provider.

## Team Page Boundary

The `/team` page is demo-only:

- Uses `IDemoTeamAccessService`.
- Stores changes in scoped in-memory state.
- Does not write to DB.
- Does not write to AD, PAM, LDAP, Entra, or any access-control system.
- Does not provision users or change real roles.

## Corporate Template Migration

The current CSS and MudBlazor theme are a temporary visual baseline based on the SecureOps architecture colors. When the official corporate template is available, Phase 2 should migrate:

- Layout shell.
- Typography scale.
- Navigation density.
- Approved color tokens.
- Form and table patterns.
- Accessibility review.

The migration should preserve the existing Blazor Server + MudBlazor technical decision unless a new ADR explicitly changes it.

## Test Coverage

Current tests cover:

- GET `/login` rendering without the Blazor headers-already-started failure.
- Demo login disabled by default, permitted only in Development/Demo with explicit config, blocked in Production, unknown profile rejection, and safe local return URL handling.
- Role-aware navigation.
- Access denied page.
- Dashboard cards and absence of remediation wording.
- Identity lookup success, not-found, and API error states with a mocked client.
- Team access actions remaining in memory.

## Out of Scope

- Phase 1 diagnostic execution.
- Real Windows Authentication or SSO implementation in this demo branch.
- Real AD/PAM/LDAP/Entra calls from the UI.
- PowerShell, Ansible, WinRM, or server access.
- Hangfire jobs.
- Background workers.
- Team provisioning or real access changes.
- Remediation controls such as reboot, delete, service restart, AppPool recycle, unlock, or password reset.
