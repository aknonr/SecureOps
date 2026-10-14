# ITSM Record Lookup Contract (OR / OCO, read-only)

Decision: ADR-0031 (Accepted) for the record lookup; ADR-0032 (Accepted, amendments 2026-10-13 and 2026-10-14) for
OR tracking and exact user match (sections "OR tracking" and "User match" below). Code:
`src/SecureOps.Infrastructure/ItsmRecords/`. Tests: `tests/SecureOps.Tests.Unit/ItsmRecords/`. No API endpoint, no
OpenAPI entry, no SQL, no UI: modules call the interfaces from their service layer. Status 2026-10-12
(registration-number match added 2026-10-13; source registration rule, active-user filter, case-sensitive user name
and status/stage display text 2026-10-14): implemented and unit-tested with synthetic responses only; no real
Turuncu Hat call has been made.

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

One `POST {BaseUrl}/query` per lookup, same envelope and session as ADR-0012. Every ITSM read sends
`NoEncode=true` (the vendor encodes Turkish characters otherwise; titles are still HTML-decoded defensively) and
never sends `QueryPermissions` (the source default is SELECT). No page parameter is sent:

| Kind | BaseObject (config) | Filter | Selects |
|---|---|---|---|
| OR | `ItsmRecordLookup:OrBaseObject` (expected `SMSS_oRFF`) | `#%m_active%#='True' AND #%p_code%#='<number>'` | `id, p_code, p_name, p_rel_group` |
| OCO | `ItsmRecordLookup:OcoBaseObject` (expected `SMSS_Operation_Change`) | same | `id, p_code, p_name, p_def_status, p_rel_stage, p_rel_group, p_proposed_start_date_time, p_proposed_finish_date_time` |

Cells are read only by exact `SET.<select>` key; `num` is ignored; extra non-conflicting keys are ignored. The
row's `SET.p_code` must equal the requested number (case-insensitive). `m_active` only excludes retired records;
it is not evidence of approval.

Completeness (all ITSM reads): the vendor example uses a 0-based `PageNo`, `MaxPages` 0 on a single page, and
`RecordCount` as the total across pages (690 records, 10 per page, 69 pages). A response is complete only when
`MaxPages` and `PageNo` are at most 1 (absent counts as one page) and `RecordCount`, when present, is not above the
returned rows; anything else is `Unknown/IncompleteResponse`, never a truncated answer. Unit tests pin the vendor
shapes (single page, empty page, 69 pages, `RecordCount` above rows).

Session: shared `ITuruncuHatSessionManager` (single-flight). On 401/403 the rejected session is invalidated and the
query is retried once with a new login; a second rejection is `AuthenticationFailed`. A failed login
(`"<tenant>|0"`-style result) is `AuthenticationFailed` without a query.

Logging and telemetry: warnings carry only kind, reason, HTTP status or `ErrorNo`. Never the session, credentials,
Authorization header, base URL, number or title. Metric `secureops.integration.operations` with provider
`TuruncuHat`, operation `itsm-lookup-or|itsm-lookup-oco`, outcome `found|notfound|ambiguous|unknown-<reason>`.

## Configuration

```json
"ItsmRecordLookup": {
  "Provider": "Disabled", "OrBaseObject": "", "OcoBaseObject": "",
  "OrStatusField": "", "OrStageField": "", "CommentMainObject": "", "CommentTimeField": "m_created_dt", "ActivityMainObject": ""
}
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
- Tracking and user-match options (ADR-0032), all empty by default and identifier-validated at startup when the
  provider is `TuruncuHat`: `OrStatusField` / `OrStageField` (OR source field names; empty means the field is not
  selected), `CommentMainObject` (the `m_mob` value that identifies an OR in comments; empty makes the comment part
  `Unknown/NotConfigured`), `CommentTimeField` (default `m_created_dt`, not yet proven for comments),
  `ActivityMainObject` (optional `m_process.m_main_object` value; empty sends the id-only filter).
- `Disabled` and `Fake` are chosen once for the lookup, tracking and user-match interfaces together.

## OR tracking (ADR-0032)

`IItsmRecordTracking.GetOrTrackingAsync(string? sourceId, CancellationToken)` takes the numeric source id of an OR
(`ItsmRecord.SourceId` of a Found lookup; `ItsmSourceId.Normalize`: 1-20 ASCII digits, not all zeros) and returns
`ItsmRecordTrackingResult(SourceId, Status, Reason, State, Activities, Comments, Provider)`.

- `Status`/`Reason` describe the OR row: Found, NotFound, Ambiguous, Unknown (same rules as the lookup; an
  invalid id is `Unknown/InvalidIdentifier` with no remote call). `State`, `Activities` and `Comments` are null
  unless Found.
- `State = ItsmOrState(LastModifiedText, RawStatusCode, RawStage, StatusLabel, StageLabel)`: raw text.
  `m_last_mod_dt` time-zone semantics are not proven. Status and stage are null unless their field names are
  configured. `StatusLabel`/`StageLabel` (added 2026-10-14, optional, default null) are the source's display text
  from the `KEY.<configured field>` cell when the source sends it (HTML-decoded, trimmed, max 256 characters, longer
  is `Unknown/InvalidResponse`, absent or empty is null). Show them beside the raw code as the source's words, not as
  a product meaning. No other `KEY.` cell is read (no person display names).
- `Activities` and `Comments` are `ItsmTrackingPart<T>` (`Known` or `Unknown` + `Reason`). A failed or incomplete
  part never hides the others and is never shown as "empty".
- `ItsmActivity(ActivityId, RawGroup, RawStatus, CreatedText, CompletedText)`, ascending by id, at most 50 rows.
- `ItsmCommentSummary(Count, LastCommentText)`: count of a complete single-page response and the raw creation time
  of the comment with the highest id (never a text comparison of times).

| Part | BaseObject | Filter | Selects |
|---|---|---|---|
| OR header | `ItsmRecordLookup:OrBaseObject` | `#%m_active%#='True' AND #%id%#=<id>` | `id, m_last_mod_dt` + configured status/stage field (their `KEY.` cells are read if returned) |
| Activities | `BPM_Actvty` | `#%m_bpe_actvty_task_model.m_action_type.m_code%#!='WorkflowSystemAction' AND #%m_process.m_main_object_id%#=<id>` (+ ` AND #%m_process.m_main_object%#=<ActivityMainObject>` when set) | `id, m_group, m_status, m_created_dt, m_completion_date` |
| Comments | `cmn_comment` | `#%m_active%#='True' AND #%m_mob%#=<CommentMainObject> AND #%m_mid%#=<id>` | `id` + `CommentTimeField` |

`m_done_by` (the completing person) and comment text are never selected. `m_group`, `m_completion_date`, the time
field and the optional header fields may be omitted by the source when empty and read as null; every other selected
key must be present or the row is malformed (`Unknown/IncompleteResponse`). The `!=` operator and the system-action
exclusion come from the Turuncu Hat team's description and are unexecuted. Telemetry operations:
`itsm-tracking-or|itsm-tracking-activities|itsm-tracking-comments`.

## User match (ADR-0032, amendment 2026-10-13)

`IItsmUserMapping` has three methods, all returning `ItsmUserMatchResult(Status, Reason, User, Provider)` with
`Matched | NotMatched | Ambiguous | Unknown` and `ItsmUserReference(UserId, OrganizationId, HomeLocationId)`. Neither
the requested user name nor the registration number is echoed.

| Method | Key | Use |
|---|---|---|
| `MatchAsync(registrationNumber, userName, ct)` | registration number first, user name second | **Consumers call this one** |
| `MatchByRegistrationNumberAsync(registrationNumber, ct)` | `cmn_user.c_registration_number` | one key only |
| `MatchByUserNameAsync(userName, ct)` | `cmn_user.m_username` | one key only |

- Call it only with values the server holds for the signed-in user: the registration number from the product user
  record (`security.Users.Uid`, written from the validated OIDC `uid` claim) and the user name derived from the
  server-verified session identity (`security.Users.LoginName`). Reading them is the consumer's job; there is no
  search for anyone else.
- User name allow-list: `ItsmUserName.Normalize` (1-64 ASCII letters, digits, `.`, `_`, `-`, starting with a letter or
  digit; domain prefix, UPN suffix, spaces, quotes and wildcards are rejected as `Unknown/InvalidIdentifier` without a
  remote call). Case is preserved: the source compares `m_username` case-sensitively.
- Registration rule: `ItsmRegistrationNumber.Normalize`, the Turuncu Hat team's official rule (2026-10-14). Outer
  whitespace trimmed; then 1-20 ASCII letters or digits, anything else (inner whitespace, signs, `_`, `-`, quotes,
  `%`, `*`, `#`, non-ASCII letters or digits) is `Unknown/InvalidIdentifier` without a remote call; only zeros is
  invalid. Not all digits → sent as it is. All digits → leading zeros dropped, then left-padded with zeros to six
  digits (fewer than six are padded, six stay as they are, more than six lose their leading zeros):

  | Input | Searched |
  |---|---|
  | `123` | `000123` |
  | `1234` | `001234` |
  | `12345` | `012345` |
  | `123456` | `123456` |
  | `1234567` | `1234567` |
  | `01234567` | `1234567` |
  | `00012341` | `012341` |
  | `ABC123` | `ABC123` |

- Wire, user name: `cmn_user`, filter `#%m_active%#='True' AND #%m_username%#='<name>'`, selects
  `id, m_username, p_obs_id, m_rel_home_lbs_id` (`m_username` only proves the filter was honoured and must be
  **ordinally** equal: a row in another case is `Unknown/InvalidResponse`).
- Wire, registration number: `cmn_user`, filter `#%m_active%#='True' AND #%c_registration_number%#='<source form>'`,
  selects `id, c_registration_number, p_obs_id, m_rel_home_lbs_id` (`c_registration_number` only proves the filter
  was honoured: the returned text or number is normalized with the same rule and must be ordinally equal, so a JSON
  number that lost its leading zero still verifies).
- Passive users (`m_active` not `'True'`) are excluded by the filter on both paths; they never match and never make a
  result Ambiguous.
- Both paths: a row for another key value is `Unknown/InvalidResponse`; `p_obs_id` and `m_rel_home_lbs_id` may be empty
  and read as null; completeness and failure rules are the same as for the record lookup.
- Combined rule (`MatchAsync`; null or blank counts as not given):

  | Registration result | User name not given | User name given |
  |---|---|---|
  | (not given) | `Unknown/InvalidIdentifier`, no call | user-name result |
  | `Unknown` (also an invalid number) | returned | returned, **no fallback** to the user name |
  | `Ambiguous` | returned | returned, user name not tried |
  | `NotMatched` | returned | user-name result |
  | `Matched` | returned | user name on the same user id → `Matched`; on another user id → `Ambiguous` (conflict); `NotMatched` → registration match stands; `Ambiguous`/`Unknown` → that result |

- **`NotMatched`, `Ambiguous` and `Unknown` are all "no mapping": the consuming flow stops.** The phone number
  (`m_phone_number`) is not selectable.
- Personal data: the registration number is only a query key. It is never returned, stored, logged, put in a
  telemetry tag or in error text; logs carry only the key kind and the reason.
- Known gap: a NotMatched registration followed by a Matched user name is accepted; a Turuncu Hat user whose
  registration number differs from the product's is not detected on that path (ADR-0032 amendment, known risk).
- Telemetry operations: `itsm-user-match` (user name), `itsm-user-match-registration` (registration number),
  `itsm-user-match-combined` (outcome as usual, or `conflict` when the two keys matched different users).

## Fake providers

`FakeItsmRecordTracking`: source id `900001` Found (two activities, two comments, synthetic status and stage labels),
`900002` Ambiguous, `900003` Unknown/Unavailable, `900004` Found with the activity part `Unknown/IncompleteResponse`
and zero comments, any other valid id NotFound. `FakeItsmUserMapping`: `synthetic.user` (exact case, like the source;
`Synthetic.User` is NotMatched) Matched, `synthetic.dup` Ambiguous, `synthetic.down` Unknown/Unavailable, any other
valid name NotMatched; registration numbers follow the source rule: `990001` (also `0990001`) is the same Fake user as
`synthetic.user`, `990004` another Fake user (conflicts with `synthetic.user` in `MatchAsync`), `990002` Ambiguous,
`990003` Unknown/Unavailable, any other valid number (also alphanumeric) NotMatched. Source id `900001` matches the
Fake OR record of the lookup.

## Vendor and team evidence (2026-10-12, sanitized)

Source: the vendor API document and the Turuncu Hat team's two replies (no real address, person, tenant, number or
registration value is recorded here).

- Endpoints `/login`, `/query`, `/insert`, `/update`, a separate attachment upload; Basic gateway header plus
  `/login`; `/logout` exists and is not called (the session simply expires).
- `/query` parameters: `BaseObject, Filters, Selects, NoEncode, Orders, PageCount, PageCounter, QueryPermissions,
  SessionID, TenantId`. `QueryPermissions` is `SELECT` or `UPDATE`, empty means `SELECT`. `NoEncode=true` returns
  Turkish characters unencoded.
- Query response `QueryResult{ErrorDescription, ErrorDetails, ErrorNo, TenantId, Items, MaxPages, PageNo,
  RecordCount}`; `SET.` is the value or id, `KEY.` the display name, `num` the row number.
- Object codes seen: OR `SMSS_oRFF`, OCO `SMSS_Operation_Change`, process `BPM_BizPrs`, activity `BPM_Actvty`;
  user `cmn_user`; comments `cmn_comment` (highest id = newest). Status meanings live in `ROMA_Status`; OR and OCO
  statuses differ. Field codes can be read in the Turuncu Hat screen with Ctrl + double click.
- `m_last_mod_dt` is the last-update field (also on OR). Time zone: "as seen in the response, may be +3" (not
  proven; raw text is shown).
- Insert example: `Success=true` and `InsertedId` filled while `ErrorDescription`/`ErrorDetails` are also filled
  ("ignore them"): see `turuncu-hat-jira-contract-gaps.md` and ADR-0032 decision 6.
- Third reply (2026-10-14): official registration-number rule (table above; the earlier "leading zeros are dropped"
  was wrong for six-digit numbers); `cmn_user` keeps passive users, add `m_active='True'`; `m_username` is whatever
  the identity system delivered and is case-sensitive; the OR form has a separate "Aşama" (stage) field, code to be
  confirmed by Ctrl+double-click (probably `p_rel_stage`); `ROMA_Status` fields were not given, so display text is
  read from `KEY.` cells; `!=` is supported; page numbers start at 0; read times look like
  `2026-10-14 17:26:45.0000000` without a zone, and time values in filters must be UTC; no rate limit (monitored);
  session about 60 minutes, logout not needed.

## Open items (ask the Turuncu Hat team)

1. Sanitized OR, OCO, activity, comment and user query responses for zero and one row (take them in TEST).
2. Meanings of `p_def_status` and `p_rel_stage` codes (`ROMA_Status` fields not given; `KEY.` display text is read
   instead, 2026-10-14). OR status field is `p_def_status`; the OR "Aşama" field code is to be confirmed in the TEST
   screen (probably `p_rel_stage`), and whether `KEY.` cells come back for both.
3. Time zone of `m_last_mod_dt` and of `p_proposed_start_date_time` / `p_proposed_finish_date_time`.
4. Whether `RecordCount` is the total of the filtered query (the team described it as the object total) and what
   `PageCount`/`PageCounter` mean (page size?), so the activity and comment reads can request a bounded page.
5. Exact OR/OCO number formats; the `m_mob` value for OR in `cmn_comment`, whether `cmn_comment` has a creation-time
   column (default `m_created_dt` is unproven), and the `m_process.m_main_object` value for OR.
6. The team confirms `!=` is supported (2026-10-14); the system-action exclusion has still not been executed.
7. One TEST query: a six-digit registration number with a leading zero matches in the six-digit form with
   `m_active='True'`, and whether the value comes
   back as text or number, with or without leading zeros (one TEST query, recorded without the real value).
