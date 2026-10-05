# ADR-0024: Read-only service account usage discovery (PROPOSED)

**Status:** Proposed — draft for review. Not accepted, not implemented, nothing deployed. **Shelved by the project owner on
2026-10-04** (Bilgi Güvenliği approval and the server pilot are open). The person-run scan and its import are decided
separately in ADR-0027, which reuses this ADR's read-only functions in a self-contained collector and needs no JEA
endpoint or Worker job. The JEA/Worker parts below remain proposed only. **Revision 2 (2026-10-06)** at the end of this
file is the version prepared for the approval decision; it stays shelved until the owner takes it to Bilgi Güvenliği.
**Date:** 2026-10-01 (revision 2: 2026-10-06)
**Decision makers (required):** project owner; Bilgi Güvenliği and Siber Güvenlik approval (JEA change); target server owners for the pilot list.

## Context

The Service Accounts module now evaluates the knowledge-base rules for service account requests from recorded
usages (database, file share, scheduled task, Windows service, UNC application, IIS virtual directory, IIS
application pool). Today those usages are entered by people. Two operational needs remain:

1. Before a database team account is converted to gMSA (owner decision 2026-10-01: SQL-team accounts are evaluated as
   gMSA by the executing team), the executing team must know on which servers the account runs an IIS application
   pool, a Windows service or a scheduled task, or is used by an IIS virtual directory.
2. After a person performs the conversion (outside this system, through the normal change record), the module should be
   able to show evidence that the component now runs as the gMSA, so that a verifier can verify the conversion.

Constraints that do not change: the MVP is read-only (AGENTS.md rule 1), all WinRM goes through JEA (rule 5), write
operations belong to Phase 8 with approval (rule 7), and "not found" never means "unused" (SPEC rule 15).

## Proposed decision

- Add an **on-demand, read-only** discovery job executed by the Worker through the existing WinRM + JEA model. It is
  started by an authorized user for one account and an explicit, bounded server list; there is no scheduled sweep.
- The JEA endpoint exposes **one new constrained function** (for example `Get-SecureOpsAccountUsage -Account <name>`)
  instead of raw cmdlets. Internally it may use already whitelisted cmdlets:
  `Get-CimInstance Win32_Service` (`StartName`), `Get-ScheduledTask` (`Principal.UserId`), `Get-WebConfigurationProperty`
  for `system.applicationHost/applicationPools/add/processModel` **userName only** and for virtual directories
  `userName`/`physicalPath` only.
- **Secrets are never read or returned.** IIS stores application pool and virtual directory passwords next to the user
  name and `Get-WebConfigurationProperty` can return them to a privileged caller. The function must select only the
  named non-secret attributes; the role capability file must not expose the raw cmdlet for these sections. This is the
  main security review point of this ADR.
- Output per server: component type, component name, matched identity, and scan result
  (`Success`/`Partial`/`Unreachable`/`Failed`). It is stored as an existing module **Finding** (server, component type,
  component name, scan result, match result, coverage window, job reference). A finding may be turned into a usage
  record only by a person.
- `NoMatch` or `Unreachable` is recorded as such and never closes, deletes or "frees" an account.
- **Post-conversion check:** the same job, run after the change, records the component's current identity
  (for example `DOMAIN\gmsa-name$`) as evidence. The verifier still performs the verification; the job never marks a
  conversion verified.
- The conversion itself (creating the gMSA, `PrincipalsAllowedToRetrieveManagedPassword`, changing the application pool
  identity, restarting) stays manual. Automating it is a Phase 8 decision with its own ADR and approval workflow.

## Design detail (2026-10-04, still PROPOSED)

Artifacts on the Service Accounts branch, none deployed or registered anywhere:

- `scripts/jea/proposed/SecureOps.ServiceAccountUsage/` — module (`.psm1`/`.psd1`), role capability
  `RoleCapabilities/SecureOpsServiceAccountUsage.psrc` and session configuration `SecureOpsServiceAccountUsage.pssc`.
- `scripts/powershell/Invoke-ServiceAccountUsageScan.ps1` — operator tooling: parallel scan through the endpoint.
- `contracts/schemas/service-account-usage.schema.json` (+ example) — output contract `service-account-usage-v1`.
- Tests: `tests/SecureOps.Tests.Unit/ServiceAccounts/ServiceAccountUsageModuleTests.cs` load the module into a real
  PowerShell runspace (repository PowerShell SDK 7.4), replace the collectors with synthetic data and check matching, IIS
  parsing with planted passwords (never returned), partial results, the gMSA check, parameter validation, the output
  contract and the absence of write commands. Not yet run under Windows PowerShell 5.1 on a server.

**One function.** `Get-SecureOpsAccountUsage -Account <1..20 names> [-ExpectedAccount <gmsa$>]`. The role capability
makes only this function visible (`VisibleCmdlets`, `VisibleProviders`, `VisibleExternalCommands` empty). Microsoft's
JEA documentation states that the body of a custom function "runs in the default language mode for the system and isn't
subject to JEA's language constraints… [it can] run commands that weren't made visible in the role capability file", and
recommends fully qualified module names inside it (PowerShell-Docs, *JEA role capabilities*). The function therefore
calls `CimCmdlets\Get-CimInstance`, `ScheduledTasks\Get-ScheduledTask` and `Microsoft.PowerShell.Management\Get-Content`
itself, and the caller can never reach them with other arguments.

**Sources and what is read.**

| Source | Read | Never read |
|---|---|---|
| Windows services | `Win32_Service` Name, StartName, State (one CIM query) | service passwords (not exposed by Windows) |
| Scheduled tasks | TaskPath, TaskName, Principal.UserId, LogonType, State | stored task credentials |
| IIS (`applicationHost.config`, parsed once as XML) | app pool name, `processModel/@identityType`, `@userName`; site/application/virtual directory path, `@userName`, `@physicalPath` | any `password` attribute (not selected, not decrypted) |

Not covered (stated in every result's absence, never inferred): COM+ application identities, user-right assignments
(`SeServiceLogonRight`, `SeBatchLogonRight`), application config files with embedded credentials, and Linux/Oracle hosts.

**Matching.** Case-insensitive account name; when both sides carry a NetBIOS domain they must match; a UPN suffix does not
block a match; `.\name` and built-in identities never match a domain account; a trailing `$` (gMSA/computer) is
significant. No SID translation per item, so no domain-controller round trip per service or task.

**Result.** One document per server (`service-account-usage-v1`): `scanResult` Success/Partial/Failed, per-source status
(Success/Failed/NotInstalled), matched components and, with `-ExpectedAccount`, `verification.Status`:
`Converted` (gMSA found, no former account left), `NotConverted` (a former account is still configured) or
`NoComponents`. Warnings carry the exception type only, never its message (messages can contain paths or names). A
server that does not answer has no document; the caller records `Unreachable`, never "not used".

**Calling it.** Worker (future job, not implemented): a PowerShell runspace over `WSManConnectionInfo` with the
configuration name, `AddCommand("Get-SecureOpsAccountUsage").AddParameter(...)` — no script text, which also fits the
endpoint's `NoLanguage` mode — with bounded parallelism and per-server timeouts; results become module Findings.
Operator tooling: one `Invoke-Command -ComputerName <list> -ConfigurationName ... -ThrottleLimit 32` with 15 s open and
180 s operation timeouts; the command line is built only from values that passed a strict pattern (no quotes, spaces or
operators).

**Finding on the existing canonical allow-list (needs a decision, not changed here).** `docs/05-security-model.md` makes
raw `Get-WebConfigurationProperty` and `Get-Content` visible. With an administrative run-as account the first can return
IIS application-pool and virtual-directory `password` attributes and the second can read any file. When this endpoint is
approved, the diagnostic role should expose them only with constrained parameters (or not at all) — a separate ADR and
Bilgi Güvenliği review per AGENTS.md rule 3.

### Analysis of the team's former tool (sanitized; the original is not stored in the repository)

The owner shared the team's WPF PowerShell tool (passwords removed). What it does: pick a service group from the ITSM
CMDB, list its servers, open remote sessions, search a given account on every server (services, scheduled tasks, IIS app
pools/sites/applications/virtual directories, COM+, user rights) and then **change the password** on AD and re-stamp
every found component (service stop/change/start, app pool identity + restart, IIS credentials, `schtasks /change`, COM+).

Why it is slow:
1. All work runs on the GUI thread inside button handlers; the window freezes and nothing runs concurrently with the UI.
2. Sessions are opened one server at a time (`New-PSSession` in a loop) with default timeouts — an unreachable server
   blocks for minutes before the next one starts; sessions are never closed.
3. Every identity of every service, task and app pool is translated to a SID (`NTAccount.Translate`), i.e. one LSA/DC
   round trip per item, uncached.
4. IIS is walked three times; `Get-WebConfiguration` is called once per application and per virtual directory, each
   re-reading `applicationHost.config`. Duplicate checks scan the result list each time.
5. COM+ catalog enumeration and a `secedit` export (temporary file written and deleted) run on every server.
6. The ITSM API is logged into on every lookup.

Risks found (reasons the redesign stays read-only and outside that tool):
- Credentials for the ITSM integration were embedded in the script text (now redacted); they belong in a secret store.
- The scheduled-task branch writes the new password to the console in clear text (transcripts/logs would keep it).
- `Get-WebConfiguration` returns whole virtual-directory elements, including the decrypted password, to the caller.
- The write path (password change, service stop/start, app pool restart) is exactly what AGENTS.md rule 1 forbids before
  Phase 8; it has no approval, no audit trail and no rollback.

What the redesign keeps: the idea (find every place an account runs, then prove the change landed) and the components
list. What it drops: writes, the GUI, per-item SID translation, repeated IIS reads, COM+/secedit, temporary files.

## Alternatives considered

- **Scripts run by a person from a jump/tool server:** rejected as the system behaviour — personal privileged session,
  no module audit, results not attached to the account. It remains an individual's manual practice outside the system.
- **Domain controller logon events (4624) / SIEM query:** strongest signal for "where does this account log on", but it
  needs Bilgi Güvenliği approval of the data source and query scope. Recorded as an open option, not part of this ADR.
- **Agent on every server / Ansible:** rejected for the MVP (ADR-0001, ADR-0003); Ansible stays deferred.
- **Full scheduled sweep of all servers:** rejected; scope must be explicit and bounded.

## Consequences

- Positive: the executing team gets a server-level checklist before conversion; verification gets real evidence; the
  knowledge-base rule input becomes more complete without guessing.
- Negative: a JEA role change must be reviewed and redeployed to each pilot server; coverage is limited to servers with
  the endpoint, so missing coverage must stay visible.

## Required before implementation

1. Bilgi Güvenliği approval of the new JEA function and of the "no secret attributes" design, recorded here.
2. Pilot server list (10–15 servers) with owner sign-off.
3. Codex: Worker job contract, migration number (if the finding/usage link needs a column), OpenAPI.
4. Synthetic test fixtures only; no real server or account names in the repository.

## Revision 2 (2026-10-06, PROPOSED — prepared for the approval decision)

Why now: the team's own tool (analysis in `docs/service-accounts/ops-research/01-script-farki.md`) is fast at reaching
servers but runs unconstrained sessions, drops servers silently and trusts a stale cache. The project owner wants the
platform to do this work better. Reading is the part that gives most of the value and needs no write, so it is proposed
first and separately from any change (ADR-0028). Everything above stays valid unless changed here.

**R2-1 Identities.** The Worker connects as a **group managed service account** used only for this endpoint
(placeholder `CONTOSO\gmsa-so-sa-read$`), member of a dedicated group (placeholder `CONTOSO\SecureOps-SaRead`) that the
session configuration maps to the role. No person's credentials, no password anywhere. A change endpoint, if ever
approved, uses a different gMSA and group (ADR-0028), so the read identity can never write.

**R2-2 What the function reads (one visible function, unchanged name).**

| Source | Status | Read | Never read |
|---|---|---|---|
| Windows services, scheduled tasks, IIS (as above) | unchanged | as above | passwords |
| COM+ application identity | **added, opt-in parameter** | application name, `Identity` | `Password` (write-only in the COM+ catalog) |
| "Log on as a service" / "Log on as a batch job" holders | **added, opt-in, method decided in the pilot** | SIDs holding the two rights | anything else in the security policy |
| Endpoint version | added | module version, so the platform knows which sources a result can contain | — |

The user-rights read has no clean read-only API in Windows PowerShell 5.1 without either compiling code (`Add-Type`) or
`secedit /export` to a temporary file. The pilot decides between the two; until then the source is reported as
`NotInstalled` and the gap stays visible. It is needed by ADR-0028 as a pre-check (a gMSA must hold the right before a
service or task is switched to it), never to change rights.

**R2-3 gMSA readiness is checked in AD, not on the server.** Whether a server may retrieve a gMSA's password
(`msDS-GroupMSAMembership`, i.e. `PrincipalsAllowedToRetrieveManagedPassword`, including nested groups) is read by the
platform from AD under its existing read-only directory access, so servers need no RSAT module. A server whose computer
account was added to the allowed group recently may still need its Kerberos tickets refreshed (reboot or purge); the
result says "AD allows; the host has not proven it yet" until a post-change scan shows the component running.

**R2-4 Product flow (on demand, bounded, no sweep).**

- Started by a person with `ServiceAccounts.Work` and the responsible basis on every selected account, for 1–20 accounts and
  1–500 planned servers (pasted list, saved server set, or later the ITSM/vCenter adapters behind fakes first, rule 8).
- One Hangfire job per scan; one call per server carrying all accounts; bounded parallelism (default 32), open timeout
  15 s, operation timeout 180 s, cancellable by the starter or an administrator; at most one running scan per account.
- Each server's document is validated by the same parser as an uploaded scan (ADR-0027 §2) and stored in the same
  append-only tables with `tool = Jea`; the account page shows servers as they finish.
- Reasons for "no information" are kept apart: `Unreachable` (no WinRM answer), `NoEndpoint` (WinRM answers, the endpoint
  is not registered — **schema addition**), `NoResult` (connected, no valid document). None is "not used" (SPEC rule 15),
  none excludes the server from the next scan. There is no reachability cache that hides a server.
- The person-run path of ADR-0027 stays for servers without the endpoint.

**R2-5 Audit.** `ServiceAccount.UsageScanRequested` / `…Completed` / `…Cancelled` with counts and the plan hash; server
names live in the scan record, not in the audit text. JEA transcripts stay on each server.

**R2-6 Approvals needed (unchanged list, made explicit).** Bilgi Güvenliği and Siber Güvenlik: the function, the two
opt-in sources and the gMSA identity; PAM / BeyondTrust team and team lead: the Worker access path (open decision in
`docs/15-system-landscape.md`; this revision assumes direct WinRM + Kerberos + JEA and does not decide it); server owners:
the 10–15 server pilot. **Before deployment** the diagnostic allow-list finding above must be closed by its own ADR.

**R2-7 Estimate.** About 10–15 developer days after approval (Worker JEA runner with fake, job, storage reuse, UI progress,
tests) plus the pilot. Split in `docs/service-accounts/ops-research/05-yol-haritasi.md` (aşama B).
