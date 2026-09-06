# ADR-0019: Resource Catalogue and Personal Shift Sets

**Status:** Accepted for backend implementation; deployment validation separate
**Date:** 2026-09-06

## Decision

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
