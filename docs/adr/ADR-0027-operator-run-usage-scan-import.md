# ADR-0027: Operator-run service account usage scan, imported as evidence

**Status:** Accepted by the module owner (2026-10-04) under the project owner's delegation for the Service Accounts module
(`docs/service-accounts/README.md`, "Authority"); it takes effect only when the project owner merges it. **Corporate
security review and deployment approval are not claimed.** Schema change `SA-004` is numbered **030**
(`sql/migrations/030-service-account-usage-scans.sql`); nothing is applied to an installed system without the project
owner's separate approval.
**Date:** 2026-10-04
**Decision makers:** project owner (scope and merge); module owner (design). Related: ADR-0024 (proposed JEA/Worker
discovery, shelved 2026-10-04), ADR-0026, SPEC rule 15.

## Context

Before a gMSA conversion the executing team must know on which servers an account runs a Windows service, a scheduled
task, an IIS application pool or an IIS site/application/virtual directory identity; after the conversion a verifier wants
evidence that those components now run as the gMSA. Today a person finds this by hand with the team's own PowerShell
tool and keeps the result outside the module.

ADR-0024 proposed a Worker job through a new JEA function. The project owner shelved that path on 2026-10-04 (Bilgi
Güvenliği approval and a server pilot are open). The owner asked instead that **a person runs the scan under their own
authority and uploads the output**, and that the module attaches the result to the account as evidence and uses it in
the gMSA conversion.

Fixed constraints: read-only (AGENTS.md rule 1; conversion automation is Phase 8, ADR-0006); every WinRM connection made
by platform code goes through JEA (rule 3); "not found" never means "not used" (SPEC rule 15) and no result closes,
frees or verifies anything; secrets are never read, stored or shown; the server decides authorization.

## Decision

### 1. Collection stays outside the product and opens no unconstrained remote session

- `scripts/powershell/Get-ServiceAccountUsage.ps1` is a **self-contained, read-only collector** for one server: it holds
  the same functions as the proposed module `SecureOps.ServiceAccountUsage.psm1` (copied verbatim between markers; a unit
  test fails on any drift) and prints one `service-account-usage-v1` document as one JSON line. It opens no network
  connection and writes no file, registry key or service setting on the server. Passwords are never read: services and
  tasks do not expose them, and from `applicationHost.config` only names, `identityType`, `userName` and `physicalPath`
  are selected.
- How the person runs it on a server (console, or a remote-execution method their organisation already approves for
  their own account) is the person's own practice under their own authority, outside the product — as ADR-0024 already
  recorded for "scripts run by a person". The repository adds **no** default-endpoint `Invoke-Command`/`New-PSSession`
  code; the only remoting code in the repository stays the dormant JEA mode of ADR-0024.
- `Invoke-ServiceAccountUsageScan.ps1 -CombinePath` runs on the person's workstation and builds the upload file
  `service-account-usage-scan-v1` from the collected documents (separate `.json` files or one JSON-Lines file) and the
  **planned server list**. A planned server without a document is recorded as `NoResult`, or `Unreachable` when the person
  lists it so; it is never dropped and never becomes "not used".

### 2. Upload contract and server-side validation (fail closed)

`contracts/schemas/service-account-usage-scan.schema.json`: `schema`, `generatedAt`, `tool` (`Combined`/`Jea`),
`accounts` (1–20), optional `expectedAccount` (a gMSA, ends with `$`), `plannedServers` (1–500, unique), `results`
(`service-account-usage-v1` documents, one per server, each from a planned server), `notReached`
(`Unreachable`/`NoResult`). It carries no person names. The API parser (`UsageScanParser`) enforces, in this order:

1. Size ≤ `ServiceAccounts:MaxUsageScanBytes` (default 4 MiB, request limit 5 MB); UTF-8 (BOM allowed), valid JSON,
   depth ≤ 12, no duplicate property names.
2. **Secret guard before anything else is read or stored:** any property name that contains `password`, `passwd`, `pwd`,
   `secret`, `credential`, `token`, `apikey`, `privatekey`, `connectionstring`, `parola` or `şifre/sifre` (any case,
   anywhere in the file), or any string value that assigns one of these words (`password=`, `pwd=`, `parola:`,
   `"Password":`, `token:`, `client_secret=`, `api-key=` …; checked after compatibility folding, so full-width letters and
   invisible characters such as a zero-width space or a soft hyphen do not split the word), rejects the whole file with
   `secretField`/`secretValue`. Any text longer than 4 096 characters is refused before it is searched (`scanSchema`), so
   no file can make the guard slow. A rejected file is not stored, hashed into a record or echoed back; nothing is audited
   from its content.
3. Closed schema: unknown properties, wrong types, patterns, lengths and enums are rejected; counts are bounded
   (≤ 2 000 components per server, ≤ 10 000 in total, ≤ 20 warnings per server).
4. Consistency: every server document has the bundle's account list (the same set, no repeated name) and expected
   account, comes from a planned server and appears once; every planned server is either in `results` or `notReached`, never both; `scanResult` agrees with the
   per-source statuses; each matched component names a searched account; the gMSA block agrees with the components; no
   timestamp is in the future (10 minutes of clock skew) or after the combination time.

### 3. Data model (append-only, migration 030)

`svcacct.UsageScans` (original bytes ≤ 4 MiB, SHA-256, purpose `Discovery`/`GmsaCheck`, expected gMSA, searched accounts,
combination time, scan time range, counts, the uploader's **run statement** — where it ran and under which authority),
`UsageScanServers` (one row per planned server: result `Success`/`Partial`/`Failed`/`Unreachable`/`NoResult`, per-source
status, warning types), `UsageScanItems` (one row per matched component: server, role `Former` = a searched account or
`Expected` = the gMSA, type, name, configured identity, state, detail such as the physical path), `UsageScanLinks` (scan →
account, with the searched name and an optional participant request) and `UsageScanDecisions` (per item and account:
`UsageRecorded` with the created usage, or `Dismissed` with a reason). All five tables refuse UPDATE and DELETE by
trigger; module History and `audit.AuditLog` are written in the same transaction as every link and decision. Runtime
grants: SELECT and INSERT only (`SA-004-API-permissions.sql`). No existing table or constraint changes; a usage created
from a scan item is an ordinary `Manual` usage (a person recorded it) whose provenance is the decision row.

The same bytes uploaded again by the same person are the same scan (`UNIQUE (UploadedBy, Sha256)`); attaching it to a
second account adds only a link. Another person uploading the same bytes creates their own record.

### 4. Authority and scope (server-side)

- **Attach a scan to an account** (`POST accounts/{id}/usage-scans`, multipart): `ServiceAccounts.Work` and either the
  *responsible* basis on that account, or the *participant* basis with one of the caller's own open requests named
  (`requestId`) — the scan is then evidence on that request, which SPEC already allows a participant. The file must have
  searched this account (same name; when both sides carry a domain, the same domain). When several searched names refer to
  the account, the one qualified with the account's domain is used; with no domain on the account the bare name is used
  (it matches every domain, so no match is hidden), and two names with different domains and no bare name refuse the
  upload (`accountAmbiguousInScan`). Out of scope and missing are indistinguishable (404).
- **Attach one scan to several accounts** (`POST usage-scans`, multipart: `file`, `runStatement`, 1–20 distinct
  `accountIds`; added 2026-10-06, no migration): `ServiceAccounts.Work`; the file is validated once and completely (secret
  guard first) before any account is looked at, so a refused file stores nothing for anyone. Each account is then checked
  on its own under the rules above with the *responsible* basis only (a participant uses its own request on the account
  page) and answered separately: `Attached`, `AlreadyAttached`, `NotInScan`, `Ambiguous`, `Unavailable` (missing, out of
  scope or not the caller's to work on — indistinguishable, and returned without the account's name when out of scope) or
  `Failed` (storage error; repeating the upload is safe). One refusal or failure never blocks another (each account has its
  own failure boundary, so earlier links stand and later accounts are still tried); the scan is stored once and linked per
  account in its own transaction, with history and audit per link. Every refusal after the capability check — a refused
  request field or file, or an upload where some accounts were not linked — writes one `ServiceAccount.UsageScanBatchRefused`
  audit row with a stable reason (`accountIds`, `runStatement`, the file code, or `accounts`) and counts only (requested and
  per outcome): no account name or id, no file name, hash or content (2026-10-07).
- **Authority is re-checked inside the write** (both upload routes, 2026-10-07): the link's transaction locks the account
  row (and the request row when attaching through a request) and re-reads the caller's active scope grants and the
  organization/team tree under HOLDLOCK. A revocation or owner-team/organization change that commits after the service's
  check but before the link is seen there and refuses the link with nothing written (out of scope = not found; a
  participant without its open request = forbidden; in a multi-account upload `Unavailable` without the name). The
  capability itself comes from the platform access service and is not re-read inside the SQL transaction.
- **Turn a matched component into a usage** or **dismiss it with a reason**: `ServiceAccounts.Work` with the responsible
  basis, one decision per item and account, never automatic. The person chooses the usage kind (a suggestion is shown).
- **Read**: everyone who can see the account sees its attached scans — only the items matched to that account's searched
  name plus per-server coverage, never another account's items. The stored file is not downloadable through the API.

### 5. Honest outcome per account and server

`Found` (components listed); `NotFound` only after a `Success` scan ("aranan kaynaklarda bulunmadı; kullanılmıyor demek
değildir"); `Uncertain` after a `Partial` scan without a match; `NotCovered` for `Failed`/`Unreachable`/`NoResult` ("bilgi
yok"). The view always states what the scan cannot see: COM+ identities, user-right assignments, credentials inside
application or connection configuration, database logins, file-share permissions, Linux/Oracle hosts.

### 6. gMSA conversion evidence

A scan with `expectedAccount` is a gMSA check. Per account the module derives, never stores: `StillFormer` (the former
account is still configured somewhere), `Incomplete` (no former account found, but a planned server is not covered or
only partially scanned), `ConvertedOnCoveredServers` (every planned server fully scanned, the former account nowhere, the
gMSA on at least one component) or `NoComponents`. It is shown on the account (scan tab and "Devir ve gMSA") as
**evidence**. It never changes the transition, a request, an action or the account; verifying the conversion remains the
verifier's existing action verification.

### 7. Not done here

No scheduled or product-initiated scan, no default-endpoint remoting, no automatic link by name, no automatic usage
creation, no change to reports or metric definitions (scan items do not count as findings or work), no write/conversion
code, no change to the JEA allow-list.

## Alternatives considered

- **Product fans out over default WinRM with the uploader's credentials:** fastest for many servers, but it is platform
  code opening unconstrained remote sessions (AGENTS.md rule 3) and would need an owner decision and its own ADR.
- **Reuse `svcacct.Findings` for every server and component:** existing tab and metric, but one scan of 50 servers would
  create dozens of open findings, re-scans would duplicate them, and coverage (which servers were not reached) has no
  place there. Rejected for a dedicated, immutable scan record linked to the account.
- **Accept the team's own tool output:** its format is not stored in the repository and includes write paths; no invented
  parser. The team runs the repository collector instead.
- **Link by name automatically after upload:** rejected; a person attaches the scan to the account they work on.

## Consequences

- Positive: a server-level checklist before conversion and real, reviewable evidence afterwards, attached to the account,
  with coverage gaps visible; no JEA deployment or Worker needed.
- Negative: the person must run the collector on each server by their own means; evidence quality depends on that run
  (the run statement and per-server timestamps are kept). Pending decisions are visible on the account only (not yet in
  the work summary or reports).

## Required before use on an installed system

1. Project owner merges; 030 applied to a copy of the installed database first, then by the approved DBA process
   (`SA-004-API-permissions.sql` with it).
2. Windows checks in `docs/service-accounts/WINDOWS-ACCEPTANCE.md` (collector under Windows PowerShell 5.1 on a test
   server, upload and attach in the browser, 390 px/dark).
