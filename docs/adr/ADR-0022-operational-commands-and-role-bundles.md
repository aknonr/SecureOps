# ADR-0022: Operational Commands And Role Bundles

Status: approved for local implementation by owner continuation, 2026-09-15.

## Owner Amendment: Module-Based Access View, 2026-10-03

Administrators see access per product module. `GET /api/v1/access/modules` lists every registered action
in catalog module order with the business roles that grant it; `GET /api/v1/access/users/{id}/effective`
and `GET /api/v1/access/me/effective` explain one user's actions as granted/not granted with the assigned
roles that grant each. Both are read-only projections (`AccessModuleView`): persisted user capabilities are
the authority, roles only explain them, a non-Approved user has nothing granted, and a persisted
capability outside the catalog is shown under "Diğer" instead of hidden. Role definitions are read through
`IAccessRepository.GetRoleDefinitionsAsync` for every provider; role editing stays SQL-only with the
existing preview, protection, self-escalation and last-administrator guards. No capability, role or SQL
object is added.

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
