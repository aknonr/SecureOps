# ADR-0030: Open a Turuncu Hat OR from an approved gMSA change plan (PROPOSED)

**Status:** Proposed — decision draft only. Nothing implemented, no migration written, no Turuncu Hat write enabled.
Implementation (OR-2, migration 035) starts only after this ADR is accepted.
**Date:** 2026-10-12
**Decision makers:** project owner (scope, role bundle, activation); Turuncu Hat team (requester mapping, field codes,
sample responses); Bilgi Güvenliği (integration identity writing on behalf of a person).
**Related:** ADR-0009 (preview first, durable idempotency, reconciliation), ADR-0012 (Turuncu Hat adapter, no requester
guessing, `ReadOnlyIntegrationMode`), ADR-0031 (read-only ITSM record lookup and Turuncu Hat write scope — Proposed, TH-2,
separate branch; referenced, not changed here), ADR-0010/0022 (capabilities and role bundles),
`docs/service-accounts/CHANGE-PLAN-DESIGN.md` (plan flow, migrations 033/034).

## Context

The Service Accounts change plan (033/034, on master) takes a gMSA conversion from plan → preview → approval →
checklist → closure. The conversion needs the gMSA to exist first, and the AD team creates it only on a Turuncu Hat
**OR** (operational request, form `SMSS_oRFF`). Today the operator opens that OR by hand and retypes what the plan already
holds: the gMSA name and the servers that will use it.

Owner decisions (2026-10-12):
- From a gMSA change plan, an OR is opened **with a button on the plan screen**. Every other module keeps Turuncu Hat
  writes closed (ADR-0031 §5).
- For the gMSA conversion the record is an **OR, not an OCO**. The plan design linked the work to an OCO number; it is
  corrected together with this ADR.
- **One OR per account.** A plan with three accounts opens three ORs, each tracked on its own.
- Reporter and affected end user are **the person who presses the button**. The panel user must map to exactly one
  Turuncu Hat user; otherwise no OR is opened. The integration account is never used as the requester.
- The environment (Test/Prod) is suggested from the plan and the person may change it. The description is generated
  from a template and edited in the preview with a simple plain-text editor; what is sent is exactly what was previewed.
- The OR's progress is visible in the panel (stage, last update, new comment/return marker), read-only, with a
  "Refresh" button first and a periodic Worker refresh later. No commenting or closing from the panel.

Evidence and its limits. The owner's Postman OR collection (kept outside the repository) shows OR creation as **at least
three separate remote writes/reads, not atomic**: insert `SMSS_oRFF` → `InsertResult.InsertedId`; query `DCM_DynamicCase`
by `m_main_object_id` for the case id; query `DCM_DynamicCaseCatalogueTemplateProperty` by `p_dcc` for the template
property ids; insert one `DCM_DynamicCaseProperty` (`p_dc`, `p_dcctp`, `p_value`) per property → `InsertResult.Success`.
The collection has no saved responses, so response shapes are script hints, not contract evidence (TH-1 analysis §1–2, §6).
There is no client key, conditional insert or other remote idempotency. Two collection defects must not be ported: step 4
overwrites the case id variable, and cells are read by position (`Items[0][0]`) instead of by `SET.`/`KEY.` key (ADR-0012).
The owner's masked sample OR (outside the repository) shows the form fields, the short-description pattern and the
description template. Two visible form fields — environment and "Jira project scope" — are **not** among the `SMSS_oRFF`
insert fields, so they are most likely catalog template properties; this makes the property step likely, not optional.
The collection contains **no user search**: the panel user → Turuncu Hat user mapping is unproven (TH answer 2026-10-12;
planned as TH-4).

## Decision (proposed)

### 1. Scope, visibility and capability

- Only plans of kind `GmsaConversion`, only in `Approved` or `InProgress`, only for an account in the plan's current
  approved preview. Hidden in `Draft`, `Previewed`, `Completed`, `Cancelled`. A plan cancelled after an OR exists keeps
  the link and shows "plan cancelled — the OR stays open in Turuncu Hat; close it there"; the product never closes it.
- Per account: the button shows while the account has no OR link and no submission that blocks a new one (§3).
- New capability **`ServiceAccounts.CreateOperationalRecord`**, in the protected Admin bundle only (same as every
  other Service Accounts capability, 028 precedent); no other bundle changes. The caller's scope must cover **all**
  accounts of the plan (same rule as preview/approve, T5); otherwise 404. No planner/approver separation is added: the plan
  is already approved by a second person, and the OR is a request to the AD team, not an approval.
- The server decides; the UI only reflects `AccessSnapshot.Can(...)` and the preview's `canSubmit` with its reason.

### 2. Preview first, separate confirmation

- `POST change-plans/{id}/accounts/{accountId}/operational-record/preview` returns every field that will be sent, with
  its value and source (plan, server configuration, requester mapping), the requester resolution result, the environment
  choices, the editable short description and description, length counters, and a **fingerprint**.
- `POST .../operational-record` (separate click) carries the edited short description, description, chosen environment
  and the fingerprint. The server re-resolves configuration and requester, recomputes the fingerprint over the canonical
  submission and refuses a mismatch with 409 `orPreviewStale`; nothing is written.
- The fingerprint (SHA-256, server only) covers: plan id, account id, approved preview version and digest, target gMSA
  name, server list, requester Turuncu Hat id, environment, exact short description and description text, every
  configured code, and the adapter contract version. **The sent text is the previewed text byte for byte**; the server
  never regenerates it. It checks only length (description ≤ 4000 characters; short description limit to be confirmed)
  and rejects control characters other than line breaks. No HTML.
- The editor (UI): plain text, prefilled from the template, character counter and limit, "back to template", one-click
  insert of plan parts (server list, gMSA name). A short plan reference (for example `WASAS plan 123 / hesap 456`) is
  appended as the last line so a person can find the OR in Turuncu Hat during reconciliation; it is visible in the
  preview and inside the fingerprint.

### 3. Durable idempotency and reconciliation

- At most **one active OR submission per (plan, account)**, guaranteed by SQL uniqueness, not memory. Reservation runs in
  one transaction under the plan-row lock (`UPDLOCK, HOLDLOCK`, T4 pattern) and writes the submission row in state
  **`Sending`**, with the fingerprint and the full submitted content, **before** any remote call.
- **No automatic retry** of any remote write, ever (ADR-0009/0012). Dispatch runs in the request with bounded timeouts and a
  cancellation scope independent of the browser once the insert has been sent; the result is persisted in its own
  transaction.
- States:

| State | Meaning | Next |
|---|---|---|
| `Sending` | Row written, insert sent or about to be sent, no persisted outcome (also what a crash leaves) | Reconciliation only |
| `NotSent` | Refused **before** the insert was dispatched (gate off, mapping missing, login failed, validation) | New explicit attempt allowed |
| `Created` | `SMSS_oRFF` inserted, every configured property inserted with `Success=true`, OR number read back by internal id | Tracking |
| `PartiallyCreated` | `SMSS_oRFF` inserted (internal id known) but the OR number could not be read back, or a property insert failed or is unknown | Person completes the missing fields in Turuncu Hat, then links/confirms |
| `OutcomeUnknown` | Insert dispatched; timeout, transport failure, HTTP 5xx, invalid or contradictory response | Reconciliation |
| `Linked` | A person linked the OR number (after `PartiallyCreated`/`OutcomeUnknown`/`Sending`) | Tracking |
| `ConfirmedNotCreated` | A person searched Turuncu Hat and states no OR exists; reason mandatory | New explicit attempt allowed |

- Reconciliation is manual: the person searches Turuncu Hat (the plan reference line helps) and either links the OR
  number or confirms none was created. A linked number is checked with the ADR-0031 lookup (`IItsmRecordLookup`, kind
  `Or`); `Found` is recorded as verified, `Unknown`/`NotFound` keeps the link marked "not verified" and never as absent.
  Same capability and whole-plan scope; audited with the reason. A wrong "not created" can produce a duplicate OR; that
  risk is accepted and stated on the screen (the AD team cancels the duplicate).
- Property inserts are never repeated automatically (a repeat could duplicate a property row).
- An HTTP 4xx or `Success=false` after dispatch counts as `OutcomeUnknown` until sample responses prove that such an
  answer means "nothing written". Only then may a definite-refusal state be added (amendment of this ADR).

### 4. Field mapping

| Field (Turuncu Hat) | Source | Rule |
|---|---|---|
| `p_dcc` catalog, `p_dct`, `p_dc_parallel`, `m_active` | Server configuration | Never in the repository; validated at start-up |
| `p_def_urgency`, `p_def_determined_urgency_by_support`, `p_def_priority` | Server configuration (default "5 – very low") | Code values from configuration, not editable in the panel |
| `p_rel_requester`, `p_rel_affected_end_user` | Requester mapping (§4a) | The person who pressed the button; both the same id |
| `p_rel_lbs` (required), `p_rel_affected_end_user.m_rel_home_lbs_id` | The requester's Turuncu Hat profile | Read with the mapping; missing → not sent, `NotSent` |
| `p_rel_organization_id` | Open question | Not sent until its source is proven |
| `p_phone_number` | Not sent | Unless the Turuncu Hat team says it is required |
| `p_name` short description | Plan | Template "gmsa service account talebi(<application or gMSA name>)"; editable in the preview |
| `p_description` | Plan | Template: gMSA name, server list from the approved preview, fixed group/authorization text; editable; ≤ 4000 |
| Environment (template property) | Plan suggestion, person may change | Allowed values and property ids from configuration |
| "Jira project scope" (template property) | Server configuration ("No") | Property id and value from configuration |
| Attachments, building, room | Not sent | First version |

Template property ids are **configured**, not discovered at run time; the adapter does not run the catalog-template query
on each submission. A configured property that the catalog does not accept surfaces as `PartiallyCreated`.

**4a. Requester mapping — open dependency, blocks activation.** The panel user (server-authenticated corporate
principal) must map to **exactly one** Turuncu Hat user by an exact key. Zero or several matches → no OR (`NotSent`,
`requesterUnmapped`/`requesterAmbiguous`). No display-name or fuzzy matching, no fallback to the integration account
(ADR-0012). The lookup path does not exist yet: the collections have no user query; the user object and key field are
requested from the Turuncu Hat team and are planned as TH-4 (TH coordinator, own ADR or ADR-0031 amendment). Until that
is proven, OR creation stays closed; the only allowed interim form is an explicit, administrator-maintained one-to-one
mapping table (corporate principal → Turuncu Hat user id), audited, with no inference — the owner chooses whether to
build the interim form or wait (open question 1). It must also be proven that the integration account may set another
person as requester.

### 5. Configuration gate

- New server-owned section (name fixed in OR-2), default **off**: `Enabled=false`, `Provider = Disabled | Fake | TuruncuHat`,
  catalog/code/property values. Validated at start-up; incomplete configuration fails closed. Never falls back to Fake;
  Fake only in Development, Demo and Test, and a Fake result is labelled synthetic in the panel.
- It is a separate flag **below** the existing fences: if `OperationalRecords:ReadOnlyIntegrationMode=true`, this write
  is blocked before HTTP like every other corporate write (ADR-0012). Enabling it does not enable any other Turuncu Hat
  write, Jira write or the In Use mutation path.
- Admin diagnostics show only `Configured`, `Disabled` or `Unavailable`.

### 6. Audit and secrets

- Audit (same transaction as the state change, ids only): `OperationalRecordSubmissionStarted`,
  `OperationalRecordCreated`, `OperationalRecordPartiallyCreated`, `OperationalRecordOutcomeUnknown`,
  `OperationalRecordNotSent` (reason code), `OperationalRecordLinked`, `OperationalRecordConfirmedNotCreated`. Payload:
  plan, account, submission, fingerprint, OR number when known, state, reason code. The description text lives in the
  submission row, not in audit.
- Never logged or stored: credentials, session id, tenant, URLs, remote bodies, exception messages. Refresh (read) is not
  audited per click; its last result is stored for display.

### 7. ITSM independence

Turuncu Hat may be replaced in January/February 2027. The module depends on a module-independent write contract (working
name `IItsmRequestCreator`: create one request from a typed draft → typed outcome matching §3) next to the ADR-0031 read
contract. Stored data are ITSM-neutral (provider, record kind `OR`, external number, internal id, state). BaseObject names,
`p_*` fields, the `#%..%#` filter grammar and catalog ids belong to the Turuncu Hat adapter only.

### 8. Tracking in the panel

Read-only, built on `IItsmRecordLookup` (ADR-0031). The plan and account screens show "OR-xxxx — stage — last update —
new comment/return" with the time of the last successful refresh. First version: a "Refresh" button; periodic Worker
refresh is a later step. If Turuncu Hat cannot answer: "unknown", with the last known value and its time. Stage codes are
shown raw until a code list is approved (ADR-0031 §4). **Dependency:** TH-2 returns only id, code, title and group for an
OR; stage/activity (`BPM_Actvty`), last update and comment count/time come with **TH-4**. Until then the panel shows only
existence and title. No comment text or person names are read.

### 9. Migration 035 (reserved in the shared merge-order file)

Additive, `svcacct` schema: OR submission per (plan, account) with state, fingerprint, submitted content, environment,
requester Turuncu Hat id, internal id, OR number; append-only submission events; last tracking result. Uniqueness for
"one active submission per (plan, account)". Role-bundle amendment for the new capability if bundles are persisted in SQL
(028 pattern). `ChangePlanApprovals.OcoNumber` becomes nullable (§10). Prerequisite 033 + 034; rejects replay; no
rollback script; DBA note like 033/034.

### 10. OCO in the plan (owner decision: OR, not OCO)

`ChangePlanApprovals.OcoNumber` (033) is `NOT NULL` and required at approval. Recommendation: **keep the column, make
it optional in 035**: the approval may still carry a change-record number when the team's process requires one for the
server-side change, but no longer must. The OR link lives in the new per-account table, because the OR is opened
**after** approval and one per account. Closure records the OR number(s) as external records on the `GmsaConversion`
action, alongside the OCO number if present. Alternatives rejected: keep OCO required (contradicts the owner decision);
rename the column to OR (wrong cardinality and timing, and 033 is unchanged by rule).

## Ownership

Owner decision 2026-10-12: OR creation from the gMSA plan and the Turuncu Hat OR-creation adapter belong to the
Service Accounts coordinator (Claude), under the module exception, for this scope only. Other Turuncu Hat work (lookup,
TH-4 user mapping and tracking fields) stays with the TH coordinator. Shared `TuruncuHat*` files are not edited by two
sides at once; the shared merge-order file records who is working.

## Activation gate

1. Accept this ADR (and ADR-0031 for the lookup).
2. Requester mapping proven (TH-4) or the interim explicit mapping approved (§4a); proof that the integration account may
   set another person as requester.
3. From the Turuncu Hat team, sanitized: sample `insert` responses (success, failure), the `DCM_DynamicCase` query, the
   property insert; template property ids for environment and Jira scope; short description length; whether
   `p_rel_organization_id` and `p_phone_number` are required; `NoEncode` meaning.
4. OR-2 implemented with Fake first; unit, SQL and browser tests green; one controlled OR in corporate TEST with
   owner approval, evidence recorded without real numbers, names or addresses.

## Consequences

- The operator no longer retypes the gMSA request; the AD team receives a uniform request.
- A remote write exists for the first time from this module; its failure modes are visible states, not hidden retries.
- Duplicate ORs remain possible after a wrong manual "not created"; accepted and stated.
- Until TH-4, the button stays hidden or disabled with "requester mapping not available".

## Rejected alternatives

- Opening an OCO for the gMSA conversion: rejected by the owner.
- Automatic retry or idempotent-looking resubmission: no remote idempotency exists.
- Requester = integration account or a display-name match: ADR-0012.
- One OR per plan: the owner chose one per account (separate tracking and outcome).
- Server-regenerated description at send time: the sent text must be the previewed text.
- Discovering template properties at run time: an extra remote read per submission and a moving contract; configuration is
  reviewed once.

## Open questions

1. Requester: wait for TH-4, or build the interim administrator-maintained one-to-one mapping?
2. Does the server-side change itself still need an OCO under the CAB process (keeps `OcoNumber` meaningful as optional)?
3. Source of `p_rel_organization_id`; whether `p_phone_number` is required.
4. Environment and Jira-scope: are they template properties, and which ids/values?
5. One button per account (proposed) or also "open for all accounts" in one confirmation?
6. Who may confirm "not created" during reconciliation — the same capability (proposed) or `ServiceAccounts.Verify`?
7. Short description maximum length and whether Turuncu Hat HTML-encodes plain text.
