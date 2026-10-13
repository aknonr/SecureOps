# ADR-0033: In Use and OR → SDM Completion Scope; OR → Jira Rollout Decision

**Status:** Proposed
**Date:** 2026-10-13

## Context

The Operational Record → SDM (Jira) workflow (ADR-0009, ADR-0010, ADR-0012, ADR-0018) and the In Use review
workspace (ADR-0020) are implemented with tests and UI. Corporate TEST has verified only the read-only Turuncu Hat
source query (2026-09-04, four records, all `NeedsManualReview`). No Jira issue has been created against corporate
Jira, and no Turuncu Hat write has been made. The TH-5 inventory (2026-10-13, kept outside the repository) found
that what is missing is mostly decisions and evidence, not code, plus these gaps:

- the owner's 2026-10-13 decision to give In Use and OR → SDM completion to Claude is not recorded anywhere, and
  `src/SecureOps.Infrastructure/OperationalRecords/README.md` still says the module is Codex-owned;
- ADR-0031 decision 5 (Turuncu Hat write scope) does not mention the In Use writes designed in ADR-0020;
- the In Use write gate depends on `OperationalRecords:SourceCloseEnabled` (see decision 5 below);
- no write gate can be opened outside the `Test` environment (`OperationalRecordConfigurationValidator`).

Turuncu Hat may be replaced after January/February 2027. Jira-only transfer writes nothing to Turuncu Hat, so its
value survives that change; the source read adapter is the only Turuncu Hat specific part.

## Decision

1. **Ownership exception (owner decision 2026-10-13).** Claude implements the In Use and OR → SDM completion work
   end to end: backend, API, SQL (migration numbers reserved in the shared coordination file first), UI, tests and
   documents, for the work items TH-6 to TH-14 below. The exception covers only these two modules and their
   documents. It does not cover Service Accounts OR creation (ADR-0030, Service Accounts owner), Planned OCO
   Announcements (Codex) or any other module; elsewhere the AGENTS.md defaults stay unchanged. A change to an
   existing `TuruncuHat*` file is announced in the shared coordination file first, because ADR-0030 work shares
   that area. Size, verification and merge rules are unchanged.
2. **OR → Jira goes into full operational use (owner decision 2026-10-13).** When an OR suitable for Jira arrives,
   the operator opens its Jira issue from the product: preview, explicit confirmation, durable key first (ADR-0009).
   The target is Jira-only. Turuncu Hat closure (transfer-and-close, ADR-0012 BPM update) is not part of this
   rollout until the Turuncu Hat team supplies the closed status codes: `SourceCloseEnabled` stays `false`, and a
   successful transfer stays `JiraCreated` / `SourceOpen` with the OR open in Turuncu Hat.
3. **This ADR opens no write gate.** Code defaults stay `ControlledTestWritesEnabled=false`,
   `SourceCloseEnabled=false`, `InUseCompletion:Enabled=false` and empty `OperationalRecords:Pilot:*`; corporate
   TEST keeps `ReadOnlyIntegrationMode=true`. Each step in "Path to Rollout" that writes needs its own explicit
   owner approval.
4. **Writes stay Test-only for now** (owner answer 2026-10-13). The `Test`-only rule on
   `ControlledTestWritesEnabled` remains until the separate ADR in step R5 is accepted.
5. **Known defect, fixed by TH-7.** `InUseCompletionPolicy.Readiness` and `TuruncuHatInUseMutationClient.SendAsync`
   require `SourceCloseEnabled=true` for In Use writes, while ADR-0009 defines that flag as the independent OR
   source-close gate. Enabling In Use would also put close intent into every new OR preview, and
   `SdmPilotPolicy` blocks the Jira-only pilot with `PilotMustRemainSourceOpen` when the flag is true; the read-only
   completion diagnostic also requires it to be false. The two flows cannot be enabled together. TH-7 gives In Use
   its own default-off write gate and removes the dependency, with an ADR-0020/ADR-0009 amendment; it opens nothing.
6. **In Use completion.** If the missing source contracts (IU-05: attachment readback, conditional property update,
   unique activity) do not arrive, the manual path may be permanent (owner answer 2026-10-13): archive and download
   the report in WASAS, approve the WASAS step in the Turuncu Hat screen, record manual confirmation in WASAS.
   A real In Use transport (large, writes to a short-lived system) is not planned under this ADR.
7. **Turuncu Hat write scope.** ADR-0031 decision 5 is amended (2026-10-13): In Use writes (ADR-0020) and the
   ADR-0012 BPM closure keep their own gates, default off.

## Path to Rollout (OR → Jira)

| Step | What | Gate |
|---|---|---|
| R1 | Current Jira evidence: product/version, SDM project and issue type metadata, current shape of the team and watcher custom fields, service-account permissions, user search with zero and several results (TH-5 E6) | Operator run of the existing metadata guide; sanitized evidence only |
| R2 | Eligibility policy (owner question 1): the one-record pilot proposal `WASAS-SDM-PILOT-2026.09-v1` (ADR-0018) for the first OR, and a rule for routine use | Owner/business decision; routine use needs an ADR-0018 (and possibly ADR-0009) amendment |
| R3 | Successor package (including migration 024) installed in TEST, read-only | Existing installation approval |
| R4 | W1: one Jira-only create for one selected OR in TEST: `ReadOnlyIntegrationMode=false`, `ControlledTestWritesEnabled=true`, `SourceCloseEnabled=false`, pilot configuration for that OR, complete Jira mapping, `ReporterMode=AuthenticatedOperator`, `UnresolvedRequesterPolicy=Block` | Separate explicit owner approval (ADR-0009/0012); no code change needed |
| R5 | W4: a write gate for environments other than `Test` | New ADR + backend change + owner approval |
| R6 | Operating readiness: who holds `JiraPublisher` (create/retry), the manual reconciliation procedure for an unknown Jira result (no remote idempotency contract, TH-5 E7), UI contract additions TH-8/TH-9 (`docs/26-ui-backend-contract-gaps.md` G-38) | Owner decisions + normal work items |

SoftwareInstallation and ServerRetirement stay mapping-blocked until their business owner (owner or team lead,
owner answer 2026-10-13) supplies the Jira labels and fields.

## Work Items

| Item | Scope |
|---|---|
| TH-6 | This ADR, ADR-0031 amendment, stale documents, UI backend requests (G-38). Documents only |
| TH-7 | Separate In Use write gate from `SourceCloseEnabled` (decision 5); defaults stay closed |
| TH-8 | OR → Jira read-contract additions (G-38 items 1-4); OpenAPI entries appended, snapshot not regenerated |
| TH-9 | OR → Jira UI completion on TH-8, unknown-date sort closed, 390 px / 200 % / dark theme evidence |
| TH-10 | Turuncu Hat `ISourceClosureVerifier` over `IItsmRecordLookup` once closed status ids are known; opens no BPM write |
| TH-11 | IU-05-STATUS: read-only WASAS activity status from the real source (raw codes until a code list exists) |
| TH-12 | UI acceptance run of existing In Use and SDM browser journeys on local Demo |
| TH-13 | Optional, later: RFC reporter suggestion via exact `IItsmUserMapping`; needs an ADR-0020 amendment |
| TH-14 | Operator evidence in TEST (R1, ITSM read-only run, SDM-01 read-only precheck); no code |

## Owner Questions

1. Routine eligibility (R2): is a Jira-suitable OR established by the operator's declaration at preview time, or by
   a positive policy rule (which categories, groups, request types)? ADR-0009 currently says operator declaration
   is not eligibility, so either answer needs an amendment.
2. Pilot (R4): is `WASAS-SDM-PILOT-2026.09-v1` accepted for the first OR, and who selects the OR, the Jira target
   and the acting operator?
3. Which operators receive the `JiraPublisher` role (create and retry)?
4. Who runs R1 and when, and when is the successor package installed in TEST (R3)?

## Activation Gate

Owner acceptance of this ADR records decisions 1, 2 and 7. It activates nothing. R4 and R5 each need their own
approval; R5 also needs its own ADR.

## Consequences

- Claude can deliver TH-7 to TH-13 without a new ownership decision for each item; Codex defaults elsewhere stay.
- Full use of OR → Jira depends on R1-R5. Until R4 is approved, the product continues to show Jira creation as
  blocked by the write fence, and nothing claims a corporate Jira issue was created.
- Jira-only transfer leaves the OR open in Turuncu Hat; the operator closes it there as today.

## Rejected Alternatives

- Amending ADR-0009 and ADR-0020 separately: the scope exception and the rollout decision span both modules.
- Opening W1 by this ADR: external writes need a separate, explicit approval (ADR-0009/0012).
- Enabling Turuncu Hat closure in the same rollout: closed status codes and BPM response samples are missing.
- Keeping OR → Jira at a single pilot record: superseded by the owner's 2026-10-13 decision.

## References

- ADR-0009, ADR-0010, ADR-0012, ADR-0018, ADR-0020, ADR-0030 (reserved), ADR-0031, ADR-0032
- `docs/22-operational-record-jira-workflow.md`, `docs/26-enterprise-turuncu-hat-jira-adapters.md`,
  `docs/26-ui-backend-contract-gaps.md` (G-38)
