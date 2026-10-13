# ADR-0031: Turuncu Hat Read-Only ITSM Record Lookup and Write Scope

**Status:** Accepted (owner, 2026-10-13); lookup implemented behind Disabled default, no real Turuncu Hat call verified
**Date:** 2026-10-12

## Context

Modules need to check an OR or OCO number that an operator typed: does the record exist, what is its title,
raw status/stage/group and (for OCO) its proposed window. Service Accounts is the first consumer: its change plan
stores an OCO number that the server does not verify today. ADR-0012 already accepted a read-only Turuncu Hat
provider with a shared single-flight session, bounded responses and keyed `SET.`/`KEY.` cell mapping. The
announcement source client reads OCO windows by `p_code`, but behind an announcement-specific contract.

The owner supplied three Turuncu Hat Postman collections (OR, OCO, record actions; kept outside the repository).
They use the same `/login`, `/query`, `/update` and `/insert` API. They contain no saved responses, so they are
hints, not contract evidence. They show that OR creation is several non-atomic writes without remote idempotency,
and that the OCO collection includes CAB approval/rejection updates.

Turuncu Hat may be replaced after January/February 2027. Lookup semantics, the stored reference and the evidence
must survive that; only the adapter should change. AGENTS.md lists "Turuncuhat inbound/outbound method and
read/write scope" as an open decision.

## Decision

1. Products look up one OR or OCO record by number through the module-independent `IItsmRecordLookup`
   (`src/SecureOps.Infrastructure/ItsmRecords/`). Turuncu Hat is its first adapter. Fake comes first; the real
   provider is enabled only by validated server configuration (`ItsmRecordLookup:Provider`, default `Disabled`)
   and never falls back to Fake. Fake is permitted only in Development, Demo and Test.
2. The result has four states: Found, NotFound, Ambiguous, Unknown. Transport failure, timeout, rejected session
   after one controlled re-login, application error, more pages or records than returned, a malformed row, a row
   for another number, or an unreadable/oversized response is Unknown with a bounded reason. Only a complete,
   successful response with zero rows is NotFound. Caller cancellation propagates.
3. Numbers are validated by a strict allow-list (`OR-` / `OCO-` plus 1-20 ASCII digits) and placed in the filter
   verbatim; nothing is escaped. Selected fields are a fixed allow-list (id, code, title, status, stage, group,
   proposed window). No person field is selected. Cells are read by exact `SET.` key, never by position. Response
   size and request time are bounded by the existing `TuruncuHat` options.
4. Status, stage and group are shown as raw source codes until the owner approves a code list from the Turuncu
   Hat team. The product never interprets them as "approved" or "closed". Window values are returned as source
   text; their time-zone semantics are not proven.
5. Write scope: apart from ADR-0030 (Service Accounts: open an OR from a gMSA plan, separate ADR), writing to
   Turuncu Hat stays closed. In particular CAB/approval workflow updates (`BPM_Actvty` approve/reject/return),
   OCO create/update and comment inserts need a new ADR and an explicit owner decision. The existing
   ADR-0012 BPM completion path keeps its own gate.
6. The open AGENTS.md decision is proposed to close as: outbound REST (`/query`), read-only; the only write
   exception is ADR-0030; no inbound (webhook/push) method. It stays open until this ADR is accepted.

## Ownership Exception

Owner decision 2026-10-12: Claude implements TH-2 under a scoped exception to the AGENTS.md default (Codex owns
integrations). The exception covers only this work: the `IItsmRecordLookup` contract, its Turuncu Hat adapter,
Fake and Disabled providers, registration and validation, their unit tests, this ADR and the contract document.
It does not cover write paths, API endpoints, OpenAPI, SQL or UI, and it does not change the defaults. Existing
`TuruncuHat*` files and the announcement client are not modified by this work.

## Activation Gate

- Accept this ADR.
- Obtain from the Turuncu Hat team, sanitized: one OCO and one OR query response (zero and one row), the meaning of
  `p_def_status` and `p_rel_stage` codes, the status field name for OR records, the time zone of the proposed
  window fields, and the meaning of `NoEncode`.
- Run one read-only lookup in corporate TEST with server-only configuration and record the evidence without real
  numbers, names or addresses in the repository.

## Consequences

- Consumers (first: Service Accounts change plans) store the number, the result state, the provider and the check
  time as their own evidence. A Fake result is synthetic and is never presented as real verification.
- Replacing Turuncu Hat means a new adapter for the same contract; stored references and evidence stay valid.
- There is no API endpoint for the lookup; modules call it from their service layer.

## Rejected Alternatives

- Extend `IOperationalRecordClient` or the announcement client: couples a general lookup to the Jira/OR import or
  announcement modules and touches files that ADR-0030 work also changes.
- Treat any failure as "not found": would let an unreachable source approve a wrong number.
- Copy the Postman collections' positional cell reads and request flow: positional reads are rejected by ADR-0012.
- Open Turuncu Hat writes for OCO or approvals now: short-lived target system, no remote idempotency evidence.

## References

- ADR-0004, ADR-0009, ADR-0012, ADR-0030 (reserved, Service Accounts OR creation)
- `docs/integrations/turuncu-hat-itsm-record-lookup.md`
- `docs/26-enterprise-turuncu-hat-jira-adapters.md`, `docs/integrations/turuncu-hat-jira-contract-gaps.md`

## Amendment 2026-10-12 (TH-4, ADR-0032)

The Turuncu Hat team and the vendor document answered part of the activation gate: `NoEncode=true` returns Turkish
characters unencoded, `QueryPermissions` defaults to SELECT, and `RecordCount` is a total across pages with a
0-based `PageNo`. The adapter now sends `NoEncode=true`, never sends `QueryPermissions`, and its page rule is pinned
by tests with the vendor shapes. Decision 3 ("no `NoEncode`" in the contract document) is superseded accordingly.
OR status/stage field names, status code meanings and time zones remain open. Tracking and exact user match are
decided in ADR-0032, not here.

## Amendment 2026-10-13 (TH-6, ADR-0033): decision 5 and the existing write paths

Owner decision 2026-10-13. Decision 5 also applies to two write paths designed before this ADR. Each keeps its own
gate, default off, and is neither opened nor widened here:

- In Use completion writes (report attachment upload, dynamic property updates, approval of the WASAS BPM
  activity; ADR-0020): `InUseCompletion:Enabled=false`, no Turuncu Hat transport registered.
- The ADR-0012 BPM closure after a persisted Jira key: `OperationalRecords:SourceCloseEnabled=false`.

Activating either needs its own owner approval. Jira creation is not a Turuncu Hat write; it is governed by
ADR-0009, ADR-0012 and ADR-0033.
