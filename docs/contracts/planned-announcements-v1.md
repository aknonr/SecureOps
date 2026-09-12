# Planned announcements: implemented backend / Claude handoff

See ADR-0021 and generated `secureops-api-v1.openapi.json`. UI is not implemented.
All routes require authenticated, approved persisted access and `Announcements.Drafts`
(Admin only), then owner isolation. Immutable user IDs remain authorization keys.
No new authentication flow. Default `Announcements:Enabled=false` fails closed.

## Implemented journey

Client creates a UUID. PUT `/api/v1/announcements/{id}?version=0` creates a draft.
GET the same path returns latest content. PUT `?version=N` saves a new revision
only when N equals current version. Every success returns quoted numeric `ETag`;
use its number in the next request, not an unsupported If-Match header.
GET `?version=N&format=html` returns safe UTF-8 HTML; `format=eml` returns
`message/rfc822` attachment `announcement-{uuid-without-hyphens}-vN.eml`.
Both require a current, complete saved revision. GET has no save/send side effect;
privileged reads/download preparation are audited fail-closed. Responses are no-store.
Body/recipient edits must be saved before preview. Download is not Sent or delivery.
An Outlook-editable draft is not guaranteed. No Outlook COM, SMTP or source calls.

Response body for save/read is the content below (camelCase); `X-Announcement-Origin`
is `Manual`. UUID lives in route; version in ETag. Audit actor/time, sender,
template `oco-v1`, banner hash and provenance are server-owned persisted metadata.
Source attestation/overrides are not available; OCO and scope are manually entered.

```json
{"ocoReference":"OCO-SYNTHETIC","scope":"Manual scope","subject":"Planned work & checks","announcementDate":"2026-09-12","workStart":"2026-09-13T01:00+03:00","workEnd":"2026-09-13T02:00+03:00","description":"Literal &lt;b&gt; and <b> remain text","impact":"Brief interruption","checks":"Verify health","notes":"","to":["reader@example.invalid"],"cc":["copy@example.invalid"],"bannerRevision":"synthetic-v1","restartStart":null,"restartEnd":null}
```

To/Cc: bare addresses only, max 50 per field, max 254 characters each. Reject
controls, display-name lists and header injection. Case-insensitive deduplication;
To wins overlap with Cc. No directory lookup. Text max 4000, subject max 200 with
no controls. Dates: announcement yyyy-MM-dd; work/restart ISO with explicit offset
or Z, end after start, restart pair optional. No inferred timezone or approval.
Empty draft fields/To may be saved; rendering requires all except notes/restart.
Banner revision must be allowlisted even for an incomplete save (a-z, 0-9, hyphen).

## Errors and UI states

401 unauthenticated; 403 policy/access denied. Owner mismatch and missing UUID:404
`AnnouncementNotFound`. 409 `AnnouncementConflict`: retain edits, read current,
show differences, explicitly reapply; never silently retry. 409 `AnnouncementAssetChanged`:
configured bytes/template changed; require reviewed new save, never export old content.
400 `AnnouncementInvalid` / `AnnouncementIncomplete` contains `fields` (PascalCase
DTO names, e.g. Subject/To) for focus navigation. Module errors include `code`,
`fields`, `correlationId`; ASP.NET binding errors use standard validation `errors`.
503 `AnnouncementsDisabled`, `AnnouncementConfigurationUnavailable` or
`AnnouncementUnavailable`: stop, preserve edits, show correlation reference.
For preview, use an authenticated backend request and sandboxed frame; no raw
MarkupString/user HTML. Do not decode user/source text. Data-URI image matches MIME
CID bytes; cid URLs are not browser URLs. Dynamic recipient rows need stable keys,
keyboard labels and field errors. Keep Blazor Server/MudBlazor; no public AI.

## Explicit follow-up boundaries

Draft list/pagination, banner discovery metadata, saved recipient sets and Send
History have no endpoints yet. Do not fabricate empty successful history or wire
send buttons. Build a paginated owner-scoped drafts API next, then Claude list/editor
views. Immutable send-intent/Hangfire/SQL states and retry rules are in ADR-0021.
Changing content invalidates future send confirmation; downloads/local captures
must stay separate from actual sends. Real SMTP remains disabled.

## Configuration and dependencies

Local opt-in requires schema 014, existing SQL access/session configuration,
`Announcements:Sender`, absolute private local `AssetDirectory` (outside publish),
and `Banners:{revision}` mapping to a flat local filename. No asset upload/URL fetch.
Retain old revision files; changing bytes blocks previous exports. PNG/JPEG only,
24..262144 bytes, max 2048x1024, single frame, complete codec decode; no symlinks.
MimeKit 4.17.0 and SkiaSharp 4.151.2 are centrally pinned, MIT, .NET 8 compatible:
[MimeKit](https://www.nuget.org/packages/MimeKit/4.17.0),
[SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.151.2).
Native Skia runtime assets need future publish/platform validation; no package here.
No appsettings, SMTP credentials, authentication, grants or production files changed.

## Local validation, 2026-09-12

Evidence: `C:\SecureOpsBuild\validation\oco-drafts-20260912` (not committed).
Executed from the ADR worktree, not release binaries; no shared HTTP test ports.
`dotnet build SecureOps.sln -c Release --no-restore`: zero warnings/errors.
`dotnet test SecureOps.sln -c Release --no-restore`: 1228 unit and 249 integration
passed; 21 opt-in SQL tests skipped here. `tests-acceptance.log` / `acceptance*.trx`.
OpenAPI snapshot comparison passed within that run; snapshot generation used
`SECUREOPS_UPDATE_OPENAPI=1` and the existing contract test, not hand-edited JSON.
`./scripts/powershell/Test-ResourceCatalogueSql.ps1 -DatabaseSuffix OcoAcceptance20260912 -IncludeAnnouncementDrafts`
created a fresh `SecureOps_ResourcesV1_OcoAcceptance20260912` on the existing
`(localdb)\SecureOpsResourcesV1`; 001-014 and preservation fixture passed.
With `SECUREOPS_SQL_TEST_CONNECTION` selecting that DB using integrated security:
`dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-build --filter 'FullyQualifiedName~ResourceSqlTests|FullyQualifiedName~AnnouncementTests'`
passed 27/27 (20 existing SQL + 7 announcement tests, including one SQL-backed API
journey). See `sql-and-announcements-acceptance.trx`; no corporate evidence reused.
TestServer uses supported Test/Demo auth, SQL access/sessions, ephemeral protection,
disabled source/Jira and ReadOnlyIntegrationMode=false; both external write flags
remain false. This is local-only disabled-adapter composition, not a release change.
Earlier failed runs remain: old migration-count assertion corrected; reusing the
SQL fixture DB exposed legacy duplicate fixtures and an unsafe audit test JSON query.
The latter now guards ISJSON. Final SQL evidence uses a fresh DB; no data was erased.
`dotnet list SecureOps.sln package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json`
reported no known vulnerabilities (`dependencies.log`). Changed-C# format passes
(`format-scoped-acceptance.log`). Full `dotnet format SecureOps.sln --verify-no-changes --no-restore`
fails on 58 untouched baseline files (`format-verified-report.json`), outside this
diff; no waiver or broad cleanup. Full DoD remains blocked on that independent work.
No UI/browser, Outlook interoperability, SMTP, publish/package or deployment tested.
