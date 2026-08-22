# Turuncu Hat to Jira Legacy Parity

Evidence source: sanitized legacy contract evidence supplied on 2026-08-23. The original PowerShell file and complete external API contracts are not in the repository. Parity is claimed only for behaviors explicitly evidenced below.

| Legacy behavior | SecureOps component | Disposition | Rationale | Automated coverage |
|---|---|---|---|---|
| POST `/login` with `req.Username`, `req.Password`, `req.TenantId` and runtime Authorization | `TuruncuHatSessionManager` | Retained and hardened | Exact reviewed property casing; secrets are server-owned and never logged | `EnterpriseAdapterContractTests` |
| Validate top-level `LoginResult` and non-empty configured session segment | `TuruncuHatSessionManager` | Retained, contract-gated | Segment index and lifetime are explicit configuration until success sample is approved | `EnterpriseAdapterContractTests` |
| One login per script run | `TuruncuHatSessionManager` | Improved | Thread-safe bounded cache and single-flight refresh avoid per-record login | `EnterpriseAdapterContractTests` |
| Query `SMSS_oRFF` active records excluding configured DCC and requiring configured group | `TuruncuHatOperationalRecordClient` | Retained as configuration | Base object and numeric values are mutable options | `EnterpriseAdapterContractTests` |
| Select id/code/name/description/requester by positional value | `TuruncuHatQueryParser` | Retained | Parses only evidenced positions and nested `Value` shape | `EnterpriseAdapterContractTests` |
| HTML-decode title and description | `TuruncuHatQueryParser` | Retained | Uses platform HTML decoding after bounded parsing | `EnterpriseAdapterContractTests` |
| Legacy query has no source-created timestamp | Nullable `CreatedAt` contract | Intentionally changed | Unknown remains null; no fabricated timestamp | API and repository regression tests |
| Malformed query item abort behavior not evidenced | `TuruncuHatQueryParser` | Improved | Malformed/oversized records are skipped and counted; valid batch items continue | `EnterpriseAdapterContractTests` |
| Duplicate handling not evidenced | `TuruncuHatQueryParser` | Improved | Every duplicated source ID or OR code is excluded as ambiguous | `EnterpriseAdapterContractTests` |
| Jira user search by source requester | `CorporateJiraRequesterResolver` | Retained | URL-encoded bounded GET; no browser query construction | `EnterpriseAdapterContractTests` |
| Exact display-name match and use `name` | `CorporateJiraRequesterResolver` | Improved | Unique exact `name` is preferred; unique exact display name is controlled fallback; ambiguity fails | `EnterpriseAdapterContractTests` |
| Up to three user-search attempts with delay | `CorporateJiraRequesterResolver` | Retained and bounded | Only safe reads retry; auth/contract failures do not | `EnterpriseAdapterContractTests` |
| Jira project, issue type, summary, description, team field/value, labels, requester field | `JiraIssueDraftService` and `CorporateJiraClient` | Retained as configuration | No mutable corporate identifier is spread through client code | `EnterpriseAdapterContractTests` |
| Omit requester custom field when unresolved | `CorporateJiraClient` | Retained | Field is emitted only for a unique resolved identifier | `EnterpriseAdapterContractTests` |
| POST Jira create and read response `key` | `CorporateJiraClient` | Retained and hardened | Bounded response; raw body is never surfaced | `EnterpriseAdapterContractTests` |
| Retry semantics after uncertain Jira create not evidenced | Existing reconciliation workflow | Intentionally changed | No create retry; ambiguous transport/5xx/invalid-success outcome blocks recreation | `EnterpriseAdapterContractTests`, existing workflow tests |
| Query configured `BPM_Actvty` after confirmed Jira creation | `TuruncuHatOperationalRecordClient.CloseAsync` | Retained | Uses evidenced backend-owned filter and selected activity fields | `EnterpriseAdapterContractTests` |
| Select first BPM activity | `TuruncuHatOperationalRecordClient.CloseAsync` | Improved | Exactly one required; zero/multiple fail safely | `EnterpriseAdapterContractTests` |
| Update status/comment with Jira key | `TuruncuHatOperationalRecordClient.CloseAsync` | Retained as configuration | Flat update list and comment template follow evidence | `EnterpriseAdapterContractTests` |
| Inspect `UpdateResult.Success` | `TuruncuHatOperationalRecordClient.CloseAsync` | Retained | Only explicit `true` completes; descriptions/details are not exposed | `EnterpriseAdapterContractTests` |
| Create then source update | `JiraTransferService` | Improved | Jira key is durable before close; close failure retries only close | Existing `JiraTransferServiceTests` and hosted workflow tests |
| Multiple operators not addressed | Existing claims/fencing/idempotency | Improved | One backend owner; SignalR is not a correctness dependency | Existing concurrency tests |

## Not Proven by Legacy Evidence

No parity claim is made for complete error envelopes, pagination, session-expiry markers, source ETags/conditional update, Jira remote idempotency, correlation headers, Jira account stability, or any response field not listed above. Required fixtures are maintained in `docs/26-enterprise-turuncu-hat-jira-adapters.md`.
