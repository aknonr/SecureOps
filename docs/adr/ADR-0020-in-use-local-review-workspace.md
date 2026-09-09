# ADR-0020: In Use Local Review Workspace

Status: Accepted for the explicitly authorized In Use V1 local milestone.

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
