# ADR-0020: In Use Local Review Workspace

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
