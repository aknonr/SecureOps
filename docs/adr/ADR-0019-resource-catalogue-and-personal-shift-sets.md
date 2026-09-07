# ADR-0019: Resource Catalogue and Personal Shift Sets

**Status:** Accepted for backend implementation; deployment validation separate
**Date:** 2026-09-06

## Decision

### Bounded Workspace Follow-up, 2026-09-07

The authorized usability follow-up adds optional WorkspaceLayout to existing
personal JSON: cards/list, comfortable/compact, page size 10/25/50/100, and at
most four unique shortcut keys (links/groups/requests/catalogue). Legacy reads
use defaults without a write. Every read filters shortcuts by current capability;
every save rejects unauthorized keys and arbitrary routes. Only the current
owner's versioned aggregate is mutable, including for Admin. Layout reset changes
layout only; favourites, default group, guide preference and hidden membership
survive. No migration, new SQL grant or browser storage is introduced.

POST resources/links/resolve revalidates 1-100 distinct IDs under current
visibility and preserves requested order. Missing/hidden details are never
returned. Resolution is read-only and cannot open a browser destination. Opening
requires a subsequent explicit native browser activation; no retrying personal
writes, bulk publication or browsing telemetry is added. Existing set merge
semantics support atomic selected-item additions. Bulk favourites remain outside
this increment. Previous size exceptions do not carry forward; commits remain
bounded by the permanent rule. rc6.12 excludes these later runtime changes.

This explicitly authorized Phase 2 support milestone adds local application data,
not target-system integration or remediation. No target URL is fetched or executed.
Shared categories and links use SQL tables, optimistic bigint versions, archive
flags, and transactional append-only audit. A bounded personal aggregate stores
favourite IDs and named ordered sets as JSON under the existing internal UserId.
One aggregate version serializes all personal edits, including default selection.
References are resolved against current catalogue visibility on every response;
missing, inactive, archived, or inaccessible links are omitted without identifiers
or counts that reveal hidden entries. Duplicate set references are rejected.

Resources.View is granted through reviewed application roles. Resources.Manage is
limited to Admin and the purpose-specific ResourceCurator role. Lead does not gain
management. Existing Admin role-assignment operations assign ResourceCurator; no
new approval workflow or grant to a real user is part of this task. Categories may
be manager-only; child links inherit this restriction. Catalogue authority never
grants permission on the destination system.

URLs must be absolute HTTPS without userinfo or fragments. A positive query-key
allowlist permits bounded non-secret dashboard filters; arbitrary query keys,
nested URLs, executable/local schemes and credential-bearing links are rejected.
Content is bounded plain text. Audit records only action, internal actor/entry ID,
version and safe field names, never URL/query values or content. Browsing and set
resolution are not audited as browsing activity. Personal mutations record only
aggregate version and action, not selected links or set names.

Migration 010 follows unchanged migrations 001-009. The application never applies
migrations. Production durability requires SQL; the in-memory implementation is
an explicitly selected test substitute. No new packages or framework changes.

## Scoped Authorization and Evidence

The owner explicitly authorized a task-scoped exception to the 1,000-line diff
limit for this complete milestone, including generated OpenAPI, tests and docs.
The permanent rule is unchanged. Starting HEAD is
`c18d196ba14905df92e57fe231c2e31b95d113de`. Commit splitting is for reviewability,
not concealment of total size. Final totals and SQL execution evidence belong in
`docs/24-api-test-deployment-readiness.md` and the completion report.

The SDM positive-policy and approval milestones remain pending. Turuncu Hat/Jira
read-only fences remain true/false respectively; no deployment is implied.

## Resource Experience and Integrity Milestone

The owner authorized Codex UI work and narrowly required backend/contracts/tests,
normal push to the verified existing branch, and a milestone-only 1,000-line
exception on 2026-09-06. Baseline: `e05977d158bfd533aa71caa0ac60f276bbc9ef37`,
branch `feature/sql-runtime-hardening-20260902`. Permanent ownership and size
rules remain unchanged. No deployment or corporate operations are authorized.

The existing personal-set PUT must no longer interpret omissions as deletions:
the response is a visibility-filtered projection, not a complete editable snapshot.
`linkIds` supplies additions and relative order; additive `removeLinkIds` supplies
explicit removals of currently visible members. Both lists must be unique and
disjoint. Every omitted saved reference survives, even if visibility changes
between reading the projection and saving. This also protects legacy callers.
After explicit removals, requested members occupy their existing ordered slots;
unmentioned members retain their relative positions, and surplus requested members
append. Archive/restoration and capability changes never erase membership.
Deleting the entire owned group is the explicit way to discard all references.
No hidden IDs, names, counts or URLs are returned. The aggregate limit, ownership,
optimistic version check and transactional audit still apply to the merged state.

Environment options are queried server-side from permitted links, independently
of catalogue pagination. A bounded searchable result returns at most 100 values
and a truncation flag; users can refine the search. Manager archive inclusion is
explicitly authorized. No client downloads the catalogue to derive facets.

The lightweight resource guide stores only an invitation-dismissed boolean in the
existing owner-scoped preference JSON, under the same version/audit boundary.
No migration, browser storage, browsing telemetry or training-progress system is
introduced. Turkish presentation uses application links and personal link groups;
stable API/DTO/database names are unchanged. The canonical UI README handoff is
updated with actual local verification and remaining TEST gates at completion.
