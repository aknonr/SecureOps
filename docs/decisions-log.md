# Decisions Log

## 2026-10-03 - OR request type: text suggestion, one-click confirm (ADR-0018 Amendment 1)

Owner decision: the operator should not start from an empty "operator declaration" select. The API suggests
one supported request type from explicit title/description words and shows the words; the operator confirms or
changes it. Conflicting or absent words give no suggestion. The suggestion stays outside SDM evaluation input and
never grants eligibility; the confirmed type is still the operator declaration checked by pilot policy and mapping.

## 2026-10-03 - .NET 10 Windows gates and package refresh (PR #8)

First Windows run of the migration branch: SDK pin moved to 10.0.401 (same 10.0.12 runtime), Release build,
tests, format and release-packaging dry run pass. Packages refreshed per ADR-0001 Amendment 2 "Package refresh".
Owner then approved the Swashbuckle 10 OpenAPI snapshot change and removal of the unused JsonSchema.Net.

## 2026-10-02 - Owner direction for the next work (session management, redesigns)

- Session policy becomes admin-adjustable within fixed lower and upper bounds (idle and absolute), audited.
- When an administrator ends someone's session, that person is signed out: today the API session ends but the UI
  cookie survives and the next request silently starts a new API session. The UI must drop its cookie and session
  store on `SessionRevoked`, and the API must not silently restart a session for that browser.
- ADRs must not block redesign: Jira/SDM, In Use, per-module permission sections and page layouts may be redesigned
  on .NET 10, amending or superseding their ADRs in the same change.
- Next steps run in local Claude Code on Windows, which can execute the Windows-only gates.

## 2026-10-02 - .NET 10 migration (ADR-0001 Amendment 2), done by Claude by owner decision

The owner assigned the backend .NET 10 migration to Claude for this change (scoped exception to the default
Codex ownership). Branch `claude/dotnet10-backend-migration`: SDK 10.0.112 pinned, `net10.0`, C# 14,
analyzer level 10.0; unused EF Core/Polly/OpenApi packages removed; OIDC PAR kept off by default; per-actor
global and access-administration rate limits added. Windows/IIS, Negotiate, DPAPI and PowerShell runspace gates
still need a Windows run before release.

## 2026-10-01 - Data access: Dapper and numbered SQL scripts retained

Owner decision recorded as ADR-0001 Amendment 1: Dapper with parameterized SQL and numbered, DBA-reviewed scripts
in `sql/schema/` and `sql/migrations/` is the approved data-access approach; EF Core adoption is no longer planned.
The original 2026-05 decision text is preserved in the ADR. Tasks that required an EF Core `DbContext` (P1-05,
P1-T05) are marked superseded, not implemented. No package, repository, SQL or product behaviour changed; the
unused EF Core package references are left for a separate code change.

## 2026-10-01 - Agent guidance rewritten for current models

Owner decision: `AGENTS.md`, `CLAUDE.md` and `docs/agent-guides/` keep every hard rule and the
Codex/Claude ownership split, but drop scaffolding written for weaker models (a 10-document
mandatory reading order before any output, generic code samples, duplicated rule lists, fixed
plan/confirm thresholds). Reading is now routed by task; each hard rule states its reason.
Statements that contradicted the code were corrected: capability-based authorization (ADR-0010,
ADR-0022) instead of AD-group roles, Dapper-only data access (settled by the owner the same day, see the entry above), `HtmlRenderer`
render tests instead of bUnit, pinned C# 12 / SDK, and a single canonical JEA allow-list in
`docs/05-security-model.md`. No architectural decision changed. Code is treated as the observed implementation; it never overrides a hard rule or an approved decision.

## 2026-09-07 - Original OR-to-Jira source review

Original-script discovery is resolved by the externally supplied local file;
the parity document records its exact provenance/hash and line-274 syntax defect.
It was parsed only, never executed or committed raw. The script proves request
construction, constant label and operator confirmation, not native watchers,
explicit operator reporter, positive eligibility or a separate approver workflow.
Existing safer policies remain. Malformed BPM results, contradictory update
success and non-object provider JSON now fail safely, with sanitized regressions.
The earlier local-only Git status below is historical: the browser milestone and
four earlier commits were pushed and live remote HEAD was verified as `1665685`
at this task's start. Current delivery and package association remain canonical
in `src/SecureOps.Ui/README.md`.

## 2026-09-07 - SDM workflow, management and read-only TEST delivery

The task authorizes cross-layer implementation, coherent commits and normal push,
plus a task-only size exception without changing the permanent cap. Source-bound
drafts and uncertain-result handling are hardened; no positive SDM/approval policy,
corporate writes, Jira reconciliation lookup or source-close activation is invented.
The existing sanitized script parity/contract-gap documents remain the evidence
boundary because no original script/access path exists in the repository.

Paired API/UI package source is `de6e5381bd3d7e053f2e9c1c6b07e93283f55ca7`;
release root is `C:\SecureOpsBuild\release\2026-09-07-pilot-rc6.10`, required
schema 001-010. API/UI/DBA ZIP entries/hashes and source versions were verified.
No migration file changed. Full local tests passed 1,020 unit + 242 integration,
including eight isolated SQL tests; subsequent UI/client checks and browser
limitations are recorded without double-counting or claiming corporate activation.

No most-active-user ranking or new usage collection was added. Existing UTC
event/session aggregates are adoption evidence, not productivity; review-age and
resource-adoption metrics were not invented. Framework and Bitbucket follow-ups
remain deferred. The normal push failed on unavailable non-interactive Git
credentials; current remote SHA could not be verified. Canonical continuation:
`src/SecureOps.Ui/README.md`, "SDM, Management and TEST Delivery Handoff, 2026-09-07".
Operator runbook: `docs/24-api-test-deployment-readiness.md` and the release export.

## 2026-05-16 — PAM pre-review meeting focus

**What we discussed:** Bilgi Güvenliği ve Siber Güvenlik ön incelemeleri politika, risk ve kontrol çerçevesi üretirken PAM görüşmesinin doğası daha operasyoneldir. PAM ekibi için asıl değer, servis hesabı, parola rotasyonu, secret retrieval yöntemi, Phase 4 read-only session correlation erişimi ve gelecek faz bağımlılıklarının somut ticket/request çıktısına dönüşmesidir.

**What was decided:** PAM toplantı belgesi karar disiplini korunarak hazırlanacak, ancak ana başarı ölçütü soyut mutabakat değil açık ticket, owner ve hedef tarih üretmek olacaktır. `svc-secureops` hesabı, secret onboarding, Phase 4 read-only API erişimi ve PAM query logging başlıkları öncelikli ele alınacaktır.

**What was deferred:** Phase 8'de PAM'ın remediation oturumlarını broker edip etmeyeceği nihai karar olarak bugünden bağlanmayacak; konu erken yönlendirme almak için sorulacak ve gerekiyorsa Phase 8 öncesi ayrı tasarım görüşmesine bırakılacaktır.

## 2026-05-17 — Gerçek operasyon zincirinin netleşmesi

**What we learned:** Turuncuhat yalnızca genel bir ticketing sistemi değil; EVT oluşturan, PR/OR/OCO kayıtlarını açabilen, mail/IVR gönderen ve acknowledge/close akışının gerçek operasyonel merkezi olan sistemdir. SolarWinds alarm kaynağı, monthly.thy.com / HPE OpsBridge ise event-detail katmanıdır.

**What was decided:** SecureOps tasarımı Turuncuhat'ı organizasyonel kayıt sistemi olarak ele alacak; entegrasyon yönü tek taraflı varsayılmayacak ve ihtiyaç halinde EVT verisini alma ile EVT kapanış alanlarını geri yazma ihtimali birlikte değerlendirilecektir.

**What was deferred:** Worker'ın hedef sunuculara erişiminde BeyondTrust broker kullanıp kullanmayacağı bugünden bağlanmadı. Nihai karar PAM ekibi, Bilgi Güvenliği ve ekip lideri girdisi geldikten sonra verilecektir.

## 2026-05-17 — Batch 4 ve Batch 5 kapanışı

**What changed:** Batch 4 ile gerçek sistem manzarası repo geneline işlendi: Turuncuhat merkezi workflow sistemi olarak netleştirildi, monthly.thy.com / HPE OpsBridge event-detail katmanı olarak işlendi ve Worker-BeyondTrust erişimi için X / Y / Z senaryoları açık karar olarak belgelendi. Batch 5 ile Phase 0 süre varsayımı yaklaşık 45 saatlik güncel efora uygun biçimde 2–3 hafta olarak yeniden bazlandı.

**What remains open:** Turuncuhat entegrasyon yöntemi hâlâ netleşmedi; webhook mu API-pull mu olacağı ve read-only mi read-write mı ilerleyeceği paydaş yanıtı bekliyor. Worker'ın BeyondTrust ile hangi senaryoda çalışacağı da henüz açık karardır.

**Mail status:** Turuncuhat ve BeyondTrust / PAM soruları taslaklandı; yönetici onayı bekleniyor, henüz gönderilmedi.

## 2026-06-18 — Phase 1A IdentityLookup kararı

**What we decided:** IdentityLookup / PamAdUserLookup, Phase 1'den önce gelen backend-only Phase 1A olarak eklenecektir. Amaç, yetkili TeamLead/Admin kullanıcıların tek bir PAM hesabını veya AD kullanıcı adını incident response verification bağlamında read-only çözebilmesidir.

**Security framing:** Bu özellik kişi arama veya performans izleme aracı değildir. Geniş arama, wildcard, bulk search, AD/PAM write, parola reset, unlock ve grup değişikliği kapsam dışıdır. Her sorgu gerekçe ister ve auditlenir.

**Implementation direction:** İlk sürüm direct read-only AD lookup kullanır. BeyondTrust/PAM metadata çözümleme mock interface olarak hazırlanır; gerçek PAM API kullanımı paydaş onayı ve ayrı karar olmadan etkinleştirilmez.

## 2026-06-19 - Repository consistency audit after Phase 1A hardening

**What changed:** Repository memory was synchronized with the current state: Phase 1A backend IdentityLookup and audit hardening are implemented and tested, while Phase 1 read-only diagnostics remain not started. `SecureOps.Shared` is documented as an accepted contracts/config/auth constants layer, not an infrastructure or UI layer.

**What remains open:** Real AD smoke testing with an approved read-only account is still pending. Phase 0 stakeholder replies are also still pending; Turuncuhat and BeyondTrust/PAM inquiry mails remain drafted and awaiting manager approval.

**Scope guard:** No new application feature was added in this audit. AI/RAG, notifications, PAM correlation, remediation, diagnostic modules, JEA scripts, and SQL schema remain later-phase or Phase 1 planned work as documented.

## 2026-06-19 - Phase 1A closure hardening decisions

**What changed:** Phase 1A now uses `ConnectionStrings:SecureOpsDb` as the single SQL audit/data store connection string name. The old `SecureOps` key is not treated as a compatibility alias, so missing production SQL configuration fails clearly when `Audit.Provider=SqlServer`.

**Audit behavior:** File/SQL audit persistence remains queued so request threads do not perform file IO. If a queued persistent write later fails, audit-store health becomes unhealthy with a safe code such as `AuditSinkUnavailable`; with fail-closed enabled, later IdentityLookup calls stop before AD provider access.

**Error taxonomy:** Directory provider timeout is separated from generic provider failure. Timeout returns `DirectoryProviderTimeout` and audits `IdentityLookupProviderTimeout`; generic provider exceptions remain `ProviderUnavailable` / `IdentityLookupFailed`.

**What remains open:** Real AD smoke testing with an approved read-only account is still pending in Test/UAT. Phase 1 diagnostic MVP has not started.

## 2026-09-06 - Resource catalogue and shift-start sets UI, with a scoped diff exception

**What changed:** The UI for the committed resource backend was implemented: `/resources`
(Bağlantılarım), `/resources/sets` (Mesai Setlerim) and `/admin/resources` (Katalog Yönetimi), plus
a typed `IResourceApiClient`, presentation rules in `ResourceView`, four dialogs, capability-gated
navigation, and `ResourceValidationFailed` / `ResourceNotFound` / `ResourceLimitExceeded` /
`ResourceConcurrencyConflict` mappings in `UiProblemFactory`. No backend, API, Shared contract,
SQL, migration, capability or authentication change was made.

**Scoped diff exception:** The owner authorized exceeding the usual reviewable-diff guidance for
this milestone only, so that implementation, tests and documentation could land together rather
than being split into partially working slices. The change is 3,269 added lines across 19 files,
all under `src/SecureOps.Ui/` and `tests/`. The permanent "implement small, keep diffs reviewable"
rule in `AGENTS.md` is unchanged and this exception does not extend to any later task.

**Link opening is UI-owned and deliberately conservative:** single links are plain anchors with
`noopener noreferrer`; a set is resolved server-side on its own click and opened by a second
explicit click so the browser user activation is not already spent. The UI reports that opening was
attempted and keeps the individual links visible. It never claims a destination loaded or
authenticated, and does not treat a missing window handle as reliable blocked-tab detection.

**What remains open:** Migrations 009-010 and the reviewed grants are not applied to corporate SQL,
and no API build containing `ResourcesController` is deployed to TEST, so these routes cannot work
there yet.

**2026-09-06 targeted correction and verification:** Started at
`f5ed367d9e61ba0097e67a65d13bdb3508973fba`, branch
`feature/sql-runtime-hardening-20260902`, clean tracked worktree; untracked `.vscode/` preserved.
The owner explicitly overrode UI ownership for this task only. The correction remains below the
1,000-line hard cap; the earlier implementation exception was not reused.

- P1: batch interop expanded `string[]` into individual arguments; `openMany` received a string,
  attempted zero opens, and the UI reported an attempt. A native browser activation handler now
  dispatches the resolved array in order without a server round trip. Chrome showed activation on
  the first attempt, opener isolation, and null handles without false popup-block assertions.
- P2: set rereads retained old resolved links and erased concurrency errors. Refresh/mutations now
  clear opening candidates, and conflict recovery retains the error. Failed forms now retain drafts
  until confirmed success; stale/unknown outcomes require closing and inspecting the refreshed state.
- P2: set-picker rows ignored Enter; inputs lacked accessible names and select attributes landed on
  hidden inputs. Native choice buttons, field names and a resource-scoped MudBlazor label bridge fix
  those cases. Long text wraps; catalogue navigation no longer highlights both resource routes.
- P2: superseded searches used uncancelled requests; both resource searches now cancel and keep their
  existing sequence guards. Personal/category load failures are visible; unavailable preferences
  disable favourite actions. Client caller cancellation remains cancellation rather than a network error.

Real Chrome 152 journeys and remaining limits are recorded in `src/SecureOps.Ui/README.md`.
Evidence is local and ignored: `artifacts/resource-ui-20260906/` includes `browser-results.json`,
`ordinary-results.json`, `manager-results.json`, screenshots and TRX results. The small replayable
regression script is `tests/browser/resource-ui.cjs`; its driver/profile stays outside the repository.
No HTTP page response was counted as browser interaction.

Validation: full Debug solution build, zero warnings/errors; 987 unit and 233 integration tests passed,
four opt-in LocalDB tests skipped. Final affected resource tests: 25 passed. Release UI build passed
with zero warnings/errors. A full Release solution build was blocked by running local API DLL locks.
Repository-wide format verification failed on existing whitespace/naming/encoding debt; scoped C#
formatting was applied, with the pre-existing `ResourceApiClient.Root` naming diagnostic still reported.
No corporate endpoints, SQL, IIS, deployment, packaging, framework/dependency or lockfile changes.

Contract blockers: hidden membership cannot survive full replacement from a filtered personal
projection; no environment-facet route supplies values beyond a returned page. Both need backend
contract decisions. Injected 503/delayed-response browser cases remain pending because automatic
approval review rejected the task-local API-proxy override. Corporate migrations/grants, resource API
and UI deployment, actual authentication and managed-browser checks remain TEST gates.

**2026-09-07 resource experience and integrity completion:** This supersedes the two contract
blockers above, not the historical verification record. Starting SHA was
`e05977d158bfd533aa71caa0ac60f276bbc9ef37`; verified implementation is
`8ef13d45912a66abad6cc04242bc6608ef0e9ed4` on `feature/sql-runtime-hardening-20260902`.
Integrity commit: `10fe8a47c98834390c273cedfe8ed0c6c5a0be2a`. The owner explicitly authorized
Codex UI/backend work, a new milestone-only size exception and normal push to the existing origin
branch. Integrity changed 24 files (+910/-28); UX changed 25 files (+1,772/-1,327), with shared files
between those commits. This final documentation-only handoff is additional. No permanent rule changed.

- P1 confirmed data loss: visibility-filtered personal projections previously replaced hidden saved
  membership. Omission now always retains saved references, including legacy callers; explicit
  `removeLinkIds` removes only currently visible members. Ordered merge preserves omitted slots,
  including archive/restoration. Ownership, versions, limits and transactional audit remain intact;
  no hidden IDs, names, counts or URLs are disclosed. No migration or SQL object change.
- P2 confirmed filter defect: environment options came from one result page. A bounded, searchable,
  authorization-aware environment endpoint now supplies them independently of paging.
- The three screens now use Uygulama Bağlantıları, Bağlantı Gruplarım and Bağlantı Yönetimi. Personal
  defaults use a pin, not the favourite star, and never open tabs automatically. The guide has a
  non-blocking first-use invitation, persistent replay, keyboard/focus handling and server-side
  dismissal only. Management navigation requires server View and Manage; Lead title is insufficient.
- Confirmed accessibility defects found during the milestone: Mud 6 autocomplete semantics, numeric
  stepper button names/keyboard access, muted-label/badge contrast, and closed popovers causing narrow
  overflow. Scoped fixes passed keyboard checks and 16 resource/dialog axe scans, zero violations.
  Fresh resolution followed by an explicit native opening gesture remains intact, with individual
  fallback and no claim of loaded/authenticated destinations or reliable blocked-tab detection.

Final full Release build: zero warnings/errors. Full Release tests: 1,009 unit + 240 integration
passed, zero skipped, including six actual isolated LocalDB tests and separate migration 001-010
upgrade evidence. Forty affected UI/client tests and six browser regression cases passed after the
last accessibility fixes. OpenAPI/API checks preserve 52 operations and 478 existing schema properties
with additive contracts. Scoped formatting, diff checks and vulnerability scan passed; repository-wide
formatting was not repeated because of known unrelated debt. No corporate endpoints or operations.

Chrome 152 ordinary/curator/denied journeys covered discovery, guide, groups, retention, opening,
conflicts and real session revocation/reauthentication at desktop/narrow widths and light/dark themes.
Automated axe checks are not screen-reader verification. Injected browser failures/delays, timed
expiration and managed-browser popup variants remain explicit gaps; deterministic handler/state tests
cover failure/delay paths without repeating the rejected proxy override. Corporate TEST deployment,
SQL/grants and real authentication remain separate gates. MudBlazor 6.16.0 is retained; official
9.9.0 migration spans shared theme, dialogs and account menu, so follow-up scope is recorded, not
partially migrated. No dependency, lockfile or framework changes.

Canonical handoff, permission matrix, before/after evidence paths, replay and next action:
`src/SecureOps.Ui/README.md`, section "Resource Experience Handoff, 2026-09-07". Contract semantics:
`docs/adr/ADR-0019-resource-catalogue-and-personal-shift-sets.md` and
`docs/contracts/secureops-api-v1-ui-integration.md`, resource handoff section. Evidence remains local
under `artifacts/resource-experience-20260906/`; durable summaries and both browser harnesses are
committed. No temporary profiles, runtime data or large logs are included; `.vscode/` is preserved.
