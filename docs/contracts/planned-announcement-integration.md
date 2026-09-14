# OCO local integration

Inputs: editor `422c940cd642537570f23f1e052dd4e0a01195cb`, source
`90300f09d37223fe10694b7dc8aa4781943edaff`; both clean at inspection.
Integration: `feature/planned-oco-integration-closeout`, isolated worktree
`C:\SecureOpsBuild\secure-ops-planned-oco-integration`, one writer.
Merge `a2607c66fb361ab1c78f89a48b2cb08db3e2a640` preserves both histories.
Imported source delta against editor: +3896/-8; OpenAPI regenerated as the exact
union, zero changed/removed entries from either input. New integration changes
are counted separately against this mechanical merge, with the repository 1000-line cap.
Common original baseline: `38f6941fbcd81b158e89dab16f814f0ae34a2696`; inherited merge +5406/-91.
Authored integration: +697/-52 = 749 lines without rename discount (rename-aware +678/-33 = 711).

The source panel uses the existing per-browser typed API client. Retrieval needs
an explicitly saved owned draft; dirty content blocks retrieval/apply. The panel
retains one submission key for retry, polls only status, never auto-applies, and
disposal/navigation/access loss invalidates responses. Review carries both versions.
Work/restart evidence remains read-only; existing manual time controls are unchanged.
Recipient differences are distribution-request proposals, not approved delivery lists.
Preparation freezes the existing source-override model alongside the exact saved
revision, MIME and assets. Old preparations without this optional metadata retain
their fingerprint representation. Later edits cannot mutate prepared bytes.

018 promotes the preparation candidate after immutable 016/017. Fresh harness
opt-in includes 001-018; existing installations apply only their missing reviewed
migration, never replay the chain. Hangfire 1.8.6 schema 9 is installed separately;
PrepareSchema=false, one dedicated DB/schema/queue, no runtime DDL. Preparation
adds SELECT/INSERT only; source tables need SELECT/INSERT/UPDATE, drafts SELECT/INSERT,
audit INSERT and existing access/session permissions; Hangfire needs schema DML.
Retain data on rollback. A previously applied unnumbered candidate needs inventory
reconciliation, not blind 018 replay. No sending or transport registration is added.

## Combined local acceptance

Evidence root: `C:\SecureOpsBuild\validation\oco-integration-20260914`.
`closeout.json` records final HEAD, cleanliness, exact imported/new diff counts,
owned process shutdown and committed-payload replay durations. `payload-hashes.json`
records actual assembly SHA-256/ProductVersion and their matching build outputs.
The committed replay uses fresh payload directories and a fresh browser SQL database;
private traces, fixture addresses, keys and evidence remain outside Git.

Combined build: zero warnings/errors (12.70 seconds before commit). Full regression
on fresh 001-018 SQL: 1268 unit + 297 integration passed, 11.18 seconds wall time.
The sole full-run skip is
`AnnouncementSourceAcceptanceTests.ApiAndWorker_ExecuteReviewedJourneyAndRecoverAnActualProcessInterruption`:
it requires `SECUREOPS_SOURCE_HOST_ACCEPTANCE=1` and was run separately, passing in
143.91 seconds with actual Worker interruption, expiry/restart, attempt count 2,
and no automatic application. `source-host-tests` retains its TRX; `source-*/acceptance.json`
retains job, queue, revision and owned process identities. SQL rollback/audit/lease tests
remain in the full run, not inferred from successful compilation.

The preparation branch's earlier skipped test was
`AnnouncementTests.BrowserDownload_ParsesSavedTurkishContentAndSixMatchingCidImagesWithoutSending`.
It needs `SECUREOPS_ANNOUNCEMENT_BROWSER_EVIDENCE` pointing to **continuity** output
containing representative.eml, saved-preview.html and saved-draft.json. It passed
separately (192 ms) and in the full run. No required opt-in remains unexecuted.

Browser scripts: `announcements.cjs ... editor` verifies new-v2/legacy upgrade,
all minutes, seconds/offset/instant preservation, six roles/155 services/inert markup,
saved/dirty/conflict behavior and denied access. `announcement-continuity.cjs ... after network`
passed actual API stop/restart, nonblank stale preview/recovery and session revocation
(103.98 seconds including controller waits). `announcement-preparations.cjs` passed
immutable history (3.47 seconds). `announcement-source-review.cjs` covers explicit
save/retrieve/review/apply, both versions, duplicate/conflicting keys, recipient decline
and profile-switch removals, partial/ambiguous results, status resume/navigation,
polling access loss, desktop/mobile previews and byte-identical historical download.

Initial failures are retained: wrong MIME evidence directory; rerunning fresh-only SQL
tests against already-populated test databases; browser selectors/status expectations
and responsive-tab timing. Corrected orchestration used new databases, expected HTTP
202, native disabled-option state and visible-tab readiness. No assertions were removed.
The network controller initially checked nonexistent /health; authenticated access/me
proved readiness and the browser recovery completed. These are not concealed passes.

Normal OpenAPI comparison passed (465 ms). The only authored contract addition is
optional PreparedAnnouncement.sourceReview plus its existing override-record schema
(67 lines); no route/removal/required-input/date change. Full `dotnet format --verify-no-changes`
still fails: 58 files, 201 raw / 123 unique findings. Exact relative path, line, column,
diagnostic ID and message match both input reports; zero new findings. This gate is
not waived. `format-final-comparison.json` records the counting comparison.
`migration-018-preservation.json` proves identical SHA-256 for populated draft revisions,
source jobs and overrides before/after 018. Existing 016/017 bytes are unchanged.

Local combined workflow is accepted subject to the recorded committed replay;
release readiness is **not** granted. Remaining gates: full-format baseline, original
six-role branding, corporate source mappings/permissions/paging/timezone evidence,
Outlook/VDI rendering and separately scoped future sending. Fixture wiring proves
none of those external gates. Transport/dispatch-claim registrations remain absent.
Input branches/worktrees are retained unchanged; later integration must review current
shared store/service/DI/API/Worker/contracts/ADR/configuration and migration inventory,
not reuse an old no-conflict claim. No In Use or OR-to-SDM work belongs to this checkout.
