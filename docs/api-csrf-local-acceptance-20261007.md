# API CSRF Local Acceptance, 2026-10-07

Branch: `fix/api-csrf-origin-guard-20261007`, based on fetched master
`8032c80cd85f82c200a68dc92684e9308a09e687`. Worktree:
`C:\SecureOpsBuild\secure-ops-api-csrf-origin-guard-20261007`.
The original checkout and its untracked editor/agent directories were preserved.

## Change and decision

The first commit, `7fee54c`, contained only ADR-0029 and G-35 design notes.
API implementation (`0dd1f00`), generated contract (`657f7fd`) and minimal shared
UI transport (`52c7dea`) are separate commits. No Service Accounts production
module code, Razor, CSS, SQL, identity policy or permission definition was changed.

The central post-authorization, pre-body guard requires `X-SecureOps-Csrf: 1` on
all unsafe methods and checks explicitly allowed Origin/Referer and Fetch Metadata.
It returns audited 403 `ApiCsrfRejected`; unavailable audit returns 503 without
endpoint execution. GET/HEAD/OPTIONS pass through. See ADR-0029 and
`docs/contracts/api-csrf-origin-guard.md` for alternatives, proxy trust and rollout.

## Verification

All .NET commands used the pinned user-local SDK **9.0.317**:

```powershell
$env:DOTNET_ROOT='C:\Users\dmtak\AppData\Local\Microsoft\dotnet'
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet --version
dotnet build SecureOps.sln -c Release --no-restore
dotnet format SecureOps.sln --verify-no-changes --no-restore
dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj -c Release --no-build --no-restore
dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-build --no-restore
```

| Check | Final result |
|---|---|
| Release solution build | 0 warnings, 0 errors |
| Repository-wide format verification | Exit 0, no diagnostics; no include/style/analyzer restriction |
| Full unit suite | 1918 passed, 1 environment-gated skip, 0 failed |
| Full integration suite | 356 passed, 117 environment-gated skips, 0 failed |
| OpenAPI snapshot and routed inventory | Pass: all 99 unsafe operations declare the header; 153 paths and existing component schemas unchanged |
| Guard behavior | Multipart POST, PUT/PATCH/DELETE, invalid/duplicate headers, exact origins/Referer, cross-site metadata, absent config, headerless clients, safe methods, authorization precedence, privacy-safe denial audit and audit failure passed |
| Body/business isolation | Unreadable body never accessed; endpoint never invoked on denial; resource business records unchanged after rejected write |
| Allowed multipart | Existing account usage-scan upload reaches its expected `ServiceAccountsNotConfigured` response; module remains disabled, no SQL import acceptance claimed |
| UI transport | All 13 registered typed API clients carry intent without fabricated browser metadata |
| Real browser cross-origin form | Loopback Chrome multipart POST to the existing usage-scan route returns 403 with actual Origin and no intent header; Demo identity injected instead of Negotiate |
| Same-origin browser and script | Browser JSON write with intent returns 200; headerless script returns 403 with no business mutation |
| Interactive UI | Favourite and group writes re-read through API; Resources search/save/edit/open journey passes with synthetic InMemory data |

Browser scripts: `tests/browser/api-csrf.cjs`, `api-csrf-ui.cjs`, and
`resource-shift-journey.cjs`; shared `journey-support.cjs` supplies the client header.
The broad journey initially timed out at text entry. Using native key events in
two test inputs made the complete replay pass; no UI implementation was changed.
Chrome's intercepted HTTP loopback form did not expose Sec-Fetch-Site in the
captured headers; that browser replay proves the Origin/intent fallback. Explicit
cross-site metadata denial was separately proven in hosted/policy tests.
Screenshots include desktop/mobile; the existing journey's zoom is simulated,
not native Windows zoom or accessibility acceptance.

Private evidence root: `C:\SecureOpsBuild\csrf-evidence-20261007` (TRX, browser JSON,
screenshots). Build/format/test logs: `C:\SecureOpsBuild\csrf-*.log`. All local test
hosts were stopped; ports 51731/51732/51733 have no remaining listeners.
No installed TEST, corporate AD/OIDC/IIS/F5, database, provider, SMTP, service,
application pool or binding was accessed or changed. No Worker or real sending ran.

## Remaining gates and Information Security note

- Confirm approved public browser origins and caller-header rollout order before deployment.
- Replay real browser Negotiate under IIS/F5, including preserved Origin/Referer/Sec-Fetch-* and HTTPS offload.
- SQL-dependent integration tests and a successful Service Accounts import were not enabled in this bounded run. The current master has `accounts/{id}/usage-scans`; PR #18's standalone route is not in this base.
- CSRF rejection prevents business/provider/upload writes. Required denial audit and existing identity registration/session lifecycle can still write before the guard; this is not an assertion of zero total persistence.
- Tell Information Security: a central fail-closed request-intent/source check now covers the entire unsafe API surface. The constant header grants no authority. Credentialed CORS changes require review; XSS and actual corporate authentication/topology remain separate concerns. Local evidence is not deployment approval.
- Push is authorized but did not complete: Git could not obtain a write credential, and GitHub CLI has no authenticated session. The stalled push/helper processes were stopped; the five commits remain local. The branch's automatic master upstream was removed. After authentication, use `git push -u origin fix/api-csrf-origin-guard-20261007`. Merge and deployment remain explicitly excluded.
