# SecureOps TEST Release Package — Active Directory Pilot

**Purpose of this deployment:** real Active Directory smoke validation. Nothing else in this package
is being put into service by it.

## Source heads

| Package | Commit | Notes |
|---|---|---|
| API | `f89f99684ece74f4728c01162e399d2edd7a2c45` | Backend AD semantics and group analysis |
| UI | `e3e6203` | Active Directory UX redesign, built on the same backend contracts |

`f89f996` is an ancestor of `e3e6203`. The API source tree at `e3e6203` is **byte-identical** to
`f89f996` — `git diff f89f996 HEAD` across `SecureOps.Api`, `SecureOps.Domain`,
`SecureOps.Infrastructure`, `SecureOps.Shared` and `SecureOps.Worker` is empty. The commits between
them touch only UI, UI tests, and docs, so the API package is the requested build regardless of which
of the two heads it is produced from.

## Packages

| Package | File | SHA256 | Entries |
|---|---|---|---|
| API | `secureops-api-TEST-f89f996.zip` | `0EA8757BE4816A0B0F823A9E1D4AD6C074556048585B6B4B70BC7A2516005069` | 237 |
| UI | `secureops-ui-TEST-e3e6203.zip` | `8A44A7812A1FD2643FD582975FFC61787BE543D7469002B8FCCB7C00D755BCCE` | 243 |

Per-file SHA256 manifests accompany each archive as
`secureops-api-TEST-f89f996.sha256-manifest.txt` and `secureops-ui-TEST-e3e6203.sha256-manifest.txt`.
Each manifest header repeats the archive name, the source commit, the archive SHA256, and the entry
count, so a manifest separated from its archive can still be matched back to one.

## What the packages do not contain

Verified by enumerating every archive entry, not by trusting the build:

- `web.config` — **absent from both.** Removed after publish.
- `appsettings.json` — **absent from both.** Removed after publish.
- `appsettings.*.json` — none exist in either project, so none were produced.
- `.pdb` — none produced. Suppressed at publish with `DebugType=None` and `DebugSymbols=false`
  rather than deleted afterwards.
- Source (`.cs`, `.csproj`), tests, `bin`, `obj`, SQL, secrets, key material — none present.

The strip step removed exactly two files per package: `web.config` and `appsettings.json`. They are
removed after publish rather than never produced, because the publish output is the audited artifact
and the difference between it and the shipped tree is what the manifest has to justify.

**The server-owned `web.config` and `appsettings*.json` on TEST must survive deployment unchanged.**
Replace the application payload only, preserving relative paths.

## HTTPS / F5 offload — regression protection

The approved implementation is present in source and compiled into the shipped UI assembly. The `/login`
HTTP 500 regression behind F5 is not reintroduced.

Source, at `e3e6203`:

- `src/SecureOps.Ui/Hosting/HttpsOffloadMiddleware.cs`
- `src/SecureOps.Ui/Hosting/HttpsOffloadOptions.cs`
- `src/SecureOps.Ui/Hosting/HttpsOffloadOptionsValidator.cs`

The middleware marks a request HTTPS only when **all** of these hold, and fails closed otherwise:

1. offload is enabled,
2. the incoming scheme is `http`,
3. the **immediate** connection remote IP is in the exact configured trusted-proxy set,
4. `Request.Host.Host` is in the configured expected-host set,
5. `Connection.LocalPort` equals the configured expected backend port.

It does **not** read `X-Forwarded-Proto`. `UseForwardedHeaders` is configured with
`XForwardedFor | XForwardedProto` and **no** `KnownProxies` and **no** `KnownNetworks` entries, so
proxy trust is not broadened. Ordering is unchanged: forwarded headers → HTTPS offload → HSTS →
HTTPS redirection → static files → routing → authentication → authorization → antiforgery. The
offload runs before anything that reads `Request.IsHttps`, which is what makes antiforgery issue its
Secure cookie on the cleartext backend hop.

Binary verification of `SecureOps.Ui.dll` in the shipped tree (657,920 bytes), scanned byte-exact in
both encodings because type and member names are UTF-8 in the metadata heap while configuration
string literals are UTF-16LE in the user-string heap:

| Symbol | Kind | UTF-8 | UTF-16 |
|---|---|---|---|
| `HttpsOffloadMiddleware` | type | ✅ | — |
| `HttpsOffloadOptions` | type | ✅ | — |
| `HttpsOffloadOptionsValidator` | type | ✅ | — |
| `TrustedProxyIps` | member | ✅ | ✅ |
| `ExpectedHosts` | member | ✅ | ✅ |
| `ExpectedLocalPort` | member | ✅ | ✅ |
| `ShouldApplyHttps` | member | ✅ | — |
| `NormalizeAddress` | member | ✅ | — |
| `ReverseProxy:HttpsOffload` | config key | — | ✅ |

A single-encoding scan reports `ReverseProxy:HttpsOffload` as missing. It is not.

## Verification performed

| Gate | Result |
|---|---|
| `git diff --check` | clean |
| Release build, whole solution | 0 warnings, 0 errors |
| Unit tests (Release) | 603 passed, 0 failed |
| Integration tests (Release) | 159 passed, 0 failed |
| HTTPS-offload + UI error-routing tests | 26 passed, 0 failed |
| Active Directory browser suite | 131 passed, 0 failed |
| Round 2 regression suite | 61 passed, 0 failed |
| Write-workflow reason guard | 2 passed, 0 failed |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any of the 8 projects |
| Package content validation | 0 forbidden entries in either archive |

The browser suites ran against the **Release** build of the UI, driven by a contract-shaped stub.
They verify presentation only. Real Active Directory behaviour is exactly what this deployment
exists to establish, and no stub result should be read as evidence of it.

## Database

**No migration was created.** The DBA bundle remains exactly 001–007, unchanged from the previous
release candidate. There is no 008.

This deployment does **not** assume SQL is ready. The AD smoke validation does not require the
reporting read model or the persisted audit store to be reachable; if SQL is unavailable the
directory screens are still the subject under test. Do not treat SQL readiness as a precondition for
this deployment, and do not treat this deployment as evidence that SQL is ready.

## TEST settings that must not change

The following are server-owned and stay exactly as they are:

- API Application Pool identity remains `ApplicationPoolIdentity`.
- Existing Demo/Auth configuration remains unchanged.
- Windows Authentication remains unchanged.
- Anonymous Authentication remains unchanged.
- SQL providers and connection settings remain unchanged.
- Server-owned `web.config` remains unchanged.
- Server-owned `appsettings*.json` remain unchanged.

Do not weaken TLS, set `TrustServerCertificate=True`, add SQL credentials, or work around Windows
authentication in application code.

## Recommended deployment order — API first

The UI is a client of the API and reads `/api/v1/access/me` before it will render anything
capability-gated. A UI deployed ahead of its API shows an unresolved-access state that reads as a
permissions defect rather than as a sequencing artifact.

1. Verify both ZIP SHA256 values and both per-file manifests against this document.
2. Confirm `web.config` and `appsettings*.json` are absent from both archives before either is opened
   on the server.
3. Back up the current API payload **and** the server-owned `web.config` / `appsettings*.json`.
4. Deploy the API payload only, preserving relative paths and the server-owned configuration files.
5. Recycle the API pool in the approved change window. Confirm `/health` and the identity-provider
   health endpoint.
6. Run an authenticated exact AD identity lookup against the API directly, before touching the UI.
   This isolates a directory problem from a UI problem.
7. Back up the current UI payload and its server-owned configuration files.
8. Deploy the UI payload only, preserving relative paths and the server-owned configuration files.
9. Recycle the UI pool. Confirm `/health`, then confirm `/login` returns 200 **through F5**, not only
   from the server itself — the previous regression was visible only on the proxied path.
10. Run the Active Directory smoke set against a real domain: exact user lookup, group memberships
    including a primary-group account, account and password evidence, an account with no SPN, group
    overview, direct members, the bounded nested analysis, a nested-group navigation, and a
    membership check with a known-negative answer.
11. On failure, restore the prior payload for the affected tier only. Configuration files were never
    part of the payload and need no restore.

## Known unresolved items

Carried forward, none of them introduced by this package:

1. **SQL TLS certificate trust** is not valid with `Encrypt=True;TrustServerCertificate=False`.
2. **`MSSQLSvc` SPNs** were not found for the TEST SQL listener names.
3. **Windows Integrated Security** for the intended runtime account is therefore not proven.
4. **The API App Pool** uses `ApplicationPoolIdentity`, not the intended domain runtime account.
5. **IIS Windows Authentication** is disabled and Anonymous Authentication is enabled.
6. **Turuncu Hat and Jira** remain blocked on sanitized response samples; keep both providers
   disabled.
7. **G-16** — no daily buckets outside identity lookup, so no adoption or OR/Jira trend series exists
   to show. Confirmed out of scope by the backend.
8. **G-18** — the UI integration contract still describes the directory `purpose` as required. The UI
   no longer sends one at all, so this can no longer mislead a UI author; it can still mislead a
   backend reader. Backend-owned.

Items 1–5 gate the **SQL** pilot. They do **not** gate this AD smoke deployment, which is why this
package is not marked blocked. Do not read that as those items being closed.

## Package policy

Both runtime archives exclude `web.config`, `appsettings*.json`, PDBs, source, tests, `bin`, `obj`,
SQL, secrets, and environment-specific configuration. The DBA bundle is separate and unchanged at
migrations 001–007.
