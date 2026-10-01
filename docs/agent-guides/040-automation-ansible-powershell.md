# 040 — PowerShell, JEA and Ansible

## MVP position

PowerShell Remoting through a JEA constrained endpoint is the only automation path until Phase 6. Existing Ansible/AWX is not modified or extended; Ansible may be reconsidered in Phase 6 for idempotent, approval-gated remediation.

## Target-server scripts

- Read-only only: `Get-*`, `Test-*`, `Measure-*`, `Select-*`, `Sort-*`, `Where-*`, `Get-CimInstance`, `Get-WinEvent`. Never service/process/app-pool control, `Set-*` that changes state, `Remove-*`, `Restart-Computer`, group/AD changes or arbitrary `Invoke-Command` script blocks.
- The canonical cmdlet allow-list is the JEA section of `docs/05-security-model.md`; extending it needs an ADR. Do not keep a second copy elsewhere.
- Advanced functions with `[CmdletBinding()]`, validated parameters, full cmdlet names, `$ErrorActionPreference = 'Stop'`; return `PSCustomObject` data whose schema matches `contracts/schemas/`, never formatted text or `Write-Host`.

## Repository scripts

`scripts/powershell/`, `scripts/diagnostics/` and `scripts/release/` are operator and release tooling that run on admin or build machines, not on managed servers. They are read-only against corporate systems unless their README states an explicitly authorized exception. See `scripts/README.md` and `scripts/release/README.md`.
