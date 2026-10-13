# ADR-0032: Turuncu Hat OR Tracking and Exact User Match (read-only)

**Status:** Proposed (owner acceptance pending); implemented behind the Disabled default, no real Turuncu Hat call made
**Date:** 2026-10-12 (amended 2026-10-13: registration-number match, see the amendment at the end)

## Context

Service Accounts will open an OR for a gMSA switch (ADR-0030, SA coordinator). After that the panel needs to
follow the OR (stage, last update, new comment or reply) and, when opening it, to know the Turuncu Hat user id of
the signed-in panel user ("Bildiren"). ADR-0031 gave a read-only record lookup (existence, title, raw codes) but
returns no tracking data and deliberately no person field. The Turuncu Hat team has since answered (sanitized
summary: `turuncu-hat-itsm-record-lookup.md` section "Vendor and team evidence"): user object `cmn_user` with exact
fields `m_username` and `c_registration_number`; readable `id`, `p_obs_id`, `m_rel_home_lbs_id`, `m_phone_number`;
activity history `BPM_Actvty` filtered by the process main-object id, excluding system actions; comments
`cmn_comment` (highest id is the newest); last update `m_last_mod_dt`; `NoEncode=true` keeps Turkish characters
unencoded; `QueryPermissions` defaults to SELECT; `RecordCount` is a total. The OR status/stage field names and
the `ROMA_Status` code list are still unknown. Turuncu Hat may be replaced after January/February 2027, so the
contract is product-owned and only the adapter is Turuncu Hat specific.

## Decision

1. **OR tracking summary** (`IItsmRecordTracking`, read-only, by source record id from an ADR-0031 Found result):
   - header: raw last-update text (`m_last_mod_dt`); raw status and stage codes only when their field names are set
     in server configuration (`OrStatusField`, `OrStageField`), otherwise not selected;
   - activities: id, group, status, created, completed from `BPM_Actvty`, system actions excluded, ordered by id,
     at most 50 rows; more rows or any page/completeness doubt makes this part Unknown (never a silent cut);
   - comments: count and the raw creation time of the highest-id comment from `cmn_comment`, keyed by the
     configured `m_mob` value (`CommentMainObject`) and the record id. No comment text, no author. If
     `CommentMainObject` is empty the part is Unknown/NotConfigured.
   - Each part carries its own Known/Unknown so a partial answer stays visible. Known-empty (zero activities, zero
     comments) requires a complete, successful, single-page response. "Not found" never comes from a failure.
2. **Exact user match** (`IItsmUserMapping`): the caller passes the user name derived from the server-verified
   session identity only. The adapter applies a strict allow-list (1-64 ASCII letters, digits, `.`, `_`, `-`; no
   domain prefix, UPN, space, quote or wildcard), sends one equality filter on `cmn_user.m_username`, selects only
   `id`, `m_username` (to verify the filter was honoured), `p_obs_id` and `m_rel_home_lbs_id`, and returns
   `Matched | NotMatched | Ambiguous | Unknown`. Zero rows, more than one row, any failure or an unverified row is
   **not a mapping and the consuming flow stops** (same posture as ADR-0012 identity lookup).
   - No free-text, prefix, partial or person search exists. This is an exact equality on an identity that the
     server already authenticated; it is not people-search (AGENTS rule 5).
   - The registration number (`c_registration_number`, leading zeros dropped) is **not implemented**: the product
     user record holds no trusted registration number, and no second path is written without one. *(Superseded by
     the 2026-10-13 amendment: the product user record does hold one.)*
   - The phone number (`m_phone_number`) is not selectable. It needs an explicit request from an accepted ADR-0030
     consumer and its own field permission; until then it stays out of the contract and the code.
   - Person name, e-mail and the completing person (`m_done_by`) are never selected, returned or logged.
3. **Wire rules for every ITSM read** (also corrects ADR-0031 TH-2): requests carry `NoEncode=true`; `QueryPermissions`
   is never sent (source default SELECT); the page rule follows the vendor example (0-based `PageNo`, `MaxPages`
   0 on a single page, `RecordCount` the total across pages): more than one page, or `RecordCount` above the
   returned rows, is incomplete. The request sends no page parameter, so a result larger than the vendor default
   page is Unknown rather than guessed. Cells are still read by exact `SET.` key; a few fields that the source may
   omit when empty are optional and read as null.
4. **Configuration:** `OrStatusField`, `OrStageField`, `CommentMainObject`, `ActivityMainObject` (optional
   `m_process.m_main_object` value) are empty by default; `CommentTimeField` defaults to `m_created_dt`. All are
   identifier-validated at startup. Object names `BPM_Actvty`, `cmn_comment`, `cmn_user` are vendor-documented
   standard objects and are constants in the adapter. Disabled stays the default and nothing falls back to Fake.
5. **No write, no API endpoint, no OpenAPI, no SQL, no UI.** Modules call the interfaces from their service layer.
   Writing to Turuncu Hat (including the OR creation of ADR-0030) is unchanged and out of scope. A consumer stores
   the result state, provider and check time as its own evidence; Fake results are synthetic.
6. **Insert warning for ADR-0030 (evidence, not a change):** in the vendor insert example `Success=true` and
   `InsertedId` are filled while `ErrorDescription`/`ErrorDetails` are also filled, and the document says to
   ignore them. ADR-0012's rule that update success must not override contradictory error metadata, if applied to an insert, can misjudge it
   and lead to a duplicate OR on retry. ADR-0030 must decide insert success from `Success` and `InsertedId` plus
   reconciliation, not from the error text alone. This ADR changes no SA code.

## Ownership Exception

Owner decision 2026-10-12: Claude implements TH-4 as a continuation of the TH-2 exception (ADR-0031). Scope: the
`IItsmRecordTracking` and `IItsmUserMapping` contracts, their Turuncu Hat, Fake and Disabled providers,
registration and validation, configuration options, unit tests, this ADR and the contract documents. Not covered:
writes, API endpoints, OpenAPI, SQL, UI, and the existing `TuruncuHat*` operational-record, In Use and
announcement files, which this work does not modify.

## Activation Gate

- Accept this ADR (and ADR-0031).
- From the Turuncu Hat team or by Ctrl+double-click in the TEST screen: the OR status and stage field names; the
  value of `m_mob` that identifies an OR in `cmn_comment`; whether `cmn_comment` has a creation-time column and
  its name; the `m_process.m_main_object` value for OR; `ROMA_Status` fields if status codes should be read.
- The meaning of `PageCount`/`PageCounter` (is it a page size?) so the activity and comment reads can request a
  bounded page instead of relying on the vendor default.
- One read-only run in corporate TEST with server-only configuration and sanitized evidence (no real numbers,
  names, ids or addresses in the repository). The filter operator `!=` on the system-action exclusion is taken
  from the team's description and has not been executed.

## Consequences

- The SA panel can show stage/last update/new-comment signals for its OR and map the acting user without
  inventing data; any missing piece shows as Unknown with a reason.
- Status and stage are raw codes; the product does not say "approved" or "closed" until the owner approves a
  code list (ADR-0031 decision 4 applies).
- A user whose `m_username` differs from the panel identity (domain suffix, alias) is NotMatched and the flow
  stops; that is intended and visible, not silently repaired.
- Replacing Turuncu Hat means new adapters for the same two contracts; stored evidence stays valid.

## Rejected Alternatives

- Extending `IItsmRecordLookup` with tracking methods: would break every existing implementer and mix a header
  lookup with multi-query tracking.
- Selecting `m_done_by`, comment text or phone by default: person data without a consumer that needs it.
- A user search by name or registration number prefix: people-search (AGENTS rule 5).
- Counting comments from `RecordCount` alone: the team's description of `RecordCount` is ambiguous (object total
  versus query total); only a complete single-page response is counted.
- Guessing the OR status field name: an unproven select could fail every OR read.

## References

- ADR-0004, ADR-0009, ADR-0012, ADR-0016 (OIDC profile metadata), ADR-0030 (reserved, Service Accounts), ADR-0031
- `docs/integrations/turuncu-hat-itsm-record-lookup.md`, `docs/integrations/turuncu-hat-jira-contract-gaps.md`

## Amendment 2026-10-13 (TH-4b): registration-number match

**Context.** Decision 2 left the registration number out because the product user record was thought to hold no
trusted value. It does: product users sign in through OIDC, and the server writes the validated `uid` claim to
`security.Users.Uid` (ADR-0016 profile metadata, written only from the validated claim at sign-in; a later
sign-in without the claim keeps the previous value). The owner reports, for corporate TEST, that all
12 users have it, each 6 ASCII digits, no duplicates, four with a leading zero; `LoginName` is a plain user name.
The Turuncu Hat team states that `cmn_user.c_registration_number` is filled and unique and is searched without
leading zeros. Owner decision 2026-10-13: add the registration path now, ahead of the user name.

**Decision.**

1. **Registration path** (`IItsmUserMapping.MatchByRegistrationNumberAsync`): input is 1-20 ASCII digits after
   trimming outer whitespace (leading zeros count toward the 20); leading zeros are dropped; a value of only zeros,
   letters, signs, inner whitespace, quotes, wildcards, filter markers and non-ASCII digits are rejected as
   `Unknown/InvalidIdentifier` without a remote call (`ItsmRegistrationNumber.Normalize`). One query on `cmn_user`
   with the single equality `#%c_registration_number%#='<digits>'`, selecting only `id`, `c_registration_number`,
   `p_obs_id`, `m_rel_home_lbs_id`. The returned number is normalized the same way and must equal the searched one;
   otherwise the filter was not honoured and the result is `Unknown/InvalidResponse`. The outcome is again
   `Matched | NotMatched | Ambiguous | Unknown` with the same completeness and failure rules as the user name.
2. **Combined match and priority** (`IItsmUserMapping.MatchAsync(registrationNumber, userName)`); null or blank
   values count as not given:

   | Registration result | User name not given | User name given |
   |---|---|---|
   | (not given) | `Unknown/InvalidIdentifier`, no call | user-name result |
   | `Unknown` (any reason, also an invalid number) | returned | returned; the user name is **not** tried |
   | `Ambiguous` | returned | returned; the user name is not tried |
   | `NotMatched` | returned | user-name result (the user name may be tried) |
   | `Matched` | returned | user name `Matched` with the same user id → `Matched`; with another user id → **`Ambiguous` (conflict)**; `NotMatched` → the registration match stands; `Ambiguous` or `Unknown` → that result (no conflict-free answer is proven) |

   Unknown is never read as "absent", so an unproven registration answer never falls back to the user name. A
   conflict is never resolved by preference; both users are withheld and the flow stops. Every non-`Matched`
   outcome still stops the consuming flow (decision 2).
3. **Personal-data boundary.** The registration number is a query key only. It is not returned (the result echoes
   neither key), not stored by the mapping, not written to logs, telemetry tags or error text; logs carry only the
   key kind ("registration number" or "user name") and the reason. The caller passes the value the server holds for
   the signed-in user (`security.Users.Uid`); reading it is the consumer's job, and this amendment changes no
   Access or OIDC code. There is still no search by registration number for anyone else, no prefix or partial
   match (AGENTS rule 5).
4. **Telemetry:** operations `itsm-user-match-registration` (registration path) and `itsm-user-match-combined`
   (combined; outcome `conflict` when the keys matched different users), next to `itsm-user-match`.
5. **Fake:** `990001` (also `0990001`) is the Fake user of `synthetic.user`; `990004` is another Fake user, so it
   conflicts with `synthetic.user`; `990002` Ambiguous; `990003` Unknown/Unavailable; any other valid number
   NotMatched. All synthetic.

**Known risk, accepted by the rule above.** A NotMatched registration followed by a Matched user name is accepted
as a match by user name. If the Turuncu Hat user found by name carries a different registration number, the two
systems disagree and the user-name match hides it. The registration column is not selected on the user-name path
(decision 2 keeps that path's selects); closing this needs an owner decision and evidence from corporate TEST.

**Ownership.** Owner decision 2026-10-13: Claude implements TH-4b under this ADR's exception, with the same scope
and exclusions (no write, API endpoint, OpenAPI, SQL, UI; no Access/OIDC, Service Accounts or existing `TuruncuHat*`
operational-record, In Use or announcement files).

**Activation gate addition.** One read-only run in corporate TEST that confirms the `c_registration_number`
equality filter without leading zeros and the returned value's shape (text or number, with or without leading
zeros), recorded without real numbers, names or ids in the repository.
