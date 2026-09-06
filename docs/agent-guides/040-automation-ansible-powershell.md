# 040 — Automation Rules: PowerShell & Ansible

## Applicability

- **Purpose:** PowerShell, JEA, and Ansible rules. MVP is PowerShell-only; Ansible is Phase 6+.
- **Applies to:** `scripts/**/*.ps1`, `scripts/**/*.psd1`, `scripts/**/*.psm1`, `scripts/**/*.yml`, and `scripts/**/*.yaml`.
- **Loading:** Routed explicitly from `AGENTS.md` or `docs/agent-guides/README.md`; do not assume automatic discovery.

## MVP Position

- **MVP uses PowerShell Remoting through a JEA constrained endpoint only.**
- The existing Ansible/AWX infrastructure at CONTOSO is **not modified, not extended, and not added to** during MVP.
- Ansible is reconsidered in Phase 6 as an option for idempotent remediation. Until then, all `.yml`/`.yaml` files in `scripts/ansible/` are placeholders or examples for future reference only.

## PowerShell Style

### Functions

```powershell
function Get-ServerDiskReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$ServerName,

        [Parameter()]
        [int]$TopFolderCount = 10
    )

    begin {
        Write-Verbose "Starting disk report for $ServerName"
    }

    process {
        # logic here
    }

    end {
        Write-Verbose "Completed disk report"
    }
}
```

### Rules

- Use full cmdlet names. No aliases (`Get-ChildItem`, not `gci`).
- Use approved verbs (`Get`, `Set`, `New`, `Test`, etc.).
- `[CmdletBinding()]` on every advanced function.
- Mandatory parameters explicit, with validation attributes.
- Use `Write-Verbose` for diagnostics, `Write-Information` for results, `Write-Error` for failures.
- Never `Write-Host` in production scripts.
- Output structured PowerShell objects (PSCustomObject), not formatted text.
- Set `$ErrorActionPreference = 'Stop'` at script top.

### Read-Only Cmdlets ONLY in MVP

Allowed in MVP:
- `Get-*` family (any read-only Get)
- `Test-*` family (read-only tests)
- `Measure-*`, `Select-*`, `Sort-*`, `Where-*`
- `Get-CimInstance` (read-only WMI)
- `Get-WinEvent` (read-only event log)

Forbidden in MVP (will be blocked by JEA):
- `Stop-Service`, `Start-Service`, `Restart-Service`
- `Remove-*` (anything that deletes)
- `Set-*` that modifies state (configuration changes)
- `Stop-Process`, `Kill-Process`
- `Restart-Computer`
- `Stop-WebAppPool`, `Restart-WebAppPool`, `New-WebAppPool`
- `Remove-Item`, `Clear-Content`
- `Invoke-Command` with arbitrary script blocks
- `Add-LocalGroupMember`, anything that modifies AD or local groups

## JEA Configuration

JEA session configuration lives in `scripts/jea/`:

- `SecureOpsDiagnosticEndpoint.pssc` — session configuration file
- `SecureOpsDiagnosticRole.psrc` — role capability file with the visible cmdlet whitelist

Whitelist (canonical list — also in `docs/05-security-model.md`):

```
VisibleCmdlets = @(
    'Get-Disk',
    'Get-PSDrive',
    'Get-CimInstance',
    'Get-Process',
    'Get-Service',
    'Get-WinEvent',
    'Get-EventLog',
    'Get-ChildItem',
    'Get-Item',
    'Get-Content',
    'Test-Path',
    'Get-WmiObject',
    'Get-Counter',
    'Get-NetTCPConnection',
    'Get-NetAdapter',
    'Measure-Object',
    'Select-Object',
    'Sort-Object',
    'Where-Object',
    'ForEach-Object',
    'Format-List',
    'Format-Table',
    'Out-String',
    'ConvertTo-Json'
)

# WebAdministration / IIS read-only
VisibleCmdlets += @(
    'Get-Website',
    'Get-WebApplication',
    'Get-WebAppPoolState',
    'Get-WebBinding',
    'Get-WebConfigurationProperty'
)
```

## Script Layout

```
scripts/
├── diagnostic/
│   ├── Get-DiskDiagnostic.ps1
│   ├── Get-CpuDiagnostic.ps1
│   ├── Get-MemoryDiagnostic.ps1
│   ├── Get-IisDiagnostic.ps1
│   ├── Get-ServiceDiagnostic.ps1
│   └── Get-EventLogDiagnostic.ps1
└── jea/
    ├── SecureOpsDiagnosticEndpoint.pssc
    ├── SecureOpsDiagnosticRole.psrc
    └── Install-SecureOpsJeaEndpoint.ps1
```

## Output Format

Every diagnostic script returns a structured hashtable that the .NET caller serializes to JSON:

```powershell
$report = [PSCustomObject]@{
    Schema       = 'disk-diagnostic-v1'
    ServerName   = $ServerName
    GeneratedAt  = (Get-Date).ToUniversalTime().ToString('o')
    Drives       = $drives
    TopFolders   = $topFolders
    Warnings     = $warnings
}

$report | ConvertTo-Json -Depth 6
```

The schema string must match the JSON schema in `contracts/schemas/diagnostic-result.schema.json`.

## Ansible (Phase 6+ Only, Reference)

When Ansible work begins in Phase 6:

- Inventory in `scripts/ansible/inventory/` (groups by environment, criticality).
- Playbooks in `scripts/ansible/playbooks/`.
- Roles in `scripts/ansible/roles/`.
- All playbooks idempotent: re-running produces the same result.
- WinRM connection: `ansible_connection=winrm`, `ansible_winrm_transport=kerberos`.
- Tag all tasks: `tags: [read-only]` or `tags: [remediation, requires-approval]`.

But again: **not in MVP**.

## Reference

Read `docs/05-security-model.md` (JEA section) and `docs/07-diagnostic-modules.md`.
