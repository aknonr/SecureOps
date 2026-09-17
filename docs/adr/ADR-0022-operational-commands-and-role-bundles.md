# ADR-0022: Operational Commands And Role Bundles

Status: approved for local implementation by owner continuation, 2026-09-15.

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
