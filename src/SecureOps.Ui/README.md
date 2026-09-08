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
| `IAccessAdminApiClient` | The access administration calls; every mutation requires an `expectedVersion` |
| `AccessUserView` | Combines user status and latest request into one presented state |
| `AccessIdentityDisplay` | Nullable profile → display name, with principal fallback |
| `AccessDecisionRules` | Client mirror of the server's reason/role rules — see below |
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
| `/access/requests` | Access-request decision queue | `Access.ApproveRequests` |
| `/access/users` | User list, grouped by access state | `Access.ManageUsers` |
| `/access/users/{id}` | User detail, role editor, disable | `Access.ManageUsers` |
| `/operational-records` | OR → Jira workspace, grouped by attention | `OperationalRecords.View` |
| `/operational-records/{id}` | Source, workflow, and Jira transfer | `OperationalRecords.View` |
| `/resources` | Uygulama Bağlantıları: search, favourites, add to a personal group | `Resources.View` |
| `/resources/sets` | Bağlantı Gruplarım: personal ordered groups, preferred group, opening | `Resources.View` |
| `/admin/resources` | Bağlantı Yönetimi: shared categories and links | `Resources.View` and `Resources.Manage` |
| `/audit-compliance`, `/diagnostics-readonly` | Future-phase placeholders | authenticated |

Interim auth endpoints: `POST /auth/sign-in`, `GET /auth/sign-out`. They establish identity only.
When an identity provider is approved they become a challenge/callback pair and `/login` is unchanged.

**Do not add a nav entry before its route exists** — the menu must never offer a dead link.

## Access administration

Three screens, each gated on its own capability, because the API gates them separately: the request
queue needs `Access.ApproveRequests` while the user read model needs `Access.ManageUsers`. An
approver without `ManageUsers` sees the queue and no user list.

### Rules that are easy to break by accident

1. **Never show a capability the UI computed.** Role *descriptions* in the picker are written
   guidance, labelled as such. Effective capabilities are rendered only from what the API returned.
   `AccessRoleCatalog` is read for role *codes* only, so the picker cannot offer an invalid one.
2. **Read before you replace.** `PUT .../roles` removes anything omitted. The editor is seeded from
   `GET /access/users/{id}` and submits that record's `version`. Never preselect a guess.
3. **Know which version guards what.** Approve and reject check the **request's** version; roles and
   disable check the **user's**. They advance independently, and using one where the other is
   expected produces a conflict that looks like someone else edited the record.
4. **A failed write still re-reads.** A conflict means the screen is stale — exactly when refreshing
   matters. The action's problem and the load's problem are separate fields; sharing one let the
   re-read wipe the conflict before it rendered, and the operator saw a silent no-op.
5. **Never auto-retry a conflict.** `AccessConcurrencyConflict` arrives `retryable: true`, which
   means re-attemptable *after a fresh read* — the submitted version is stale by definition. The
   retry action reloads and is labelled accordingly; the failed write is never re-issued.

### Rejected is not pending

There is **no `Rejected` user status.** A refused user keeps `AccessStatus.Pending` forever, and only
`latestRequest.status` distinguishes them. Reading status alone shows a closed decision as an open
task, and an administrator would re-approve someone a colleague turned down.

Use `AccessUserView` (admin screens) or `AccessSnapshot.IsRejected` / `IsAwaitingDecision` (the
signed-in user). Both are tested. There is no "request again" action anywhere: the API creates no
replacement request and offers no reapplication route.

### Profile enrichment is nullable

Every field of `AccessIdentityProfileResponse` is nullable and the object itself can be `null` — the
demo bridge resolves nothing, so absent is the common case. Fall back to the principal identifier via
`AccessIdentityDisplay`, and say enrichment is unavailable. Never render an invented name, a blank
identity field, or an em dash placeholder.

## Operational Record → Jira

The rule that governs this screen: **an existing Jira issue means create is never offered.**
`OperationalRecordView.ActionsFor` checks that before any per-state rule, and a unit test asserts it
across every workflow state — a single missed case is a duplicate Jira issue.

Three things are kept apart and must not be merged into one "status":

| Concept | Source |
|---|---|
| Workflow stage | `WorkflowState` |
| Ownership | `Claimed` (liveness) + `ClaimedBy` / `ClaimedAt` (who) |
| Source freshness | `LastSourceValidationAt`, `Version` |

Two states are deliberately **not** toned as errors:

- `OperationalRecordCloseFailed` — the Jira issue **exists**; only the source close is outstanding.
  Toned Caution, because painting a partial success as a failure invites someone to "fix" it by
  creating a second issue.
- **`reconciliationRequired` — the outcome is unknown.** Per the workflow contract an unknown Jira
  outcome settles as `JiraCreateFailed` with `reconciliationRequired = true`; the flag, not the
  stage, is the signal. `CreatingJira` without an issue key is treated the same way as a secondary
  signal, covering the window before the flag is set. Either way: a prominent amber panel, create
  blocked in every state, retry only if `retryEligible`, and an explicit duplicate-risk warning.

**Idempotency belongs to the backend.** The UI sends no `Idempotency-Key`. The contract makes it
optional and the API then derives a deterministic key from actor, command, and record — which is
already the desired behaviour, and generating one here would be a second competing policy.

**`claimed` decides liveness; `claimedBy` only decides whose.** An expired claim may keep its
`claimedBy` while `claimed` is false — reading the owner alone would show a lapsed claim as active
and block a record nobody holds. Compare against `GET /identity/me`'s `name`, never the cookie
principal: the API sees `demo:platform-admin` where the session says `platform-admin`.

**`reconciliationRequired` outranks the state machine.** While it is set, create is blocked in every
state and retry is offered only if `retryEligible` is also true.

**Never auto-retry.** Retry is offered when authoritative record state says a stage is resumable, not
because a call failed. `WorkflowConflict` at `stage: "jira-reconciliation"` is mapped to a dedicated
non-retryable presentation, never to the generic conflict message.

**No polling.** `GET /operational-records` is a rate-limited source refresh, not a passive read.
Refresh is a deliberate operator action, plus an automatic re-read after every write.

## Resource catalogue and shift-start sets

Three routes over `/api/v1/resources`, implemented against the "Resource Catalogue and Shift Start
Sets: Claude Handoff" section of `docs/contracts/secureops-api-v1-ui-integration.md`.

**Journeys.** An operator with `Resources.View` searches the catalogue, toggles favourites, and adds
a permitted link to one of their own groups, including creating the first group in that dialog;
manages those groups (create, rename, delete, reorder, choose a default), then prepares and opens
selected links. A default is a preference, never automatic opening. An operator with `Resources.Manage` additionally
creates and edits categories and links, and archives them. Everyone else sees an explanation on
`/admin/resources` rather than a redirect.

**Ownership.** Personal routes are scoped server-side to the caller's own user. There is no route to
another operator's favourites or sets, including for Admin, and the UI submits no owner ID.

**Concurrency.** Every personal mutation sends the personal aggregate version and replaces local
state with the refreshed response. Every catalogue write sends the exact loaded entity version —
zero to create. On `ResourceConcurrencyConflict` the UI reloads authoritative state and leaves the
problem on screen; it never resubmits over the edit that won.

**Opening links.** Single links are ordinary anchors with `target="_blank" rel="noopener noreferrer"`.
A set is resolved through `GET /resources/me/sets/{id}/resolve` on its own click, and only the
returned links are offered. Opening is a second explicit browser click or keyboard activation;
its native handler calls `window.secureOpsLinks.openMany` without a Blazor Server round trip.
The UI reports that opening was *attempted* and always keeps the individual links visible; it never
claims a destination loaded or authenticated, and blocked-tab detection is not treated as reliable.
No tab is opened during load, render, or refresh. The client does not forward WASAS credentials or
authorization headers; normal destination cookie handling remains the browser's responsibility.

**Search.** Server-side and debounced, with a monotonic request guard so a slower earlier response
cannot overwrite a later one, including stale errors from a transport that ignores cancellation.
Environment autocomplete uses the bounded, authorization-aware environment endpoint, never the
current result page or a downloaded catalogue. The favourite view filters the already bounded personal
projection (at most 200 favourites); mutations replace that projection with the server response.

**Kullanım Rehberi.** A non-blocking first-use invitation offers Başla and Daha sonra. Both persist
only `guideDismissed` through the versioned personal API. No browser storage or training history is
introduced. Nasıl kullanılır? always replays the task-based guide. Next/back/finish/skip/close and
Escape are keyboard-operable; heading focus moves with the step and returns to replay on close.
The inline panel adapts to narrow layouts and reduced motion. Highlighting never activates a control;
missing/hidden targets fall back to working route links. Management instructions require the actual
server capability. A guide never modifies links/groups/favourites or opens destination sites.

**MudBlazor assessment, 2026-09-06.** Keep central `6.16.0` for this milestone. The latest stable
[9.9.0 package](https://www.nuget.org/packages/MudBlazor/9.9.0) targets .NET 8 (as well as later
frameworks), so .NET 10 is not required. Version 6 support has ended; this deferral is not an
endorsement of indefinite support. The migration is application-wide, not a resource-only update:

- [v7 migration](https://github.com/MudBlazor/MudBlazor/issues/8447): `MudTheme.Palette` changes to
  `PaletteLight`, grey CSS variables become gray, and popover/negative-parameter changes affect
  `Shared/SecureOpsTheme.cs`, `Shared/MainLayout.razor`, theme CSS and existing dialogs.
- [v8 migration](https://github.com/MudBlazor/MudBlazor/issues/9953): `MudDialogInstance` becomes
  `IMudDialogInstance`; dialog options become immutable. Affected shared components include
  `AccessActionDialog.razor`, `JiraCreateDialog.razor` and `SessionRevokeDialog.razor`, not only resources.
- [v9 migration](https://github.com/MudBlazor/MudBlazor/issues/12666): the custom account menu in
  `Shared/UserMenu.razor` must explicitly invoke `MenuContext` activation; message-box APIs also change.
  Compilation alone cannot validate these interactions. Follow-up scope: shared shell/theme/providers,
  all dialogs and form APIs, removal/revalidation of the Mud 6 resource accessibility bridge, then
  keyboard/theme/mobile regression of account, access, sessions and operational-record/Jira UI with
  fake external adapters. No dependency, lockfile or framework version is changed here.

### Verified

Against a local Demo API with `Access:RepositoryProvider=InMemory` and synthetic fixtures created
through the documented endpoints: the capability split (Admin has `Resources.Manage`, Lead does
not), server-side 403 on both management write and `includeArchived` read for Lead, 409
`ResourceConcurrencyConflict` on a stale version, and authenticated render of all three routes with
real catalogue data. Automated coverage is in `ResourceViewTests` and `ResourceApiClientTests`, plus
the route rows in `UiErrorRoutingTests`.

**2026-09-06 correction verification:** Chrome 152 against the existing loopback Development hosts
and synthetic InMemory data exercised ordinary-user search, category/environment filters, paging,
page size, favourites and denied management; set create/rename/default/delete, keyboard membership
selection and reordering; manager create/edit/archive and real stale-version conflicts; failed-save
draft retention; partial resolution after archive/restoration; single/batch opening to an intercepted
harmless local page; and server session
revocation followed by reauthentication back to `/resources`. Layouts were checked at 1440x900,
1366x768 and 390x844, including a dark narrow view and long text.

Corrections keep dialogs open until saves succeed, preserve conflict messages after rereads, clear
resolved opening candidates on refresh/mutation, cancel superseded searches, surface personal/category
load failures, and provide accessible field names and keyboard set choices. Missing popup handles
still do not mean blocked tabs. Opening attempts do not prove target authentication or loading.

Replayable regression: `node tests/browser/resource-ui.cjs <playwright-core path> <UI URL> <API URL>`.
Supply the task-local driver, loopback HTTPS UI and loopback API with synthetic InMemory data;
the script refuses non-loopback hostnames and creates/archives its own catalogue fixtures.
Screenshots and local journey results: ignored `artifacts/resource-ui-20260906/`.

**Still pending:** injected 503/transport-loss and delayed-response browser cases (automatic policy
review blocked the task-local API-proxy override), timed idle/absolute expiry, managed corporate
browser popup policies, and screen-reader verification. Cancellation/timeout and stale-response
guards were checked in tests/code; this is not complete UI or corporate TEST sign-off.

### Resource Experience Handoff, 2026-09-07

**Verdict:** implemented and locally verified, with the explicit browser/TEST limits below.
Starting SHA: `e05977d158bfd533aa71caa0ac60f276bbc9ef37`. Verified implementation SHA:
`8ef13d45912a66abad6cc04242bc6608ef0e9ed4`, including integrity commit
`10fe8a47c98834390c273cedfe8ed0c6c5a0be2a`. Branch:
`feature/sql-runtime-hardening-20260902`; upstream is the same branch on `origin`
(`https://github.com/aknonr/SecureOps.git`). This entry is finalized in a subsequent
documentation-only commit; use branch HEAD for that handoff commit, not as a different runtime build.
The owner authorized Codex UI/backend work, normal push and a new task-scoped size exception.
Permanent ownership/rules are unchanged. No deployment, corporate SQL or framework changes.

**Contracts:** the existing personal PUT now retains every omitted saved reference, including
legacy callers and visibility changes between read and save. `removeLinkIds` explicitly removes
currently visible members; rename/default requests do not replace membership. Requested ordering
uses existing slots and appends surplus additions; archive/restoration preserves omitted slots.
No hidden identifiers, names, counts or URLs are exposed. Ownership, merged aggregate limits,
optimistic concurrency and transactional audit remain enforced. Bounded, authorization-aware
`GET /api/v1/resources/environments` fixes options beyond page one; `PUT /api/v1/resources/me/guide`
persists invitation dismissal in existing personal JSON. No migration or new grant.
Exact semantics: `docs/adr/ADR-0019-resource-catalogue-and-personal-shift-sets.md`,
`docs/contracts/secureops-api-v1-ui-integration.md` (Resource Catalogue and Shift Start Sets:
Claude Handoff), and `src/SecureOps.Infrastructure/Resources/README.md`.

**Verified permission matrix:** server-capability checks, not an Admin screenshot or job title.

| Synthetic actor | Links and own groups | Management menu, direct route and API | Another user's preferences |
|---|---|---|---|
| Ordinary Lead, `Resources.View` only | Allowed | Denied; menu absent | Denied |
| ResourceCurator, View and Manage | Allowed | Allowed | No personal ownership override |
| Disabled actor | Session gate denies all three routes; resource API 403 | Denied | Denied |
| Admin | Own preferences only | Existing capability policy | Cross-owner group resolution returned 404 |

**Local gates:** final full Release solution build passed, zero warnings/errors, using isolated
build output to avoid unrelated host locks. One full Release test run passed 1,009 unit and 240
integration tests, zero failed/skipped (1,249 total). This includes six actual SQL persistence tests
on LocalDB `SecureOpsResourcesV1`, database `SecureOps_ResourcesV1_Experience20260906`; the approved
001-010 migration upgrade harness also passed there. SQL tests cover hidden membership, restoration,
ordering, explicit removal, default/guide persistence, ownership, versions, audit rollback and bounded
authorized environments. No InMemory result is presented as SQL evidence. After the final accessibility
fixes, 40 affected UI/client tests, the full Release build and the six-case browser regression passed.
OpenAPI snapshot/API checks passed; semantic comparison preserves all 52 existing operations and 478
existing schema properties, with additive contracts only. Vulnerability scan found no known vulnerable
packages. Scoped C# formatting and `git diff --check` passed. Repository-wide format was not rerun:
the earlier unrelated formatting debt remains; the touched client naming diagnostic is now fixed.

**Interactive Chrome 152:** ordinary, ResourceCurator and denied journeys ran separately against
the existing loopback Demo hosts and synthetic InMemory data. Actual browser input/circuit interaction
covered first-use invitation/replay, guide next/back/skip/close/Escape and focus, page-two search,
favourites and environment selection, first-group creation from the picker, membership/order/default/
rename/delete, archive/restoration retention, native keyboard-initiated batch opening and individual
fallback, management creation/edit/archive/restore, validation, real 409 draft retention and cancel,
capability-gated navigation/direct access, real session revocation and reauthentication. Desktop
1440x900 and narrow 390x844 layouts passed in light/dark themes, with reduced motion and no horizontal
overflow. Sixteen resource-surface/dialog axe WCAG A/AA scans reported zero violations. These are
automated accessibility checks plus keyboard tests, not screen-reader testing. Null popup handles did
not produce false blocked messages; opening attempts do not prove destination loading/authentication.
Navigation targets were harmless locally fulfilled pages, never corporate sites.

**Evidence index:** small representative screenshots remain in the established ignored artifact
workflow; this durable summary and replayable harnesses are committed. Paths are repository-relative.

| View | Before | After |
|---|---|---|
| Links | `artifacts/resource-experience-20260906/before-links-desktop.png` | `artifacts/resource-experience-20260906/after-links-light-desktop.png` |
| Groups | `artifacts/resource-experience-20260906/before-groups-desktop.png` | `artifacts/resource-experience-20260906/demo-groups-desktop.png` |
| Management | `artifacts/resource-experience-20260906/before-management-desktop.png` | `artifacts/resource-experience-20260906/after-management-light-desktop.png` |

Narrow/dark samples: `demo-groups-mobile.png`, `demo-groups-dark-mobile.png`, `demo-guide-mobile.png`,
`after-links-dark-mobile.png`, `after-management-form-mobile.png` under the same directory.
Journey summaries: `browser-ordinary.json`, `browser-manager.json`, `browser-denied.json`.
Full test TRX: `final-tests/dmtak_DEMET_2026-09-06_22_35_06.trx` and
`final-tests/dmtak_DEMET_2026-09-06_22_35_06[1].trx` under that directory.
No browser profiles, credentials, runtime fixtures or diagnostic logs are committed.

**Replay:** `tests/browser/resource-experience.cjs` accepts an existing task-local `playwright-core`
path, loopback UI URL, loopback API URL, actor mode, evidence directory and optional `axe-core` path.
Use the existing Demo profiles, API `Access__DemoCompatibilityEnabled=true`, and UI process-local
`DemoMode__ApiDemoActor=team-lead`. Run ordinary, then manager, then denied LAST (it disables that
synthetic actor). Use fresh synthetic InMemory state for first-use invitation assertions. The older
six-case `tests/browser/resource-ui.cjs` uses the default `platform-admin` UI actor. Both refuse
non-loopback hosts. Driver/axe installations stay outside project dependencies. No API proxy override
was retried. Failure/delayed-response coverage uses deterministic `ResourceExperienceTests`,
`ResourceApiClientTests` and existing error-routing tests, not injected browser traffic.

**Remaining gates / next action:** obtain separate corporate TEST approval for migrations 009-010
on the required 001-010 baseline, reviewed grants, API/UI deployment and actual AD/Windows/session/
IIS/F5 behavior. Managed-browser popup policy variants, real screen-reader use and timed idle/absolute
expiration remain untested locally. Injected 503/transport-loss/delayed-response browser scenarios
remain pending after the earlier proxy rejection; deterministic handler/state tests cover these paths.
MudBlazor stays at 6.16.0; the boundedness assessment and exact application-wide migration follow-up
are above. Local success is not TEST sign-off. Resume from this entry and `docs/decisions-log.md`,
not the historical blocker list above; the hidden-membership and environment contract blockers are resolved.

### In Use V1 Canonical Handoff, 2026-09-08

#### TEST Delivery Addendum, 2026-09-09

**Paired delivery prepared and verified:** build source/live remote at packaging
`b217f000eafb4a18f7029629e1119c86205d9295`; ProductVersion
`0.1.0+b217f000eafb4a18f7029629e1119c86205d9295`, FileVersion 0.1.0.0.
Release root: `C:\SecureOpsBuild\release\2026-09-09-pilot-rc6.14`.

| Archive (relative to release root) | Bytes | SHA-256 |
|---|---:|---|
| API/secureops-api-TEST-b217f00.zip | 24639779 | E70B8AD75EF4F9A76DBE2A3C991CEF0B379A32C106D0BEDB141247127AD9FB3D |
| UI/secureops-ui-TEST-b217f00.zip | 26411433 | 994EC7556C294CE9E65CE122ECBCBBC565C3DE02E020D3FC4DF36F03B2C5A66E |
| DBA/secureops-database-001-012-TEST-rc6.14.zip | 39123 | 968D668CA89B692278E6F34E5027E5A418384E6D6F53E27E624D9CCE2A1744FF |

238 API / 254 UI / 26 DBA entries passed per-file ZIP hash checks. Required
publish/payload/secret/path/AD-dependency/Swagger gates passed; rc6.13 archive
hashes are preserved. The first locally generated DBA ZIP's backslash paths
were rejected; only its corrected, validated successor above is deliverable.
The rejected file stays under build-only evidence, never in the DBA delivery.
No source rebuild is needed for this later handoff-only commit; final documentation
HEAD/live push verification belongs to release-metadata.json, separate from build.

The requester list and relationship evidence delivery starts at verified local/
live remote `cee08c4c13a446d4482b401423612b472c41c2e8`. The explicit task addendum
authorizes the targeted UI/backend changes. `.vscode/` remains untouched.
Operational Records now displays its persisted `Requester` as **Talep eden**,
without per-row requests or changed pagination/search semantics. In Use displays
separate service-item/affected-asset states, a server table, creator evidence and
per-field provenance. Failed/ambiguous enrichment retains earlier server evidence
and invalidates affected review/export versions; manual assignment is preserved.
Real mappings, Virtual PC User/RFC semantics and configured source links remain
unresolved. The read-only TEST diagnostic is explicit, audited and dual-capability
protected; it does not enrich records or infer identities. Exact collection and
operator installation/acceptance steps are in the current rc6.14 section of
`docs/24-api-test-deployment-readiness.md`, exported with the paired delivery.

Required schema remains 001-012; no applied SQL or runtime config was changed.
New archive paths, exact committed build SHA, hashes and later documentation HEAD
are recorded separately under `C:\SecureOpsBuild\release\2026-09-09-pilot-rc6.14`.
The older **not packaged** statement below describes the original V1 milestone.
This release is not deployed or corporate-accepted. Positive ServerRequest
eligibility still needs approved policy plus code/tests, not configuration alone.

Fresh verification: Release build 0 warnings/errors; 187 targeted unit tests,
10 hosted In Use/OpenAPI/Swagger tests and 14 real isolated SQL tests passed.
The published SQL/Simulation UI journey passed at 1440x900 and 390x844 in dark/
light themes, including requester text, pagination, assignment, explicit bulk,
per-server answers, preview/download, stale conflict and guide focus/no mutation.
Evidence: `C:\SecureOpsBuild\validation\inuse-delivery-20260909\evidence`.
Prior 1,108 unit/253 integration totals are source-V1 evidence, not new full-suite
runs; unchanged roles/resources/write fences reuse that evidence. NuGet reports
no vulnerable packages. Scoped formatting excludes pre-existing IDE1006 debt.
No corporate requests, source writes, deployment or scheduler were performed.

**Implemented locally, not deployed or packaged.** Start source and synchronized
remote were `49ca6e81457ed9f86b1e46833e30f17bc754d208` on
`feature/sql-runtime-hardening-20260902`. GCM authentication and the three prior
documentation pushes were already complete. This entry supersedes the earlier
authentication, truncated-source and scope-approval blockers, not rc6.13 evidence.
The scoped total-diff authorization is recorded once in
`docs/22-operational-record-jira-workflow.md`, In Use V1 Authorization.
Untracked `.vscode/` and all existing release archives remain excluded/preserved.
The fresh live read/fetch required a process-only Git credential username hint
for the existing `aknonr` GCM account; no sign-in, credential deletion or global
setting change was needed. The final response records the independently queried post-push source/remote SHA;
the implementation commit is not an rc6.13 build or a corporate acceptance claim.

**Operator workflow:** an approved `InUseCoordinator` (or existing Admin) opens
`/in-use`, explicitly refreshes, searches/filters the persisted list and assigns an
approved reviewer by stable application identity with a reason. `InUseReviewer`
can browse and review records assigned to them. All/mine/unassigned, review-status
filters and footer page size are supported. Open a record, inspect the separately
sourced requester/service owner/provisioner/assignee, review each server, and save
a local draft. Unknown is valid; other check answers require evidence. Bulk changes
require selected servers, a visible answer/evidence and explicit confirmation, then
individual review/save. Preview the saved workbook and download it. Neither action
uploads or changes Turuncu Hat. Concurrent assignment/refresh/save rejects stale
drafts and downloads; reload the persisted record and review again. The contextual
guide is replayable, has real targets, keyboard navigation and focus return, and
does not refresh or save workflow state. Rapid guide closure no longer races a
second focus interop call against a removed element.

**Reuse and boundaries:** existing Turuncu Hat session/transport/response bounds,
persisted capabilities/users, command tracking, same-transaction SQL audit,
versioned writes, typed UI HTTP/session handling, theme/components and guide.
New `InUse` contracts/service/repositories/controller/workbook/UI are independent
of Jira rules. Real discovery uses active `SMSS_oRFF`, category 4241/group 68,
strict semantic root keys, maximum 100 records and a 45-second service timeout.
Unproven completeness is visibly partial/stale; failures and missing rows never
delete stored records. Real server/owner joins remain unresolved, not guessed.
The existing OR-to-Jira category-4241 exclusion and corporate write settings
`ReadOnlyIntegrationMode=true`, `ControlledTestWritesEnabled=false`,
`SourceCloseEnabled=false` are unchanged. New capabilities authorize local work
only. Resources personal ownership and SDM permissions are unchanged.

**Source and workbook:** the complete 18,661-byte/742-line Downloads script has
zero AST parse errors and includes save/upload/BPM sections. Its exact SHA-256,
line-based behavior/defects, 29-row Sunucular and 22-column NMS mappings are in
`docs/integrations/turuncu-hat-jira-legacy-parity.md`. It was not executed, edited
or committed. The Desktop fragment is historical incomplete evidence. Managed
`.NET ZipArchive/XmlWriter` emits the four legacy sheets plus provenance and review
evidence, with text-only, formula-safe cells. The script leaves both technical
check sheets blank; no unprovided corporate template is claimed. Hardcoded owners,
environment-dependent successful checks and monitoring enrollment are not copied
as facts. Preview/download are bound to source hash/version and reviewed aggregate
version; report hash and actor are audited before delivery.

**Schema/grants:** source baseline was 001-011; additive 012 creates
`ops.InUseRecords`, singleton `ops.InUseRefresh` and two unassigned role definitions.
No applied migration was edited. Runtime delta: SELECT/INSERT/UPDATE on records,
SELECT/UPDATE on refresh; existing audit INSERT, command-store permissions and
access reads are reused. No DELETE/DDL/db_owner/audit mutation is granted. Exact
reviewed grant statements are in `sql/README.md`. Only the approved isolated
`(localdb)\SecureOpsResourcesV1`, `SecureOps_ResourcesV1_*` test databases were used.

**Verification and replay:** `tests/browser/in-use-workspace.cjs` exercises the
published local HTTPS UI with synthetic SQL-backed records, assignment, explicit
bulk and differing server answers, draft save, workbook download, stale rejection,
guide non-mutation/focus, shared Resources modal-guide regression, next/previous
pagination and desktop/mobile light/dark states. Separate published runs prove
direct-route/menu denial and API-unavailable presentation with retained rows. Local evidence root:
`C:\SecureOpsBuild\validation\inuse-v1-20260908\evidence`. Before images are from
preserved rc6.13 archive copies (`before-rc6.13-no-inuse-*`); after images are
`after-list-*`, `after-review-dark-*`, `after-workbook-dark-*` and
`after-concurrency-*`, at 1440x900 and 390x844. `*-browser-result.json` records
completed runs. Published UI/API loopback ports are 5314 HTTPS / 5313 HTTP; harness
arguments specify the installed playwright-core path, these URLs and evidence root.
New unit/hosted API/isolated SQL tests cover malformed and reordered cells, zero
and multiple servers, ambiguous ownership, refresh retention, stale/concurrent
save/assignment, direct authorization, audit-failure rollback, workbook safety and
provenance. The fresh SQL harness passed 001-012, the existing upgrade path and all
14 SQL tests in `SecureOps_ResourcesV1_v1verified20260908`. Full suites passed
1,108 unit and 253 integration tests, zero skips. Release build and local publish
passed without warnings/errors; OpenAPI snapshot passed and structural comparison
proved every prior path/schema unchanged. NuGet transitive vulnerability scan found
none; no package/project dependency was added or upgraded. New C# files pass strict
format verification; all changed C# files pass with only existing IDE1006 naming
findings excluded in the two old Turuncu Hat files and UiProblemFactory. Those
pre-existing private constants were not renamed. rc6.13 API/UI archive SHA-256
values still match the historical manifest. No new release archive was created.

Synthetic published hosts use existing Demo/Simulation profile validation
(`ReadOnlyIntegrationMode=false` is required for that non-corporate provider pair);
this is a process-only synthetic setting, not a change to corporate fences or
appsettings. Browser network is loopback-only. No corporate data or API call,
release packaging, deployment, attachment upload, Jira creation or BPM close was
performed. Corporate expanded response keys/cardinality/completeness, owner joins,
approved workbook consumption/template acceptance, real screen-reader/browser
policy and deployed AD/IIS/F5/SQL behavior remain unverified. No external model is used.

**Bounded continuation:** Worker remains an empty generic host, not a deployed
scheduler. After separate hosting/storage/service-identity/schedule/retry approval,
add one read-only job invoking the same bounded synchronization path; it is not
required for V1. Attachment/property/BPM closure needs a separately authorized
In Use capability and activation fence, approved transition contracts, unique BPM
matching, reconciliation/idempotency and authoritative OR-state verification.
Enabling SDM must never enable those commands implicitly. Jira-only TEST pilot
prerequisites remain the separate current list in
`docs/24-api-test-deployment-readiness.md`; In Use is not an SDM activation dependency.

### SDM, Management and TEST Delivery Handoff, 2026-09-07

**Current packaged delivery, rc6.13 (not deployed):** clean tracked build source
`1935dc522e70b0fcfe602812bffb06f2858d61b1`, runtime
`2b895f6e6c66553be52f44471a231892865a9937`. All nine outgoing commits were inspected
and normal-pushed using existing approved Git authentication. Live `ls-remote`
matched the full build source SHA. This supersedes the historical authentication
failure below. The final documentation commit is separate from build source and
recorded after commit in the new release root's `release-metadata.json`.
The subsequent documentation push stalled in Git Credential Manager and was
cancelled; a non-interactive retry failed with `unable to get password from user`.
An independent read-only GitHub ref GET still verified remote at the build SHA.
Release preparation is complete locally; later documentation is not yet remote.
Complete the existing approved GCM sign-in before retrying normal push; do not
extract credentials or bypass authentication. Final SHA/status are in metadata.

Release root: `C:\SecureOpsBuild\release\2026-09-07-pilot-rc6.13`.
API/UI ProductVersion `0.1.0+1935dc522e70b0fcfe602812bffb06f2858d61b1`,
FileVersion `0.1.0.0`, framework-dependent net8.0; no dependency/framework upgrade.

| rc6.13 artifact | SHA-256 |
|---|---|
| `API/secureops-api-TEST-1935dc5.zip` | `BB9D198C5C1CA789280716263FA607CC56BE66DD1A811D587A322B9EDEAEA368` |
| `UI/secureops-ui-TEST-1935dc5.zip` | `7ED33EE296997DE72B60C024BFEEC76089CD1BA9CB6D9B8482C7F6C4940CF69B` |
| `DBA/secureops-database-001-011-TEST-rc6.12.zip` (unchanged reuse) | `CE38FF8ECDA060B3111E7C9FE9A23A2F5AD6088E36499478FAFBF9A2D317F2D1` |

Changes from rc6.12: responsive selectable workspace, server-backed versioned
layout, current-page selected-link resolution/group addition, persisted record
browsing separate from source refresh, guided review and positioned task tours,
content-versioned application CSS/JS. `manifests/changes-from-rc6.12.json` records
exact payload differences; `release-artifacts.sha256` and `manifests/*-payload.sha256`
record archive/per-file hashes. API 238/UI 253 files passed existing release
scripts, forbidden-content scans, ZIP/hash validation, API AD closure and offline
TEST Swagger gates. Same shared Infrastructure binary in both packages. Compiler
PathMap and no debug symbols exclude developer paths. Server-owned config, source,
logs, tests and secrets are excluded. `staging/` and `evidence/` are not deployable.

Required schema remains **001-011**, no new runtime grants. No SQL asset diff
against rc6.12 source `682fa8eafcac611b0d18f93d0eb541f6a5acd2fc`; all 24 DBA ZIP
entries match its original manifest and all 22 SQL files match current source.
The unchanged DBA ZIP contains a historical rc6.12 runbook from `aac1be5`; use
**only the new release root `operator-runbook-tr.md`** exported from canonical
`docs/24-api-test-deployment-readiness.md`. Existing release archives, including
all three rc6.12 hashes, remain unchanged.

Fresh gates: Release build zero warnings/errors; API/UI publish, 9 OpenAPI/Swagger
tests, package validation and NuGet vulnerability check (none reported). Reused:
1092 unit/250 integration tests including isolated SQL, and published local
workspace/guidance/outage journeys. Runtime is unchanged from `2b895f6` to build
source; final CSS/JS hashes also match the browser-tested publish. Exact evidence
hashes/source applicability are in `evidence/reused-verification.json`. These are
reused working-tree-verification results, not fresh rc6.13 SQL/browser execution.
No broad suite/format repetition, corporate calls, SQL execution, deployment or
write activation. Live TEST rendering cause remains unverified; cache invalidation
alone is not a verified fix. Current Turkish checklist covers pairing, asset
hash/MIME, desktop/mobile layout, selection, preferences, stored paging, tours and
roles; native 200% zoom, screen-reader and managed browser behavior remain manual.

Branch status at packaging: upstream
`origin/feature/sql-runtime-hardening-20260902`; live repository default is
`master` at `869fc161ef6457a9f25922e755188ee859d56fa7`. No branch-specific PR target
is configured and GitHub PR queries found no PR for this branch. `master` is the
default prospective target, not a verified existing PR base. The build source is
not an ancestor of fetched live `master`, so current work is not merged. No PR,
merge or branch deletion performed; untracked `.vscode/` remains untouched.
First manual TEST action: read-only inventory of actual targets, installed API/UI
versions, DBA-confirmed schema and rollback/backup references in the change record;
stop before installation if any prerequisite or separate approval is absent.
Keep ReadOnlyIntegrationMode=true, ControlledTestWritesEnabled=false and
SourceCloseEnabled=false. JiraCreated never means source completion.

**Historical workspace verification before rc6.13 packaging:** resumed actual `aac1be56cf9921555e2d1c67f0f7a22c9a4a1e3e`.
Latest functional runtime commit: `2b895f6e6c66553be52f44471a231892865a9937`.
Increments: `e5d7065` preferences/resolution, `4d95656` resource workspace,
`957a70f` persisted browsing, `2b895f6` guided review/tours. Subsequent browser
harness/handoff commits do not change runtime. The local published verification
outputs are under `artifacts/workspace-usability-20260907/final-api` and `final-ui`;
they were compiled during reviewed working-tree verification, not produced by
release packaging and must not be treated as a new TEST deployment package.
Normal push and live `ls-remote` on 2026-09-07 failed because approved GitHub
authentication could not prompt. Remote publication is unverified; cached
upstream remains `7c118f31d7c1aaa687b723b793b3bc792c4cbee4`, not a live remote claim.
User action: complete the existing Git Credential Manager browser sign-in, then
normal-push `feature/sql-runtime-hardening-20260902` and verify `ls-remote`.
The rc6.12 archives were extracted into ignored local artifacts and hosted in
foreground Demo with Simulation providers, no corporate configuration/calls.
Published theme CSS equals source SHA-256 `63184EAAD85A94E56E23AC94AE0D035D4309CAFDBF362928CB84F35CDB20A0D5`.
Chrome computes the desktop filter as grid (680/220/220 pixels at 1440 width),
resource rows as grid and metadata as flex; no JS errors or failed assets (304
is normal cache revalidation). Sparse rows are confirmed design limitations;
stacked desktop filters are not reproduced. No TEST screenshot attachment is
available in this turn, so live deployed CSS/cache/version remains unverified.
Application CSS now has content-versioned references. Baseline screenshots and
computed evidence: `artifacts/workspace-usability-20260907/before-*.png`,
`baseline-results.json`. Server-backed layout and selected-link resolution are
additive contracts, now consumed by the workspace UI. Initial verification covered
56 resource unit, 6 API/OpenAPI tests and 11 actual isolated SQL cases, including legacy layout
JSON, restart round-trip and transactional audit rollback, on
`SecureOps_ResourcesV1_Workspace20260907`; fresh 001-011 harness upgrade passed.
No schema/grant changes. rc6.12 remains immutable and excludes all follow-up
runtime changes. No prior size exception is used; increments stay below 1000 lines.

Published loopback SQL-backed `workspace-usability.cjs`
passes page-scoped selection, atomic two-link group addition, authoritative
resolution/native keyboard activation with isolated openers, server layout
persistence, desktop/mobile light/dark and no-result states. 44 affected unit
tests pass. Evidence: `artifacts/workspace-usability-20260907/workspace-results.json`
and `before-links-*`, `after-links-*`, `after-groups-*`. 720 CSS-pixel reflow
was checked; native managed-browser 200% zoom is not claimed.

Persisted browsing now uses `GET /api/v1/operational-records/stored`; only
**Kaynağı yenile** calls the bounded source refresh. Search, stable sort, status,
page and bottom page-size controls use actual persisted totals. Review starts
with source summary, unselected type, authoritative blockers, then supported
preview/actions. Retirement is explicitly outside the two confirmed types;
an operator declaration never grants eligibility or approval. Source/Jira/BPM
write fences and reconciliation semantics are unchanged.

Task tours for links, groups, layout and review highlight actual targets, scroll
and position a non-modal native popover, support next/back/skip/Escape/replay,
and restore initiating focus including first-use invitation. Navigation performs
no mutations. Missing targets remain skippable. Browsers without Popover API
fall back to inline guidance; managed TEST browser compatibility is unverified.
The popover top layer avoids Mud dialog transform/clipping without changing the
framework or overriding dialog layout. Published desktop/mobile and 720 CSS-pixel
review tour bounds and focus passed with reduced motion.

Final local evidence: Release build/API+UI publish; 1092 unit tests, 250 integration
tests (including actual isolated SQL, no skipped SQL cases), additive OpenAPI
snapshot (55 prior operations retained), NuGet vulnerability scan (none reported),
scoped formatting and diff checks. `guidance-results.json` covers explicit refresh,
persisted browsing without row-version changes, declaration-only preview, tours,
curator/denied routes, and auditor dashboard/date navigation. `workspace-error-results.json`
uses an intentionally stopped owned loopback API, not interception: safe outage
message and preserved search. Screenshots: `after-records-*`, `after-review-*`,
`after-links-tour-*`, `after-dashboard-regression-*`, `after-links-api-unavailable-*`.
All are local synthetic evidence, not corporate acceptance. No screen-reader,
managed popup-policy/authentication, native browser zoom, or live TEST asset
verification is claimed. Bulk favourites were not added; group addition is atomic
and versioned, selection means only the current page, and native opening reports
requests/fallback rather than guaranteed tab or destination authentication success.

Operator boundary: request the missing TEST screenshot and, read-only in the TEST
browser, record viewport/zoom, loaded application CSS URL/status/content hash and
computed filter grid. Compare the served file against the installed package and
its source; stop on mismatch before considering any server change. rc6.12 still
excludes every follow-up runtime change; no new release archive or deployment was made.
Long contiguous names, long purposes/notes, short and paged lists and explicit
layout reset were exercised in the published workspace. Native keyboard browser
zoom was attempted but the headless browser did not change its zoom factor;
only 200%-equivalent reflow is evidence. Whole-repository historical formatting
debt was not rewritten; the new C# files passed scoped `dotnet format` verification.
rc6.12 API/UI/DBA archive hashes were rechecked and match the table below.

**Historical packaged delivery, rc6.12 (preserved):** build source
`682fa8eafcac611b0d18f93d0eb541f6a5acd2fc`, latest runtime
`f528da27611f52ab3c5676c485d6f6e7756af54c`. The three local commits
`14f2f46`, `f528da2`, `682fa8e` form the direct chain above cached upstream
`7c118f31d7c1aaa687b723b793b3bc792c4cbee4`. Tracked source was clean during
build/publish; untracked `.vscode/` remains untouched. This release-documentation
commit is not a new build source; its full SHA is recorded separately in the
release root's `release-metadata.json` after commit.

Release root: `C:\SecureOpsBuild\release\2026-09-07-pilot-rc6.12`.
Required schema **001-011**, no new runtime grants versus rc6.11.
Both API/UI ProductVersion values are
`0.1.0+682fa8eafcac611b0d18f93d0eb541f6a5acd2fc` (FileVersion 0.1.0.0,
framework-dependent net8.0). Shared Infrastructure SHA-256 is identical:
`D1C566ED75564881F74144A4DC5CC68B3F2548F8FEC33BDC106D3C6062D40232`.

| rc6.12 artifact | SHA-256 |
|---|---|
| `API/secureops-api-TEST-682fa8e.zip` | `D8CB49B082D092FB663C72E4B7659EF64AFD3F8532BCCF46FE0821B102E4C17E` |
| `UI/secureops-ui-TEST-682fa8e.zip` | `34F722506B80C091A9FFA796CA6198A212E585B9008339EE715F97098F17119F` |
| `DBA/secureops-database-001-011-TEST-rc6.12.zip` | `CE38FF8ECDA060B3111E7C9FE9A23A2F5AD6088E36499478FAFBF9A2D317F2D1` |

The existing release scripts validated API 238/UI 252 entries, path/hash
manifests, AD runtime dependency closure, UI assets and offline TEST Swagger.
Both publishes passed with compiler PathMap to `/_/src` and no debug symbols;
the initial unmapped API scan correctly rejected a developer path before ZIP
creation. No scanner bypass. Server-owned config, logs, source/test files and
prohibited content are excluded. DBA has 22 unchanged-source SQL files, SQL
README and the current runbook export; all 24 ZIP entry hashes were verified.
Per-file manifests and archive hashes are under `manifests/` and
`release-artifacts.sha256`. Local `staging/`/`evidence/` are not deployable.

Fresh Release build: zero warnings/errors. **1068 unit and 236 non-SQL integration
tests passed**, zero failed/skipped, including current API/OpenAPI contracts.
Ten SQL cases were explicitly excluded from the fresh run and reused from the
existing actual LocalDB evidence below. SQL/browser/harness inputs are unchanged
from runtime f528da2 to build HEAD; original evidence hashes and applicability
are recorded in `evidence/reused-verification.json`. No SQL/browser rerun or new
corporate evidence is claimed. Full unit closes the previously corrected
migration-count checkpoint. NuGet direct/transitive vulnerability scan reported
none across eight projects. Repository-wide format was not rerun; prior
whitespace/naming debt remains, not a clean format gate. No dependency changes.

rc6.10 and rc6.11 retain their original sources and all five archive hashes were
checked unchanged. rc6.11's 001-010 packages lack the new independent close gate,
two-type review and migration 011; rc6.12 now includes them. Runtime support is
not corporate activation: preserve ReadOnlyIntegrationMode=true,
ControlledTestWritesEnabled=false, SourceCloseEnabled=false. JiraCreated is not
source completion. Installation mapping, positive policy and external-contract
evidence remain blocked as described below.

Normal push during this packaging task failed because approved GitHub credentials
were unavailable non-interactively. Local source/package provenance is verified;
live remote publication is not. The final normal push and independent ls-remote
outcome, plus the exact documentation HEAD, are recorded in release metadata.
If authentication remains blocked, the user must complete existing-account
`git credential-manager github login --browser`, normal push to the same branch,
then compare HEAD/ls-remote. No secrets are requested or controls bypassed.

Canonical runbook `docs/24-api-test-deployment-readiness.md` now starts with one
current rc6.12 sequence; prior deployment instructions are explicitly historical.
Current-only export is `operator-runbook-tr.md` in the release and DBA archive.
First manual TEST action: inventory actual targets, current binary/config/schema
and rollback baseline in the change record. Then verified backups, missing
migrations/grants, API, UI and role-based TEST. 011 keeps legacy close intent
false; old-binary compatibility is unverified and must not be assumed for
rollback. No deployment, corporate calls/SQL, IIS changes or write activation.

**Historical source-only checkpoint (superseded by the package above):**

**Current multi-type/Jira-only implementation:** resumed actual HEAD
`7c118f31d7c1aaa687b723b793b3bc792c4cbee4` on
`feature/sql-runtime-hardening-20260902`; only untracked `.vscode/` was present
and remains untouched. Runtime increments:
`14f2f466368a05d21d324dae03c3a1005190e1d0` (durable close gate, 299 changed lines)
and `f528da27611f52ab3c5676c485d6f6e7756af54c` (review/UI/browser, 887 changed
lines). Each is below the permanent 1000-line cap; this handoff is a separate
documentation increment. No reset, force push, main merge, deployment or corporate
request. Normal push was attempted but Git Credential Manager required interactive
sign-in. Non-interactive push and independent `ls-remote` both failed with missing
GitHub username/interactive credentials. The cached upstream remains `7c118f3`;
that is not a live remote-SHA verification. Runtime commits are local-only.

The owner confirmed **Sunucu Talebi** and **Uygulama Kurulumu**, not positive
eligibility for either. The existing enum/draft architecture exposes both through
`jira-review` with the existing preview capability and expected version. The
choice is explicitly an operator declaration, held in the UI draft and audited,
not persisted as source classification/approval or a create-authorizing preview.
Changing it clears preview/confirmation; version-conflict recovery retains it.
Server review displays configured established mapping. Installation review has
no SunucuTalep fallback, shows its mapping blocker and cannot publish; no invented
label/field or keyword rule. Corporate positive eligibility remains fail closed.
Viewing, reviewing and publishing use distinct existing API capabilities; a Lead
title does not grant publication. No separate approver subsystem was added.

`OperationalRecords:SourceCloseEnabled` defaults false under the existing global
read-only/controlled-write fences. New migration 011 persists default-false close
intent on transfers and append-only history, including existing rows. The preview
fingerprints intent and acquisition persists it before Jira dispatch. Jira-only
success keeps the key and `JiraCreated`, with `SourceOpen` presentation and
"Jira oluşturuldu. Turuncu Hat kaydı açık bırakıldı." No pending close step,
failed close or Completed is fabricated. Retry/replay/restart dispatch no second
create or BPM write; no-op retry also preserves RetryCount and JiraCreated history.
Enabling the gate later never upgrades deliberately false intent. No intent-upgrade
API or automatic worker/queue exists. Already true close intent can resume only
on explicit authorized retry with all gates open. Source freshness is retained;
only BPM write configuration becomes optional while its independent gate is off.

New verification, separate from historical/reused evidence:
- Release solution build: 0 warnings, 0 errors. Full integration run: **245 passed,
  zero skipped**, including **10 actual SQL tests**. Full unit run: 1065 passed,
  one outdated migration-count assertion failed; corrected 10-to-11 assertion
  passed in the 10-case SQL-asset subset. After the final presentation changes,
  **66 affected unit tests and 18 controller/OpenAPI tests passed**. These subsets
  overlap earlier runs and are not additional unique suite totals.
- SQL: guarded `(localdb)\SecureOpsResourcesV1`, final database
  `SecureOps_ResourcesV1_MultiTypeFinal20260907`. Clean 001-011, legacy transfer/
  history upgrade with false intent, and repeated 011 application passed. New
  restart/config-change and interrupted-close tests use real SQL repositories and
  command store with deterministic external substitutes. All ten SQL cases ran;
  there is no skipped persistence gap for these runtime changes.
- Browser: `tests/browser/sdm-jira-only.cjs` reuses existing loopback SQL/Simulation
  support and supported foreground hosts. Five interactive checks passed: denied
  controls/API 403; both declarations and invalidation; SQL version conflict and
  retained-selection recovery; confirmation focus/cancel/double click with one
  persisted Jira key and zero close stages; ambiguous result with reconciliation
  and retry 409. A real browser after host restart also checked SourceOpen detail
  and list interaction. Desktop 1440x900 and mobile 390x844 screenshots were checked
  for overflow and visually inspected; labelled native select and dialog focus work.
- Evidence directory (local ignored artifacts): `artifacts/sdm-multitype-20260907/`.
  `jira-only-results.json`, `final-suite/*.trx`, and paired screenshots:
  `review-denied`, `review-server`, `review-installation`, `review-version-conflict`,
  `jira-only-confirmation`, `jira-only-result`, `jira-only-list`,
  `jira-unknown-confirmation`, `jira-unknown-result` (`-1440.png` / `-390.png`).
  Test-owned hosts were stopped and synthetic role edits restored; unrelated
  existing hosts were untouched. Local databases are retained for inspection.
- OpenAPI snapshot/compatibility and diff checks passed. NuGet vulnerability
  scan found none in all eight projects. Added-line sensitive-pattern review
  passed. Whole-repository format still fails on existing whitespace/naming debt;
  touched transfer/review test whitespace checks passed after scoped formatting.
  This is not a clean repository-wide format gate. No unrelated cleanup.

rc6.11 remains source `74cd8274250302a977cbc4c5cd6e4f1789c01459`, schema 001-010;
all old packages/hashes remain unchanged. None contains the new runtime or 011.
No new package was produced. Existing dashboard/resource and legacy-parser browser
evidence is reused, not rerun or claimed as new corporate evidence.
The independent BPM implementation gap is closed locally. Real TEST still needs
the bounded Jira metadata/permission/identity and authoritative correlation
evidence in `docs/integrations/turuncu-hat-jira-contract-gaps.md`, approved positive
per-record policy, installation mapping and separate controlled TEST authorization.
BPM close additionally needs its response/conditional-write/reconciliation evidence
and separate approval. Do not activate either write path from this handoff.

Exact delivery next action: in an interactive terminal use the existing approved
GitHub account with `git credential-manager github login --browser`, then normal
`git push origin feature/sql-runtime-hardening-20260902` and compare `git rev-parse
HEAD` with `git ls-remote --heads origin feature/sql-runtime-hardening-20260902`.
Do not send credentials to an agent. In parallel, the owner supplies per-record
positive criteria and the installation Jira mapping; the Jira administrator returns
the existing bounded evidence checklist. No repeated request for the two scope types.

**Historical activation evidence preparation (superseded implementation gap):** starting HEAD was
`ac00c9cacab52471b778c6d358f9144442d5f4a5`, on the existing feature branch with
only untracked `.vscode/`. This task changes documentation only. rc6.11 retains
runtime source `74cd8274250302a977cbc4c5cd6e4f1789c01459`; all release directories,
packages and recorded hashes are untouched. No packaging, deployment or corporate
request was performed.

The Turkish five-step operator checklist is now in the existing
`docs/integrations/turuncu-hat-jira-contract-gaps.md`: product/version identification,
SDM/3 metadata and service permissions, custom fields/requester/watcher evidence,
bounded authoritative issue correlation, and separate BPM semantics. Official
Atlassian references are version-gated: this repository does not establish the
actual Jira product/version. Returned data is explicitly allowlisted and sanitized;
no credentials, broad searches or raw dumps are requested.

Jira-only TEST can be planned but cannot be activated through current rc6.11
configuration: successful creation proceeds directly to source close, retry with
a key also closes source, and the write fence is shared. A separately approved
implementation must independently fence BPM dispatch and persist/present Jira-only
success without pretending source completion. Publisher review is a business
proposal, not an approved eligibility change or a separate-approver requirement.

The eight skipped SQL cases do not expose a new persistence gap in the latest
HTTP/parser-only changes; repository/command/schema/workflow code is unchanged.
Prior SQL and browser evidence is reused, with no new build/test/SQL run. A future
Jira-only mode requires targeted persistence/restart/duplicate and browser coverage.
Exact next action: the owner/Jira administrator returns step 1 product/version
and SDM/3-context evidence using the checklist; business policy and remote
correlation remain decisions/evidence, not inferred activation permission.

**Original-script review update:** start here for contract discovery; the browser
and rc6.10 evidence below is retained, not rerun or overwritten. Starting local
and independently queried remote HEAD both matched
`1665685b488165ee62bbe9437e0b6b55993cf73d` on the existing feature branch, with
only untracked `.vscode/` preserved.

The original file supplied by the task is now reviewed as source only. Its actual
attachment path, SHA-256, original line references, UTF-8 parser results and
sanitized parity matrix are in
`docs/integrations/turuncu-hat-jira-legacy-parity.md`. Script discovery is resolved.
The line-274 `%22` string defect is in the supplied copy; an in-memory diagnostic
repair parses cleanly but was neither saved nor substituted for the original.
No script execution, corporate call, credentials output or raw-script commit.

Mapping already matches the historical SDM / issue-type ID 3 / WASAS custom field /
SunucuTalep / optional user-custom-field payload. Corrected documentation no longer
attributes operator reporter, native watchers, an actual Success check or separate
approver flow to the script. Existing Block requester policy and approval/eligibility
fences remain unchanged. A static label is not a positive classification rule.

Backend fixes reject malformed/mixed BPM projections without silently discarding
rows, normalize activity application errors, reject contradictory update success,
and safely classify non-object source/update/Jira success JSON. Exact corporate
SET/KEY semantics, source-bound preview, authorization, durable ambiguity handling,
Jira-key-first persistence and separate source-close recovery remain unchanged.
No migration, UI runtime code, policy or provider activation changed.

Verification: Release build has zero warnings/errors. The full checkpoint passed
1,055 unit and 234 integration tests; eight opt-in SQL tests were skipped, not
reported as SQL execution. After final error-normalization changes, all 90 adapter
contract cases (24 new cases in this task) and 38 affected hosted/API/OpenAPI cases
passed. Counts overlap and must not be added. TRX evidence is in ignored
`artifacts/script-contract-review-20260907/`. Vulnerability scan found no known
vulnerable packages in eight projects; diff check passed. Repository-wide format
verification still fails on pre-existing whitespace/naming diagnostics. Existing
LocalDB and desktop/mobile browser evidence below is reused for unchanged behavior.

Activation still requires current field metadata/permissions and durable identity
decisions, positive SDM policy/attestations, a decision on operator confirmation
versus separate approval, remote write success/failure evidence and authoritative
reconciliation/conditional-write semantics. No approval subsystem is inferred.
Exact next action: obtain only these remaining owner inputs before SDM activation;
any later TEST deployment first needs approved target/schema/backup/rollback
inventory under `docs/24-api-test-deployment-readiness.md`. Deployment and corporate
writes remain separately gated.

**Source/package delivery:** implementation `74cd8274250302a977cbc4c5cd6e4f1789c01459`
was committed and normally pushed using existing authentication. After one
transient DNS failure, `git ls-remote --heads` confirmed that exact live remote SHA.
A following
documentation-only commit records this package result; it is not another build
source. Paired release root:
`C:\SecureOpsBuild\release\2026-09-07-pilot-rc6.11`.
Both API/UI ProductVersion values include the exact `74cd827` full SHA and the
shared Infrastructure DLL hashes match. Release/publish, API dependency closure,
offline Swagger, payload scans and every archive-entry hash passed validation.
`release-readiness.md`, `release-metadata.json`, `release-artifacts.sha256` and
per-file manifests are in that new root. No deployment was performed.

| rc6.11 artifact | SHA-256 |
|---|---|
| `API/secureops-api-TEST-74cd827.zip` | `CB34CDA66EE6EBEA9B7818F6A0F8D50A455098B945F81B5D97B8EFC8AEAFF597` |
| `UI/secureops-ui-TEST-74cd827.zip` | `2FAEC67EC6757D08C6CFBC6B1203E8F00378CD571589B70788BE9C7C9ED341D5` |

Schema remains 001-010; no new DBA archive was necessary. rc6.10 retains source
`de6e538` and all three original ZIP hashes, rechecked unchanged. Those old API/UI
packages do not contain the later runtime changes. The supplied original script
hash was also rechecked unchanged, and `.vscode/` remains excluded.

#### Prior SQL and Browser Milestone

**Resume update, 02:38 TRT:** resume from this update; the rc6.10 milestone below
is historical evidence. The checkout matched branch
`feature/sql-runtime-hardening-20260902` and HEAD
`1156ccf20f56bf0686c9e4a025edb484229a0b46`, with four unpushed commits and only
untracked `.vscode/`. No clone, reset, unrelated edit or configuration overwrite.
The initial remote query failed DNS resolution; a later live query returned
`0276bf173705fa5919b1d2f665a2f0d182534b99`. Normal push then succeeded, including
all four prior commits and new implementation
`efc6c59412980ab84a736896ee0fe69f3963e685`; `git ls-remote --heads` independently
confirmed that exact remote SHA. This update is a subsequent documentation-only
commit; use final branch HEAD for its identity, not as a different runtime build.
Existing approved Git authentication worked; no new sign-in or secret was needed.

**Implemented:** accessible, keyboard-editable custom UTC date fields; a named
Jira confirmation dialog with initial focus on Cancel; uncertainty-aware list
and detail labels with caution styling; and one reconciliation notice when the
action and persisted state share the same support reference. Different support
references remain visible. The existing MudBlazor 6 accessibility bridge handles
the dialog container because component attributes are not forwarded by that
version. No framework, eligibility, approval, backend adapter, SQL migration or
external-write fence changed. Runtime/test diff: 596 additions, 24 removals,
16 files, below the permanent 1,000-line cap without an exception.

**Current local evidence:**

| Evidence | Executed result |
|---|---|
| Release solution build | Zero warnings/errors |
| Unit tests | 1,034 passed, zero failures/skips; three new uncertainty presentation cases. The earlier 1,020 total was a previous checkpoint, not the current baseline |
| Integration tests | 242 passed, zero failures/skips; eight are actual guarded LocalDB tests. Six OpenAPI cases are included. Other hosted, InMemory/fake-backed and offline checks are not relabelled as SQL evidence |
| Isolated SQL | Existing migration harness passed 001-010 on `SecureOpsResourcesV1` / `SecureOps_ResourcesV1_JourneysFinal20260907`; the earlier `Journeys20260907` database is retained separately |
| Dashboard browser | Four checks: populated custom window/API agreement, empty/preset recovery, real SQL-lock loading/failure/retry, and 100ns inclusive/exclusive boundary evidence. Synthetic historical transitions differ from current eligibility/backlog |
| SDM browser | Six checks: view/publish separation, evaluated actionable blockers and cancelled draft, double confirmation/success/key replay, changed-source conflict, known failure versus unknown outcome, and close-only retry preserving the Jira key |
| Reporting matrix | Admin/Auditor receive summary and operator data; Lead/JiraPublisher/Operator/ReadOnly receive 403. All six rows verify actual menu and direct `/dashboard` / `/reporting/operators` behavior. No new ranking UI or metric was introduced |
| Package scan | No known vulnerable direct/transitive packages reported across eight projects |
| Diff / format | `git diff --check` passed. Repository-wide `dotnet format --verify-no-changes --no-restore` failed on existing whitespace/naming violations, including untouched Infrastructure/Identity files. No broad formatting cleanup was performed |

**Browser artifacts:** ignored `artifacts/sdm-journeys-final-20260907/` contains
34 PNGs, the authoritative custom-window JSON, four management checks, six SDM
checks, the six-role matrix and full-suite TRX files. Screenshots use 1440x900 and
390x844; responsive drawer closure is awaited before capture. Confirmation shots
come from interactive dialogs. Result shots additionally verify persisted state
after reread, not static component HTML. See `dashboard-populated-*.png`,
`sdm-success-confirmation-*.png`, `sdm-success-result-*.png`,
`sdm-unknown-result-*.png` and `sdm-source-close-recovered-*.png`.
Replay instructions and synthetic role restoration are in `tests/README.md` and
the three browser scripts. The existing source/Jira Simulation pair performs no
network I/O; Access, Audit, sessions, workflow, idempotency and reporting use SQL.
Only task-owned foreground API/UI processes on 5100/64947/64949 were stopped;
pre-existing local hosts and `.vscode/` were preserved.

**Evidence limits:** the known pre-dispatch browser rejection is a changed-source
conflict. A source-validation outage before dispatch and actual transport/response
loss remain client/hosted-test evidence, not injected browser-network evidence.
The ambiguous browser outcome comes from deterministic Simulation, not a real
Jira timeout. Corporate browser policy, timed session expiry and screen-reader
verification remain unrun. No corporate service, SQL, IIS, external write or
deployment was exercised. The original Jira script/access location, positive SDM
policy, requester/reporter mappings, human approval workflow and authoritative
remote reconciliation contract remain external activation inputs.

**Source versus packages:** current verified runtime source is `efc6c59`.
Existing rc6.10 API/UI packages still belong to
`de6e5381bd3d7e053f2e9c1c6b07e93283f55ca7`; these fixes are not inside them.
All three ZIP SHA-256 values in the historical table below were rechecked and
are unchanged. No package was rebuilt or overwritten.

**Exact next action:** synchronize this branch normally, then obtain the approved
original script location and sanitized positive policy/identity/approval/
reconciliation inputs before any SDM activation work. For the unrun browser fault
cases, extend test-only hosting around the existing scripted adapters; do not add
production failure controls or retry rejected proxy/background startup methods.
Any new package must have a new release root and its own source association;
corporate TEST deployment remains a separately authorized operation.

#### Historical rc6.10 Milestone

Starting branch/HEAD were verified as `feature/sql-runtime-hardening-20260902` /
`0276bf173705fa5919b1d2f665a2f0d182534b99`, matching the requested baseline.
Only the existing untracked `.vscode/` was present and is excluded. This task
authorizes Codex cross-layer changes, coherent commits/normal push and a scoped
size-cap exception; permanent rules are preserved. Source and release evidence
below do not change the previous four-record corporate TEST verification.

| Evidence class | Current milestone |
|---|---|
| Implemented and locally verified | Source-bound preview fingerprint, absent/empty requester Block policy, bounded key persistence after Jira success, restart-safe rejection after uncertain SQL persistence, actionable SDM evidence UI, complete reviewed preview fields, duplicate-dialog guard, distinct publication error semantics, dashboard loading/ARIA correction and resource entry point |
| Existing verification reused | Resource hidden membership, filtering, curator/ordinary/denied authorization and draft/guide behavior; previous ordinary/curator/denied desktop/mobile light/dark journeys above remain the baseline for unchanged resource code |
| Implemented but not corporate-verified | Corporate Jira create/source update adapters; their sanitized contract tests prove local mapping only. Native conditional source writes, remote duplicate lookup and remote idempotency are not proven |
| Missing implementation and blocked policy | Human approval workflow, positive SDM category/label policy, structured per-record group/DCC/category/infrastructure evidence and authoritative remote reconciliation contract. No eligibility/approval is inferred from imported scope |
| Missing source evidence | Original organization Jira script: absent from repository, with no documented accessible location. Comparison is limited to `docs/integrations/turuncu-hat-jira-legacy-parity.md` and the exact open samples in `turuncu-hat-jira-contract-gaps.md` |

**Publication boundaries:** candidate recommendation, publication eligibility,
confirmed Jira key and completed source close remain separate. No approval route,
source update/BPM activation or remote lookup was added. Old persisted preview
fingerprints conflict after this upgrade and are not reset. A successful Jira
create with failed local acknowledgement remains reconciliation-required; no
exactly-once claim is made. Known pre-dispatch outages remain distinct from
transport loss or an unreadable publication response. Safe support references
survive error presentation. The original write fences remain unchanged.

**Management:** use existing backend counts and UTC half-open reporting windows.
Active users count distinct actors with a recorded session start or catalogued
business event; they are not concurrent sessions or employee productivity. Reloads,
polling, report reads and LastAuthenticatedAt are not new usage events. Resource
adoption is not currently measured by that catalog. No most-active-user ranking,
new surveillance collection, invented savings, review-age KPI or trend was added.
Current reports count transitions during the chosen period, not the current total
backlog; source creation time may be unknown. Broader backlog/age and resource
adoption metrics require a separately defined trustworthy read model.

**Local verification:** the full Release run passed 1,020 unit and 242 integration
tests, zero failures/skips; eight were real LocalDB SQL cases. Subsequent changed
UI/client tests extend that evidence without relabelling InMemory as SQL. The
001-010 representative upgrade passed in isolated `SecureOpsResourcesV1` /
`SecureOps_ResourcesV1_SdmMilestone20260907`. New SQL checks cover concurrent owners,
durable command replay and a Jira-key/history transaction failure followed by a
fresh service instance and blocked retry/new-key create. No migration changed.
OpenAPI snapshot/permission gates passed in the full run. The vulnerability scan
reported no vulnerable direct/transitive packages across eight projects.

**Browser evidence:** `tests/browser/resource-ui.cjs` passed all six existing
regression cases against the updated UI using synthetic loopback data. New
`tests/browser/sdm-milestone.cjs` checks the interactive management period picker
and captures resource/group/management empty states, SDM source-unavailable and
reporting-unavailable states at 1440x900 and 390x844 with no horizontal overflow.
Images and TRX files are under ignored `artifacts/sdm-milestone-20260907/`.
`sdm-synthetic-component-*.png` renders the real Razor component with deterministic
synthetic evaluation evidence and actual theme styles; it is static component
visual verification, not an interactive publication journey. Populated resource
screenshots from the unchanged baseline remain linked above.

The existing Demo API has reporting/source disabled. A populated SQL-backed
dashboard, publisher confirmation, corporate browser policies, timed expiry and
screen-reader journeys were not browser-verified in this task. Hosted tests cover
publisher/manager/denied API paths. No rejected proxy override was retried.
Automatic review also rejected background host startup with `blocked by policy`;
the standard foreground Demo profile and existing API connection were used, then
stopped. No corporate call, SQL operation, IIS change or deployment occurred.
MudBlazor/.NET and Bitbucket remain separate follow-up milestones.

**Final source/release association:** backend hardening is
`bdcdce033702bab5f7fc9ec3147dee05a09d9574`; the exact paired API/UI build source is
`de6e5381bd3d7e053f2e9c1c6b07e93283f55ca7`. Packaging tooling is committed as
`6e039e2c8ca19c929345ed3ede107d96efa9a81d`. Later handoff-only commits do not
change the package source SHA. Both DLL ProductVersion values contain that exact
build SHA; the shared Infrastructure assembly is byte-identical in both packages.

Release root: `C:\SecureOpsBuild\release\2026-09-07-pilot-rc6.10`.

| Artifact relative to release root | SHA-256 |
|---|---|
| `API/secureops-api-TEST-de6e538.zip` | `542DD21F081BFE557DC9B87EC45A9F9BB8A827FF566050C4BC765731EB3F0661` |
| `UI/secureops-ui-TEST-de6e538.zip` | `3AA2226C3E492482149EFD2624CDD1B20EFDC208810093CF2C4EAD30530F81CA` |
| `DBA/secureops-database-001-010-TEST-rc6.10.zip` | `8EC5D20BB6517017801EB0D31EFA15E1576EFFCC851412AEAD22CBFD4F9440E8` |

The release includes `release-readiness.md`, `release-metadata.json`,
`release-artifacts.sha256`, per-archive/per-file hashes and `operator-runbook-tr.md`.
API 238 entries, UI 252 entries and DBA 22 files passed exact path/hash checks.
API AD runtime and offline Test Swagger gates passed. UI packaging also proved
refusal to overwrite an existing archive without changing its hash. Server-owned
configuration/secrets and PDB/source/log files are outside application ZIPs.

Final source builds/publishes passed with zero warnings/errors. After the full
1,020 + 242 test run, 499 UI tests passed and the final nine publication-client
tests passed after the known-transient distinction; these overlapping runs are
not summed as independent totals. SQL tests use the isolated local owner; actual
corporate runtime grants/ownership-chain validation is still a DBA gate. No
repository-wide formatting cleanup was attempted; build analyzers and diff checks
passed for the changes. All task additions were below 1,000 lines at final review;
the authorized scoped exception was recorded but no permanent rule was changed.

**Push outcome:** normal push to the configured origin branch was attempted and
failed: Git could not obtain credentials with interactive prompting disabled.
Remote URL/upstream configuration is verified; live remote SHA is **not verified**.
The cached tracking SHA remains the starting `0276bf1`, not evidence of the current
remote. No force push, merge, branch deletion or credential-store inspection.
All milestone commits remain local until normal repository authentication works.

The Turkish operator sequence is in `docs/24-api-test-deployment-readiness.md`,
SDM/resource runbook section, and exported into the release. First manual TEST
step: record the actual targets, current API/UI/schema and verified backup/rollback
references using read-only inventory under the separate TEST change approval.

### Resource backend prerequisites

Migrations 001-010 and the reviewed object grants must be applied, and an API build containing
`ResourcesController` deployed, before these routes work against corporate TEST. Neither has
happened yet. `ResourceCurator` is assigned by an existing Admin through
`PUT /api/v1/access/users/{id}/roles`; no migration or task assigns it.

## HTTPS offload behind the corporate load balancer

TLS terminates at the F5 and the backend hop to IIS is cleartext HTTP. Antiforgery and the session
cookie are both `__Host-` prefixed with `SecurePolicy = Always`, so if the application does not see
the request as HTTPS, rendering `/login` throws and returns 500.

`Hosting/HttpsOffloadMiddleware` restores the external scheme, and runs immediately after
`UseForwardedHeaders` — before HTTPS redirection, authentication, secure cookies and antiforgery.

It **does not trust `X-Forwarded-Proto`**. The scheme is set to HTTPS only when *all* of these hold:

| Condition | Key |
|---|---|
| feature explicitly enabled | `ReverseProxy:HttpsOffload:Enabled` |
| immediate `RemoteIpAddress` is an exact configured proxy | `ReverseProxy:HttpsOffload:TrustedProxyIps` |
| `Host` is an exact configured host | `ReverseProxy:HttpsOffload:ExpectedHosts` |
| `Connection.LocalPort` matches | `ReverseProxy:HttpsOffload:ExpectedLocalPort` |

Any mismatch leaves the request as HTTP. Options are validated at startup, so a half-configured trust
boundary stops the host rather than degrading silently.

The UI explicitly binds the same `DataProtection` option names as the API. Controlled single-node
hosting uses `DataProtection:Mode=FileSystemDpapi`, `DataProtection:ApplicationName=SecureOps.Ui`,
and an absolute server-owned UI key-ring path outside the deployment. Its App Pool identity needs
read/write/create access only to that directory. The UI authentication and antiforgery cookies use
this ring; configuring only the API ring does not make UI cookies survive a recycle. Multi-node UI
hosting requires `FileSystemCertificate`, a shared UI ring, and the same `SecureOps.Ui`
discriminator on every UI node.

**Trusted proxy IPs are server-owned.** Never widen them in application defaults, never trust a CIDR
range, and confirm the authoritative LB SNAT/backend source set with the network owners rather than
inferring it from observed traffic.

Note: `UseForwardedHeaders` is left with framework defaults, which trust loopback only. It is
therefore inert on the LB path — deliberately, since the offload decision above does not depend on
forwarded headers. Client IPs in logs will be the LB address until known proxies are configured
separately.

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

See `docs/agent-guides/060-ui.md`. Highlights:

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

UPN lookup works as of backend commit `704c32ba` (G-7, resolved). `supportsUpnLookup` now reports the
active provider's effective capability, so the flag and the endpoint agree: with
`IdentityLookup:EnableUpnLookup` on, `pam12356@contoso.local` resolves; with it off, the flag reports
`false` and the UPN returns 404. `AccountInputRules` reads the flag rather than assuming either way.

If Chrome shows "Güvenli değil", run `dotnet dev-certs https --trust` once. No certificates are
committed.

## Testing

Pure UI logic is unit-tested in `tests/SecureOps.Tests.Unit/Ui/`: error translation, account input
rules, lookup result reuse, and return-URL safety. `LocalReturnUrl` is `internal` and reachable via
the `InternalsVisibleTo` entry in the csproj.

Responsive and visual behaviour is validated in a real browser at 1440×900, 1366×768, and 390px in
both themes. See `docs/25-ui-enterprise-shell.md` §8.
