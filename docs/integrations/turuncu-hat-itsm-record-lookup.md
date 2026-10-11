# ITSM Record Lookup Contract (OR / OCO, read-only)

Decision: ADR-0031 (Proposed). Code: `src/SecureOps.Infrastructure/ItsmRecords/`. Tests:
`tests/SecureOps.Tests.Unit/ItsmRecords/`. No API endpoint, no OpenAPI entry, no SQL, no UI: modules call the
lookup from their service layer. Status 2026-10-12: implemented and unit-tested with synthetic responses only;
no real Turuncu Hat call has been made.

## Contract

`IItsmRecordLookup.LookupAsync(ItsmRecordKind kind, string? number, CancellationToken)` returns
`ItsmRecordLookupResult(Kind, Number, Status, Reason, Record, Provider)`.

| Status | Meaning | Record |
|---|---|---|
| `Found` | Exactly one active record with this number | set |
| `NotFound` | A complete, successful response held zero rows | null |
| `Ambiguous` | Two or more rows matched (also identical duplicates); none is chosen | null |
| `Unknown` | The source could not prove an answer. Never read as "absent" | null |

`Reason` is `None` except for Unknown: `InvalidNumber`, `NotConfigured`, `Unavailable` (transport, 5xx, login
unavailable), `Timeout`, `AuthenticationFailed` (login failed, or rejected again after one re-login), `Rejected`
(4xx or application error in the envelope), `IncompleteResponse` (more pages, `RecordCount` above returned items,
malformed row, missing selected key, conflicting duplicate key, keyless/positional cells, value over a limit),
`InvalidResponse` (oversized, not JSON, outside the envelope, row for another number).
Caller cancellation throws `OperationCanceledException`; a dependency timeout is `Unknown/Timeout`.

`ItsmRecord(SourceId, Code, Title, RawStatusCode, RawStage, RawGroup, ProposedStartText, ProposedFinishText)`:
raw source values. Title is HTML-decoded and trimmed (max 500). Status, stage and group are codes, not meanings
(ADR-0031 decision 4). Window fields are source text, unparsed; their time zone is not proven. For OR, status,
stage and window are always null (the OR status field is not evidenced yet). No person field exists.

`Provider` is `Disabled`, `Fake` or `TuruncuHat`. Consumers store it with the result; `Fake` is synthetic.

## Number allow-list

`ItsmRecordNumber.Normalize(kind, value)`: outer whitespace trimmed, prefix `OR-` or `OCO-` (any case, must match
the kind), then 1-20 ASCII digits; result is upper case. Everything else (quotes, `%`, `#`, inner spaces,
non-ASCII digits, wrong prefix, >64 input characters) is rejected before any remote call, so the value is placed
in the filter verbatim. Consumers can call `Normalize` to pre-validate input. The OCO number shape is taken from
existing synthetic examples; confirm it with the Turuncu Hat team.

## Turuncu Hat wire mapping

One `POST {BaseUrl}/query` per lookup, same envelope and session as ADR-0012 (no `NoEncode`):

| Kind | BaseObject (config) | Filter | Selects |
|---|---|---|---|
| OR | `ItsmRecordLookup:OrBaseObject` (expected `SMSS_oRFF`) | `#%m_active%#='True' AND #%p_code%#='<number>'` | `id, p_code, p_name, p_rel_group` |
| OCO | `ItsmRecordLookup:OcoBaseObject` (expected `SMSS_Operation_Change`) | same | `id, p_code, p_name, p_def_status, p_rel_stage, p_rel_group, p_proposed_start_date_time, p_proposed_finish_date_time` |

Cells are read only by exact `SET.<select>` key; `num` is ignored; extra non-conflicting keys are ignored. The
row's `SET.p_code` must equal the requested number (case-insensitive). `m_active` only excludes retired records;
it is not evidence of approval.

Session: shared `ITuruncuHatSessionManager` (single-flight). On 401/403 the rejected session is invalidated and the
query is retried once with a new login; a second rejection is `AuthenticationFailed`. A failed login
(`"<tenant>|0"`-style result) is `AuthenticationFailed` without a query.

Logging and telemetry: warnings carry only kind, reason, HTTP status or `ErrorNo`. Never the session, credentials,
Authorization header, base URL, number or title. Metric `secureops.integration.operations` with provider
`TuruncuHat`, operation `itsm-lookup-or|itsm-lookup-oco`, outcome `found|notfound|ambiguous|unknown-<reason>`.

## Configuration

```json
"ItsmRecordLookup": { "Provider": "Disabled", "OrBaseObject": "", "OcoBaseObject": "" }
```

- `Disabled` (default): every lookup is `Unknown/NotConfigured`.
- `Fake`: Development, Demo and Test only; deterministic placeholders: `OR-000001`/`OCO-000001` Found,
  `-000002` Ambiguous, `-000003` Unknown/Unavailable, any other valid number NotFound.
- `TuruncuHat`: requires both base objects and the `TuruncuHat` read settings (HTTPS `BaseUrl` without embedded
  credentials, `Authorization`, `Username`, `Password`, `TenantId`, session lifetime and HTTP bounds). It does not
  require the OR-import mapping (`SourceBaseObject`, `RelatedGroupId`, `ExcludedDccIds`). Values come only from
  server configuration, never from the repository.
- Unknown provider names fail at startup. Validation runs inside `AddSecureOpsInfrastructure`, so API and Worker
  both fail fast.

## Open items (ask the Turuncu Hat team)

1. Sanitized OR and OCO query responses for zero and one row.
2. Meanings of `p_def_status` and `p_rel_stage` codes; status field name on OR records.
3. Time zone of `p_proposed_start_date_time` / `p_proposed_finish_date_time`.
4. Meaning of `NoEncode`; whether `RecordCount` is the total across pages.
5. Exact OR/OCO number formats.
