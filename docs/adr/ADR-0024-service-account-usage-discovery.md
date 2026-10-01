# ADR-0024: Read-only service account usage discovery (PROPOSED)

**Status:** Proposed — draft for review. Not accepted, not implemented, nothing deployed.
**Date:** 2026-10-01
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
