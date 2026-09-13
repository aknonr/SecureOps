# ADR-0021: Planned announcement drafts

Status: accepted for this explicitly authorized local draft-only increment.

Acceptance repair from 38f6941: temporary invalid dates remain invalid; API save,
preview and export validation is unchanged. Same-context preview retains its last
successful content with explicit stale status during edits/errors. A sandboxed
staging frame loads before presentation, without reading its internals. Access
loss, navigation and draft replacement clear private content and cancel results.
No source/profile scope, SQL, dependency or external-write changes belong here.

Editor continuation from e48f13e: new UI drafts select v2; legacy drafts require an
explicit versioned upgrade. A debounced transient preview transforms caller-supplied
bounded content through the same renderer, without reading draft records, writing
revisions or sending. Capability/session checks are never cached. This transformation
is not a privileged stored-content read; existing discovery/save/read/download audit
remains fail-closed. Incomplete fields stay blank. Preview retention is defined above.
Configured manual display offsets never reinterpret source LMT or change an existing
instant. The 1000-line cap defers the separate source/profile slice: Worker has no
Hangfire composition and SCCM is absent. Required durable jobs, adapters, SQL/audit,
reviewed application UI and tests are estimated at 1300-1700 additional changed lines.
DateTextRevision independently versions email date presentation: omitted iso-v1
preserves existing rendering/hashes; new UI v2 drafts and explicit upgrades use
tr-v1, fingerprinted with the presentation. Raw ISO dates/instants stay unchanged.

Continuation from 142a48f: explicitly selected `oco-table-v2` adds a bounded
structured affected-service list, separate from the system/application Scope,
and an immutable six-role private asset bundle. `oco-v1` rendering and old JSON
defaults stay unchanged. No implicit template migration or distribution-request
recipient reuse. The bundle fingerprint binds ordered validated assets and footer;
missing/changed configuration blocks export, never substitutes an image. This
bounded increment does not implement SCCM/source jobs or profile defaults. Worker
composition is currently a stub; source work needs a separate Hangfire/SQL slice.
Initial isolation base: `81f5757a869442ae92b0c90a3d7dfebab9796d6d`; branch
`feature/planned-oco-drafts-20260912`; worktree
`C:\SecureOpsBuild\secure-ops-planned-oco-20260912`.
The release checkout, its hosts and rc6.17 readiness records are excluded.

Reuse persisted access/capability validation, owner isolation, SQL optimistic
versions and transactional audit. New announcement revisions live in their own
append-only table, not In Use JSON. The API delegates rendering to Infrastructure.
The initial increment provided PUT/GET representations by client-generated UUID.
Discovery and UI are implemented below; saved recipient sets and send history remain pending.
Only Admin has Announcements.Drafts initially; no user or new role is assigned.

UI continuation from a1818e17 is owned by Codex, including Razor and browser tests.
Reuse authenticated API/session clients, WASAS shell, explicit saved previews and
existing resource-guide infrastructure. A separate AnnouncementGuideDismissed flag
in the versioned owner-only personal preferences keeps module invitations independent.
No browser storage, auto-save, polling, sending or new authentication is introduced.
Unsaved edits survive failures; conflicts require comparison and explicit adoption
of the current version before a separate Save. Preview is sandboxed, never raw HTML.

Follow-up from 1e6454cebfd0944d114bd413fd462b37499bc66c adds owner-scoped
SQL pagination (page 1, size 25, cap 100), latest revision only, SavedAt descending
and UUID text ascending as tie-breaker. One serializable read gives consistent
count/page; separate requests are not a frozen snapshot.
Owner transaction application locks (Shared list / Exclusive save, public DB
principal, bounded wait) prevent the observed owner-index/PK lock-order deadlock.
Additive 015 indexes (OwnerId, Id, Version DESC); JSON bodies are never returned by list queries.
New saves persist a derived missing-field count. Historical JSON is untouched;
old revisions report unknown completeness until explicitly saved again.
Banner discovery is bounded to 32 allowlisted revisions with optional labels;
only file metadata is inspected, never image decoding/rendering per row. Presence
is not validation: selected saves/previews/exports still validate bytes and hashes.
No cache, source calls, sending, scheduling or role changes are introduced.

All submitted content is manual/unverified, including OCO reference and scope.
Provenance is server-stamped Manual, never source attestation. Source integration
must later retain source snapshots separately from overrides. Dates are distinct;
work/restart values require explicit ISO offsets. No m_active/collection approval
or timezone inference. Legacy PowerShell was parsed as UTF-8, not executed. MSG
compound-file bytes contained remote JPG/PNG image tags; no URLs were fetched or
private content copied. TCP 25 evidence does not prove TLS, relay or delivery.

MimeKit creates MIME; SkiaSharp validates bounded local PNG/JPEG assets. Versions
are centrally pinned. No SMTP library/client or sending endpoint is introduced.
An allowlisted banner revision resolves under a private server-owned directory;
hash-bound bytes are identical in data-URI preview and CID MIME. Text is escaped
without HTML decoding. Deterministic template revision is immutable. The sender
is configuration-owned and captured on save. GET export requires current version,
records DownloadPrepared (not Sent), and cannot confirm or enqueue a send.

Next sending contract: immutable intent binds draft revision, recipients, sender,
rendered alternatives, template and exact asset bytes/hashes. A SQL unique intent
plus existing command idempotency prevents repeated clicks/jobs. Hangfire + SQL
dispatch rechecks access and default-off write gates. States: Queued, Processing,
SmtpAccepted, KnownFailure, UnknownOutcome; acceptance is not mailbox delivery.
No generic retry after uncertain submission; Message-ID is not deduplication.
Persist per-recipient acceptance and reconcile before any targeted retry. Editing
invalidates confirmation; sent revisions/history never change. History is bounded,
paginated, permission-controlled operational evidence, not employee ranking.
Implement local capture first; SMTP/SCCM configuration is independent of protected
Turuncu Hat configuration. No source adapter, worker, auth or external write changes.
