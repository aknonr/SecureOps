# Turuncu Hat to Jira Legacy Parity

## Complete In Use Source Review, 2026-09-08

The complete local candidate is `C:\Users\dmtak\Downloads\İNUSE_V2.ps1`:
18,661 bytes, 742 lines, SHA-256
`5A1ED6EF3FE5FA4CDAA6FA6CD57E520F6102A824F94A9C6A8F9AD8DD4DEB7942`.
PowerShell AST parsing returned zero errors. It was read only, not executed,
modified or copied into Git. Authentication material and runtime addresses were
excluded from inspection output. This is the located full source copy; no
separately addressable attachment object was available in the tool interface.
The old Desktop fragment below remains historical evidence of truncation, not
syntax defects in this complete file. No repair of the original was performed.

### Verified Behavior And Defects

| Original lines | Evidence | V1 treatment |
|---|---|---|
| 323-334 | Active SMSS_oRFF, DCC IN (4241), group IN (68); selects id, p_code, p_emb_dynamic_case_orff | Separate read-only discovery; existing Jira NOT IN exclusion is unchanged |
| 341-343 | Dynamic-case identity from positional OR cell 3; per-OR Yes/No confirmation | No dynamic-case mutation; explicit operator refresh/review |
| 382-436 | `rel`, m_tid=100049, m_lid=OR id, 15 service-instance select expressions | Request is evidence, not an expanded response contract; real server relationships remain unresolved |
| 439, 487-490 | `$servisID` reads `$si[25][0]` before the relevant server foreach; the in-loop replacement is commented out | Per-server evidence only; no service identity obtained from another server |
| 442-468 | Functional aspect query: name [Genel], p_rel_service_id; takes first result | Zero/multiple owner/aspect relationships never assign a local identity; no real aspect query until response keys are evidenced |
| 492-522 | Three security questions; accepted common answers apply to remaining servers without individual re-review | Explicit selected-server bulk draft with environment/service labels and confirmation; no silent save |
| 524-608 | Prod implies NMS/CPU/memory/disk Yes; up/down and KONTROL Yes; country, department, contact/owner/event addresses, OS release and aspect text are constants | Unknown/not verified unless an operator supplies an explicit answer and evidence; no embedded addresses reused |
| 637-658 | OR dynamic properties: p_dcctp=4463 gets Application Server; 4464 gets `$si[5][0]` after the loop | Confirms last-server dependency. Neither property is updated; no OR-wide environment is inferred |
| 663-698 | Workbook saved only after property writes; then file bytes/base64 sent to UploadAttachmentString using fBase=SMSS_oRFF, fId, fName, datastring | Managed local report only; no upload dependency or endpoint. Original upload error output also references the wrong response variable |
| 703-732 | BPM_Actvty models 103626 OR 103627, status=1, group=68, main-object id; Items[0][0] used for update m_status=4 | No BPM call. Original does not prove uniqueness, require Success=true, or reread authoritative OR state after success |

The original expanded positional reads do not establish response keys or joins:
SI cells 0 inventory, 1 hostname, 2 server type, 4 environment display,
6 consumer, 8 service display, 10 network segment, 12 IP, 13 OS,
15 OS version, 17 owner directorate, 19 building, 21 city, 23 device category,
25 service id. Cell 5 is used separately for the OR environment property.
These are exact script positions, not approved field semantics. In particular,
owner directorate, service owner, provisioning team and application reviewer
must remain distinct; no corporate identity is inferred from an address/name.

### Workbook Compatibility

`Sunucular` has column A labels, columns B onward one server each; row 1 is
ALAN ADI / Sunucu N Bilgileri. Rows 2-29 are inventory, hostname, server type,
environment, consumer, owner directorate, service name, service aspect, network,
IP, OS, OS version, OS release, InternetOut, InternetIn, Microsegmented,
NMS requested, country, city, building, department, subdepartment, contact,
UY owner mail, memory, CPU, up/down and disk alarms (469-482, 616-630).
`NMS` has the exact 22 headers from 175-219, with one server per row;
its ENV_ID column maps ENVANTER_ID, and its Server_Type column historically
maps the per-server environment, not a newly inferred server-type meaning.
The V1 workbook retains these positions and exact labels, including Turkish
text. All unproven values are explicit unknowns. `CheckList_TEKNIK` and
`CheckList_THY` are created at 358-364 but never populated by this full script;
V1 preserves their empty state rather than fabricate technical approval.

V1 additionally includes `Provenance` and `ReviewEvidence`, text-only cells,
source/aggregate versions, source hash, last observation, authenticated reviewer
and preparer, timestamps, synthetic marker and local-draft warning. Formula-like
text is prefixed and every cell is an inline string with no formula element.
Compatibility with a corporate template validator, additional-sheet acceptance,
formatting requirements and technical-sheet content still need a sanitized
approved workbook/template. The script alone cannot approve those requirements.

## In Use Source Recovery, 2026-09-08 (Historical Truncated Copy)

This is a separate workflow from the OR-to-Jira script reviewed below. The task
states active `SMSS_oRFF`, `p_dcc IN (4241)`, `p_rel_group IN (68)` for In Use;
the original Jira script excludes 4241. This task statement is not a response
contract or corporate business-policy approval.

Located source-only artifact:
`C:\Users\dmtak\Desktop\in_use_v2_sifresiz hali.txt`, 3,205 bytes, SHA-256
`7E95E749AC4F03D038A542686FBB750AFB08D4E0261BC5DC33A7F4F7ED2A437C`.
The file was read with sensitive/endpoint lines suppressed, not executed,
modified or copied into Git. UTF-8 PowerShell AST parsing returned
`MissingExpressionAfterToken` (217), `MissingEndParenthesisInSubexpression` (218)
and `MissingEndCurlyBrace` (171). The file ends inside the NMS header array.

| Available source | Established behavior |
|---|---|
| Lines 3-12, comment only | Intended per-OR workbook and per-server Sunucular/NMS processing; not executed implementation |
| Lines 92-108, `Write-ExcelBlock` | Hashtable row-to-key mapping writes present data into the supplied server column |
| Lines 110-120, `Write-Header` | Supplied header map writes row labels into column 1 |
| Lines 121-131, `Write-ServerColumnHeaders` | Sequential server headings starting at the caller's column |
| Lines 133-169, `Ask-YesNo` | Windows Forms Yes/No returns Evet/Hayir; no unknown choice in this helper |
| Lines 171-217, incomplete `Write-NMSHeader` | Partial header list only; no completed writer or data mappings |

Visible header order is KONTROL, IP Address, ENV_ID, ITMC_Service_ID,
ITMC_Turuncu_Sunucu_Listesi_Karsilik, ITMC_Servis_Unsuru_ID,
ITMC_Servis_Unsuru, Hardware_Type, Device_Type, Server_Type, Country, City,
Building, Department, Sub_Department, Contact_email, UY_Owner Mail Address,
ITMC_Event_Owner_Group, ITMC_MEMORY_Alarm, ITMC_CPU_Alarm, ITMC_UP_DOWN_Alarm.
The trailing comma proves this is not the complete template. Header names do
not establish source properties, ownership joins or completed technical checks.

The available file contains no actual discovery/server relation queries, call
sites for operator questions/bulk answers, Sunucular field map, dynamic-case
updates, workbook save/upload, or BPM lookup/update/verification. Consequently
the reported premature `$si`, last-server environment, positional relation,
hardcoded ownership/check, write-before-upload, first-BPM-result and missing
post-update-verification defects are **unverified against this artifact**.
Do not present them as observed defects or reuse Jira-script findings as proof.

Required missing evidence is the complete original In Use script, inspected
locally without execution, plus sanitized exact-key relationship/projection
examples where that source still cannot establish the response contract.
Passwords, tokens, runtime headers and full server configuration are unnecessary.
Local workbook implementation must not claim legacy compatibility from this
partial header list; unresolved requirements must remain visible in previews.

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
