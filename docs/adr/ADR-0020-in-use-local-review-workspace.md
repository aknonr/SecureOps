# ADR-0020: In Use Local Review Workspace

## Archive Catalogue

Reporter suggestion uses the already persisted provider/tenant IdentityScope and
exact RFC SET.p_rel_requester reference. A server-owned, source-owner-reviewed
crosswalk may bind that pair to an existing application UserId, with review
reference, revision and expiry. No default links ship. This is not name matching,
an account provisioning route, or authorization. Current approval and review/view
capabilities are mandatory. Missing, ambiguous, expired and ineligible mappings
stay explicit. Acceptance/rejection/override is version/fingerprint-bound and
persists the source relation, mapping review and authenticated decision actor;
opening/selecting a suggestion alone performs no assignment.

024 adds an immutable archive-metadata projection with bounded SQL search and
paging. It does not change 023 receipts or archive bytes. Metadata is extracted
from each verified envelope only; legacy provenance may supply its own code and
server names. Current profiles never supply historical preparers. Current record
disposition and separately verified attachment evidence are labelled projections.
Explicit indexing of at most 25 selected versions can resume after interruption:
identical entries are no-ops, conflicts stop, and already indexed entries remain.
Current review/view access is checked under the administration transaction lock
before counts/pages. Missing metadata and incomplete historical coverage remain
visible. New reports freeze the trusted preparer's account in addition to label.

## Post-rc6.26 Draft Recovery and Corporate Workbook

The owner-approved continuation preserves four corporate worksheets in validated
legacy order. Internal provenance stays in the immutable envelope and authorized
application preview, not extra corporate worksheets. Historical bytes never change;
download headers may use the frozen source code, original preparer and UTC time.

Explicit Reset, Discard and Restart create audited aggregate revisions. Discard is
a local disposition, not source deletion. A monotonic invalidated-review watermark
prevents trial answers from becoming reuse proposals while retaining their history.
Existing JSON storage is extended; the SQL ReviewStatus column retains its existing
review-only meaning. Queries and reporting apply the separate disposition before
counting active work. Older binaries must not write these extended aggregates.
The access administration lock also serializes lifecycle commands with execution
creation/claim. Any active execution blocks local lifecycle mutation; no remote
effect is cancelled or replayed. Archives and completed remote facts are preserved.

## Post-rc6.22 Owner-Approved Continuation

The 2026-09-18 owner instruction authorizes the bounded end-to-end In Use v2
follow-up, including Razor, server review history/reuse, controlled executor,
necessary additive persistence and local release preparation. Its task-specific
size exception starts at aa9e4d2 and does not change permanent rules. See
../inuse-v2-followup.md. Existing local-only and blocked-intent implementations
remain historical evidence, not proof of remote completion. Corporate execution
and deployment remain outside this authorization.

## rc6.21 Completion, 2026-09-17

The owner authorizes the bounded completion exception from 778dca3, including the
preserved checkpoint. A server-owned InUsePolicy revision produces per-server
monitoring and organizational proposals. Explicit acceptance binds its fingerprint
to the current source version and authenticated review actor; ordinary saves retain
the accepted snapshot. Source changes require a fresh review. Unknown environments
remain unresolved. Proposals never set Verified, individual ownership or OS release.
Existing JSON persistence and immutable archive envelopes remain; no SQL delta or
external-write implementation is introduced. See ../rc621-completion.md.

## 2026-09-14 Local Recovery Continuation

Preserve the implemented source mapping and versioned review contract. Recoverable
HTTP/transport failures retain unsaved answers and assignment intent. Conflict
recovery explicitly compares the current version before preserving same-identity
edits; it never retries a write automatically. Pending responses must not apply
after route/filter/access changes or disposal. Unchanged access revalidation must
not discard edits; confirmed denial clears protected state. Navigation with unsaved
work requires confirmation, and navigation during a command waits for its outcome.
Session expiry retains the existing terminal-session/reauthentication boundary;
no draft is copied into browser storage or across authentication sessions.

Acceptance uses fresh task-owned SQL and the supported local paired Simulation
composition. Corporate adapters, production limits, schema and shipped read-only
protections are unchanged. Controlled local transport faults belong only to the
test harness. Full-format debt remains a separate required shared-delivery gate.

## Related Request Reporter, 2026-09-11

The observed SMSS_oRFF form labels p_rel_requester as Bildiren. Existing
Requester C# members are compatibility names, not verification of the separate
Istem Sahibi field. That selector remains unknown. Do not rewrite historical
snapshots or infer ownership, provisioning, assignment or reviewer identity.

DOM controls establish c_rfc_record and c_virtual_pc_user as direct candidate
Service Item properties only. The standalone candidate inspection queries these
through the existing bounded relation transport without a traversal contract.
An exact RFC hop requires operator-verified cell kind and identity/code meaning.
It selects only id, p_code and p_rel_requester, includes closed requests, and
keeps reporter display and stable reference separate. No invented Reporter field
is required. No candidate dictionary constitutes production mapping approval.

Prepare reusable projection/persistence behavior locally; connect production
explicit refresh only after sanitized operator evidence validates the mapping.
List/detail remain database reads. Preserve reviewer assignments, source version
checks, raw display fingerprints and single-pass plain-text presentation.

## Display Correction, 2026-09-11

Use persisted trusted access profiles for reviewer labels, never directory scans
or identity/name joins. Assignment and audit retain immutable application IDs.
Decode the evidenced entity-encoded requester and service display values once at
presentation/export, not in persisted source snapshots or fingerprints. Titles
are already decoded by the root parser and must not be decoded again. Existing
archives remain immutable; corrected output requires a new saved draft version.
RFC diagnostic traversal may omit Reporter: the established requester projection
is independently useful. Neither this nor a DOM selector approves a runtime RFC
mapping. The explicitly requested cross-layer display fixes preserve all policies.

## Stored Overview And Blocked Completion Intent, 2026-09-10

Source creation and lifecycle require explicit evidence; active query membership
does not establish an open lifecycle. New records retain local first-seen time;
legacy timestamps remain unknown, not reconstructed from last refresh. Management
counts are per stored OR, authorized by InUse.View plus Reporting.ManagementView.
Readiness means local structural review readiness, not corporate template approval.
Completion confirmation retains actor, command and the exact archived version/hash
under existing aggregate concurrency and atomic audit. No external adapter is wired:
unique BPM relation, upload response/idempotency, final OR reread and template
acceptance are unverified. Local state transitions distinguish failure/uncertainty;
uncertainty is terminal reconciliation, never an automatic write retry. Existing
JSON persistence/command tables suffice; no migration or runtime grant is added.

Status: Accepted for the explicitly authorized In Use V1 local milestone.

The 2026-09-10 workflow follow-up makes assignment optional for approved
InUse.View + InUse.Review users. Review provenance is the authenticated actor,
not the assignee; version checks protect concurrent operators. Assignment keeps
its separate capability and never grants review permission. Explicit refresh
holds a database-scoped application lock for 4241:68 before calling the source;
the in-memory substitute uses an equivalent non-waiting gate. Existing command
IDs still protect retries. The lock uses a dedicated transaction and releases on
disposal without holding record/refresh row locks during network reads. No new
schema, scheduler or external-write permission is introduced.

An optional source-owner dictionary contract extends only the existing standalone
evidence collector: exact direct RFC selector, semantic cell kind, identity/code
type and separate Reporter selector. One request hop, shared-reference deduplication,
strict bounds and shared aliases; no active In Use filter on the referenced OR.
This is fixture-tested collection, not corporate mapping or persisted ownership.
That earlier increment did not deliver management/completion. The stored overview
and local journal above now supersede that backlog portion; a real executor and
read-only reconciliation remain unimplemented and require verified source contracts.

The 2026-09-10 operator collection establishes the exact 27 semantic cells for
the existing 15-select service-item query, not global completeness. Explicit
refresh now enriches each bounded root with those fields only. KEY display and
SET reference identities remain separate; numeric inventory identity is required.
Reordering is immaterial; duplicate/conflicting keys or identities fail closed.
Absent optional fields remain unknown with their expected key as provenance.
Observed rows can support a local draft/report marked completeness unverified.
Failed enrichment and unverified disappearance retain prior rows; a Partial or
failed relationship cannot be archived as a current review. Source changes
invalidate saved drafts without changing their answers or the manual reviewer.
No Virtual PC User/RFC/creator/affected-assets lookup or inferred assignment.
The existing JSON snapshot stores the additive evidence without a migration.

The operator-effort follow-up limits normal input to outbound/inbound internet
and microsegmentation. Incomplete drafts remain valid; readiness requires an
explicit Yes/No for each of these fields on every verified service item. Bulk
copy changes answers only, after a field-level difference preview. Historical
notes and technical-check evidence remain stored, without new mandatory notes.

Reports are archived in a server-configured private directory, not a deployment
or static directory. An atomic envelope commits workbook bytes and provenance
together; record/version is the idempotency key. SQL audit authorizes the exact
version before file commit, and is not a claim that a file write succeeded.
An archive is immutable historical evidence, not a current source approval or
Turuncu Hat attachment. There is no automatic deletion or schema/grant change.
The application identity resolved from authenticated access supplies the actor;
transport service-account credentials never represent that human actor.

The 2026-09-09 delivery addendum keeps service items distinct from affected
assets. Additive JSON snapshot fields need no migration beyond 012. Unqueried,
denied, failed and ambiguous relationships are not an authoritative empty set;
failed enrichment retains previous evidence and invalidates review provenance.
An explicitly invoked, audited, exact-record diagnostic may read the evidenced
legacy relation query, but cannot interpret unverified response keys as inventory
or ownership. It requires both InUse.Refresh and operational diagnostics access.
No source navigation route is configured or inferred from task screenshot URLs.

Separate In Use discovery (active SMSS_oRFF category 4241/group 68) from the
existing SDM/Jira workflow. Reuse authenticated transport, exact-key parsing,
application capabilities, command tracking and transactional SQL audit.

Persist source snapshots separately from reviewed answers and local assignment.
Source changes invalidate drafts by source version. Every local mutation checks
the aggregate version. Retain absent records and all records on failed refresh;
bounded discovery never proves source closure. Corporate relationship response
keys remain unresolved until sanitized evidence establishes their exact shape.

Use the .NET ZIP/XML APIs for a minimal text-only XLSX without formulas, macros,
external links, Office automation or new dependencies. Retain legacy sheet and
column positions; blank legacy technical sheets remain blank. Add provenance
and explicitly unknown values rather than claim successful checks. Exports are
audited against a saved, current draft and exact record version.

InUse.View and InUse.Review grant only local workflow access; InUse.Assign and
InUse.Refresh are separate. Existing Admin receives these local capabilities;
explicit InUseReviewer and InUseCoordinator roles allow narrow approval. No
existing role acquires any additional external-write power. Scheduled refresh
is deferred to the planned Hangfire Worker after separate hosting approval.

## 2026-09-11 Verified RFC Reporter Wiring

Operator evidence establishes SET.(LCSIMS_ServiceInstance)m_rid.c_rfc_record
as String / OrCode: four direct items, one distinct exact lookup, four preserved
links. The operator attests browser agreement. KEY.p_rel_requester is Bildiren
display and SET.p_rel_requester its reference, not independently verified Istem
Sahibi, server ownership, provisioning operator or WASAS reviewer. Both Virtual
PC User cells were null; no populated-value mapping is approved. Private values
and comparison data are excluded from source. Historical evidence above remains
an account of its original delivery, not the current wiring contract.

Explicit refresh adds only the verified RFC select, reuses the diagnostic exact
resolver and existing session/access transport. Each item's own reference is
resolved once per refresh (maximum ten distinct target reads, existing 45-second
deadline and ten items per parent). Excess references remain NotQueried. Target
reads select identity/code/p_rel_requester only, without parent listing filters.
List/detail remain database-only and show related-request reporter with RFC.
Narrow UI integration is included in this explicitly requested end-to-end task.

Existing JSON persistence/version/audit boundaries are reused, with no new
migration, configuration, role or assignment. Failed/omitted evidence retains
old values and verification time, explicitly stale or failed; timestamps alone
do not change source fingerprints. A failed whole refresh also stales retained
reporters without advancing LastSeenAt. Optional reviewer and archives survive.
Local synthetic verification cannot establish broader corporate acceptance.

## 2026-09-12 Source/UI Acceptance Closure

Keep the verified RFC resolver/persistence unchanged. The server table must not
inherit the Excel preview's bounded vertical scroll region. All observed servers
remain in normal page flow, keyboard-selectable by stable identity; mobile uses
stacked rows. Ordinary rows show operational context, RFC, Bildiren and explicit
RFC-only matching/failure/freshness labels. Technical IDs stay in the existing
InUse.View-gated collapsed source evidence, not in ordinary person labels.

Current and retained raw display values use the same one-pass text boundary.
Different single/double-encoded fixture inputs intentionally produce different
inert output. Newly generated ReviewEvidence sheets include reporter text and
state separately from references; legacy columns and immutable archives remain
unchanged. Persisted trusted reviewer profiles, including missing/duplicate names,
are exercised without changing identity, authorization or authentication flows.
Acceptance uses existing isolated LocalDB schema only: no migration/repair/grant
or corporate execution. New synthetic records do not replace previous fixtures.

## 2026-09-22 Manual Post-Close Verification

Owner decision: new completion intents may defer automatic final OR readback.
The supported legacy operation remains BPM_Actvty update with m_status=4 on the
uniquely eligible activity, not a claim that the OR reached its terminal state.
Keep attachment content proof, source eligibility/concurrency and write fences.
Manual verification does not replace those preconditions or enable the unfinished
corporate transport. No source navigation URL is established; do not invent one.

Freeze Manual verification mode on new intents; old serialized intents retain
SourceReadback mode. Version the execution fingerprint so old queued intents
cannot silently adopt changed semantics. After BPM acknowledgement in Manual mode,
persist Unconfirmed and stop dispatch. Unknown mutations never retry. An explicit
negative Success response is Rejected; contradictory/malformed responses remain
Unknown. Completed still requires a separate authoritative Closure/Verified event.

An authorized operator may explicitly record manual closure confirmation for a
stopped, attachment-verified BPM acknowledgement/unknown result. Compare exact OR,
operation and revision inside the access/SQL transaction; record trusted confirmer
identity/label and server UTC time in append-only evidence and audit. This is not
system verification, does not release the active duplicate guard, does not alter
the original initiator, and cannot increase verified-closure reporting counts.
Existing 022 tables/JSON evidence support this without another SQL migration.
Matched component rollout and stopped writers are required for rollback; older
serializers/readers do not understand the new verification metadata.
# 2026-09-22 clarification: WASAS activity, not whole-OR closure

The approved business action uploads the reviewed report, updates the required
properties and approves only the uniquely eligible WASAS activity. A subsequent
team may remain pending while the overall OR remains Open. BPM acknowledgement,
verified WASAS activity completion, observed next stage, manual attestation and
verified overall OR closure are separate evidence categories. Historical manual
closure attestations retain their original meaning. New activity intents use a
new frozen execution contract; old blocked intents are not upgraded in place.

Source refresh retains saved answers. Review freshness depends on server review
context, not parent title or workflow progression; execution retains the full
source hash/version fence. Field differences remain visible before re-review.
New reports freeze trusted preparer/reviewer labels and accounts separately from
their application identifiers. Existing archive bytes are never rewritten.

Screenshots establish business semantics only. Attachment content reconciliation,
keyed dynamic-property concurrency and unique-activity conditional update/response
contracts remain required before corporate mutation. Final OR readback is not a
prerequisite for advancing the WASAS activity. No target flag or SQL change is
authorized by this decision.

## 2026-09-22 Archive Integrity Follow-Up

The supplied historical workbook now matches its immutable envelope byte for byte.
Keep historical six-sheet bytes even when current exports use four corporate sheets.
Archive integrity failures must map to `InUseArchiveIntegrityFailed` before the
authorization/audit callback or any download; `InvalidDataException` is not an
`IOException`. Do not reclassify exceptions raised by the authorization callback.
This is a narrow error-category correction, not an archive repair or new format.
Sealed E-08 remains unchanged; subsequent source/testing is recorded separately.

## Scope Clarification After wip.4179fca3c22c

The owner defers next-team details and the comprehensive workflow timeline. Keep
only source-backed WASAS activity status for actionable/completed/verification-
pending separation. Actionability retains all existing eligibility, authorization,
attachment and concurrency guards; acknowledgement or manual confirmation is not
authoritative activity completion. Missing/ambiguous readback remains pending;
zero pending rows alone is not completion and does not permit automatic retry.
Separate required upload/property/approval contracts (IU-05) from the minimal
bounded activity-status query (IU-05-STATUS). Automatic post-approval readback and
overall OR closure are not required; manual verification remains explicitly labelled.
Next-team/timeline work is deferred as IU-07-NEXT, not a delivery gate. Preserve
already stored observations and historical decisions above without expanding them.
This is a documentation scope correction only; E-08 and the archive-integrity fix
remain intact. The completed v19 comparison is not reopened.

## List Ordering and Minimal Status Implementation

Owner correction, 2026-09-23: chronological ordering is optional and deferred,
not a delivery/source-owner prerequisite. Default API/UI ordering is `code`, with
stable Code/record-ID ties before paging; UI labels it `OR numarasina gore` and
disables date choices with an explanation. Old date-sort UI links fall back to
code. Explicit oldest/newest backend support and its tests remain for future use
with verified creation evidence. The date-first default described below is the
superseded implementation checkpoint, not the active product default.

The next implementation orders persisted lists before paging by verified parent
request creation time, oldest by default or newest when selected. Unknown, invalid,
offset-free and future dates sort last in both directions. Code and ordinal record
identity break ties. Refresh/first-observation times are never creation dates.
The real parser currently returns no creation date: a keyed parent creation field
and timezone contract are still required, not guessed from a referenced RFC.

Keep review and tracking views; add source-pending and verification-pending views.
Only provenance-backed pending activity evidence without an active execution fence
is a source-pending candidate; this is not permission to execute. Existing command
eligibility, authorization and attachment/concurrency checks still apply. Missing
discoveries retain answers and prior evidence but cannot assert current pending
eligibility. Verified completion remains historical source evidence, distinct from
overall OR closure; local intent and manual confirmation never promote completion.
These rules are locally testable with labelled fixtures, not a replacement for the
missing corporate IU-05-STATUS query. Next-team/timeline stay deferred.
