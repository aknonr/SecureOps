# ADR-0021: Planned announcement drafts

Status: accepted for this explicitly authorized local draft-only increment.
Base: `81f5757a869442ae92b0c90a3d7dfebab9796d6d`; branch
`feature/planned-oco-drafts-20260912`; worktree
`C:\SecureOpsBuild\secure-ops-planned-oco-20260912`.
The release checkout, its hosts and rc6.17 readiness records are excluded.

Reuse persisted access/capability validation, owner isolation, SQL optimistic
versions and transactional audit. New announcement revisions live in their own
append-only table, not In Use JSON. The API delegates rendering to Infrastructure.
This increment provides PUT and GET representations by client-generated UUID;
paginated draft discovery, saved recipient sets, send history and UI follow later.
Only Admin has Announcements.Drafts initially; no user or new role is assigned.

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
