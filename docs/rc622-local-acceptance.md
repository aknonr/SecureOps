# rc6.22 Local Acceptance

Fixed task baseline: `778dca3bc1de6f854c60671a6d1f8dbbd69fbffd`.
Build source: `9ec65eb377ea020916bab8c803601e237153f34c`.
Release: `C:\SecureOpsBuild\release\2026-09-18-pilot-rc6.22`.
Private evidence: `C:\SecureOpsBuild\validation\rc621-completion-20260917`.
The owner-approved bounded size exception includes the preserved 591-line checkpoint.
Implementation commit: 101 files, +2851/-179. Final cumulative accounting, including
this handoff and browser capture correction, is in the release's `evidence/scope.json`.
Commits do not reset the fixed baseline. No SQL migration or artwork changed.

## Executed Gates

| Gate | Result / wall seconds |
| --- | --- |
| Final Release build | 0 warnings/errors, 1.569 s incremental; preceding clean correction build 6.823 s |
| Full mandatory format | Pass, 37.316 s; no baseline waiver |
| Normal regression/OpenAPI | 1312 unit + 278 integration, 39 explicit opt-ins, 6.884 s; normal snapshot comparison |
| SQL-enabled regression | 315 integration pass, two separate opt-ins, 13.514 s; overlapping tests not added to totals |
| Fresh isolated upgrade | Five final 001-021 databases, 2.795-3.004 s each; Hangfire 9, 0.260 s |
| Dependency audit | No vulnerable packages reported by nuget.org, 9.878 s |
| Matched packaging | 64.827 s; API/UI/Worker identify the exact build SHA |
| Extracted file hashes | 763/763 match: API 246, UI 263, Worker 254; 28.526 s |
| Exact packaged API/Worker | 1 pass, 144.227 s; actual interruption, attempt recovery, obsolete completion rejection, explicit apply/preparation |
| Packaged source/renderer browser | 30.479 s; OCO/profile only, 155 services, source dates with fractional seconds, selected apply, six original images and immutable preparation |
| Packaged access browser | 11.625 s; 140 users, page two/filtering, role impact/assignment/revocation, conflict retention, denied actor |
| Packaged archive/offset browser | 4.223 s; mismatched offsets/seconds, failed JS download, identical retry/reload and unchanged archive count |
| Packaged reviewer browser | 6.340 s; mine/unassigned/all, reassign, optional assignment, cross-assignee save and actor history |
| API restart | Archive/MIME byte identity, 3.217 s; reviewer/history equality, 3.345 s |
| Source failure browser | 4.868 s; UI-server API path, Disabled/Failed/missing config/no Worker/wrong queue/Ready/denied, retained manual input and support reference |
| Native Chrome 200% | Seven surfaces, both themes, keyboard focus and no horizontal overflow; corrected physical-width captures, 9.437 s |
| Configuration comparator | 2.254 s; real Worker diagnostic exit, matching settings, queue mismatch, missing keys on both sides, secret allowlist |
| Packaged browser MIME opt-in | 1 pass, 1.623 s; saved recipients/content and all six original CID bytes/types |
| Local filesystem failures | Actual denied read on disposable assets, missing file/invalid mapping; denied archive write gives 503/report-archive, no archive success, restored ACL retry keeps immutable identity |

`evidence/skips-and-optins.json` maps every normal opt-in to a passing separate
run: 37 SQL, one actual process-host journey and one browser-downloaded MIME.
No supported opt-in remains unexecuted. These are local fixtures, not corporate evidence.
Additional browser/host databases are fresh and retained; installed SQL was not touched.

## Rendered Evidence

The private `packaged-acceptance2` directory contains the exact-payload browser
results, desktop/mobile screenshots and `zoom-capture-corrected` native 200% shots.
Measured normal viewports: 1366x768, 1440x900 and 390x844; both themes, 16 CSS px
workspace text. Sample body foreground/background contrast is 13.80-17.81:1.
Native zoom records outer width 1366, CSS width 674, DPR 2 and viewport scale 1.
These measurements are not managed VDI, screen-reader or full WCAG certification.
The release's `evidence/screenshots.json` maps supplied before identifiers to local
after artifacts. `evidence/outlook-comparison.eml` is synthetic, unsent, v3 output.
Actual Outlook at the reference's 120% zoom remains pending.

Failed attempts remain recorded: private gate parameter shadowing; PowerShell
JSON array wrapping; inherited launcher pipe; browser timing/iframe selectors;
native-zoom CSS screenshot clipping; and filesystem helper preview/archive and
response-reader mistakes. The source denial case additionally revealed a lost
support-reference panel, fixed before the build commit. No assertion or security
gate was removed to obtain a pass. ACL failure injection is confined to disposable
local copies and restored in finally blocks; no target ACL was changed.

## Remaining Target Acceptance

Run the Turkish upgrade guide through the existing approved change process.
Record only missing sanitized support reference/UTC/endpoint/problem/stage and
actor access version for corporate request correlation. Validate effective API/
Worker composition, one real SCCM/Turuncu Hat source job, actual Outlook layout,
target archive/asset ACL and real role-change outcome. Individual owner/aspect
representation and source completeness need the bounded read-only evidence in
the parity document. WebSocket/proxy and Windows Service remain separate work.
No distribution message, upload, source property update, BPM/OR closure or Jira
create is authorized by this package. Local SQL mail regression uses loopback
only; no corporate SMTP or inbox delivery is claimed. Installation readiness
stays false until the owner approves and completes the target checks.
