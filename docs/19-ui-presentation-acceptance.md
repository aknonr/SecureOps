# 19 — UI Presentation Acceptance (Gate 1)

Gate 1 establishes a proven local dual-host UI → Phase 1A Identity Lookup API flow and pins its
configuration. It is not a visual redesign. The connectivity below is verified by a real
browser-driven lookup, not only by mocked tests.

## 1. UI/API Dual-Host Topology

```
Browser ──HTTPS──> SecureOps.Ui (Blazor Server, Demo)  https://localhost:63947
                        │
                        │  server-side HttpClient (no UI cookie forwarded)
                        │  header: X-SecureOps-Demo-Actor: platform-admin
                        ▼
                   SecureOps.Api (Phase 1A, Demo)        http://localhost:5000
                        │  demo auth bridge → TeamLeadOrAbove policy
                        ▼
                   Mock PAM resolver + Mock AD provider (in-memory audit)
```

- Browser → UI is HTTPS. UI → API is HTTP on the API Demo profile's `http://localhost:5000`
  endpoint (a real URL the API Demo profile exposes). The server-to-server hop uses HTTP on
  purpose: the local ASP.NET dev certificate is **not trusted** by .NET certificate validation on
  this machine, so HTTPS UI → API fails the TLS handshake. HTTP avoids that friction; no browser
  traffic and no cookies cross this hop.
- The UI has no static fallback identity data. If the API is unreachable the UI shows a safe
  unavailable state only.

## 2. Canonical Configuration Key

`IdentityLookupApi:BaseAddress` is the single source of truth for the API address.

- Typed options: `SecureOps.Ui.Configuration.IdentityLookupApiOptions`, validated at startup
  (`ValidateOnStart`) to be an absolute URI; normalized to a trailing slash.
- Migration: if the canonical key is absent, the legacy `DemoMode:ApiBaseAddress` is used and a
  warning is logged. Resolution happens at options-build time so launch-profile environment
  variables and appsettings are both honored.
- The effective address is logged once at startup (server-side only).

## 3. Configuration Matrix

| Context | Key source | Value | Notes |
|---|---|---|---|
| Local demo (committed) | `appsettings.json` | `http://localhost:5000/` | Works even without the Demo launch profile, as long as the API Demo profile runs. |
| Local demo (UI Demo profile) | `IdentityLookupApi__BaseAddress` env | `http://localhost:5000/` | Matches the API Demo profile's HTTP endpoint. |
| Development (deployment) | `appsettings.Development.json` (not committed here) | HTTPS API URL with a **trusted** dev cert | Use `dotnet dev-certs https --trust` and HTTPS. |
| Test | environment config / user secrets | Test API base URL (HTTPS) | Demo auth bridge still gated to Development/Demo only. |
| Production (IIS) | `appsettings.Production.json` / env | HTTPS API URL behind IIS | Windows Authentication; demo bridge never activates; static assets published to `wwwroot`. |

Legacy `DemoMode:ApiBaseAddress` remains read-only fallback for backward compatibility.

## 4. Current Authentication Boundary

- API normal scheme: Windows Authentication (Negotiate).
- Demo bridge (`DemoApiAuthentication` / `DemoApiAuthenticationHandler`): active only when the
  environment is Development or Demo **and** `DemoAuth:Enabled=true`. Never active in Test, Staging,
  or Production. Maps the fixed actors `platform-admin` / `team-lead` to the configured RBAC group;
  unknown/missing actor → 401. The `TeamLeadOrAbove` policy still decides authorization.
- The UI never forwards its authentication cookie to the API. `DemoApiAuthHeaderHandler` only adds
  the actor header in Development/Demo with demo mode enabled.
- Account and Purpose are required; Alert ID and Turuncuhat Event ID are optional and validated when
  supplied. Audit behavior is unchanged (health endpoints are not audited).

## 5. Visual Studio Multiple Startup Projects

1. Solution → Properties → Startup Project → **Multiple startup projects**.
2. `SecureOps.Api` → Action **Start**, profile **`SecureOps.Api (Demo)`** (`https://localhost:5001`,
   `http://localhost:5000`).
3. `SecureOps.Ui` → Action **Start**, profile **`SecureOps.Ui (Demo)`** (`https://localhost:63947`).
4. Start the API before (or together with) the UI. The UI Demo profile already targets
   `http://localhost:5000/`.

CLI equivalent (two terminals):

```powershell
dotnet run --project src/SecureOps.Api/SecureOps.Api.csproj --launch-profile "SecureOps.Api (Demo)"
dotnet run --project src/SecureOps.Ui/SecureOps.Ui.csproj  --launch-profile "SecureOps.Ui (Demo)"
```

## 6. Required Smoke-Test Path

1. Start the API Demo profile, then the UI Demo profile.
2. Open `https://localhost:63947/login`; sign in as **Platform Yöneticisi**.
3. Open `/identity-lookup`. The provider pill must resolve to `Demo API · Mock sağlayıcı`.
4. Query `pam12356` with Account + Purpose only → a real **Bulundu** result (Example Admin).
5. Query an unknown account (e.g. `pam00000`) → safe **bulunamadı** (Not Found).
6. Stop the API and query again → safe `ulaşılamıyor` with the pill `Demo API · bağlantı yok`, and
   **no fabricated identity data**.

This path was executed end-to-end in a real Chrome browser for this gate and passed.

## 7. Current Known Gaps

- Server-to-server UI → API uses HTTP locally because the dev certificate is not trusted on this
  machine; an HTTPS-with-trusted-cert path is documented but not the committed local default.
- No real Windows Authentication for the API in this branch (demo bridge only).
- No browser-driven test is wired into CI; the gate's browser run is manual (puppeteer-core driving
  the installed Chrome). Automated coverage is via in-process client↔API integration tests.
- Phase 1 diagnostics, Phase 2 production UI template, and AI/remediation remain out of scope.

## 8. Visual Acceptance Checklist (for the later redesign)

For each page, confirm at **1366×768**, **1440×900**, and **mobile (~390px)** width: no horizontal
scroll, no oversized empty areas, readable typography, controls reachable, and demo boundary visible.

| Page | Route | 1366×768 | 1440×900 | Mobile |
|---|---|---|---|---|
| Login | `/login` | ☐ | ☐ | ☐ |
| Operasyon Panosu | `/dashboard` | ☐ | ☐ | ☐ |
| PAM / AD Kimlik Sorgulama | `/identity-lookup` | ☐ | ☐ | ☐ |
| Takım ve Yetki | `/team` | ☐ | ☐ | ☐ |

Per-page checks: Login fits without vertical scroll and shows the demo boundary; Dashboard shows the
primary action and module status without remediation controls; Kimlik Sorgulama shows the provider
pill, form, optional event references, and a real result/empty/error state; Takım ve Yetki shows the
compact table with the detail panel only after selection and the persistent demo-only message.
