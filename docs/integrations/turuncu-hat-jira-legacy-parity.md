# Turuncu Hat to Jira Legacy Parity

## Original Source Review, 2026-09-07

Original-script discovery is resolved. The actual task attachment resolves to
`C:\Users\dmtak\Desktop\jira kaydı açan script.txt`, not the `(2)` filename in
the task prose. This external file was inspected as source only, never executed
or copied into Git. SHA-256:
`768D3646A152A046CD2FA6338FFC6B65DF1CBAB0C91D92D287D69B19A23AAF68`.

Windows PowerShell `Parser.ParseFile` reported four errors; explicit UTF-8
`Parser.ParseInput` reported eight cascading errors. Line 274 ends the user-search
URL string with `%22` instead of a closing quote. Replacing only that suffix in
an in-memory UTF-8 diagnostic copy yields zero parser errors. This establishes a
syntax/copy defect in the supplied file, not a repaired original or evidence that
the historical operational copy failed. The file and its hash remain unchanged.
No credentials, headers, session values or raw responses are reproduced here.

The following request construction is established by source inspection. It does
not establish current remote acceptance. Earlier controlled metadata/query
evidence remains separately described in the contract-gap document.

| Script behavior (original line) | Application behavior / evidence | Required action |
|---|---|---|
| `SMSS_oRFF`; active=true, DCC NOT IN (4241), group IN (68); selects exactly `id,p_code,p_name,p_description,p_rel_requester` (58-62) | Same configurable grammar and selects; `EnterpriseAdapterContractTests` | Preserve source scope; not positive classification |
| Positional extraction and HTML decode (88-92) | Exact `SET.*`/`KEY.p_rel_requester` parsing from later real TEST evidence, bounded keyless fallback | Preserve semantic keys and duplicate rejection; do not revert to positional corporate parsing |
| Operator chooses one record, sees description and confirms OK (178,204,242-244) | Authorized, source-bound preview and confirmation; prior interactive SQL/Simulation browser evidence | No separate legacy approver is established |
| `project.key=SDM`, `issuetype.id="3"` (298-299) | Draft mapping already emits configured key and ID; adapter contract tests | The unused `Task` variable does not establish the current name of ID 3 |
| Summary `OR code + " - " + decoded title`; decoded description (300-301) | Draft service, bounded summary and full reviewed description | Existing mapping retained |
| `customfield_12700={value:"WASAS"}`; constant `labels=["SunucuTalep"]` (302-303) | Same reviewed draft mapping | Static label is not a category/eligibility rule |
| Search encoded requester; first `displayName -eq` match; returned `name` (274-278); up to three attempts | Unique exact name first, then unique ordinal display name; bounded safe-read retries | Keep safer ambiguity and authentication/contract failure handling; legacy PowerShell `-eq` is case-insensitive |
| Optional `customfield_11500=[{name:resolvedName}]` (305-306); unresolved lookup permits creation | Same wire shape, but default unresolved-requester policy blocks preview/create | Preserve Block policy; field is not proven to be native Jira watchers |
| No explicit `assignee` or `reporter` in create payload (297-307) | ProjectDefault omits both; separately configured verified policies exist | Correct former claim that operator reporter was legacy parity; owner decision still required |
| BPM query after Jira POST: task model 103652, status 1, source ID, group 68, main-object type 106684, active true; selects `id,m_created_dt` (319-322) | Same configurable filter; source freshness re-read and durable Jira key precede close | Keep numeric IDs in controlled configuration, not new production defaults |
| First activity value without count/key validation (329) | Exactly one valid activity required; malformed rows now invalidate the entire activity result | No zero/multiple or malformed-row fallback; exact remote BPM key spellings still need evidence |
| Flat update: `m_status,"100056",m_comments,comment`, filtered only by activity ID (334-336) | Same configurable flat update; no invented conditional update | Read-before-write does not make this atomic; remote conditional-write contract remains unknown |
| Comment: `{JiraKey} ile kaydi takip edebilirsiniz.Kayda izleyici olarak eklendiniz.` (336) | Configurable `{JiraKey}` substitution; test template makes no watcher claim | Do not activate legacy watcher sentence without established field semantics and successful operation evidence |
| Checks non-null error description/details; `Success` appears only in a commented diagnostic (342-346) | Requires boolean true and no contradictory error metadata | Correct former parity claim; synthetic response tests do not prove remote update outcomes |

## Retained Safety and Earlier Evidence

| Legacy behavior | SecureOps component | Disposition | Rationale | Automated coverage |
|---|---|---|---|---|
| POST `/login` with `req.Username`, `req.Password`, `req.TenantId` and runtime Authorization | `TuruncuHatSessionManager` | Retained and hardened | Exact reviewed property casing; secrets are server-owned and never logged | `EnterpriseAdapterContractTests` |
| Validate top-level string `LoginResult` and non-empty configured session segment | `TuruncuHatSessionManager` | Retained and corrected | The configured segment is validated, while the complete pipe-delimited `LoginResult` is preserved unchanged as the query `SessionID` | `EnterpriseAdapterContractTests` |
| One login per script run | `TuruncuHatSessionManager` | Improved | Thread-safe bounded cache and single-flight refresh avoid per-record login | `EnterpriseAdapterContractTests` |
| Query `SMSS_oRFF` active records excluding configured DCC and requiring configured group | `TuruncuHatOperationalRecordClient` | Retained as configuration | Base object and numeric values are mutable options | `EnterpriseAdapterContractTests` |
| Select id/code/name/description/requester from `Key`/`Value` cells | `TuruncuHatQueryParser` | Retained and corrected from real TEST evidence | Keyed rows map exact `SET.*` scalar fields and the `KEY.p_rel_requester` display field independent of order; internal `SET.p_rel_requester`, `num`, and bounded non-conflicting extras never override mapped values. Required/recognized duplicates and conflicting unknown duplicates are rejected; exact-count positional fallback is keyless only | `EnterpriseAdapterContractTests` |
| Read the evidenced query envelope | `TuruncuHatQueryParser` | Corrected from real TEST evidence | `Items` is required; optional metadata may be absent independently; empty/null error text and zero error number succeed; actual application errors fail closed | `EnterpriseAdapterContractTests` |
| HTML-decode title and description | `TuruncuHatQueryParser` | Retained | Uses platform HTML decoding after bounded parsing | `EnterpriseAdapterContractTests` |
| Legacy query has no source-created timestamp | Nullable `CreatedAt` contract | Intentionally changed | Unknown remains null; no fabricated timestamp | API and repository regression tests |
| Malformed query item abort behavior not evidenced | `TuruncuHatQueryParser` | Improved | Malformed/oversized records are skipped and counted; valid batch items continue | `EnterpriseAdapterContractTests` |
| Duplicate handling not evidenced | `TuruncuHatQueryParser` | Improved | Every duplicated source ID or OR code is excluded as ambiguous | `EnterpriseAdapterContractTests` |
| Basic-authenticated Jira identity and user search | Runtime configuration and `CorporateJiraRequesterResolver` | Evidenced and hardened | Corporate provider validates Basic mode; exact URL-encoded bounded GET; integration identity remains separate from business identities | `EnterpriseAdapterContractTests`, configuration tests |
| Exact display-name match and use `name` | `CorporateJiraRequesterResolver` | Improved | Unique exact `name` is preferred; unique exact display name is controlled fallback; ambiguity fails | `EnterpriseAdapterContractTests` |
| Up to three user-search attempts with delay | `CorporateJiraRequesterResolver` | Retained and bounded | Only safe reads retry; auth/contract failures do not | `EnterpriseAdapterContractTests` |
| Jira project, issue type, summary, description, team field/value, labels, requester field | `JiraIssueDraftService` and `CorporateJiraClient` | Retained as configuration | Preview exposes the complete validated create mapping and the adapter consumes that same draft | `JiraIssueDraftServiceTests`, `EnterpriseAdapterContractTests`, `OperationalRecordsControllerTests` |
| Omit requester custom field when unresolved | `CorporateJiraClient` | Retained | Field is emitted only for a unique resolved identifier | `EnterpriseAdapterContractTests` |
| Leave Jira assignment to project default | `JiraIssueDraftService` and `CorporateJiraClient` | Explicit policy | Default emits no assignee; exact configured operator mapping is the only supported override | `JiraIssueDraftServiceTests`, `EnterpriseAdapterContractTests` |
| No explicit reporter in original payload | `JiraIssueDraftService` and `CorporateJiraClient` | Separate application policy | `AuthenticatedOperator` is not established by the script; project default preserves omission | `JiraIssueDraftServiceTests`, `EnterpriseAdapterContractTests`, configuration tests |
| POST Jira create and read response `key` | `CorporateJiraClient` | Retained and hardened | Bounded response; raw body is never surfaced | `EnterpriseAdapterContractTests` |
| Retry semantics after uncertain Jira create not evidenced | Existing reconciliation workflow | Intentionally changed | No create retry; ambiguous transport/5xx/invalid-success outcome blocks recreation | `EnterpriseAdapterContractTests`, existing workflow tests |
| Query configured `BPM_Actvty` after confirmed Jira creation | `TuruncuHatOperationalRecordClient.CloseAsync` | Retained | Uses evidenced backend-owned filter and selected activity fields | `EnterpriseAdapterContractTests` |
| Select first BPM activity | `TuruncuHatOperationalRecordClient.CloseAsync` | Improved | Exactly one required; zero/multiple fail safely | `EnterpriseAdapterContractTests` |
| Update status/comment with Jira key | `TuruncuHatOperationalRecordClient.CloseAsync` | Retained as configuration | Flat update list and comment template follow evidence | `EnterpriseAdapterContractTests` |
| Reject non-null update error fields; no Success check | `TuruncuHatOperationalRecordClient.CloseAsync` | Hardened | Boolean true and no contradictory error metadata required; remote success envelope still unverified | `EnterpriseAdapterContractTests` |
| Create then source update | `JiraTransferService` | Improved | Jira key is durable before close; close failure retries only close | Existing `JiraTransferServiceTests` and hosted workflow tests |
| Multiple operators not addressed | Existing claims/fencing/idempotency | Improved | One backend owner; SignalR is not a correctness dependency | Existing concurrency tests |

## Not Proven by Legacy Evidence

No parity claim is made for pagination, session-expiry markers, source ETags/conditional update, BPM update outcomes, Jira create outcomes, Jira remote idempotency, provider correlation headers, or Jira account stability. Required fixtures are maintained in `docs/26-enterprise-turuncu-hat-jira-adapters.md`.
