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

### SDM, Management and TEST Delivery Handoff, 2026-09-07

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
