# Planned announcements: implemented API and UI

## Post-rc6.24 confirmation continuation

Mail preview adds immutable-preparation `ocoReference`, `workStart`, `workEnd`,
`preparedAt` and distinct `recipientCount` output. The confirmation displays
seconds and the saved offsets, not browser-local reinterpretation. Self-test
count is one with no Cc. No request accepts these fields; existing protected
preview-token comparison binds the same preparation fingerprint. No migration,
relay activation or change to historical MIME. Evidence/status is tracked in
`docs/rc624-workflow-continuation.md`; rc6.24 does not contain this correction.

## Current Mail Contract, 2026-09-17

ADR-0022 implements opt-in SMTP through the existing foreground Worker. This
supersedes the absence statements in the historical checkpoint below, not its
immutable preparation evidence. Corporate sending remains disabled and unverified.

All commands require approved persisted access, `Announcements.Drafts` and ownership.
Source submission/review/apply additionally requires `Announcements.Source`; new
preparation requires `Announcements.Prepare`. Migration 020 preserves the formerly
implicit source/preparation permissions. `Announcements.SelfTest` and `.Send` are
new, unassigned capabilities, including for Admin; authorization does not enable SMTP.

`POST /api/v1/announcements/mail/preview` accepts only `preparationId` and
`kind` (`SelfTest` or `Send`). Response includes frozen sender, envelope sender,
To/Cc, subject, draft/preparation/version, message ID/hash, command ID, expiry and
protected preview token. Sender is the actor's saved valid Mail, never client From.
SelfTest has only that Mail in To and empty Cc/Bcc; the actual draft is unchanged.
`POST mail/confirm` accepts only `previewToken`, returns 200 with durable status,
and is limited to six confirmations/minute per authenticated principal. Ten-minute
preview expiry, changed draft/profile/access/relay policy reject new commands.
An exact replay returns the prior command, even after preview expiry, but remains
owner/capability protected. `GET /{draftId}/mail` returns at most 100 owned results.
Responses are no-store. No client actor, transport credential or arbitrary MIME input.

SQL commits immutable intent/bytes/profile and required audit before enqueue. Worker
rechecks saved actor access/version/Mail, latest draft and relay-policy fingerprint
inside its claim transaction. States: Queued, Dispatching, Accepted, Partial, Failed,
Unknown, Denied. Only Queued may be re-enqueued; five-minute expired Dispatching
becomes Unknown. Hangfire mail attempts have zero automatic retries. DATA ambiguity
is never a known failure. Acknowledged DATA survives a later QUIT failure. Accepted
means SMTP acceptance, not inbox delivery. Known rejected recipients remain separate.

One distribution command per draft is enforced by a unique SQL index, including
failed/denied commands. Pending, partial or unknown commands also block a new
self-test for that draft. Existing immutable intent is never edited to retry.
Reconciliation is read-only status/evidence plus accountable relay inspection;
there is no operator SQL reset, automatic resend or invented remote absence proof.
New distribution after a terminal failure requires an independently reviewed new
draft, not copying an uncertain command to evade reconciliation.

`AnnouncementMail` is disabled by default. Exact settings and private credential
handling are in the current deployment runbook and disabled sample fragment.
SMTP identity, envelope sender and visible From are distinct. TLS validation stays
on; plaintext is accepted only for literal loopback in local test environments.
No new service host, scheduler, SMTP grant or corporate network call is implied.

Original six assets are retained byte-for-byte. Bounds are now 1 MiB per image,
2 MiB aggregate, six roles, 2048x1024 decoded dimensions, single frame, complete
PNG/JPEG decode. MIME type follows decoded bytes, including the PNG named .jpg.
Historical preparations are neither rerendered nor relabelled as sent. Local
browser/MIME evidence with original assets is not Outlook/inbox acceptance.

## Historical Mail And Access Checkpoint, 2026-09-15

The owner now authorizes real self-test/send and dynamic access management.
Implementation is NOT complete. The bounded correction uses trusted persisted
actor Mail for new saved revisions; legacy Announcements.Sender is ignored.
GET list adds currentSender (valid persisted Mail or null), displayed read-only.
Draft/HTML work remains available without valid Mail. MIME/new preparation returns
400 AnnouncementSenderUnavailable, or 409 AnnouncementSenderChanged when current
Mail differs from the saved revision. Explicit resave/new preparation is required;
historical downloads never re-render or resolve new Mail/assets/recipients.
No client From, username inference, shared-account fallback or SMTP activation.
Existing editable To/Cc, overlap normalization and reviewed source apply remain.

All six supplied images were acquired without credentials/redirects; originals
and hashes are private in task evidence source-assets/manifest.json. The main
.jpg is actually PNG, 924x530, 554652 bytes, exceeding the current 262144-byte
bound. Header: JPEG 1600x472, 54212 bytes; logo: JPEG 482x96, 16240 bytes;
social PNGs: 61x47, 593/860/540 bytes. No artwork or runtime limit was changed.
Approved bounded main artwork/asset handling remains required. MIME tests alone
cannot establish corporate Outlook appearance.

No SMTP options/transport exists in current application code; MimeKit renders.
The unregistered dispatch boundary is not an outbox or real send implementation.
Remaining: persisted actor/preparation-bound intent, dedicated authorization,
reviewed confirmation/self-only envelope, SMTP options/transport/validation,
durable unknown/partial results without auto-retry, Worker revalidation, local
sink/restart acceptance and a fresh matched delivery. Inventory ends at 018;
019 was free at inspection, not assigned/applied. Corporate relay identity,
envelope/From permissions and final audience policy require private evidence.

Current combined workflow and acceptance: [integration handoff](planned-announcement-integration.md).
Preparation optionally freezes `sourceReview` (the existing accepted override record);
null is omitted so legacy preparation fingerprints retain their original representation.
Historical increments below describe their original branch boundaries, not current gaps.

## Preparation/history from 72a503a

This increment adds persisted **Prepared** final-announcement snapshots, not a send
confirmation, queued work, SMTP acceptance or delivery. Save/live preview/download
remain independent. Claude owns source/profile/recipient/Worker work in its separate
tree; this increment does not change that boundary or introduce queue/storage hosts.

Implemented routes, authenticated Admin-only Announcements.Drafts plus owner checks:
- `PUT /api/v1/announcements/preparations/{id}?draftId={uuid}&version={N}`: explicit preparation of a complete current saved revision. UUID is the preparation idempotency key, not a send confirmation. The server reloads/revalidates owner, revision, sender and selected assets; client content/hashes/confirmation flags are not accepted. Same key/revision returns the original snapshot; reuse for a different revision returns 409 AnnouncementPreparationConflict. Stale draft returns 409 AnnouncementConflict; inaccessible data 404, denied capability 403, incomplete content 400, unavailable persistence/audit 503.
- `GET /api/v1/announcements/preparations?page=1&pageSize=25`: PreparationPage (items, page, pageSize, total); bounds 1..10000 / 1..100. Summary: id, subject, version, preparedAt, preparedBy. Every row is Prepared. SQL owner filter, timestamp DESC/unique ID order and covering index; serializable count/page, no JSON/image/render work per row. Separate pages are not a frozen snapshot.
- `GET /api/v1/announcements/preparations/{id}`: PreparedAnnouncement (id, draft, preparedAt, preparedBy, fingerprint, html, email as base64, assetRevisions, artifactType=FinalAnnouncement, state=Prepared). Privileged no-store detail only: exact rendered alternatives and CID bytes are in MIME; data-image HTML is captured in the same render action. Historical reads never resolve latest draft/files. Sender and trusted preparer profile are server-owned; immutable owner ID remains the authorization/audit key.

Snapshot SHA-256 binds the complete serialized record with an empty fingerprint
field, including exact MIME bytes, metadata, HTML, original draft and asset revisions.
It is a persisted artifact identity, not deterministic MIME-boundary regeneration or
a Message-ID deduplication guarantee. Preparation retries return stored bytes.
JSON is bounded to 24,000,000 UTF-16 bytes, MIME to 3,000,000 bytes; existing six-image
and 155-service limits remain. Storage/audit insert is one serializable transaction
under the existing owner lock, with latest-revision fence and append-only trigger.
Runtime needs only SELECT/INSERT on the new table plus existing access/audit grants.
No startup DDL, role assignment, production configuration or SMTP switch is added.

Preparation storage is promoted to 018 after immutable source migrations 016/017.
The fresh isolated `Test-AnnouncementPreparationsSql.ps1` wrapper includes 001-018.
Existing installations apply only missing reviewed migrations; no corporate execution
is authorized here. An already-applied candidate requires inventory reconciliation.

UI: saved clean editor -> **İncelemeye hazırla** -> read-only recipients/sender,
saved artifact preview/download -> **Hazırlık geçmişi**. Unsaved edits must be saved
explicitly first; existing date, preview and conflict controls remain unchanged.
No operational Send/Confirm button, simulated Sent row or distribution-recipient
defaults. Preparation/download labels explicitly say no message was sent.

Worker handoff: AnnouncementDispatchBoundary.ExecuteAsync consumes a stored
PreparedAnnouncement, IAnnouncementDispatchClaim and IAnnouncementTransport.
Neither interface has a runtime registration. The future claim must load a durable
confirmed intent, compare the stored fingerprint, recheck current approved owner/
send capability and server write gates, and atomically exclude duplicate jobs.
Preparation alone MUST NOT pass that claim. Persist the claim before transport and
immutable outcome/recipient evidence afterward through the agreed Hangfire/SQL
boundary; a crash after submission is UnknownOutcome, never an automatic resend.
The current boundary has no queue/retry and returns NotDispatched on denied claims
or invalid fingerprints. Tests only inject local in-memory capture and fake claims.
Transport outcomes distinguish LocalCapture, KnownPreSubmissionFailure,
PartialRecipientAcceptance and UnknownOutcome; accepted/rejected recipients survive
unchanged. IO/cancellation during submission becomes UnknownOutcome. Future
SmtpAccepted means relay acceptance, not mailbox delivery; partial/uncertain outcomes
require reconciliation and a new explicit targeted decision, not retry-to-all.
Confirmed intents, dispatch claims/outcomes/history and their idempotent confirmation
API remain a bounded follow-up, not implemented acceptance claims in this increment.
Confirmation must reload the current revision/assets, reject stale review, atomically
persist intent/audit and bind its idempotency key to the exact preparation fingerprint.
Different-content key reuse must fail; editing never modifies an earlier intent.
Original six branding files and Outlook opening/editing/save-copy remain outstanding.

Local evidence: `C:/SecureOpsBuild/validation/oco-preparation-20260914`; synthetic only.
`dotnet build SecureOps.sln -c Release --no-restore`: zero warnings/errors.
`dotnet test SecureOps.sln -c Release --no-build`: 1245 unit + 263 integration pass,
24 opt-in skips. `tests-hardened` contains TRX; OpenAPI comparison is included.
Fresh owned SQL harnesses, then integration `--filter 'FullyQualifiedName~ResourceSqlTests|FullyQualifiedName~AnnouncementTests'`:
43 pass (23 SQL-enabled tests), one opt-in prior browser-artifact check skipped.
Preparation SQL uses SECUREOPS_PREPARATION_SQL=1 and its separate
SECUREOPS_PREPARATION_SQL_CONNECTION; existing SQL uses SECUREOPS_SQL_TEST_CONNECTION.
`publish-final` is the matching API/UI payload; manifests/versions are in the evidence root.
`browser-final/editor` and `browser-final/preparations`: desktop/mobile, retained
editor behavior, exact stored MIME/download, 155 services/six synthetic CID images,
history and denial. Replay uses the existing announcement-hosts/journey-support harness.
The flaky legacy-upgrade replay now waits for an interactive Blazor connection;
the SQL discovery test selects the actual ordered page rather than assuming a random
UUID is in page one. Failed attempts are retained, not replaced as passing evidence.
Full format remains a release blocker on unchanged baseline findings; no waiver.

See ADR-0021 and generated `secureops-api-v1.openapi.json`. UI route: `/announcements`.
All routes require authenticated, approved persisted access and `Announcements.Drafts`
(Admin only), then owner isolation. Immutable user IDs remain authorization keys.
No new authentication flow. Default `Announcements:Enabled=false` fails closed.

## Acceptance repair from 38f6941

The 2026-09-14 user evidence supersedes earlier automated UX acceptance. Validation
is unchanged: start-after-end and inconsistent offsets return 400 AnnouncementInvalid
with WorkEnd; partial components return WorkStart/WorkEnd; an entirely blank required
time may render with an incomplete-field header but cannot be exported. No dates,
seconds or offsets are silently repaired. UI explains returned keys beside fields.

This explicitly REPLACES loading/failure-removes-HTML behavior below. During ordinary
editing, incomplete input and recoverable errors, retain the last successful preview
only for the same authorized draft. Mark it stale, show compact updating/field state,
and recover on the next valid edit after 600ms. Hidden preview does no new work.
Preparing a new sandboxed frame before its load event avoids replacing readable
content with a blank document. At most two frames exist; canceled/obsolete loads
cannot become active. Identical HTML reuses the active frame and its scroll position.
During loading/validation failure the active frame is untouched. New changed HTML
starts at its own top: opaque sandbox scroll cannot safely be copied. No scripts,
same-origin permission, iframe DOM access or alternate renderer were added.

Saved/dirty is compared with the last explicitly read/saved content; reverting edits
restores saved status. Live rendering of unchanged saved content is labeled saved,
not an unconditional unsaved preview. Loading/stale/incomplete are separate states.
Explicit save/conflict/version and saved-download rules are unchanged. Access/session
denial clears preview and editor state; navigation and draft switches invalidate all
pending work. Retention is not an authorization cache or persisted render history.

Source/profile/default-recipient work remains unimplemented, with the existing
1300-1700-line cohesive estimate and unresolved 1000-line allowance. No source fixture
journey, original branding, corporate/VDI/SMTP acceptance or release readiness claim.
No new SQL, grants, runtime configuration or dependency changes in this repair.

### Repair validation and remaining acceptance

Evidence root: `C:\SecureOpsBuild\validation\oco-repair-20260914` (outside Git).
Baseline `baseline/evidence/results.json` records sanitized requests/responses;
`continuity.json` reproduced 225 missing-frame samples and a 655px pane collapse.
`CancelPreview` cleared HTML on every edit, conditional Razor removed the iframe,
and expected field validation used a disruptive problem panel. Unconditional unsaved
preview text and an event-only dirty flag caused conflicting save labels. No API
date validation or serialization defect was found; those contracts are unchanged.

Final matched local publications: `publish-verified/api` and `publish-verified/ui`.
`payload-manifest.json` records every file's size/SHA-256; `payload-versions.json`
matches the final compiled application DLLs. ProductVersion embeds base `38f6941...`,
not the later commit: these binaries include the tested working-tree repair.
`tested-source-manifest.json` and final `closeout.json` identify that source precisely.
They are local validation payloads, not release candidates or installed binaries.

Executed commands (logs/TRX under the evidence root):
- `dotnet build SecureOps.sln -c Release --no-restore`: zero warnings/errors (`build-closeout.log`).
- `dotnet test SecureOps.sln -c Release --no-build --logger trx --results-directory <root>/tests-closeout`: 1245 unit and 259 integration passed; 23 skipped (22 opt-in SQL, one browser-artifact test).
- `scripts/powershell/Test-ResourceCatalogueSql.ps1 -DatabaseSuffix OcoRepairSql20260914 -IncludeAnnouncementDrafts`, followed by the integration project with `--filter 'FullyQualifiedName~ResourceSqlTests|FullyQualifiedName~AnnouncementTests'`: 39 passed, no skips (`sql-verified.log`). This includes 22 SQL tests, not 39 SQL tests; the remaining checks cover announcement contracts/rendering, including the earlier repaired-browser download.
- With `SECUREOPS_ANNOUNCEMENT_BROWSER_EVIDENCE=<root>/browser-continuity-final/evidence`, the integration project filter `FullyQualifiedName~BrowserDownload_ParsesSavedTurkishContentAndSixMatchingCidImagesWithoutSending|FullyQualifiedName~Test_OpenApiDocument_MatchesCheckedInUiContractSnapshot`: two passed, no skips (`mail-contract-closeout.log`). Actual final browser download, six CID/preview byte matches, recipients, Turkish inert text and all 155 services in both MIME alternatives passed; OpenAPI snapshot unchanged.
- `dotnet format SecureOps.sln --verify-no-changes --no-restore`: still fails on 58 unchanged baseline files / 201 diagnostic findings (`format-closeout.json`, `format-comparison.json`). No exception or full-DoD pass; scoped changed-C# verification is separate.

Fresh SQL-backed browser runs use the existing Chrome/Playwright installation:
`node tests/browser/announcements.cjs <playwright-core> https://localhost:5622
http://127.0.0.1:5621 https://localhost:5623 <root>/browser-regression/evidence editor`
passed v1 preservation/upgrade, save, conflict comparison, canceled navigation,
pagination, capability denial, guide persistence and six-role final export.
`node tests/browser/announcement-continuity.cjs <playwright-core>
https://localhost:5652 http://127.0.0.1:5651
<root>/browser-continuity-final/evidence after network` passed initial Start/Next/Back/
Exit without mutation, both themes' calendar and keyboard minute controls, temporary
invalid/recovery, seconds/offset/month boundaries, continuous typing/focus, network
recovery, draft switches and session revocation. Its 256 animation-frame samples
contain zero missing previews, constant pane height and unchanged page scroll.
Desktop/mobile `footer-1440.png` / `footer-390.png`, `temporary-invalid.png`,
`recovered.png` and theme captures are new evidence. `private-browser-trace.zip`
contains a timed screenshot/DOM sequence; keep it local because session context is
private. Late success/error and disposal cancellation also have deterministic unit
coverage; no delayed corporate response was exercised.

The optional `network` mode requires a local controller: on `stop-api.request`,
validate the task-owned API PID/DLL/port before stopping it and write
`api-stopped.signal`; on `resume-api.request`, rerun `announcement-hosts.ps1` with
the same root/database/payload/port plus `-FinalPresentation -ResumeApiOnly`, wait for
authenticated loopback health, then write `api-resumed.signal`. The harness now uses
supported private FileSystemDpapi keys so transport restart does not revoke sessions.
This is test setup only, not a production authentication change. Earlier ephemeral
key restart correctly returned access denial; another controller attempt used the
wrong Demo health header and timed out. Both failed attempts are retained, not passes.

The six originals named below remain absent from available local inputs; only those
files were requested. Synthetic images do not validate branding or an approved new
bundle. Outlook `16.0.20326.20144` was inventoried read-only, but no permitted desktop
interaction tool was available: open/render/edit/save-copy checks are unexecuted.
Operator check: open `browser-continuity-final/evidence/representative.eml` locally
without sending, inspect all 155 services, six images, Turkish text, recipients and
offset dates; test editing and saving a separate copy and record actual support.
Do not rename to MSG/OFT or infer universal Outlook draft behavior. Original-branding,
Outlook, source integration and full-format acceptance remain distinct blockers.

## Integrated editor continuation from e48f13e

This section supersedes earlier explicit-preview-only/new-v1 UI descriptions;
historical evidence below is retained. New drafts choose `oco-table-v2`, with no
routine template switch. A single available bundle is selected; absent/invalid
bundles show a setup error, never a v1 fallback. Existing v1 records open unchanged.
The explicit upgrade preserves text/recipients, asks for a v2 bundle, and appends
only on Save using the existing version/conflict rules. Old revisions/assets remain.

POST `/api/v1/announcements/preview` accepts the existing AnnouncementContent JSON
without a draft ID. It transforms only supplied data; extra query IDs cannot read
another owner's draft. Authentication, persisted capability/session validation and
module opt-in remain mandatory. Limit: 131072 request bytes; actor limit 120/minute,
no queue (429). Text/recipient/date/asset safety limits remain identical to Save.
Blank required fields are allowed, not fabricated; malformed nonblank fields return
400 AnnouncementInvalid. Success is UTF-8 text/html, no-store, no ETag, sandbox CSP;
`X-Announcement-Incomplete` contains comma-separated PascalCase missing-field keys.
No announcement SQL reads/inserts, revisions or sending; access/session maintenance
still uses its established SQL path. This caller-input transformation is not a
privileged stored-data read; discovery/save/saved-preview/download audit is unchanged.
No per-keystroke persistence or render audit was added. Private input is not logged.

The editor debounces visible live preview for 600ms. Generation checks reject old
success AND error responses, cancellation covers edit/hide/save/navigation/disposal.
Wide screens use a right preview; smaller screens use Edit/Preview tabs. Only a
visibility change restarts rendering; resizing an already visible saved frame does
not. Preview continuity and saved/dirty labels now follow the acceptance repair above.
Final preview and download still require a complete saved version.
Both use the same versioned rendering pipeline, escaped text, data images/CID bytes.
No second template, scripts, HTML decoding, polling, send or fake history was added.

New manual time entry uses UI-host `Announcements:DefaultDisplayOffset` (default
`+00:00`; configure the intended explicit offset, e.g. `+03:00`). This is not a
Turuncu Hat timezone mapping. Date is separate from labeled 00..23 hour and 00..59
minute controls. Advanced seconds retain imported values. Explicit display-offset
conversion preserves the instant and adjusts the date across midnight; direct time
editing intentionally changes the instant. Partial entry stays incomplete/invalid,
not the previous valid time. Existing strings/offsets survive unrelated edits.
Native date controls inherit the actual app theme's light/dark color-scheme;
the baseline kept normal black picker icons on a dark field. Additive content field
`dateTextRevision` defaults to `iso-v1` for old JSON/API clients, preserving old
rendering/hashes. New UI v2 drafts and explicit upgrades select `tr-v1`: email dates
use dd.MM.yyyy HH:mm:ss UTC +/-HH:mm, in both alternatives. This presentation revision
is hash-bound; raw ISO values and instants remain unchanged. Other values, and tr-v1
with legacy oco-v1, are rejected. Existing v2 drafts retain their saved selection.

Renderer reuse is limited to 32 successful SHA-256/type validation receipts, under
a lock; no bytes, paths, private draft data or access decisions are cached. Every
request still validates path/permissions/size, reads bytes and computes their hash.
Only a previously fully decoded identical hash skips codec work. Missing/changed
bytes cannot be concealed by the receipt; save/export fingerprints are unchanged.

Delivery checklist: editor/live/date work implemented; source jobs, maintenance
profile/default-recipient review and corporate acceptance are NOT implemented or
simulated here. Worker Program.cs still has no Hangfire registration; SCCM is absent.
Completing that boundary adds an estimated 1300-1700 lines: job/SQL state 350-450,
adapters 300-400, protected profiles/provenance 200-300, reviewed UI/tests 450-550.
The five names remain NonProd/Prod01/Prod02/ProdSingle/ProdRPA maintenance groups.
Local script inspection confirms two active base To rules, five extra Cc for
ProdSingle, two Cc plus High for ProdRPA; commented To is excluded. Those recipients
belong to a distribution request, NOT an approved final-announcement audience.
No real IDs, addresses or source snapshot were copied into this increment.

No dependency, migration or grant delta. Existing 014-015 prerequisites and six
original files listed below remain required. UI-only DefaultDisplayOffset is the
only optional configuration addition; no server configuration was changed. No new
asset revision, original branding approval, Outlook acceptance or external writes.

### Editor verification, 2026-09-13

Evidence root: `C:\SecureOpsBuild\validation\oco-editor-20260913` (outside Git).
`dotnet build SecureOps.sln -c Release --no-restore`: zero warnings/errors,
build-verified.log. `dotnet test SecureOps.sln -c Release --no-build --logger trx`:
1240 unit + 254 integration passed; 22 opt-in SQL skipped (tests-closeout.log).
OpenAPI was regenerated with SECUREOPS_UPDATE_OPENAPI=1 and the existing snapshot
test; the normal comparison passes. Fresh SQL harness: DatabaseSuffix
OcoEditorDateSql20260913, IncludeAnnouncementDrafts; ResourceSqlTests|AnnouncementTests
filter: 33 passed, zero skips (20 existing SQL + 13 announcement tests, two SQL).
sql-verified.log covers owner/version/audit, transient no-save, date presentation,
155-service MIME alternatives, asset invalidation and authenticated download.
No corporate database, source adapter or SMTP call was used.

Full `dotnet format SecureOps.sln --verify-no-changes --no-restore` still fails:
58 baseline files; diagnostic comparison with oco-template-20260913 is identical
(zero differences). format-verified.json is retained. Changed-C# format is checked
separately; it is not a waiver or substitute. Full DoD/release readiness stays blocked.
Local synthetic performance: 240 drafts / 1200 revisions, five 25-item list queries
210ms total in the recorded run, largest response 6999 bytes; owner index seek seen.
These are local observations, not corporate capacity/VDI acceptance. Asset tests
record cold versus 20 hash-checked reads; no latency guarantee is asserted.

Matched working-tree payloads: publish-verified/api and publish-verified/ui.
payload-manifest.json contains per-file sizes/SHA-256; payload-versions.json records
assembly ProductVersion 0.1.0+e48f13e... (pre-commit base, NOT the final source HEAD).
API DLL SHA-256: 017852179CEF80720A5FB3B2E1F5E22A1D21ACFAB40BC524051B95090687CA57.
UI DLL SHA-256: BDC77216783B031DBF367546E1E533D362E7FCC03AD079CC36CA39E150E2E37D.
No release package was prepared. Final source commit is recorded in closeout.json.

Fresh browser DB OcoEditorVerified20260913; local ports 5561/5562/5563. Run existing
announcements.cjs with the published UI/API/denied URLs, a fresh evidence directory
and `editor` mode, using announcement-hosts.ps1 -FinalPresentation. The successful
browser-verified/evidence/results.json records new-v2/legacy upgrade, keyboard calendar
in both themes, every minute, seconds/offset preservation, rapid edits/hidden preview,
failure clearing, conflict recovery, guide dismissal/reopening, owner paging, denial,
155 services and download. Actual images: live-desktop.png, live-mobile.png,
calendar-light.png, calendar-dark.png, final-preview-1440.png, final-footer-390.png.
Screenshots were taken from the visible iframe and inspected; synthetic branding only.
Earlier failed attempts remain: stale Blazor test reads, encoded srcdoc assertions,
and the fixed unnecessary responsive iframe recreation. No rejected browser command
was retried or bypassed. Initial Start/Outlook/corporate source acceptance remain
unverified; source-fixture journey is not claimed because the source slice is absent.
Older writers may drop new JSON presentation metadata; downgrade writes are not safe
by assumption. Release/In Use/OR-to-SDM checkouts and previous archives are untouched.

## Implemented journey

Client creates a UUID. PUT `/api/v1/announcements/{id}?version=0` creates a draft.
GET the same path returns latest content. PUT `?version=N` saves a new revision
only when N equals current version. Single-draft successes return quoted numeric `ETag`;
use its number in the next request, not an unsupported If-Match header.
GET `?version=N&format=html` returns safe UTF-8 HTML; `format=eml` returns
`message/rfc822` attachment `announcement-{uuid-without-hyphens}-vN.eml`.
Both require a current, complete saved revision. GET has no save/send side effect;
privileged reads/download preparation are audited fail-closed. Responses are no-store.
Body/recipient edits must be saved before preview. Download is not Sent or delivery.
An Outlook-editable draft is not guaranteed. No Outlook COM, SMTP or source calls.

Response body for save/read is the content below (camelCase); `X-Announcement-Origin`
is `Manual`. UUID lives in route; version in ETag. Audit actor/time, sender,
validated template revision, asset fingerprint and provenance are persisted metadata.
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
409 `AnnouncementAssetMissing`: selected file/directory is absent; exports stop.
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

Saved recipient sets and Send History have no endpoints yet. Do not fabricate
empty successful history or wire send buttons. Codex owns this module's API/UI
against the discovery routes below. Immutable send-intent/Hangfire/SQL
states and retry rules are in ADR-0021.
Changing content invalidates future send confirmation; downloads/local captures
must stay separate from actual sends. Real SMTP remains disabled.

## Configuration and dependencies

Local opt-in now requires schemas 014-015, existing SQL access/session configuration,
`Announcements:Sender`, absolute private local `AssetDirectory` (outside publish),
and `Banners:{revision}` mapping to a flat local filename. No asset upload/URL fetch.
Retain old revision files; changing bytes blocks previous exports. PNG/JPEG only,
24..262144 bytes, max 2048x1024, single frame, complete codec decode; no symlinks.
MimeKit 4.17.0 and SkiaSharp 4.151.2 are centrally pinned, MIT, .NET 8 compatible:
[MimeKit](https://www.nuget.org/packages/MimeKit/4.17.0),
[SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.151.2).
Windows x64 native Skia assets were locally published and exercised below; other
platforms and corporate installations remain unverified. No release package here.
No appsettings, SMTP credentials, authentication, grants or production files changed.

## Discovery contract and implemented editor

GET `/api/v1/announcements?page=1&pageSize=25` returns `AnnouncementPage`:
`items`, `page`, `pageSize`, `total`. Page 1..10000, size 1..100; invalid input is
400 AnnouncementInvalid with Page/PageSize fields (no silent clamp). An empty or
out-of-range page has empty items and the correct total. There is no owner filter
parameter: the server always resolves the current persisted user, including Admin.
SQL selects one latest revision per owned UUID, then orders UpdatedAt DESC and
UUID text ASC (binary ordering). Count/items share a serializable read. Subsequent
pages are live, not a snapshot: concurrent saves may reorder them; refresh/reset
to page 1 after saving. Do not assemble all pages into a supposedly frozen history.

`AnnouncementSummary` contains only id, version, ocoReference, subject, workStart,
workEnd, updatedAt and nullable missingFieldCount. Strings remain original plain
text. MissingFieldCount is derived at save: 0 means required text/recipient fields
present, positive means incomplete, null means legacy unknown. It does NOT verify
banner availability, scope, source dates, approval or send readiness. A historical
JSON revision is never rewritten for this projection. Open/edit/save to recompute.

```json
{"items":[{"id":"11111111-1111-4111-8111-111111111111","version":2,"ocoReference":"OCO-SYNTHETIC","subject":"Planned work & checks","workStart":"2026-09-13T01:00+03:00","workEnd":"2026-09-13T02:00+03:00","updatedAt":"2026-09-12T10:00:00+00:00","missingFieldCount":0}],"page":1,"pageSize":25,"total":1}
```

GET `/api/v1/announcements/banners` returns `AnnouncementBanner[]` sorted by
revision (ordinal). Maximum 32 allowlisted revisions, no filesystem enumeration.
`Announcements:BannerLabels:{revision}` optionally supplies a plain label (1..80
characters, no controls/angle brackets); invalid/missing labels use the revision.
No config change is needed for existing installations; labels are optional.

```json
[{"revision":"synthetic-v1","label":"Local banner & checks","state":"PresentNotValidated"},{"revision":"unavailable-v1","label":"Unavailable banner","state":"Missing"}]
```

States: PresentNotValidated = file metadata/size checks passed, not codec/hash
attestation; Missing = file/directory absent; Invalid = invalid size; Unavailable =
path/permission/I/O failure. Only PresentNotValidated may be selected; a later
selected save/preview/export can still fail. No thumbnail, image bytes, hash,
sender or physical path is returned. Unknown persisted revisions stay visibly
unavailable, never replaced silently. Do not claim metadata identifies a changed
hash: explicit preview/export compares current validated bytes to the saved hash
and returns AnnouncementAssetChanged. Metadata is uncached, and never decodes images.
Both discovery routes use the unchanged Admin-only Announcements.Drafts capability,
no-store responses and fail-closed audit. List has no per-row asset I/O or rendering.

Implemented "Planlı Çalışma Duyuruları" uses existing Blazor Server,
MudBlazor, authenticated typed API/session transport and capability visibility.
Use a server-paged table with subject/OCO, work dates, update time and field status;
preserve literal text via escaped rendering, never HTML decode or MarkupString.
New opens a client-generated UUID; edit explicitly GETs the selected UUID and ETag.
Dynamic To/Cc rows need stable keys, field labels, limits and duplicate feedback.
Load banner choices on editor entry or explicit retry, not per keystroke; display
the saved missing revision and actionable failure instead of silently switching it.
Explicit Save PUTs version 0/current N; replace local content with normalized reply
and capture ETag. Preserve edits on failure. Show dirty state and confirm before
navigating/discarding; no background autosave or preview polling. Preview/Download
require the selected saved N with no unsaved changes; ask to save first. Fetch HTML
only on Preview and isolate it in a sandboxed frame with the existing safe contract.
Download uses authenticated bytes + server attachment filename, never a sending UI.
400 fields navigate to editor inputs; 409 keeps edits, reads latest separately,
shows explicit differences and asks before reapplying to the new version. Do not
blindly retry writes or discard changes. Missing/changed banner errors remain visible.
No Send button or fictional Sent tab. Browser evidence below covers keyboard/mobile
and conflict UI behavior; it is not corporate/VDI acceptance.

Future ordinary-user mapping (proposal, not implemented): a limited
AnnouncementAuthor application role grants ONLY Announcements.Drafts to approved
users through existing versioned role assignment. It permits owned list/edit/
preview/download and banner metadata, not Admin, directory access or sending.
Real sending and send-history capabilities require separate reviewed contracts.

Executable contract exercise: `AnnouncementTests.DraftApi_PersistsOwnRevisionsPreviewsAndDownloadsWithoutSending`
uses TestServer, synthetic assets, persisted access/session SQL and disabled adapters;
it exercises discovery, save/read, incomplete fields, preview/MIME, conflicts,
401/403, audit failure and missing/changed banners. `AnnouncementTests.Discovery*`
adds SQL paging/ownership/concurrent update checks. Run with the fresh harness below;
do not point fixtures at an existing release host or corporate database.

015 only adds IX_AnnouncementDraftRevisions_OwnerLatest(OwnerId,Id,Version DESC),
supporting the owner filter / MAX version grouping without indexing body JSON.
The final page still sorts latest-owned JSON timestamps and uses PK lookups; it
is not a constant-cost query for unbounded owner history. Save/list use transaction
application locks (Exclusive/Shared, public principal, 5-second bound), preventing
the locally reproduced index/PK deadlock; timeout fails closed, no automatic retry.
All announcement writers must use this protocol; do not mix old/new writer binaries.
No new runtime grants, auth flows, cache framework, SMTP or source calls are added.

## First-increment validation, 2026-09-12 (1e6454c)

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

## Historical discovery validation and fresh backend replay

Base: `1e6454cebfd0944d114bd413fd462b37499bc66c`, same isolated announcement
worktree/branch as ADR-0021. Evidence: `C:\SecureOpsBuild\validation\oco-discovery-20260912`.
`dotnet build SecureOps.sln -c Release --no-restore`: zero warnings/errors.
`dotnet test SecureOps.sln -c Release --no-build`: 1228 unit + 251 integration
passed; 22 opt-in SQL skipped in this run (`tests-all-final.log`). OpenAPI passed.
Fresh harness `-DatabaseSuffix OcoDiscoveryFinal20260912 -IncludeAnnouncementDrafts`
applied 001-015 with preservation checks, without touching older DBs/archives.
With its guarded connection, `dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-build --filter 'FullyQualifiedName~ResourceSqlTests|FullyQualifiedName~AnnouncementTests'`
passed 30/30, no skips (20 existing SQL + 10 announcement tests, including 2 SQL).
`sql-final.trx` contains measurements: 240 synthetic drafts, 1200 revisions,
5 x 25-item SQL pages took 212.68 ms including serialization/assertions; largest
page was 7000 bytes. A prior successful run was 146.82 ms / 7002 bytes. The cached
plan referenced the owner index and Index Seek. This is not an actual-row execution
plan or a load/corporate capacity test. Own-history grouping and latest JSON sorting
still scale with data size; separate live pages can move under concurrent edits.
100 two-banner metadata calls took 6.99 ms; exclusive file locking proved no image
bytes were read. Selected actions still fully validate their one asset; no new
per-row or keystroke decoding exists, so no cache was justified or introduced.
The authenticated one-row list response was 287 bytes. Measurements are local,
synthetic and warm-state, not VDI acceptance. Public-only SQL user acquired the
transaction application lock without extra grants. Earlier deadlock evidence in
`announcement-A.trx` is retained; owner locks fixed it, tested with repeated readers/writes.
Full format failed on 58 untouched baseline files; scoped format and diff checks
passed. No waiver. Dependency/config provider/auth changes: none; BannerLabels is optional.
No UI, SMTP, source integration, packaging, deployment or active-release edits.

Execute the backend contract journey from the isolated worktree with
a NEW test-owned DB each time (existing approved LocalDB harness required):
```powershell
$suffix = 'OcoUi_' + [Guid]::NewGuid().ToString('N')
$results = Join-Path $env:TEMP ('wasas-oco-ui-' + [Guid]::NewGuid().ToString('N'))
./scripts/powershell/Test-ResourceCatalogueSql.ps1 -DatabaseSuffix $suffix -IncludeAnnouncementDrafts
$previous = $env:SECUREOPS_SQL_TEST_CONNECTION
try {
    $env:SECUREOPS_SQL_TEST_CONNECTION = "Data Source=(localdb)\SecureOpsResourcesV1;Initial Catalog=SecureOps_ResourcesV1_$suffix;Integrated Security=True;Encrypt=False;Connect Timeout=15"
    dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-restore --filter 'FullyQualifiedName~AnnouncementTests' --logger 'trx;LogFileName=announcement-ui-contract.trx' --results-directory $results
    if ($LASTEXITCODE -ne 0) { throw 'Local announcement contract failed; retain evidence.' }
} finally { $env:SECUREOPS_SQL_TEST_CONNECTION = $previous }
```
This command validates backend contracts, not a running browser UI.

## UI increment and local verification, 2026-09-12

Base `a1818e17ef498a01c3b7444d0381a75724184122`, isolated branch/worktree unchanged.
The UI has a 25-row owner page, one New action, structured editor, stable To/Cc
rows, allowlisted banners, optional notes/restart details, explicit Save/Preview/
Download, field-focus errors, dirty-navigation confirmation and reviewed conflict
reapplication. Dates retain explicit offsets; text is escaped, never decoded.
Preview is a sandboxed no-script frame with data-only images and a restrictive CSP.
The typed client uses existing per-browser API sessions; API authorization remains
authoritative. No autosave, per-keystroke preview, send/history or caching was added.

The existing guided tour supports first-entry Start/Not now and manual reopening.
`PUT /api/v1/resources/me/guide` accepts `{expectedVersion,guide}`;
guide defaults to `resources`; `announcements` additionally requires
Announcements.Drafts. `ResourcePreferencesResponse.announcementGuideDismissed`
is a separate optional persisted boolean, default false; shared preferences retain
their version/owner/audit protocol. The tour never saves draft content. Current
Admins have both Resources.View and Announcements.Drafts. A future author role
must also review personal-guide access without granting administrative capabilities.
No SQL/config/grant delta in this increment; existing 014-015 and module opt-in
prerequisites above still apply. Additive JSON is not a promise of safe downgrade.

Evidence root: `C:\SecureOpsBuild\validation\oco-ui-20260912` (outside Git).
`dotnet restore SecureOps.sln`; `dotnet build SecureOps.sln -c Release --no-restore`:
success, zero warnings/errors. `dotnet test SecureOps.sln -c Release --no-restore`
with TRX results: 1236 unit + 251 integration passed; 22 opt-in SQL skipped in the
default run. OpenAPI generation used the existing opt-in test; normal comparison passed.
Fresh harness `-DatabaseSuffix OcoUi20260912 -IncludeAnnouncementDrafts`, then the
ResourceSqlTests/AnnouncementTests filter above: 30 passed, no skips (20 existing
SQL + 10 announcement tests, including 2 SQL); `sql.trx`. No corporate calls.
Full format command remains `dotnet format SecureOps.sln --verify-no-changes --no-restore`.
Its 58 baseline files have the same diagnostic descriptions as discovery evidence,
including existing naming findings in the touched UiProblemFactory. No waiver;
full DoD is still blocked. New C# formatting and changed-file whitespace are checked
separately, not substituted for the mandatory full command.

`tests/browser/announcement-hosts.ps1 -EvidenceRoot <fresh-published-root> -DatabaseSuffix <fresh-Oco-suffix>`
starts local published API/UI on 5431/5432 and denied UI on 5433, refusing occupied
ports. First run the existing fresh SQL harness and publish API/UI into `api`/`ui`
under that root. It uses supported Demo auth, SQL access/session/audit, Mock identity,
disabled source/Jira and ReadOnlyIntegrationMode=false ONLY for this local composition;
both external write flags remain false. No runtime settings or test data enter Git.
Run `node tests/browser/announcements.cjs <playwright-core> https://localhost:5432 http://127.0.0.1:5431 https://localhost:5433 <fresh-evidence>`.
Optional last argument `start` checks initial Start instead of Not now. Test-only
playwright-core 1.63.0 uses installed headless Chrome; no application dependency.
`attempt4/browser/results.json` records real SQL persistence, inert Turkish preview,
image loading, authenticated MIME download without send/version mutation, saved tour
dismissal/reopening, keyboard tour, conflict recovery, dirty-discard cancellation,
25/2 paging and direct API/UI denial. Desktop 1440/mobile 390 PNGs are beside it.
Earlier failed attempts are retained: fixed Razor string binding and missing scoped
CSS bundle; native date seconds and asynchronous preference timing were test issues.
`payload-versions.json` records exact attempted/tested DLL and native asset hashes;
these are working-tree builds, not committed release payloads. The full-page preview
PNGs have a blank off-screen iframe despite passing frame DOM/image assertions;
the final replay adds scrolled viewport captures. Visual frame acceptance is pending.
Subsequent shell navigation cancellation and comparison-edit guards are not included
in attempt4 binaries. The prepared attempt5 replay additionally checks those guards,
initial Start and mobile re-anchoring, but its invocation returned `rejected: blocked by policy`.
Do not label that final browser replay passed. Outlook/VDI/SMTP are not tested.

## Versioned final-table increment, 2026-09-13

Base `142a48f41c2faaead2d151486304e0b8f41f0e50`, same isolated worktree.
Explicit `templateRevision: "oco-table-v2"` selects the new table; omitted means
`oco-v1`. Old stored JSON, rendering, revisions, hashes and archives are untouched.
`scope` now labels the system/application separately from `affectedServices`:
an optional string array, at most 256 nonblank entries, 256 characters per entry,
16000 total characters including one separator per entry; controls are rejected.
A v2 draft may save an empty array but cannot preview/export until populated.
v1 rejects a nonempty array rather than silently dropping services. Existing body
and serialized-JSON size caps remain. The 155-entry representative case is tested
without truncation; all entries appear in both MIME alternatives. No HTML decoding.

GET `/api/v1/announcements/banners?templateRevision=oco-table-v2` returns bundle
metadata in the existing DTO/error/capability contract, at most 16 bundles. Omit
the parameter for existing banners; unsupported template values return 400.
Metadata does not decode images or expose paths. Selected Save/Preview/Download
validates up to six bounded assets sequentially. The persisted hash includes
template, bundle revision, ordered role/revision/type/hash identities and footer.
Changed bytes/footer block export until an explicit reviewed save. Missing files
return 409; invalid configuration fails closed (503). Metadata presence is not
branding approval. No substitution, URL fetch, new cache or per-keystroke rendering.

The editor offers explicit format selection, a collapsed counted service list,
saved preview/download and the existing optional tour. Switching formats preserves
edits; a nonempty service list prevents reverting to v1 until explicitly cleared.
This is a final-announcement artifact, NOT the legacy distribution-request email
with an HTML attachment. No legacy distribution recipients are imported or inferred
as the final audience. Both modes remain unsent; distribution-request output itself
is not implemented. All content and OCO/scope provenance remain Manual.

### Legacy comparison and remaining parity

Local PowerShell AST/text and MSG HTML fragments were inspected without execution,
corporate URL fetching or committing private values. HTML and MSG represent different
OCOs: only structure was compared. The new table follows the MSG heading order,
alternating rows, bounded width, header/main images and footer roles. Checks remain
a separate saved field, presented with notes under Notlar/Ozel Durumlar. Restart
dates remain separate optional rows. Work times retain explicit offsets: source
LMT/timezone semantics are unverified, so no conversion or LMT attestation is made.
Tracking script/remote image URLs/private footer text were not copied.

Parity checklist:
- Implemented: separate system/services, bounded long list, versioned six-role
  bundle, safe preview/CID MIME correspondence, v1 preservation and manual editing.
- Pending source slice: configured NonProd/Prod01/Prod02/ProdSingle/ProdRPA maintenance
  profiles (not hosting environments), protected collection-ID mapping and explicit
  source action. Script getCollID/getServerList/getRFS/getOCO are evidence only.
  No SCCM adapter exists and Worker startup is a stub. Reuse Hangfire/SQL and existing
  Turuncu Hat transport; add bounded jobs, exact/ambiguous/partial outcomes and
  per-device/service provenance before wiring a source button. Preserve source
  snapshots separately from overrides; re-fetch requires a reviewed difference.
- Pending profile defaults: sendMail has two active base To rules, five extra Cc
  rules for ProdSingle, two Cc rules and High priority for ProdRPA; commented To is
  excluded. Real addresses/collection IDs belong only in protected configuration.
  Proposed profile switches must retain additions/removals and require confirmation.
  Collection membership is not OCO scope; m_active is not approval.
- Pending final acceptance: six approved originals, approved footer, visual/Outlook
  comparison. Synthetic assets prove rendering only. SMTP and sending remain absent.

### TEST prerequisites (operator managed, not deployed here)

No new migration or grants. Existing 014-015/access/session/audit prerequisites
still apply; do not replay 012/013 or any migration chain blindly. No auth/runtime
dependency update. Existing v1 configuration remains valid. Only v2 requires an
additional protected `Announcements:Bundles` mapping. Illustrative local-only shape:

```json
{"Announcements":{"Bundles":{"approved-v1":{"Label":"Approved presentation","Footer":"Approved plain footer","Assets":{"header":"header-v1","main":"main-v1","logo":"logo-v1","linkedin":"linkedin-v1","instagram":"instagram-v1","youtube":"youtube-v1"}}},"Banners":{"header-v1":"header-v1.jpg","main-v1":"main-v1.jpg","logo-v1":"logo-v1.jpg","linkedin-v1":"linkedin-v1.png","instagram-v1":"instagram-v1.png","youtube-v1":"youtube-v1.png"}}}
```

Example identifiers are not an approval. Supply all six approved files under the
existing absolute private AssetDirectory outside publish/webroot; flat filenames,
PNG/JPEG signature, 24..262144 bytes, single frame, max 2048x1024, no reparse points.
API process identity needs only directory traversal/read and file read; deployment
custodian controls revisions. UI does not need direct filesystem access. No write,
upload or executable-content permissions, web endpoint or external image URL.
Keep all prior immutable assets/config revisions for retained drafts; never overwrite
bytes under an existing revision. Validate metadata then a saved preview and MIME
locally; missing/invalid/changed roles stop affected exports, not silently fall back.
Use existing protected sender, SQL permissions and external-write guards unchanged.

Missing originals requested, not downloaded: `planlimail_duyuru_header.jpg`,
`planlimail_duyuru_main.jpg`, `turkish_technology_logo.jpg`,
`planlimail_duyuru_linkedin.png`, `planlimail_duyuru_instagram.png`,
`planlimail_duyuru_youtube.png`. Confirm dimensions/byte limits before deployment;
out-of-policy originals need separately reviewed preparation, not silent resizing.
Windows x64 .NET 8 with the matching existing Skia native assets is the tested
runtime. No package/deployment, production config edit, source call or SMTP activity.

### Final-table validation evidence

Evidence root `C:\SecureOpsBuild\validation\oco-template-20260913`, outside Git.
`dotnet build SecureOps.sln -c Release --no-restore`: zero warnings/errors,
`build-stable.log`. `dotnet test SecureOps.sln -c Release --no-build` with TRX:
1236 unit + 253 integration passed; 22 opt-in SQL skipped (`tests-stable.log`).
OpenAPI normal comparison passed; the updated snapshot was generated by the existing
opt-in contract test (`openapi.log`), not edited by hand. Dependencies unchanged.
Fresh harness `-DatabaseSuffix OcoTemplateStable20260913 -IncludeAnnouncementDrafts`
then the ResourceSqlTests/AnnouncementTests filter above: 32 passed, no skips,
`sql-stable.trx` (20 existing SQL + 12 announcement tests, including two SQL journeys).
The new tests cover legacy JSON/defaults, size bounds, six-role metadata without
image reads, hash/asset/role failures, safe long text, MIME byte correspondence,
version persistence, owner isolation, access denial and export without mutation.
Contract JSON examples, including protected configuration shape, are executable tests.
Full `dotnet format SecureOps.sln --verify-no-changes --no-restore` still fails:
58 baseline files, identical diagnostic records (`format-closeout.json` and
`format-comparison.json`). Changed-C# scoped format passes (`format-scoped-closeout.log`);
this is not a waiver of the full gate. Full DoD remains blocked by baseline formatting.
`dotnet list SecureOps.sln package --vulnerable --include-transitive --source
https://api.nuget.org/v3/index.json` found no known advisories at execution time
(`vulnerabilities-stable.log`), not a future-safety guarantee. No package pins changed.

The retained failed `sql-final` run exposed parallel test interference from another
fixture's database-wide audit-failure trigger. Announcement tests now use an xUnit
nonparallel collection; existing application concurrency tests still run concurrent
operations internally. No production SQL or audit code changed. Earlier failed
build/test attempts (style and newly added documentation-example count) are retained,
not presented as final success or corporate evidence.

The matched working-tree API/UI were published under `browser-final/api` and
`browser-final/ui`. `payload-manifest.json` records every file's size/SHA-256;
application assemblies were compared against the final production build. Later
edits only affect tests/docs. These are local validation payloads, not release ZIPs.
`payload-versions.json` retains assembly ProductVersion `0.1.0+142a48f...`:
it embeds the pre-commit base, not a claim that unchanged base binaries contain v2.
The working-tree additions are identified by exact payload hashes and this diff.
Fresh browser harness: `-DatabaseSuffix OcoTemplateBrowserAccept20260913 -Port 5481
-FinalPresentation -PayloadRoot <evidence-root>/browser-final -EvidenceRoot
<evidence-root>/browser-acceptance`, after its separate fresh SQL harness.
Run `node tests/browser/announcements.cjs <existing-playwright-core>
https://localhost:5482 http://127.0.0.1:5481 https://localhost:5483
<evidence-root>/browser-acceptance/evidence final-template`.
`browser-acceptance/evidence/results.json` passes saved v1/v2 preview/download,
155 services, six loaded data images, Turkish inert markup, native keyboard scroll
to footer, guide dismissal/reopening/nonmutation, stale-save comparison, retained
edits, canceled navigation, 25/3 pagination and direct API/UI capability denial.
Desktop/mobile screenshots are `final-preview-1440.png`, `final-footer-1440.png`,
`final-preview-390.png`, `final-footer-390.png`; they were visually inspected.
No previous screenshot was reused. The new allowed replay did not retry the earlier
policy-rejected `start` command; initial Start remains separately unverified.

The replay caught and fixed MainLayout's literal `Value="this"` cascade: using the
actual instance allows canceled navigation to clear its progress indicator. Long
iframe content itself was not missing: off-screen full-page captures and in-frame
scroll waits were misleading. Visible viewport captures and bounded external
observation of actual Ctrl+End now verify the footer without weakening sandbox/CSP.
Synthetic images do not establish original branding; Outlook/VDI/corporate source
acceptance and full legacy parity remain pending. No release checkout was modified.

## Bounded dependency decision

Direct/transitive API inventory used NuGet v3, with solution-wide vulnerability
scan after restore (`vulnerabilities.log`: no known advisories at execution time).
Dapper 2.1.28 -> 2.1.86 centrally pinned, Apache-2.0, net8.0; SQL regressions above
exercise the update. [Official release](https://github.com/DapperLib/Dapper/releases/tag/2.1.86).
No tracked lockfiles exist; .NET 8 and Microsoft 8.0.30 pins remain unchanged.
MimeKit 4.17.0 / SkiaSharp and native assets 4.151.2 (MIT) remain matched. NuGet
lists 4.152.0, but [official notes](https://mono.github.io/SkiaSharp/docs/releases/4.152.0.html)
still call it preview; defer until stable-support status and native compatibility
are reconciled. Only local Windows x64 codec/preview/MIME execution is evidenced.
MudBlazor 6.16 -> 9.9.0 is NOT applied: theme/dialog/menu activation changes affect
the shared shell; [migration guide](https://github.com/MudBlazor/MudBlazor/issues/12666).
Microsoft 10 requires framework migration. FluentValidation 12, SqlClient 7,
Swashbuckle 10 and major Serilog updates need separate contract/platform review;
Hangfire 1.8.25 and other servicing updates remain inventoried, not validated here.
This bounded UI increment does not certify all dependencies current or future-safe.

## Source integration contract, 2026-09-14 (pending Codex editor connection)

Routes need the same authenticated, approved, `Announcements.Drafts` owner as the draft routes, plus
`Announcements:Enabled` and `AnnouncementSource:Enabled`. GET `source/profiles` returns
`MaintenanceProfileChoice[]` (`name`, `label`, `state` Configured|Unconfigured|Invalid, `missing`);
collection IDs are never returned. POST `{id}/source/jobs` with `{profile, ocoReference, submissionKey}`
returns 202 `AnnouncementSourceJobStatus`; an identical normalized submission returns the same job with
`duplicateOf` set. Different profile/OCO input for that key returns 409. GET `{id}/source/jobs[?jobId=]` refreshes status; poll
only while `terminal` is false. States are Queued, Running, Succeeded, Partial and Failed; Partial is
usable but explicitly incomplete, so read `completeness`. GET `{id}/source/jobs/{jobId}/proposal` returns
`AnnouncementSourceProposal`, whose `fields[]` carry `current`, `proposed`, `sourceText`, `origin` and
`state` (Unchanged, Changed, SourceUnavailable, RequiresOperatorOffset). WorkStart/WorkEnd are always
RequiresOperatorOffset with intact raw `sourceText`; explicit offsets are separate snapshot evidence, not automatic draft times. RestartStart/RestartEnd are always
SourceUnavailable. `to`/`cc` are `RecipientDifference` (`added`, `removed`, `preservedManual`,
`preservedRemoval`): show it, never auto-replace. `audience` is always `DistributionRequest`, not a
final-announcement audience, and `stale` true means a newer snapshot was already applied. POST
`{id}/source/apply` with `{jobId, expectedVersion, expectedOverrideVersion, fields[], applyRecipients, applyAffectedServices}`
returns `AnnouncementSourceApplyResult` plus a new draft revision, where `skippedFields` lists what the
source could not supply. Errors reuse `code`/`fields`/`correlationId`: 403 AccessDenied; 404
AnnouncementNotFound|AnnouncementSourceJobNotFound; 409 AnnouncementConflict (re-read
version)|AnnouncementSourceStale|AnnouncementSourceOverrideConflict|AnnouncementSourceSubmissionConflict; 400 AnnouncementSourceInvalid; 503
AnnouncementSourceDisabled|AnnouncementSourceProfileUnavailable|AnnouncementSourceJobHostUnavailable|
AnnouncementSourceUnavailable/Timeout/Rejected. Configuration: `AnnouncementSource:{Enabled,
CollectionProvider,ServiceProvider,...}` with per profile `{CollectionId,Scope,Impact,Checks,Description,
To,Cc,HighPriority}`, plus `Hangfire:{Enabled,SchemaName,Queue}`, migrations 016-017 and the separately installed
Hangfire schema (`PrepareSchema=false`). Proposal `overrideVersion` must be returned as `expectedOverrideVersion`.
Draft revision, overrides and both required audits commit atomically. Declining recipients preserves their
entire reconciliation baseline. No endpoint sends mail; connection to Codex's editor remains explicitly pending.
See [source repair acceptance and integration handoff](planned-announcement-source-acceptance.md) for executed
SQL/API/Worker evidence, recovery boundaries, exact contracts and remaining gates.
