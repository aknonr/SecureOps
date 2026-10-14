# scripts/worker/

Windows service registration for the SecureOps Worker, run by the Worker host's administrator (never by the product). Full operating
procedure, acceptance gates and upgrade/rollback: `docs/worker-service-operations-tr.md`.

| Script | What it does |
|---|---|
| `Install-SecureOpsWorkerService.ps1` | Creates `SecureOps.Worker` (Manual start, `"<exe>" --environment Test`), sets the log-on account to the gMSA with `sc.exe config obj=` (no password exists), checks `Test-ADServiceAccount` when the AD module is present, creates the Application event log source `SecureOps.Worker`. Refuses the placeholder `CONTOSO\gmsa-placeholder$`, a non-gMSA account, an existing service or a missing data directory. Does not start the service, set recovery/delayed start, grant rights or change ACLs. |
| `Uninstall-SecureOpsWorkerService.ps1` | Stops the service (120 s wait) and `sc.exe delete`s the registration; keeps files, data directory, logs, SQL and audit. `-RemoveEventLogSource` also removes the source. |

Both support `-WhatIf` / `-Confirm` and need an elevated Windows PowerShell 5.1 or later.

**Identity.** The Worker's service identity is the read gMSA (ADR-0024 R2-1): the JEA runner passes no credential, Kerberos uses the
process identity. The gMSA needs "Log on as a service" on the Worker host (granted by the Windows owner through GPO, not by the script),
read/execute on the payload and write on the data directory only.

**Event log.** The Worker writes three fixed-text lifecycle entries (1000 started, 1001 failed, 1002 stopped) when the source exists and
it runs as a service; it never creates the source and never writes exception text, configuration, server or account names there.
