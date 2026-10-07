# SecureOps.Ui
## Start here

Blazor Server UI for WASAS SecureOps: `net8.0`, C# 12, SDK pinned in `global.json`, MudBlazor 6.16. It talks
only to the SecureOps API; the API decides every permission ([the one rule](#the-one-rule-that-shapes-everything)).
Agent rules: `AGENTS.md`, `CLAUDE.md`, `docs/agent-guides/060-ui.md`.

**Reading status words.** *Implemented* means present in source; *local tested* means verified on a developer
host with synthetic data; *installed* and *configured* refer to a TEST package and its settings; *accepted* means
verified in corporate TEST by the owner. The dated sections below record each step with its SHA and evidence;
none of them implies a later state. The single current remaining-work register is
[`docs/integrated-test-activation.md`](../../docs/integrated-test-activation.md#requirementevidence-tracking).

**Commands**

| Task | Command / location |
|---|---|
| Run API + UI locally | [Running locally](#running-locally) (Demo profiles, synthetic InMemory data) |
| Build | `dotnet build SecureOps.sln` (warnings are errors) |
| UI unit and render tests | `dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj --filter "FullyQualifiedName~SecureOps.Tests.Unit.Ui"` |
| Browser journeys | `tests/browser/*.cjs` — loopback hosts and synthetic data only; usage line at the top of each script |
| Format gate | `dotnet format --verify-no-changes` (repository-wide; scoped runs are partial evidence) |

**Feature navigation** (routes and capabilities: [Routes](#routes))

| Feature | Routes | In this file | Contract / decision |
|---|---|---|---|
| Shell, sign-in, theme | `/login`, `/account`, `/dashboard` | [Layout](#layout), [Theme](#theme), [Error handling](#error-handling) | `docs/25-ui-enterprise-shell.md`, ADR-0014, ADR-0016 |
| Access administration | `/access/*`, `/admin/sessions`, `/admin/system-status` | [Access administration](#access-administration), [Access pass 2026-10-01](#access-and-management-usability-pass-2026-10-01) | ADR-0010, ADR-0022, `docs/contracts/secureops-api-v1-ui-integration.md` |
| Identity lookup, directory | `/identity-lookup`, `/directory/users`, `/directory/groups` | [Running locally](#running-locally) | ADR-0008, ADR-0013, ADR-0015, `docs/contracts/directory-group-analysis-ui-integration-delta.md` |
| Operational Record → Jira, SDM | `/operational-records` | [Operational Record → Jira](#operational-record--jira), [SDM handoff](#sdm-management-and-test-delivery-handoff-2026-09-07) | ADR-0009, ADR-0012, ADR-0018, `docs/22-operational-record-jira-workflow.md` |
| Application links, personal groups | `/resources`, `/resources/sets`, `/admin/resources` | [Resource catalogue](#resource-catalogue-and-shift-start-sets) | ADR-0019 |
| In Use | `/in-use`, `/in-use/reports` | [In Use handoff](#in-use-v1-canonical-handoff-2026-09-08), [E-08](#e-08-in-use-activity-review) | ADR-0020 |
| Planned announcements (Codex) | `/announcements`, `/announcements/preparations` | [Planned Announcements UI](#planned-announcements-ui) | ADR-0021, `docs/contracts/planned-announcements-v1.md` |
| Management reporting | `/dashboard`, `/reporting/operators` | [Reporting continuation](#integrated-management-reporting-continuation) | ADR-0011, ADR-0023 |
| Service Accounts (scoped exception) | `/service-accounts/*` | — | `docs/service-accounts/README.md` |

Open UI ↔ API contract gaps: [`docs/26-ui-backend-contract-gaps.md`](../../docs/26-ui-backend-contract-gaps.md).
Below this point: reference sections (Layout to Testing) and dated handoff logs, kept with their source SHAs and
evidence paths. Search for the feature or date you need rather than reading top to bottom.

## PR #4 Windows Verification, 2026-10-02

The Resources/personal shift-group source from 286fac7 was verified on Windows
with the committed SDK 9.0.317/C#12/net8.0 policy, not a substituted SDK10.
Clean Release builds pass with no warnings/errors. Normal unit: 1,610/0/0;
focused UI/render: 676/0/0, Resources: 125/0/0 (overlapping unit). Normal final
integration: 322/0/61, including 39 Service Accounts cases for a naming-only
correction; separate isolated ResourceSQL: 49/0/0.
The real resource-shift Chrome journey passes against combined API/UI and
synthetic InMemory data. WASAS_CHROME selects an installed browser; search
awaits the interactive input and asserted filter state. Archived links are
excluded by name only, returning to editing refreshes before reorder, and
feedback only says an opening request was sent. Desktop/mobile overflow checks
pass; DPR2 captures are not native zoom or managed popup-policy acceptance.

**Merge NO-GO:** repository-wide format verification still reports 313 baseline
whitespace/import-order diagnostics in 13 files. Scoped Resources formatting
passes but is not a waiver. Two private identifiers were renamed without
changing Service Accounts behavior; no module/API/SQL/Worker or external write
contract changed. See the single `docs/integrated-test-activation.md` register
for exact source identities, retained failures and structured baseline comparison.
G31/G32 stay deferred and do not block this bounded journey. No PR4 master
merge, installation approval or new review package is claimed at this checkpoint.

## PR #3 Windows Verification, 2026-10-02

The original UI tip 77a59dc is combined with the G-30 deterministic toolchain
repair. SDK 9.0.317 is exact; C# 12.0/analyzers 9.0 target net8.0 without a
command-line language override. Runtime inputs c7ff942 passed Windows solution
build, 1,597 unit and 322 integration (61 opt-in skips); 663 UI/render cases
are part of the unit total. Native DPAPI cases run on Windows, never skipped.
See the single docs/integrated-test-activation.md register for TRX paths and
separate isolated SQL/browser evidence, retained failures and limitations.
The access browser now matches the explanatory queue/impact text while retaining
exact count, preview, concurrency and denied-actor assertions. The PR3 usability
runner checks both themes/mobile/keyboard and actual Chrome 200% zoom with
physical-width screenshots. These are synthetic local composition journeys,
not normal corporate OIDC, IIS, SQL permissions or installation acceptance.
G-19 through G-25 fallbacks remain; G-26 gMSA is a separate supported-directory
repair/real TEST check, not silently fixed. G-27 through G-29 are not implemented.
The older a457 review packages lack this UI. A new exact-source matched review
is required; neither source merge nor this evidence authorizes deployment.

In Use closure now confirms the exact OR and archived report before submission,
and distinguishes acknowledged, unknown, rejected, source-verified and manually
confirmed results. Manual confirmation is explicit and capability-gated; its time
is displayed in UTC. No source URL was inferred. Component rendering is locally
testable; browser/200%-zoom and corporate closure still require their existing gates.

## Access And Management Usability Pass, 2026-10-01

UI-only pass (Claude) on branch `feature/ui-access-management-20261001`; no API, SQL or contract change.

- **Capability labels.** `AccessLabels` now describes every `ServiceAccounts.*` action, groups
  capabilities by function (OR, In Use, OCO, Servis Hesapları, Kimlik, Bağlantılar, Erişim,
  Denetim) and sorts unknown groups last. `ModuleOrder`/`ModuleGuidance` key on the server catalogue's
  own module names. `HeldRoleLabel` shows an administrator-defined role as "İş rolü" (code as tooltip).
- **Rol tanımları.** Wider role list (purpose, action and module counts, protected tag); the first
  role opens on load (read only). Actions are `AccessModuleCard`s per server module with "x / y seçili",
  written guidance, a Service Accounts scope note and an "Eklenecek/Çıkarılacak" word on changed rows.
  Non-assignable historical actions are collapsed. A sticky bar summarises the definition draft; who
  gains or loses access is still known only from the server preview, which remains mandatory.
- **Erişim talepleri.** Compact rows (name, one secondary line, waiting time or decision time), a
  7-day long-wait label, pager above the list and an explicit statement of the server order (newest
  first). After a decision, "Listedeki ilk bekleyen talebi aç" is offered; nothing is auto-selected.
  The approve dialog's permission difference is grouped by module (`AccessActionGroups`).
- **Erişimim.** Status summary (status, role/capability/area counts, latest request and decision
  time) and capability cards by area.
- **Sistem Durumu.** `Configured` is informational "Yapılandırıldı · sınanmadı", never green;
  `Unavailable` reads "Son çağrı başarısız"; an unreadable provider renders "Okunamadı". The page shows
  its own UTC read time, the response's simulation/read-only notices and the test-directory note, and
  lists SQL, audit store, Worker and SMTP as not reported here.
- **Genel Bakış / Yönetim Panosu.** Management order is window → coverage → summary → attention →
  detailed report → module panels (which keep their own filters) → quick access, with a focus-based
  section bar. Quick access adds existing capability-gated routes only. The operator board no longer
  says the directory "responds"; it shows "Yapılandırıldı · sınanmadı" and only to `Identity.Lookup`.

- **Giriş sayfası.** The flight overlay is locked to the photograph's pixel space (1672x468, same
  height and `--so-photo-x` position as the photo), so the Istanbul hub stays aligned at every size.
  Seven routes leave Istanbul for Europe, the Americas and Asia; each carries an outbound and an
  inbound aircraft driven by SVG `animateMotion` + `mpath` on that same path (the old CSS
  `offset-path` copy had drifted from the drawn line). The artwork's day/night follows the Istanbul
  clock (`data-so-sky`, 07:00–19:00 day; appearance is the fallback), night adds navigation lights;
  the card still follows the reader's appearance. Reduced motion freezes the timeline with aircraft
  on their routes. No place labels are drawn.

- **Navigasyon.** Groups follow shift work: Vardiya işleri (OR, In Use, Duyurular), Kimlik ve
  hesaplar (AD kullanıcı/hesap, AD grup, Servis Hesapları), Bağlantılar, Raporlar, then a folded
  **Yönetim** group that opens itself on its own routes. Planned pages moved to one footer line;
  Erişimim and the non-production marker sit in the footer. The light theme's drawer now shares the
  app bar navy (`SecureOpsTheme` DrawerBackground/Text/Icon, width 264px); the active item is white
  text with the brand-red marker, because red text on navy fails contrast. Capability gates and
  routes are unchanged.

Missing contract data is recorded as G-19 to G-30 in `docs/26-ui-backend-contract-gaps.md`.
Verification: targeted unit/render tests and a local Playwright run against a synthetic stub API
(390px, emulated 200% zoom at 683px CSS width, light/dark, keyboard, denial, loading/error/empty).
The stub is not committed. Windows/IIS/LocalDB, native browser zoom and corporate data remain unverified.
Handoff check (Linux, SDK 10.0.112, no `LangVersion` override): `SecureOps.Ui` builds clean, but the
solution does not build as committed (G-30). With the G-30 test line changed in a throw-away copy only,
the 663 `SecureOps.Tests.Unit.Ui` tests pass; the remaining unit and integration failures match master
exactly and are platform-dependent (recorded under G-30).

## System Status Presentation Continuation, 2026-09-20

`/admin/system-status` keeps the authorized read-only diagnostic and JSON download
contracts. The workflow-check card separates disabled, unchecked and partial
evidence; capture/source-check times remain distinct UTC values from the response.
Page refresh is integration-only, mutually disabled with an active workflow check.
No automatic check, polling, configuration update, source/SMTP request or archive
write probe was added. Transient failure retains a labelled previous snapshot;
access/session denial clears it. Synthetic presentation tests are not visual or
installed acceptance. See the single `docs/integrated-test-activation.md` register.

The current continuation adds `/in-use/reports`, linked from In Use, with bounded
server search by OR/hostname/original preparer/account, UTC dates, version and
lifecycle. It displays exact historical download names and separate attachment
verification; no source action is triggered. The assignment dialog loads an
authorized RFC suggestion with explicit matched/missing/ambiguous/ineligible
states. Selecting is local; saving records an explicit reviewed decision.
The revised `inuse-rc626-repair.cjs` includes catalogue download integrity,
mouse/touch/keyboard/focus return, themes and optional native Chrome 200% zoom.
Its syntax is checked, but the final host/browser run remains pending after the
previous tool-policy rejection. No replacement launcher was used to evade it.

Post-rc6.26 In Use continuation: assignment uses the existing MudBlazor dialog
service, explicit save/cancel and version-bound API, preserving unsaved answers.
Unsaved undo is local; saved reset/discard/restart calls the authorized lifecycle
endpoint and never deletes files. Removed drafts have an explicit list filter and
restart action. Names use the original report preparer; evidence is outside the
four corporate worksheet preview. OCO profile failure/disabled/unconfigured states
are distinct; mail flags do not enable source collection. Final current-payload
browser/touch/zoom acceptance remains pending, not inferred from compilation.

## rc6.22 Operator Completion

The repaired OCO, In Use and access journeys are packaged at build source
`9ec65eb377ea020916bab8c803601e237153f34c`. Current acceptance and remaining
corporate checks are in `docs/rc622-local-acceptance.md`; historical totals below
are not evidence for these repairs. Turkish upgrade instructions are in
`docs/rc621-upgrade-tr.md`. This continuation explicitly includes the affected UI.
The local packaged walkthrough uses synthetic data only at
`https://localhost:64652/announcements` and `/in-use`; its private host record is
under `rc621-completion-20260917/packaged-acceptance2/hosts`. It is not TEST configuration.

### Kısa Rol Ve İş Akışı Rehberi

Güncel rol içeriği SQL'deki sürümlü tanımdır; iş unvanı yetki vermez. Dokuz eski
rolün kimlikleri korunur. Yönetici kullanıcı/erişim yönetimini ve korumalı Admin'i
görür; yeni iş rolünü eylemlerden oluşturur, etki farkını inceler, açıkça uygular.
Sıradan rol değişiminde gerekçe yok; ret/kapatma gerekçesi korunur. Kendi hakkını
yükseltme ve eşzamanlı son Admin kaybı engellenir.

| İş | Gerekli başlangıç yetkisi ve kısa akış |
|---|---|
| In Use | Reviewer: görüntüle/cevapla; Coordinator veya Admin: ayrıca ata/yenile. Liste → kayıt → isteğe bağlı kişi/gerekçe → üç kontrol → taslağı kaydet → Excel. Başka atanmış kaydı inceleme yetkiyle mümkündür. |
| Operasyon Uzmanı | OR görüntüleme/önizleme; Jira create hakkı yok. İş unvanı veya tür beyanı yayın onayı değildir. |
| OR yayımlama | JiraPublisher/Lead/Admin, ayrıca exact policy ve kapılar. Kayıt → kaynak/engeller → önizleme → tek onay → kayıtlı Jira key; Jira-only kaynak açık. |
| OCO hazırlık | Drafts + ayrı Source/Prepare eylemleri: kaynak önerisini incele/uygula → alıcıları düzenle → kaydet → hazırlık/.eml. |
| OCO mail | Ayrı SelfTest/Send hakları ve yapılandırma kapıları; Admin dahil otomatik verilmez. Hazırlıktan önizleme → From/alıcı/içerik → tek açık onay → kayıtlı sonuç. Kendime deneme yalnız kendi kayıtlı Mail'ine; Unknown/Partial yeni gönderimi engeller. |

Kaydetmek, rapor hazırlamak, SMTP kabulü ve doğrulanmış kaynak kapanışı ayrı
sonuçlardır. Aktör geçmişi bu ayrımı korur. Kurumsal aktivasyon ve güncel paket
kanıtı `docs/24-api-test-deployment-readiness.md` içindeki son teslimat bölümündedir.

## Current Operations Continuation, 2026-09-17

The owner explicitly assigns this bounded backend/UI continuation to Codex.
Current source supersedes the incomplete implementation statements in historical
sections below. Canonical validation/release status is the current section of
docs/24-api-test-deployment-readiness.md, not an older local payload version.

In Use list/detail exposes assigned reviewer, saved versus local progress, freshness
and next missing action before technical evidence. Assignment remains optional;
cross-assignee review is allowed. A bounded persisted-profile picker, distinct
assignment actor/time and saving actor remain separate from RFC/Bildiren. Selected
bulk differences, missing-answer focus, draft preservation/conflict comparison,
immutable reports and optional non-mutating tour remain. Assignment is no closure.

Access requests/users use SQL search/filter/count/paging and retain list context.
User detail groups effective actions by the role providing them. Roles can be
created from registered actions; impact, concurrent version comparison and explicit
apply precede changes. Ordinary role replacement has no reason field. Rejection and
disable still do. Protected Admin, no self-escalation, last Admin and current access
are enforced by SQL, not UI labels. Request approval is not user-management access.

OCO source proposal review/apply remains explicit. Saved preparation now offers
separate capability-controlled self-test/send previews: exact actor-Mail From,
audience, version and visible effect; one confirmation queues the frozen command.
Uncertain/pending results block another command; refresh reads status, not resend.
SMTP acknowledgment is not inbox delivery. History retains original preparation
bytes and initiator even after profile change or Worker restart. Source, preparation,
self-test and send are separately authorized; all corporate activation remains off.
Original six images are embedded with byte-derived types and bounded sizes.

OR retains one reviewed ServerRequest confirmation, prominent stored Jira result,
source-open Jira-only intent and uncertain-result blocking. Corporate source closure
and other request-type mappings remain blocked. Typed record history shows known
initiator/executor/verifier and leaves unavailable source closer unknown.

Operator journeys: In Use filter -> assignment when useful -> server checks ->
save -> missing checks/bulk differences -> preview/report; Access search -> detail
-> reviewed role change; OCO source review -> edit audience -> save -> preparation
-> self-only preview/confirm -> recorded SMTP result -> separately authorized final
audience preview/confirm; OR stored record -> declaration -> eligibility/blockers ->
source-bound preview -> explicit Jira-only confirmation -> persisted key/status.

## Planned Announcements UI

2026-09-15 sender/access continuation: API currentSender uses stored Mail and is
read-only; missing Mail permits drafts but blocks new email artifacts. Ordinary
role replacement no longer asks for justification; rejection/disable still do.
User detail groups returned actions, with technical codes secondary. The matrix
and pending dynamic role/paging work are in docs/23. Real SMTP and full access
redesign are not complete or packaged by this bounded checkpoint.

Explicit source review now joins the saved editor and immutable preparation history.
See `docs/contracts/planned-announcement-integration.md` for combined acceptance and limits.

`/announcements` now connects owner-scoped paging, structured drafts, banners,
explicit saved preview/download, conflict comparison and per-user guided help.
Codex owns this module's UI and backend by explicit user instruction. Owned immutable
preparation history is implemented; sending/confirmation and ordinary-user roles are not. See `docs/contracts/planned-announcements-v1.md`
for API behavior, local browser replay, evidence and remaining quality gates.
New drafts default to the final table; existing v1 requires explicit versioned upgrade.
System/application and the collapsed service list stay separate. Debounced unsaved
preview shares the saved-preview/export renderer, with responsive tabs and cancellation.
Date/hour/minute controls retain seconds and explicit offsets. Source/profile flow is pending.
Acceptance repair retains stale same-draft previews during edits/errors, stages loaded
sandbox frames and explains date validation inline. Access loss clears private state.

## Current RFC Reporter Acceptance, 2026-09-12

The preparation-only statements below are historical. Operator-approved RFC
enrichment is connected through explicit refresh; list/detail read stored data.
See `docs/24-api-test-deployment-readiness.md` for current evidence and limits.
All four server rows use page flow, with keyboard answer selection and mobile
stacking. Technical reporter references remain in collapsed source evidence;
ordinary rows say `RFC eşleşti`, not ownership/completion verified. UI and new
ReviewEvidence exports decode once; archives and raw values remain unchanged.

## RFC Reporter Preparation, 2026-09-11

The current correction supersedes historical requester/Reporter/ownership wording
below: observed SMSS_oRFF p_rel_requester is Bildiren. The separate Istem Sahibi
selector is unknown. Do not rewrite old source/audit values or rename broad DTOs.
DOM establishes c_rfc_record/c_virtual_pc_user as candidates only; _i_ is not an
API selector. New collector A inspects them independently; B follows one exact
RFC only after representation verification. Current commands are in
`scripts/diagnostics/InUseEvidence/operator-reporter-tr.md`; preserve 6e05b45.

Local preparation adds a per-server reporter observation, strict exact projection,
JSON persistence, raw display/reference separation and stale/version behavior.
The additive contract is in `docs/contracts/secureops-api-v1-ui-integration.md`.
Production DiscoverAsync is intentionally unchanged pending operator evidence.
The UI owner must then replace ownership/requester labels with the observed
semantics and show **İlgili talebi bildiren** per stored server on list/detail.
No person is inferred as owner, provisioner or reviewer. Existing optional review,
trusted reviewer labels and single-pass decoding are preserved. UI rendering,
production RFC persistence and corporate/VDI acceptance are NOT complete.

Local verification: Release solution and standalone builds passed with zero
warnings/errors; 1,209 unit and 243 integration tests passed, with 18 opt-in SQL
tests skipped (no database execution). Coverage includes field order/types,
private comparison isolation, empty Virtual PC User, missing RFC, exact closed
targets, ambiguous/denied results, shared-reference deduplication, per-server
reporters, Turkish decoding, stale/failed retention and existing authorization/
concurrency regressions. OpenAPI compatibility passed without snapshot changes.

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
`SoField`, plus the `.so-panel` CSS class. Access screens add `AccessModuleCard` (one server catalogue
module in the role editor) and `AccessActionGroups` (server-returned action codes grouped by module). Use these rather than new one-off markup, so states look
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
| `/directory/users` / `/identity-lookup` | Exact-account lookup and bounded first/full-name search; no module dependency or general-search inventory links | `Identity.Lookup`; group panels `Identity.Groups.View` / `Identity.PrivilegedGroups.View` |
| `/directory/groups` | Read-only AD group analysis | `Identity.Groups.View`; members and export need their own capability |
| `/account` | Identity and session security | authenticated |
| `/access/me` | Status, roles, grouped capabilities | authenticated |
| `/access/requests` | Access-request decision queue | `Access.ApproveRequests` |
| `/access/users` | User list, grouped by access state | `Access.ManageUsers` |
| `/access/users/{id}` | User detail, role editor, disable | `Access.ManageUsers` |
| `/access/roles` | Role definitions by module, server impact preview, explicit apply | `Access.ManageUsers` and `Access.AssignRoles` |
| `/admin/system-status` | Provider settings (configured ≠ healthy) and explicit workflow check | `Access.ManageUsers` (API: Admin); workflow check `SystemDiagnostics` |
| `/admin/sessions` | Active application sessions and revocation | `Access.ManageUsers` |
| `/operational-records` | OR → Jira workspace, grouped by attention | `OperationalRecords.View` |
| `/operational-records/{id}` | Source, workflow, and Jira transfer | `OperationalRecords.View` |
| `/resources` | Uygulama Bağlantıları: search, favourites, add to a personal group | `Resources.View` |
| `/resources/sets` | Bağlantı Gruplarım: personal ordered groups, preferred group, opening | `Resources.View` |
| `/admin/resources` | Bağlantı Yönetimi: shared categories and links | `Resources.View` and `Resources.Manage` |
| `/service-accounts` | Servis Hesapları: entry work summary, scoped list and filters; selected accounts take one mail record or one usage-scan file; bounded directory name search panel (ADR-0025) | `ServiceAccounts.View` and scope grant; the name search also needs `Identity.Lookup` |
| `/service-accounts/{id}` | Account detail, work, usages with the explained knowledge-base rule, evidence and history | `ServiceAccounts.View`; commands require their own capability (rule exception: `ServiceAccounts.Verify`) |
| `/service-accounts/work` | Entry work summary, in-app reminders and unsent coordinator drafts | `ServiceAccounts.View` |
| `/service-accounts/imports` | Import guide (tracking workbook once, weekly list, DBA list), preview, decisions and idempotent commit; without organization scope it explains how another module administrator grants it | `ServiceAccounts.Import` and organization scope |
| `/service-accounts/reports` | Live report (directorate view, rules, gMSA funnel, trend, risk candidates), immutable snapshots, snapshot comparison, XLSX/PDF | `ServiceAccounts.Report` |
| `/service-accounts/admin` | Scope grants, dictionaries and gMSA routing team roles | `ServiceAccounts.Administer` |
| `/in-use`, `/in-use/{id}` | In Use local review workspace | `InUse.View`; review, assign, refresh and completion need their own capability |
| `/in-use/reports` | In Use report catalogue | `InUse.View` |
| `/announcements`, `/announcements/preparations` | Planned announcement drafts and preparations (Codex-owned) | `Announcements.Drafts` / `Announcements.Prepare` per the contract |
| `/reporting/operators` | Management reporting view | `Reporting.ManagementView` |
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
  and retry blocked in every state, and an explicit duplicate-risk warning.

**Idempotency belongs to the backend.** The UI sends no `Idempotency-Key`. The contract makes it
optional and the API then derives a deterministic key from actor, command, and record — which is
already the desired behaviour, and generating one here would be a second competing policy.

**`claimed` decides liveness; `claimedBy` only decides whose.** An expired claim may keep its
`claimedBy` while `claimed` is false — reading the owner alone would show a lapsed claim as active
and block a record nobody holds. Compare against `GET /identity/me`'s `name`, never the cookie
principal: the API sees `demo:platform-admin` where the session says `platform-admin`.

**`reconciliationRequired` outranks the state machine.** While it is set, create and retry are
blocked in every state. The contract keeps `retryEligible` false here; a contradictory payload fails
closed.

**Never auto-retry.** Retry is offered when authoritative record state says a stage is resumable, not
because a call failed. `WorkflowConflict` at `stage: "jira-reconciliation"` is mapped to a dedicated
non-retryable presentation, never to the generic conflict message.

**No polling.** `GET /operational-records` is a rate-limited source refresh, not a passive read.
Refresh is a deliberate operator action, plus an automatic re-read after every write.

### Jira-only submission states (UI, 2026-09-28)

Combined-source verification found duplicate access notifications during first
render. Cached waiters now reuse the snapshot without another `Changed` event;
explicit refresh and newly loaded access still notify. The OR list/detail subscribe
after acquiring their initial access snapshot, avoiding a redundant initial load
that can discard a just-requested preview. API authorization is unchanged.

`JiraSubmissionPanel` sits directly above the action buttons and names one phase, resolved by
`JiraSubmissionView.PhaseOf` from the re-read record first and the page's last command second:

| Phase | Shown when | Never |
|---|---|---|
| Ready | server preview held, `ActionsFor().Create`, create capability | shown for a review-only draft |
| Submitting | create/retry command in flight (`aria-busy`) | shown while the dialog is still open |
| Created | re-read record has `jiraExists`/`jiraIssueKey` | derived from a `JiraTransferResponse` alone |
| Acknowledged | command answered, re-read record has no saved key | called a created issue |
| Rejected | command refused, no key, outcome not unknown | offered as uncertain |
| Uncertain | `reconciliationRequired`, key-less `CreatingJira`, or `stage=jira-reconciliation` | offers create or retry |

- Ready shows the selected OR and source id, request type and its basis, Jira project/issue type/id,
  every preview field, the transfer key as duplicate protection, and a prominent statement that the
  Turuncu Hat record stays open (from `preview.sourceCloseRequested`, never assumed).
- Transfer-and-close is not offered anywhere on this screen; the panel states why, using
  `sourceCloseEnabled`. Unsupported request types show only server blocker guidance
  (`TypeMappingPending`, `RetirementMappingPending`, `ApplicationMappingPending`, category codes).
- The confirmation dialog requires ticking a statement naming the OR and the source outcome
  (`SoConfirmStatement`); **Oluştur** stays disabled until then. Initial focus is the statement,
  because with Oluştur disabled MudBlazor's focus trap left forward Tab unable to reach it.
- An unacknowledged command stays Uncertain until an explicit **Yenile**; the automatic re-read can
  race a request the server has not started. Create/retry stay disabled while Uncertain or
  Acknowledged. Under `reconciliationRequired` retry is never offered, even if a payload
  contradicts the contract by also sending `retryEligible=true`.
- After a command settles, focus moves to the panel heading.
- No Jira link is rendered: the contract carries no issue URL, and none is composed from the key.
- **Yeniden Dene is a write and is confirmed.** For a safe `JiraCreateFailed` the server reports
  `retryEligible=true` and `POST /retry` rebuilds the draft and calls Jira create again.
  `JiraRetryDialog` states that effect and the source outcome and uses the same statement gate.
  The reviewed fields cannot be re-shown there: no preview exists in a failed stage.
- A lost response that the re-read resolves to a persisted key is shown as Created with a
  resolution note; the stale "may or may not exist" notice is not kept beside the saved key.
- `WorkflowAlreadyInProgress` on create/retry is Uncertain, not Rejected: the same command scope
  is still executing and this page's request was not refused on its merits.
- **UI transport resend (measured 2026-09-28).** .NET `SocketsHttpHandler` can transparently resend
  this body's-empty UI-to-API POST when the connection closes before any response byte,
  and `PooledConnectionLifetime`/`Connection: close` do not stop it. A lost create response can
  therefore reach the API twice. The API's deterministic actor/command/target command key turns
  the second into a replay (one `JiraCreated` observed). A UI-only suppression needs a custom
  connection stream that is not reliable under TLS, so none is shipped; the backend key semantics
  are a hard dependency of this screen.
  This observation is not a claim about the API-to-Jira JSON POST. The separate
  `CorporateJiraSocketTests` exercises that actual client against a loopback server
  which consumes the reviewed body then aborts the response: both fresh and reused
  connections produce one create and a non-retryable unknown outcome. No corporate
  destination is contacted, and proxy/TLS behavior still needs target acceptance.

Backend contract needs (for Codex; nothing here is assumed by the UI):

1. `jiraIssueUrl` (nullable, absolute https, server-derived from configuration) on
   `OperationalRecordResponse` and `JiraTransferResponse`, to render the saved issue as a link.
2. The transfer key (`idempotencyKey`) and the UTC time the Jira key was persisted on
   `OperationalRecordResponse`, so duplicate protection stays visible after a page reload.
3. A replay indicator on `JiraTransferResponse` (for example `replayed: true` when an existing key
   was returned without a new Jira call), so "created now" and "already existed" can be told apart.
4. A read-only view of the persisted reviewed draft (fields and mapping version) for a
   `JiraCreateFailed` record, so the retry confirmation can show what Jira will receive.
5. Keep the deterministic actor/command/target fallback command key for create/retry; the UI sends
   no `Idempotency-Key` and relies on it to absorb transport resends (see above).

None of these fields exist in the current DTOs or in the backend worktree's uncommitted changes;
the UI does not read or assume them.

Replay (all Simulation-only, synthetic `SIM-*` keys, loopback, test-owned LocalDB):

- `tests/browser/sdm-jira-submission.cjs <playwright-core> <UI URL> <API URL> <evidence dir>` —
  ready/confirm/created/rejected/uncertain/review-only.
- `tests/browser/sdm-jira-negative.cjs <playwright-core> <admin UI URL> <API URL> <proxy URL> <db> <evidence dir>`
  with `announcement-hosts.ps1 -OperationalRecordSimulation -UiApiProxyPort <proxy>` and
  `tests/browser/sdm-fault-proxy.cjs <proxy port> <API port>` — stale preview after another
  operator, lost response, request never delivered, stale source plus retry gate, unsupported types.
- `tests/browser/sdm-jira-only.cjs` (existing) after the five `ResourceSqlTests` SDM cases seed a
  fresh `Test-ResourceCatalogueSql.ps1` database; `--verify-presentation` after a real host restart.

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

### Resources Pre-Package Correction, 2026-09-10

#### Consolidated TEST Continuation, rc6.15

Canonical installation/pilot procedure: the first rc6.15 section of
`docs/24-api-test-deployment-readiness.md`; export markers feed the paired-release
script. This supersedes older "do not package" source-only notes, not their
historical evidence. Resources improvements and stored In Use overview/local
completion journal are included; RFC ownership and real source completion are not.
SDM now has three review declarations and separately labelled reason suggestion,
source work/outcome, missing facts and specific blockers. Server-owned, exact
single-record policy can produce positive ServerRequest preview; default empty
policy and unchanged write fences keep corporate publication disabled. Retirement
and application installation remain mapping-blocked. Schema 013, no new grants.

Verification source: current task starts at `dbd09219ebf88e326462fb8d34affea50302acd4`.
Targeted unit 345; API/OpenAPI/DP 43 (four isolated SQL tests intentionally skipped
in that host-only run); isolated 001-013 harness passes 17 tests for upgrade, evaluation/audit
rollback, constraints and workflow persistence. Published loopback SQL/Simulation
journeys cover three review types, direct API denial, conflict preservation,
confirmation double-click, Jira-only linkage and unknown-result no-retry; In Use
after/mapped/progress cover desktop/mobile, themes, archive and local completion.
Evidence root: `C:\SecureOpsBuild\validation\sdm-delivery-20260910\browser`.
No corporate call, real VDI, deployment or source-write acceptance is claimed.

Antiforgery reproduction: provider restart with Ephemeral rejects the previous
token; FileSystemDpapi with the same application/ring validates; different app
identity rejects. Six DP tests pass, no antiforgery weakening. Published persistent
hosts pass login and all journeys. Five fresh-circuit refreshes completed in
188-266 ms without page errors; earlier timeout did not recur. Its historical root
cause remains unproven, not declared a corporate outage or product regression.

Release build source: `fcde31010e82c5551ce1cb06846ba8fbd16e620c`.
Root `C:\SecureOpsBuild\release\2026-09-10-pilot-rc6.15`; generated manifests
contain every file's size/SHA256. All older ZIP hashes were rechecked unchanged.
| Artifact under release root | Bytes | SHA-256 |
|---|---:|---|
| API/secureops-api-TEST-fcde310.zip | 24673169 | 4D06FFAC69FE3D5CBEAF6B96967B71CC384DFB9A24CF77DA5042608CA3FFA8B6 |
| UI/secureops-ui-TEST-fcde310.zip | 26457611 | ED1DEC135762BF5361F895A275C9BE8E260975804B80C93F078ACF4BE9EBA1E0 |
| DBA/secureops-database-001-013-TEST-rc6.15.zip | 22726 | 16778AFF16ABE2B4D68A029924F5F92A4A0E0A606EE45CB792A83C208926A433 |
API 238/UI 254/DBA 28 entries verified, config/source/PDB/secret-marker gates
and API AD dependency closure passed. No NuGet vulnerability was reported for
the eight solution projects. Scoped formatting passed excluding pre-existing
IDE1006 private-constant naming diagnostics; build has zero warnings/errors.
Both framework-dependent hosts require .NETCore.App and AspNetCore.App 8.0
(IIS Hosting Bundle for IIS); no SDK on TEST. .NET 10 migration is separate.
Exact release binary journeys passed in `browser/release-sdm`, `release-mapped`
and `release-refresh`; five refresh notices took 186-310 ms, no page errors.
Later documentation commits do not alter these payloads. Final Git synchronization
is recorded separately in release metadata after a live remote query.
One persistent team-lead host also logged one antiforgery exception; its detailed
cause was not retained. Fresh isolated replay (`browser/release-auth`) passed
without that exception, including shutdown. Do not equate this with a resolved
IIS/cookie issue: persistent-key/app-identity/login smoke remains a TEST acceptance
gate. No antiforgery bypass or corporate configuration change was made.


Start local/live HEAD: `b4910ad045c688eaeb148c5d5b93d255330c9142`, same feature
branch. This is the explicitly requested bounded Resources increment. The prior
In Use increment (692 lines) and its unfinished backlog below remain separate;
no old scope exception is reused. No package, deployment or release archive change.

Full Resources increment: 559 changed lines (477 additions, 82 deletions), 21 files.
Including the earlier independent In Use increment: 1,251 lines, not concealed by commit splitting.

**Opening diagnosis:** the previous published UI resolved three distinct synthetic
IDs/URLs in the requested order. Chrome 152.0.7977.83 opened one actual page under
default blocking and three under a site-specific popup allowance. `_blank` is not
reused, the loop continues after null/exception, and the delegated trusted browser
click contains no Blazor/async boundary. `noopener,noreferrer` remains on every
call/link; its null return is NOT a blocked-tab detector. The rc6.14 recorded build
source `b217f000eafb4a18f7029629e1119c86205d9295` has identical opener/component
source to this task's baseline. Installed binaries, actual selected corporate
catalogue entries and managed TEST browser policy were not inspected; their live
root cause is not proven by local reproduction.

**Operator flow:** select once -> Açmak için hazırla (server access resolution) ->
the explicit native N bağlantıyı aç button. The prepared view replaces the duplicate
catalogue/selection list; Seçimi değiştir restores the same selection. Native Aç
links remain individually usable. Guidance explains popup permission and avoiding
duplicate tabs, never claims destination load/login, and never retries automatically.
Selection/filter/page/access/refresh invalidation also rejects late preparation
responses. No background catalogue polling or target fetching was added.

Kişisel bağlantı gruplarım and Seçilenleri grubuma kaydet identify personal ownership.
Resources.View is sufficient; Resources.Manage remains required only for shared
catalogue administration. The dialog retains selected links when choosing an
existing group or naming a new one. A known version rejection retains the name
and selection, offers an explicit current-group reread, then requires another save.
Unknown write outcomes never acquire this retry path. Existing preferences,
hidden-member merge, server-owned actor attribution, audit and version checks remain.
The four targeted tour steps are select/open/save/reopen; navigation is read-only.
Duplicate header/shortcut routes are suppressed without rewriting preferences.

**Verification:** Release solution build: zero warnings/errors. 233 focused unit
tests and 6 hosted API/OpenAPI/In Use tests pass. Scoped dotnet format passes.
`resource-opening.cjs` checks real page counts/URLs and opener isolation under both
policies, non-admin create/existing-group save/reorder/reopen, actual 409 recovery,
cross-user direct denial, partial resolution after archive, restored membership,
tour keyboard/focus/nonmutation and desktop/mobile light/dark. It removes
Playwright's `--disable-popup-blocking` in BOTH modes; the allowance exists only in
a disposable local Chrome profile. This is not a corporate GPO override/test.
`workspace-usability.cjs` also passes paging, page size, persistent ordered layout,
reset isolation, long text, empty states and 720 CSS-pixel reflow. Its normal
automation popup settings are NOT evidence of policy-respecting bulk opening.

Published local Demo API/UI only, synthetic InMemory Resources/Access/Audit; no new
SQL run was needed for these UI-only contract-preserving changes. Prior isolated
SQL ownership/merge/audit evidence is unchanged, not rerun evidence. No real VDI,
native 200% zoom, screen reader, OIDC, corporate target authentication or deployment
acceptance is claimed. Evidence is under
`C:\SecureOpsBuild\validation\resources-opening-20260910\evidence`: before/after
`*-prepared-1440.png`, `*-prepared-390.png`, group conflict/refreshed/personal-group
captures and `after-opening-results.json` / `workspace-results.json`.
No API/OpenAPI/schema/grant/configuration/dependency change. Corporate external-write
defaults and SDM eligibility remain unchanged. Before a later package decision,
the operator must inspect the actual deployed source/version and effective site
popup policy without reopening all already-open tabs. In Use waiting age/dashboard,
persisted RFC ownership and controlled upload/completion/reconciliation remain
active LOCAL work; the combined milestone is NOT complete.

### Shift Set Journey Pass, 2026-10-01

UI-only (Claude), branch `claude/quirky-goldberg-1uxpt7` from master `e670617`. Files: `Pages/Resources.razor`,
`Pages/ShiftSets.razor`, `Shared/Components/ResolvedResourceLinks.razor` (each with a new scoped `.razor.css`) and
`Services/ResourceView.cs`. No API, DTO, SQL, Worker, policy, NavMenu, theme or `_Host` opener change.

- **Find.** The result head states total, visible range and page (`30 bağlantı · 1–25 / 30 gösteriliyor ·
  Sayfa 1 / 2`) from the server's own paging, and a "Listeyi daraltan" line names each active filter. An empty
  filtered result offers Filtreleri temizle as well as Tüm bağlantılara dön. Favouriting reports what changed.
- **Add to a personal group.** The confirmation names the link (or counts several) and the group the server
  confirmed, with a **Grubu aç** link to `/resources/sets?set={id}`; that page selects the group on arrival
  (`?set=` is only a selection hint; an unknown id falls back to the usual default).
- **Understand what will open.** Each group shows its link count. Before preparing, the page says how many links
  are saved and that tabs open only on the operator's own click. The prepared list is numbered in opening order
  with each destination host. Saved links the operator could see but the resolution did not return are named
  under `PartialSetNotice`, without address or anchor, so they can never be part of the batch. Hidden members
  remain unnamed and uncounted, as the contract requires.
- **Open.** Unchanged: one native `N bağlantıyı aç` click through `window.secureOpsLinks.openMany`; the status
  says opening was *requested*, never that a tab or destination opened.
- **Feedback.** Create, rename, delete, default, reorder and remove each leave a `role=status` sentence. Preparing
  reads "Erişim denetleniyor…". Returning to editing after a resolution that left links out re-reads the group,
  so a reorder is not built from a link that is no longer visible (previously the server correctly rejected it
  with 404 and the page recovered with an error).

**Verification (Linux).** The committed `global.json` pins SDK 9.0.317, which this host could not install, so a
throw-away copy changed only `global.json` to the installed SDK 10.0.112; `LangVersion` stays 12.0 and no source
was edited. There the full solution builds with zero warnings/errors. `ResourceViewTests`, `ResourceWorkspaceTests`,
`ResourceExperienceTests` and `ResourceApiClientTests` pass 62/62 (13 new) and all `SecureOps.Tests.Unit.Ui` tests
pass. Full unit: 1,607 passed, 3 failed; master in the same setup: 1,594 passed, the same 3 failed (the toolchain
contract, which rejects the substituted SDK, plus two platform-dependent tests). Integration: the same 7 failures
as master (Windows DPAPI/SkiaSharp). No result is a Windows or corporate TEST pass.

`tests/browser/resource-shift-journey.cjs` passes against the local Demo API with synthetic InMemory data (real API
code, not a stub) and Playwright's bundled Chromium: search, favourite, create-and-save, save-by-selection, Grubu aç,
archive-then-prepare with the left-out link named, keyboard activation of prepare, one-click batch open (attempt
message only), reorder/remove feedback, 1440/390 widths and a 720 CSS-pixel viewport at device scale 2 as a 200% zoom
approximation, with no horizontal overflow and no page errors; a dark-appearance capture was checked by eye.
Playwright's default popup handling is used, so its popup count is not evidence of browser policy.
`workspace-usability.cjs` and `resource-experience.cjs` now read only the total from the richer status line; on this
Linux host they, and `resource-ui.cjs`, stop at the same step on master as on this branch (selection text, first-use
guide and a removed page-size button respectively), so they were not usable as regression gates here. Windows/IIS,
native browser zoom, managed popup policy and screen readers were not tested.

**Backend contract notes (recorded, not implemented):** G-31 (no usage signal for frequently used links) and
G-32 (favourites view has no server-side paging) in `docs/26-ui-backend-contract-gaps.md`.

### In Use V1 Canonical Handoff, 2026-09-08

#### Recovery And Local Acceptance Closeout, 2026-09-14

**Latest local status: ACCEPTED for the bounded In Use journey; not release readiness.**
This subsection supersedes older remaining-work statements below for recovery and
local acceptance. The RFC mapping was already implemented before this continuation:
`SET.c_rfc_record` with exact referenced OR resolution and `KEY.p_rel_requester`
display; `SET.p_rel_requester` remains reference evidence. The September 11
"runtime mapping not implemented" statement below is historical, not current.
Reporter identity still does not imply ownership or Virtual PC User.

Recovered starting HEAD: `81f5757a869442ae92b0c90a3d7dfebab9796d6d`, branch
`feature/sql-runtime-hardening-20260902`, registered checkout
`C:\Users\dmtak\Desktop\Yeni Otomasyon\secure-ops-repo`. It contained only the
pre-existing untracked `.vscode/launch.json` and also served the protected release
tip. One continuation worktree was created from that exact HEAD:
`C:\SecureOpsBuild\secure-ops-inuse-local-acceptance-20260914`, branch
`feature/inuse-local-acceptance-20260914`. Historical SHAs were checked for ancestry,
never reset/cherry-pick targets. No other writer or parallel implementation was used.
The commit containing this subsection is the source closeout; fixed-start change
accounting includes tests/docs and deletions, without reusing an earlier exception.
Scope: 668 changed lines (+634/-34), 9 files; no scope exception.

Already implemented and retained: refresh/search/filter, stable application-ID
assignment/reason, per-server three-answer editing, explicit bulk differences and
confirmation, persisted reopening, preview/archive/download, conflict comparison,
optional tour and trusted reporter/profile display. The review capability does
not require assignment to the caller: cross-assignee review remains permitted by
contract. Missing capabilities, stale versions and audit failures remain rejected.
No API/schema/source mapping, ownership, production limits, dependencies or visual
design changes were needed. OCO and OR-to-SDM worktrees/data/hosts remain untouched.

Corrected actual gaps: identical access revalidation and same-record reload no
longer silently discard local answers or assignment intent; obsolete API results
and errors cannot replace newer filter/access state. Assignment-only conflict
recovery retains identity/reason without overwriting newer saved answers. Commands
disable editable controls while pending. Unsaved navigation can be canceled, with
the existing shell progress indicator cleared. Authorization loss clears protected
state; forced session reauthentication bypasses the unsaved-navigation guard.
503/transport failures preserve recoverable edits without automatic write retries.

Evidence root: `C:\SecureOpsBuild\validation\inuse-closeout-20260914` (private,
outside Git). Fresh supported LocalDB harness applied inventory 001-013 plus its
upgrade checks to `SecureOps_ResourcesV1_inusecloseout20260914` on
`(localdb)\SecureOpsResourcesV1`; no other database or migration package was replayed.
`Start-Local.ps1` records actual process-only composition: Demo auth/access,
SQL Access/Audit/SessionSecurity/OperationalRecords, synchronous audit, Mock identity
and PAM, OIDC off, paired Simulation source/Jira, controlled writes/source close off.
`ReadOnlyIntegrationMode=false` belongs only to this supported Demo/Simulation
profile. The shipped Test+TuruncuHat+Corporate read-only validators/defaults were
not changed. The historical incompatible Demo+read-only attempt was superseded by
rc6.17 loopback-adapter evidence; neither it nor this run proves corporate access.
Task ports: API 64041, UI 64042, test-owned fault proxy 64043; only task hosts stopped.
Database, synthetic reports and private DPAPI keys are retained outside Git.

Actual checks, seconds are command wall time unless stated otherwise:
- Final Release solution build: PASS, 0 warnings/errors, 5.965 s.
- Normal solution regression: 1,240 unit + 243 integration PASS, 11.409 s;
  20 explicit SQL opt-ins skipped because the normal process had no SQL connection.
  `skips-and-optins.json` records every identity/reason and its separate PASS:
  all 20 SQL tests, 3.253 s, zero remaining SQL skips. Fresh harness: 2.755 s.
- Normal `Test_OpenApiDocument_MatchesCheckedInUiContractSnapshot`: PASS;
  `SECUREOPS_UPDATE_OPENAPI` unset; no snapshot regeneration.
- After scoped line-ending normalization, 21 recovery/manual-clock session cases
  PASS (12 recovery + 9 session), 2.645 s. Idle and absolute expiry use existing
  manual-time tests, not changed production timeouts. Full regression predates
  only that whitespace normalization; no duplicate full suite for documentation.
- `browser-recovery-accepted/result.json`: PASS, 7.450 s inside browser harness.
  Actual refresh through SQL, reason/answer 503 and transport, held save/list,
  real assignment conflict, canceled navigation, explicit bulk, persisted reopen,
  per-server exception, preview/download/archive, repeated persisted 403 and actual
  server session revocation to reauthentication while edits were unsaved.
- `browser-reporter-accepted/result.json`: PASS, 5.815 s. Existing fake-HTTP
  adapter/SQL fixture opt-in PASS (1 case, 1.841 s); four-server stored journey,
  shared/different/null RFC, retained denied evidence, trusted reviewer labels,
  single-pass display, real answer conflict accepted/saved, reopened download/archive.
  `workbook-verification.json` confirms four server columns/NMS rows and retained
  review evidence. This complements, not replaces, the adapter-to-SQL regression.
- Browser screenshots/keyboard checks cover 1440x900 and 390x844, light/dark,
  labels, missing-answer focus and tour reopening/focus return/nonmutation.
  Failed intermediate attempts remain in evidence, separate from the accepted runs.

Mandatory full `dotnet format --verify-no-changes --no-restore --report <path>`:
exit 2 at baseline (37.833 s) and final (34.440 s). Both have the same 201 unique
diagnostics in 58 files, compared by path/line/column/ID/full message, including
unfixable console diagnostics. All introduced line-ending findings were fixed;
`format-accepted-comparison.json` has zero added/removed diagnostics. The shared
full-format gate remains unresolved and is not waived by this local acceptance.

`final-tested-payloads.json` binds 430 payload/static files and
`final-tested-source.json` binds 478 non-document source files. API DLL SHA-256:
`4630AB5C6E45D489EBD23A3917EB84BEEE3C9D1323C7D0CA6DE8B6E4870954E2`;
UI DLL: `AD94B6664061C9E4497753302F896E07F41A8043B62DAF4766B152A833A19E72`.
The pre-commit build embeds ProductVersion `0.1.0+81f5757...`; content hashes,
not that embedded base SHA, establish the tested changed source. Historical rc6.17
still identifies build `f519b5022760e71cc3a7edf6b3c87f3b7c943dce`; all three archive
hashes match its existing manifest read-only. It does not contain this continuation.
Cached upstream remains `5692c77fbab0aea542d6408dfc1756ee8d965140`; the new branch
has no upstream. Current remote state was not queried and no push is claimed.

Remaining external gates: corporate source/AD/OIDC and destination authentication,
managed-browser popup policy variants, actual screen reader/VDI and shared full
format. In Use currently has no configured source navigation link, so no destination
popup claim is made. No package, push, deployment, main merge or branch cleanup.

#### Display Acceptance Correction, 2026-09-11 (Source Only)

Scope: 19 files, 584 changed lines (+536/-48); no scope exception.

Start: `428160968c5309c89cd1267616b5444b017b2220`, expected feature branch,
only pre-existing `.vscode/` untracked. rc6.15 remains unchanged and does not
contain this fix. No release/deployment, corporate call, SQL repair or auth change.

Causes and corrected boundaries:
- Assignees previously projected CorporateIdentity directly. The existing
  security.Users DisplayName/LoginName profile now supplies picker and assigned
  record labels. Duplicate/missing names get a collision-checked application-ID
  suffix; IDs, eligibility, assignment concurrency and actor audit are unchanged.
  Reads batch existing local access records, never AD/OIDC per row. Old stored
  assignment labels are refreshed in the read projection, not repaired in SQL.
  Missing profiles use the existing successful OIDC login/profile-upsert path;
  the current-user header is not evidence of another user's saved profile.
- Root title/description were already HtmlDecoded; requester and service fields
  were not. InUseDisplayText decodes those display values ONCE at UI/XLSX output,
  including old persisted rows. Raw source snapshots, IDs, hashes and historical
  notes remain unchanged. Razor escapes plain text; XLSX uses inline strings and
  formula protection AFTER decoding. No raw HTML rendering. Existing archived
  XLSX stays byte-identical; save a new draft version for a corrected new report.
- Parent requester is now `In Use kaydındaki talep eden`, never RFC owner.
  `Sunucu Kullanıma Alma Kontrolleri` retains three questions, explicit bulk
  differences, exceptions, optional secondary assignment and source-version
  checks. Technical evidence is collapsed under the same authorized source view.
  Specific Turkish missing-answer messages name server/question and restore focus.

RFC and Virtual PC User are **not requested / runtime mapping not implemented**:
the real selects are still the verified 15 legacy fields. Neither field appears
in the 27-cell evidence, parser projection or persisted runtime field dictionary;
DTO forwarding does not lose them. The UI now says Sorgulanmadı instead of implying
an empty source value. Null returned evidence, omitted cells and retained prior
values remain distinct. No RFC owner, Reporter, creator or affected-asset mapping
was invented. Optional collector ReporterProperty no longer blocks the exact
requester-only hop; tests prove deduplication and closed/out-of-scope exact lookup.
That diagnostic is not yet a persisted runtime ownership implementation.

Next single evidence action: inspect only the RFC and Virtual PC User controls
on one explicitly related Service Item, per `scripts/diagnostics/InUseEvidence/README.md`.
No supported metadata/detail endpoint is established; do not guess one. Return
two bounded sanitized candidate-attribute fragments, not page HTML/HAR/values.
Then verify exact selectors and SET/KEY identity-or-code meaning with the existing
protected-config collector. Old `6e05b45` cannot accept the newer RFC-hop option.

Verification: Release solution and standalone collector builds passed, zero
warnings/errors; 99 focused unit, 28 hosted API/OpenAPI/access tests and 6 actual
isolated In Use SQL tests passed (fresh 001-013). Scoped formatter passes excluding
pre-existing IDE1006 naming diagnostics. No API schema/dependency change.
Published local Demo/Simulation browser: same synthetic stored system requester,
encoded Turkish/markup text, missing/duplicate OIDC profiles, long titles and 2/4
servers; list/review/preview, bulk exceptions, incomplete draft/focus, authenticated
archive, direct API denial and tour non-mutation passed at 1440/390, light/dark.
Existing full In Use journey also passed conflict/edit preservation and pagination.
One old assertion required the new server-name error text; one new browser search
ran before interactive readiness and passed after the existing busy-state barrier.
Neither is claimed as corporate/VDI acceptance. No real VDI test was performed.

Evidence: `C:\SecureOpsBuild\validation\inuse-display-20260911\browser\before`,
`after-final`, `regression-final` (screenshots, results and synthetic XLSX).
The initial `after` run additionally shows the named assigned reviewer before
the optional unassignment journey. Local isolated database:
`SecureOps_ResourcesV1_inuse_display_20260911`, retained. No new migrations,
grants, settings, scheduler, external-write capability or retention policy.
Runtime schema remains 001-013; private report/DP prerequisites remain as rc6.15.

#### Stored Overview And Local Completion Journal, 2026-09-10

Start: `4fc1f80d632a89d2f06e3640512afcce82c7cf56`, same feature branch. This
explicitly resumed increment preserves Resources, `.vscode/` and every old archive.
It delivers stored waiting/management views and a blocked local completion journal;
it is NOT corporate ownership resolution or executable source closure.

Full increment: 934 changed lines (927 additions, 7 deletions), 23 files.
The permanent 1,000-line cap is respected; no old exception is reused.

List/detail use parent In Use OR creation evidence only. The real adapter has no
verified creation/lifecycle wire mapping and leaves both unknown; active 4241/68
query membership is not an open-state contract. New records retain WASAS first-seen
time separately. Existing records are not backfilled from last refresh. Absent new
source fields are omitted from JSON so unchanged legacy hashes/drafts remain valid.
No SLA threshold is introduced. Oldest records require both explicit Open evidence
and a valid non-future offset-bearing parent creation date; referenced RFC dates
are never used. Unknown/closed records cannot enter the confirmed-open count.

GET `/api/v1/in-use/overview` requires InUse.View AND Reporting.ManagementView,
also enforced by the service. It reads a consistent stored SQL snapshot and audits
before release. Counts are per OR, not per server/owner. AwaitingAnswers/ReportReady
partition all retained ORs by current local structural readiness, not corporate
open status or approved-template acceptance. Only counts and up to five lightweight
oldest-record links reach the browser. Last successful refresh, existing stale and
partial flags are shown. The implementation aggregates stored JSON on the API host;
large accumulated inventories have not been benchmarked. Opening/reloading the
panel, list, detail or preview never calls the source.

An approved InUse.View + InUse.Review actor can preview, privately archive, then
confirm the exact current version/hash through POST `/{id}/completion-intent`.
Assignment remains optional. Server-side checks require all three answers for
each server, acceptable observed relationships and a valid immutable archive.
Actor comes from authenticated WASAS identity; browser input contains no actor.
The command ID, actor, source/report versions, hash and time persist with atomic
audit and optimistic concurrency. Replay cannot duplicate the intent. Confirmation
always saves Blocked, never grants external-write authority or uploads anything.

The local result journal and transition reducer distinguish upload failure,
ambiguous result/reconciliation, unique authorized task lookup, task failure,
task success awaiting OR read, task completed with OR still open, and verified
closure. Result journal writes use existing version/audit protection and the
original authorized actor. There is deliberately no HTTP result-ingestion endpoint
or production executor; pending states are seeded ONLY by synthetic tests. No
production transition leaves Blocked, and failed/uncertain states cannot retry writes.
The UI displays separate messages/remote references, and flags superseded evidence.
The three-question/bulk-difference flow, optional notes and private historical
downloads remain. Missing answers save as drafts; `Alana git` focuses the exact
server/question. No new mandatory operator input or completion activation setting.

Schema remains 012. Existing JSON aggregates and command/audit tables store this
increment: ops.InUseRecords SELECT/INSERT/UPDATE, ops.InUseRefresh SELECT/UPDATE,
existing command SELECT/INSERT/UPDATE and append-only audit INSERT. No migration,
new grant, DDL permission, role/configuration change or package. Existing
InUseReports:Directory private archive configuration still applies. Corporate
ReadOnlyIntegrationMode=true, ControlledTestWritesEnabled=false and
SourceCloseEnabled=false remain unchanged; .NET/UI dependency versions unchanged.
Older binaries may drop additive JSON metadata when rewriting aggregates; downgrade
write compatibility is not asserted. No rollback/deployment was performed.

Verification: Release build 0 warnings/errors; 132 focused InUse/SDM/write-fence
unit tests; 10 hosted API/OpenAPI/Swagger tests; 17 approved isolated SQL tests.
SQL: `SecureOps_ResourcesV1_inuse_progress_20260910`, migrations 001-012.
Published local `in-use-workspace.cjs mapped` and `in-use-progress.cjs` passed:
four independent servers, unassigned review, explicit bulk-copy differences,
missing-answer keyboard focus, immutable archive/preview, stored dashboard and
blocked confirmation. 1440x900/390x844, light/dark, no overflow/page errors.
Screenshots/results: `C:\SecureOpsBuild\validation\inuse-progress-20260910\browser`.
The wider historical `after` journey timed out on the unrelated Operational Records
refresh notice; it is NOT claimed passed here. Previous conflict/tour/outage
evidence above remains scoped prior evidence. No actual VDI, corporate OIDC,
corporate API/SQL, deployment, source write, scheduler or release package validation.
The local UI host also logged an antiforgery token-decryption failure during reruns;
passed journeys do not establish persistent-key or corporate-session acceptance.

Remaining: exact RFC dictionary/reference kind and Reporter selector, then persisted
server-to-request owner enrichment/candidates; verified parent creation/open-state
mapping; corporate XLSX acceptance/required-unknown policy; upload result/idempotency,
unique task-to-OR authorization and authoritative final-state response contracts.
The real executor, read-only reconciliation and separately authorized activation
still require implementation after those contracts, not merely configuration.
Next single evidence action: source owner completes the RFC/Reporter contract in
`scripts/diagnostics/InUseEvidence/README.md`, then one bounded selected-OR run
returns ONLY Evidence. Virtual PC User remains independent and optional. SDM/Jira-only
eligibility/activation remains a separate continuation, never gated on In Use.

#### Optional Review And Bounded RFC Evidence, 2026-09-10

Start local/live HEAD: `fb84053a26025cbd3f77ad550fc3c6c4821ef813`, same feature
branch. This is an independent implementation increment, NOT the entire requested
ownership/dashboard/completion milestone. The permanent 1,000-line task cap remains;
the prior V1 exception is not reused or concealed through multiple commits.

Full increment diff: 692 lines (663 additions, 29 deletions) across 23 files.

Approved InUse.View + InUse.Review actors now save version-protected drafts without
assignment, including when another reviewer is assigned. Assignment remains optional
and separately capability-protected. Audit/ReviewedBy/PreparedBy use the authenticated
application identity, never browser names or the transport account. No role/Resources
ownership change. The three questions, recipient/diff confirmation, exceptions and
private archives remain. Three labelled steps show answered-server progress; optional
assignment stays collapsed and cannot discard dirty answers. Explicit stored-record
comparison after a conflict/outage preserves local answers. Confirming the comparison
rebases matching server identities; new servers start Unknown. No corporate refresh,
silent save retry, automatic assignment or mutating tour navigation is involved.

Refresh acquires a non-waiting 4241:68 scope lock before discovery. SQL uses
sys.sp_getapplock with Transaction ownership on a dedicated connection, shared by
all API instances. Disposal/rollback releases it; no inventory/state row locks are
held during source requests, so stored reads continue. Command IDs/audit are retained.
No schema/grant/configuration delta: schema 012, ops.InUseRecords SELECT/INSERT/UPDATE,
ops.InUseRefresh SELECT/UPDATE and existing audit/command/access grants remain.
PUBLIC application-lock access was verified with an isolated no-login user.

The existing standalone collector gains optional --rfc-contract; see its README.
The source owner must supply exact RFC property, SET/KEY reference cell, identity/code
meaning and separate Reporter selector. Direct references are deduplicated and read
without an active/catalogue/group filter on the referenced OR. Closed/out-of-scope
requests work in synthetic tests only; no corporate keys/ownership are verified.
No recursive RFC traversal, inferred owner or persisted assignment. The complete
legacy script remains unchanged: 742 lines, same documented hash, zero parse errors,
never executed/copied. It proves neither the RFC contract nor final OR verification.

Verification: 162 focused unit tests; 26 integration/OpenAPI/Swagger tests including
16 isolated SQL tests. Published local SQL/Simulation mapped/after journeys passed
at 1440x900/390x844, light/dark: unassigned review, bulk exceptions, validation focus,
archive, conflict comparison/recovery, tours, pagination and Resources ownership.
Screenshots/results: `C:\SecureOpsBuild\validation\inuse-workflow-20260910\evidence`.
DB: `SecureOps_ResourcesV1_workflow20260910`. Demo/Simulation uses its required
fake read-only-mode=false profile; no corporate provider or write is selected.
Corporate defaults/fences remain unchanged. No actual VDI/OIDC or corporate test,
deployment, scheduler, release package or old archive modification occurred.

Remaining LOCAL implementation: persisted RFC owners/exact identity candidates;
source creation/status mapping, waiting age and In Use dashboard; durable completion
intent, upload/task execution/reconciliation and authoritative final-state reread.
These are not delivered activation switches. The full combined milestone requires
a new scoped size authorization or separately agreed narrower tasks. No dashboard
screenshot or uncertain-completion test is claimed. Corporate template acceptance,
required-unknown policy, upload result/idempotency, unique task-to-OR relationship,
authorized transition and final-state contracts remain pending. All completion
steps stay closed. .NET 10 compatibility remains separate; no upgrades were made.

Next evidence action: obtain the exact RFC/Reporter dictionary contract, then run
one approved selected-OR collection and return ONLY Evidence. Virtual PC User is
optional and unmapped; do not return credentials, raw payload or Windows actor data.
Old rc6.14/6e05b45 collector artifacts are preserved and do not include this change.
SDM/Jira-only policy, implementation, evidence and activation remain independent.

#### Supported Service-Item Mappings, 2026-09-10

Starting HEAD: `6e05b454b6e93fdb9e3383ae6dd2f073847c2376`, branch
`feature/sql-runtime-hardening-20260902`. Operator evidence confirms one root,
four service-item rows and 27 semantic cells per row; the exact mapping and
attachment hash are in `docs/integrations/turuncu-hat-jira-legacy-parity.md`.
Aliases are equality evidence only, not corporate identities or business policy.
No attachment, Windows actor SID or corporate values were committed.

Explicit authorized refresh now enriches the existing bounded In Use scope
using the original 15-select relation query: at most 100 roots, 10 service items
per root, 64 KiB per related response and 45 seconds overall. Stored list/detail
reads never query the source. Semantic keys replace positional assumptions.
Inventory ID, hostname, type, environment, company, service, network, IP, OS/
version, directorate, building/city, category and each server's own service ID
flow into persisted fields, existing read-only UI/evidence and legacy XLSX cells.
KEY display and SET reference values remain separate; no display-name assignment.
The legacy directorate column is not an individual service/application owner.

Observed means mapped rows with unverified completeness, including observed zero.
Duplicate/conflicting cells or identities reject enrichment. Missing optional
fields remain Unknown; previously known fields/rows omitted by a later result
are retained with Partial/stale provenance, not silently deleted. Refresh/source
changes invalidate the draft while preserving answers, notes and the reviewer.
Observed nonempty records support a local archived report after the same three
answers, version and authorization checks; global completeness remains visibly
unverified in provenance. Partial/failed/ambiguous relationships cannot archive
as current. No approval, closure, upload or completed monitoring is implied.

Virtual PC User, RFC, technical creator, service-item status and Affected Assets
were not queried and remain unknown. Functional aspect and individual ownership
joins remain unresolved. To add ONLY Virtual PC User/RFC, obtain each label's
approved direct LCSIMS_ServiceInstance property key, type, null/cardinality,
reference target and sanitized KEY/SET structure. Existing standalone dictionary
supports those two labels only; do not guess keys or follow RFC targets.

No migration, grant, configuration, dependency or release-artifact change.
Schema 012 and all external-write fences remain unchanged. Verification: 148
focused unit tests; 25 integration/OpenAPI/Swagger tests including 15 isolated
SQL tests; Release solution build zero warnings/errors. Published loopback
journeys `mapped` and `after` passed at 1440x900/390x844, light/dark, including
four differing service/environment rows, three-answer bulk confirmation, XLSX
archive download and existing conflict/authorization/tour regressions.
Evidence: `C:\SecureOpsBuild\validation\inuse-mapped-20260910\evidence`.
SQL: `SecureOps_ResourcesV1_mapped20260910` on approved isolated LocalDB.
This is source/local verification, not deployment or corporate acceptance.
rc6.14 and standalone collector delivery remain unchanged. SDM is independent.

#### Operator Effort And Report Retention, 2026-09-09

Start local/live source: `38eee722dbb18619ca6d8eb9a05a58a4afb16112` on
`feature/sql-runtime-hardening-20260902`. This follow-up is implemented and
locally verified, not deployed or release-packaged. rc6.14 below is unchanged
and does not contain this follow-up. The scoped V1 line-limit exception was not
reused; the complete follow-up is reviewed as one diff under the permanent cap.

The old editor exposed nine answers and nine evidence inputs per server, plus
one optional general note. Normal review now requires only three answers:
outbound internet, inbound internet and microsegmentation. Select one server,
answer it, explicitly select recipients, preview changed fields, confirm, then
edit exceptions. For four identical servers this is three answer selections
plus three recipient checkboxes and preview/confirmation, not 36 answers and
36 evidence inputs. No new evidence/general note is required. Historical notes
remain read-only and stored; bulk copy never copies source facts or notes.
Incomplete drafts save; unknown/missing answers prevent archive readiness with
server/field-specific Turkish validation and focus. Failed saves preserve edits.

Operational Records retains its authoritative requester list mapping. In Use
shows source Virtual PC User evidence separately from the manually assigned
WASAS reviewer. Real Virtual PC User/RFC keys and identity joins remain unresolved;
synthetic fixtures are not corporate proof. No source-person assignment candidate
is offered without exact identity/capability evidence. The bounded standalone
collection command and two-field dictionary prerequisite are in the canonical
TEST runbook; neither UI OIDC tokens nor UseDefaultCredentials are assumed.

**Storage:** configure API `InUseReports:Directory` (environment override
`InUseReports__Directory`) to a private absolute directory outside published
content/wwwroot. Default is unconfigured: preview/draft still work, archive
requests fail closed. Grant the API runtime identity only required directory
read/create/write/rename access, with no public static mapping; provision ACLs,
capacity monitoring and backup separately. Reparse-point paths are refused.
Archive files are `<record-guid>/<version>.json` atomic envelopes containing
the XLSX and OR/source-version, actor, timestamp, size and SHA-256 metadata.
Same-version requests return the original bytes/provenance. Historical versions
remain downloadable only through View+Review authorization. No automatic deletion;
`.lock` files and at most one uncommitted `.pending` per version may remain.
SQL audit records authorization before file commit, not a false successful write;
filesystem and SQL are not one distributed transaction. No schema/grant delta:
012, ops.InUseRecords SELECT/INSERT/UPDATE and ops.InUseRefresh SELECT/UPDATE remain.

Assignment/review/report actors are stable application user IDs resolved from
authenticated access (OIDC issuer/subject in that profile), never browser actor
names or the integration service account. Future separately approved closure
must retain this actor, durable command ID, immutable report hash/version and
remote request/result references, followed by authoritative state verification.
No source-history human attribution is claimed without a supported contract.

Verification: 85 targeted unit tests, 10 hosted API/OpenAPI/Swagger tests and
14 isolated SQL tests passed. Published local SQL/Simulation journeys passed at
1440x900 and 390x844 in light/dark themes, including bulk difference preview,
mobile exception editing, missing-answer focus, conflict preservation, archive
download, pagination and non-mutating keyboard tours. Evidence and before/after
screenshots: `C:\SecureOpsBuild\validation\inuse-effort-20260909\evidence`;
retained synthetic DB: `SecureOps_ResourcesV1_effort20260909`. Storage failure,
audit rollback, repeated versions and unauthorized download have focused tests.
Corporate OIDC/ACLs/SMB durability, relationship mapping and Excel template
acceptance are not validated locally. No corporate calls, deployment, scheduler,
Jira creation, upload or source/BPM mutation. SDM remains an independent policy/
implementation/evidence/activation continuation, not blocked on In Use acceptance.

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

#### OR To SDM Local Acceptance Closeout, 2026-09-15

This is the current OR-to-SDM continuation; dated delivery notes below retain
their original scope. Start: `5298d5639b148f2a0d343063323efc8633520813`, branch
`feature/or-sdm-local-acceptance-20260914`, worktree
`C:\SecureOpsBuild\secure-ops-or-sdm-local-acceptance-20260914`.
No newer dedicated OR branch/worktree or second writer was found. The protected
original remains `81f5757` with its untracked `.vscode/`; accepted In Use and OCO
worktrees remain unchanged. No merge, push, packaging, deployment or corporate
call. Final commit/status and fixed-start line accounting are recorded in
`C:\SecureOpsBuild\validation\or-sdm-closeout-20260914\final-git.json`.

Already implemented: explicit source refresh versus stored search/filter/paging,
three review declarations, source work/outcome and missing-fact guidance,
source/version-bound preview, exact requester/reporter resolution, capabilities,
claims, SQL idempotency/audit, durable Jira-only intent and reconciliation blocks.
The exact server-owned positive **ServerRequest** policy exists; this is not an
absent implementation. SoftwareInstallation (ApplicationInstallation in business
wording) and ServerRetirement remain mapping-blocked; no SunucuTalep fallback.
In Use reporter, reviewer, actor and cross-assignee semantics are unchanged.

Corrections: detail access notifications now revalidate the actual capability
snapshot. Record changes and access revocation invalidate obsolete reads/reviews/
commands; delayed results cannot repopulate old state. Same-access revalidation
and recoverable review errors retain the declaration. Route parameter changes
load the requested record. The existing fixed Simulation happy fixture alone is
now ServerRequest, so its confirmation names the actual synthetic request type.
No new policy, approval subsystem, dependency, schema or corporate flag was added.

Local acceptance is complete for these supported paths, not corporate publication
readiness. Two deliberately separate proofs are retained: the numeric exact-record
policy runs with real draft/SQL services and test-only source/Jira/identity
substitutes; the published browser uses paired Simulation's fixed allowlist.
Simulation is not a corporate policy attestation. Its identity defaults are not
evidence of authenticated corporate reporter acceptance.

Evidence root: `C:\SecureOpsBuild\validation\or-sdm-closeout-20260914`.

| Executed gate | Result | Command seconds |
|---|---|---:|
| Release build, `build-accepted` | 0 warnings/errors | 3.653 |
| Normal regression, `regression-final` | 1247 unit + 243 integration passed; 21 SQL opt-ins skipped | 7.938 |
| Fresh SQL harness, `sql-harness-final` | 001-013 and upgrade preservation passed | 2.235 |
| Separate SQL opt-ins, `sql-final` | 21 passed, no skips | 2.800 |
| Post-normalization SDM unit checks, `focused-accepted` | 75 passed | 2.129 |
| Published journey, `browser-final` | Passed | 12.360 |
| Actual host restart, `browser-restart-accepted` | Passed | 2.874 |

Normal OpenAPI comparison passed without snapshot-update opt-in. Regression also
includes deterministic idle/absolute session-clock and unknown transport-result
tests. Six new detail-handler cases cover delayed success/failure, 503/network
review preservation, revoked access and route changes. SQL additionally proves
exact positive policy/evaluation audit, real source fingerprint rejection,
read-only create rejection, one key, restart/replay and no close-intent upgrade.
Existing claim/concurrency, audit rollback and interrupted-key-persistence cases
passed. `skips-and-optins.json` maps every normal SQL skip to its executed result.

Browser: explicit UI refresh; stored search/sort/paging without workflow changes;
all three declarations; ReadOnly/Operator API denial; real SQL version conflict
with preserved selection; confirmation focus/cancel/double-click; one simulated
key; source-open result; unknown outcome with no retry. At actual restart, stored
views reopen without source refresh, the known key replays, and a new unknown-case
command is rejected with only one persisted create request. Desktop 1440x900 and
mobile 390x844 screenshots have no horizontal overflow. Initial restart assertion
targeted an old badge inside collapsed details and failed in 33.267 s; the corrected
visible outcome assertion passed. No product change was needed for that failure.

`final-tested-source.json`, `final-tested-api.json`, `final-tested-ui.json` and
`final-content-verification.json` bind final source/payload files by SHA-256.
API DLL: `004CD028BDEC378A04575103C526FCA94EA7A07800883D86C0559FAFE6FB2CAE`.
UI DLL: `72D135F17112E9E0CBCDAEFE7B734368A9CB19333EC22BE0C94864CD016854B0`.
Both embed base ProductVersion `0.1.0+5298d5639b148f2a0d343063323efc8633520813`;
it is not the final commit identity. Full browser confirmation preceded only
C# line-ending normalization; rebuilding changed Infrastructure DLL/PDB hashes,
not API/UI DLLs. The final 75 focused checks, restart and fresh-owner preview ran
on the rebuilt payload. Earlier manifests and the exact four-file difference are
retained rather than silently reassociated. No full-suite repetition for docs.
rc6.17 remains the historical `f519b50` package, not this source or walkthrough.
This branch has no upstream; cached original upstream is `5692c77`, not live remote
verification. No push was attempted.

Mandatory full format: exit 2 in 33.783 s. Actual start/final reports contain the
same 123 distinct location/ID/message diagnostics in 58 files, zero introduced or
removed. `format-comparison.json` and identity lists preserve the comparison.
The initial baseline wrapper failed to record elapsed time; it is not fabricated.
Scoped new C# formatting and diff checks pass. Shared full-format debt remains an
unwaived combined-delivery gate, not release readiness.

**Owner walkthrough:** running UI `https://localhost:64152/operational-records`,
API `http://127.0.0.1:64151/`; only task-owned processes are retained. Current PIDs
and guarded stop instructions are in `hosts.json` / `Stop-Local.ps1` in the evidence
root. `browser-owner-ready/walkthrough.json` gives the verified direct record URL.
This separate fresh database is `SecureOps_ResourcesV1_orsdmwalkthrough20260915`;
preview/confirmation cancellation was verified without consuming its create.
Sign in with the existing Demo button, open **SIM-OR-100**, leave the declaration
unset (it is not approval), select **Jira Taslağını Önizle**, inspect **Sunucu
Talebi** and source-open notice, then **Jira Kaydı Oluştur > Oluştur** once.
The result is explicitly simulated. Reopen to see the stored key; never reset
completed/uncertain rows to replay. Use only this one-record fresh walkthrough.
The acceptance result **SIM-1** remains separately preserved in
`SecureOps_ResourcesV1_orsdmfinal20260915`; the first database is also retained.

Start/restart the same tested payload in two foreground PowerShell terminals:

```powershell
& C:\SecureOpsBuild\validation\or-sdm-closeout-20260914\Start-Local.ps1 -Role Api
& C:\SecureOpsBuild\validation\or-sdm-closeout-20260914\Start-Local.ps1 -Role Ui
```

Run one command per terminal. Stop only these hosts first; default dataset is
Walkthrough. To inspect accepted history instead, start API with `-Dataset Accepted`
and reauthenticate. Launcher uses process-only Demo auth/access compatibility,
Mock identity/PAM, paired Simulation, SQL Access/Audit/Session/OR, synchronous
audit and private FileSystemDpapi rings. ReadOnlyIntegrationMode=false is required
only by this supported local composition; ControlledTestWritesEnabled=false and
SourceCloseEnabled=false remain set. Corporate validators/defaults are unchanged.
No production session limits or source writes are changed. Actual settings are in
the private launcher; no credentials, keys, traces or test data enter Git.

Next TEST action and missing inputs: the current section of
`docs/24-api-test-deployment-readiness.md`. Managed browser/GPO, actual screen reader,
VDI, corporate authentication, current Jira permissions/mapping/identity and
authoritative remote duplicate correlation remain external acceptance, not local
passes. BPM/source closure is separately blocked. Combined source integration,
shared formatting and a newly identified paired TEST package are later tasks.

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

## Service Accounts multi-account scan upload (2026-10-06)

On `/service-accounts`, selecting accounts (Work capability) offers "Tek tarama dosyasını bu hesaplara bağla":
`SaUsageScanBatchForm` sends one file, the run statement and the selected ids (repeated `accountIds` field) to
`POST /api/v1/service-accounts/usage-scans` and shows the server's answer per account: a short text badge (Bağlandı, Zaten
bağlıydı, Aranmamış, Belirsiz, Bulunamadı / yetki yok, Kaydedilemedi; never colour alone) plus the server's own wording.
An account the server returns without a name is shown as "Bulunamadı veya kapsamınızda değil" (or "Hesap bilgisi okunamadı"
for a `Failed` row whose account could not be read, since 2026-10-07), never with the list label.
A refused file uses the existing usage-scan problem texts; nothing is stored. The selection stays after an upload so a
failed row can be retried. Tests: `tests/SecureOps.Tests.Unit/Ui/ServiceAccountUsageScanBatchUiTests.cs`.

2026-10-07 review fixes: the badge for `NotInScan` reads "Aranmamış" (the file did not search the account; earlier "Dosyada
yok"). A new upload clears the previous answer first; a failed upload is shown inside the form (`Problem` parameter, shared
`SaProblem`) and its "Tekrar dene" repeats the upload with the current file, statement and selection — the page-level
problem panel (whose retry reloads the list) is kept only for session/access problems. The send button is never
`disabled`: while not ready it is `aria-disabled="true"`, stays in the tab order (typing the statement and pressing Tab
reaches it even before the server re-renders), does nothing when activated, and points with `aria-describedby` to a line
that says what is missing (selection, more than 20, file, statement of at least 5 characters, upload in progress). The
"Tek e-posta kaydını…" and "Tek tarama dosyasını…" toggles carry `aria-expanded`.

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

- No `localStorage` / `sessionStorage` except the non-sensitive appearance preference `wasas.appearance`; all
  other state is server-side.
- No direct database access from the UI; it calls the API.
- No direct PowerShell invocation.
- No colour literals in CSS; extend the theme.
- No leaderboards or per-operator comparison widgets ("audit is not surveillance").
- No role selection presented to the user.
- No remediation controls.

## Running locally

All shared server-side API clients send the public `X-SecureOps-Csrf: 1` request
intent header (ADR-0029). Browser Origin/Fetch Metadata is not forwarded. Direct
unsafe API scripts need this header too; a headerless client receives 403. The UI's
existing antiforgery, authentication and API session transport remain required.

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

**The Demo API also needs `Access__DemoCompatibilityEnabled=true`** (PowerShell: `$env:Access__DemoCompatibilityEnabled='true'`
before starting the API; bash: prefix the command with `Access__DemoCompatibilityEnabled=true`). Without it the demo actor is
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

UI logic and component rendering are tested in `tests/SecureOps.Tests.Unit/Ui/` (render tests use
`HtmlRenderer`, not bUnit): error translation, account input rules, lookup result reuse, return-URL safety
and per-feature presentation. Browser journeys are `tests/browser/*.cjs` (Playwright against loopback hosts
with synthetic data; each script's first line gives its arguments). `LocalReturnUrl` is `internal` and reachable via
the `InternalsVisibleTo` entry in the csproj.

Responsive and visual behaviour is validated in a real browser at 1440×900, 1366×768, and 390px in
both themes. See `docs/25-ui-enterprise-shell.md` §8.
# Integrated management reporting continuation

The owner explicitly includes the existing dashboard in the bounded Codex scope.
`/dashboard` now includes `WorkflowReportsPanel`, SQL-backed same-cut metrics,
bounded detail pages, module/status/type filters and safe Excel export. OCO links
open the exact owned draft; access/synthetic/historical limitations stay visible.
An unavailable response retains the previous cut and restores its applied filters.
This is not target activation or an employee productivity ranking. See
`docs/integrated-test-activation.md` and `docs/integrated-activation-tr.md`.
## gMSA/MSA lookup input (G-26 UI, 2026-10-07)

`AccountInputRules` accepts a gMSA/MSA `sAMAccountName` with one trailing `$` (e.g. `syn.gmsa$`, also after `DOMAIN\`),
mirroring the server guard: `$` anywhere else, alone, twice or together with `@` is refused with its own message, and the
character list is otherwise unchanged. The identity result's Genel tab shows "Dizin nesne türü" from the server's
`accountTypeEvidence` (gMSA / MSA / standart kullanıcı nesnesi), worded as what the directory object is, never how it is
used. Tests: `AccountInputRulesTests`, `IdentityLookupManagedAccountUiTests`.

## Service Accounts next-step card (2026-10-06)

`SaNextStepCard` on `/service-accounts/{id}` lists what is open, most important first, from the already loaded
`AccountDetail` (`ServiceAccountNextStep.Compute`; UI only, no API or contract change). Order: ownership proposal or missing
owner, overdue requests, scan matches awaiting a decision (server total `UsageScanPending` since PR #12 paged the items), performed actions awaiting verification, rule conformance
(`Unplanned`, `ManualReviewPending`, `IncompleteInformation`), handover proposals, unknown usage. Each step carries a text kind
(Sizden bekleniyor / Başkasından bekleniyor / Bilgi eksik; never colour alone) and a button that only switches the tab. A step
the server-computed `AccountPermissions` does not allow is shown as waiting for someone else. An empty list says only that
this screen knows of nothing pending, not that the account is closed or verified. Tests:
`tests/SecureOps.Tests.Unit/Ui/ServiceAccountNextStepTests.cs`. Checked locally (Demo API + UI, synthetic LocalDB) at
390 px, light and dark, no horizontal scroll.

## E-08 In Use activity review

The primary action submits only the eligible WASAS activity, not overall OR
closure. One `Cevapları kaydet` action persists answers and explicitly selected
proposal review. Source diffs, independent RFC/server columns, saved state,
required values and disabled reasons remain visible. Copying answers still needs
a target/field preview. Tracking distinguishes source-verified completion from
manual attestation. Next-team/stage display is deferred, not a current list gate.
Current browser selectors are updated, but execution remains pending through the
authorized runner; component rendering and syntax checks are not browser proof.
See the single register `docs/integrated-test-activation.md`, IU-07 / E-08.

List ordering defaults to `OR numarasina gore`, Code/record Id before paging.
Date options are disabled with an explanation, including on old date-sort links;
backend oldest/newest support is retained for later verified mapping, not a gate.
The date column explicitly labels unavailable evidence and shows UTC, never the
refresh timestamp. Source-pending and verification-pending filters complement the
existing review/tracking views. The real source date/activity read contract is
still absent; local fixtures and component checks are not corporate acceptance.
