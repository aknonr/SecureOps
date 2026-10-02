# 030 — Worker Service

Owner: Codex. Current hosting and job notes: `src/SecureOps.Worker/README.md`.

- `SecureOps.Worker` is a separate Windows Service hosting the Hangfire server on SQL Server storage. It serves no HTTP and never calls the API or UI; coordination is only the shared database and Hangfire queues.
- Jobs take small serializable arguments, are idempotent, honour cancellation, and use explicit timeouts and bounded retries. A failure is logged, audited and rethrown so Hangfire records it — never swallowed.
- Each diagnostic run audits start, completion and failure.
- PowerShell runs in-process through `System.Management.Automation` over WinRM HTTPS with Kerberos to the JEA endpoint — never `powershell.exe` via `Process.Start`, never an unconstrained runspace. Results are structured objects matching `contracts/schemas/`.
- File I/O only under explicitly configured paths. No UI push from the Worker.
- How the Worker obtains privileged access is an open decision (`AGENTS.md`); do not assume BeyondTrust brokering or direct credentials.
