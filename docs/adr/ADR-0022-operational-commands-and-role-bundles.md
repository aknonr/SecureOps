# ADR-0022: Operational Commands And Role Bundles

Status: approved for local implementation by owner continuation, 2026-09-15.

## Owner Amendment: Service Accounts Admin Operations, 2026-10-03

The owner explicitly approves all seven current Service Accounts capabilities for
the genuine persisted protected Admin bundle: View, Work, Assign, Verify, Import,
Report and Administer. This supersedes the navigation-only operational-capability
restriction in the 2026-10-02 amendment below, for this module only. It does not
grant unrelated capabilities or automatically include future module capabilities.

Reviewed migration 028 adds only missing module capabilities after the 026/027
baseline, preserves other role permissions and identities, advances role and
affected access versions, and requires same-transaction audit. It rejects replay.
The in-memory reviewed catalog matches the SQL bundle; production authorization
still comes from persisted approval/assignment, never a display name or claim.

Explicit Organization/Team/All scope remains a separate persisted grant. Admin
does not gain global inventory scope, self-grant or self-escalation exceptions.
An independent authorized actor is still needed to grant scope to the Admin.
Business validation, action verification requirements and fail-closed audit stay
unchanged. No provider, integration, scheduler, SQL runtime grant or principal
membership is activated by this decision. Source work is approved; target SQL,
installation and activation remain separate owner-executed operations.

## Owner Amendment: Administrative Page Access, 2026-10-02

The genuine system-administrator is an Approved application user with an active
persisted assignment to the protected `Admin` bundle, not a login claim or display
name. That bundle includes `ServiceAccounts.View` and `ServiceAccounts.Administer`
so administrators can open the module and its administration page. The reviewed
027 data migration extends the existing protected bundle without removing prior
capabilities, advances its version and affected access versions, and commits the
required audit atomically. No startup repair or claim-based capability fallback.

Module data still requires an explicit All/Organization/Team grant from another
authorized administrator. An administrator without scope may open administration
and the module's empty/no-scope view, but cannot read an ungranted account or grant
scope to themself. Work, Import, Assign, Verify and Report remain separate
capabilities. Page access never activates a provider, integration or scheduler;
the existing manual reminder evaluation is an explicit Administer command and
must not be invoked as a page-access check. Worker/reminders remain outside this task.
SQL runtime grants and application capabilities are independent. Target changes
and installation remain separately approved; this amendment authorizes local
source implementation only.

Preserve the nine existing role identities and permissions. Store versioned business
role definitions as registered capability bundles, with Turkish action descriptions.
Preview effective differences and affected users before definition changes. Serialize
administrative writes, protect the core Admin role, prevent self-escalation and loss
of the last active Admin, and advance affected users' access versions atomically with
required audit. Neither directory titles nor client actor fields grant authority.
Use SQL-bounded stable paging for administrator lists; preserve legacy contracts.

Separate OCO source, preparation, self-test and send capabilities. New external-send
rights are unassigned by migration. SMTP is opt-in and disabled by default; source
collection uses the existing console Worker and dedicated Hangfire queue. A send
command freezes the trusted initiating actor, saved preparation, audience and version.
Self-test replaces the envelope audience with that actor's saved Mail only, without
changing the draft. Revalidate authority before dispatch. Queue replay is idempotent;
an interrupted/ambiguous dispatched command is not automatically sent again. SMTP
acceptance means relay acceptance, not inbox delivery. No distributed transaction is
claimed across SQL and SMTP/Jira/source systems.

Operational events retain a versioned actor/action/record/command/time/outcome contract,
minimal trusted profile snapshots and correlation. Initiator, assignee, executor and
verifier are different fields; unknown source-system closer stays unknown. Required
intent and audit commit before an external effect. History is append-only. Future
agents must use the same authorized commands with explicit scoped delegation; no
agent identity, impersonation, direct workflow SQL or robot is enabled here.

Jira-only intent never becomes close intent after a configuration change. An activity
update acknowledgment cannot establish authoritative OR closure. If the exact source
post-state contract is unavailable, use a typed unavailable verification boundary and
keep closure blocked; do not infer transitions or retry an uncertain external write.

Preserve original branding bytes with bounded encoded size, aggregate size, decoded
dimensions, image count and MIME type checks. Retain immutable historical MIME.
Use consequential preview/confirmation, field-specific errors and recoverable edits,
not repeated confirmation on routine draft saves. The existing design/framework and
foreground hosting architecture remain unchanged. Corporate activation is separate.

Post-rc6.24 owner continuation retains this architecture and explicitly includes
both In Use completion and optional OCO mail. Mail confirmation presents frozen
dates/count alongside sender/audience. Known In Use mutation request fields are
not unknown contracts; attachment reconciliation, conditional target semantics
and authoritative OR state still require source evidence. The bounded read-only
completion probe uses script-backed projections only, and cannot approve writes.
See `docs/rc624-workflow-continuation.md` for scope exception and cumulative baseline.
