# scripts/

PowerShell artifacts. Production diagnostic and JEA scripts are not implemented yet; current API scripts provide offline release validation and an explicitly invoked read-only TEST smoke check.

## Structure

```
scripts/
└── powershell/
    ├── diagnostic/   # Per-module diagnostic scripts (Phase 1)
    │   ├── Get-DiskDiagnostic.ps1
    │   ├── Get-CpuDiagnostic.ps1
    │   ├── Get-MemoryDiagnostic.ps1
    │   ├── Get-IisDiagnostic.ps1
    │   ├── Get-ServiceDiagnostic.ps1
    │   └── Get-EventLogDiagnostic.ps1
    └── jea/          # JEA constrained endpoint configuration (Phase 1)
        ├── SecureOpsDiagnosticEndpoint.pssc
        ├── SecureOpsDiagnosticRole.psrc
        └── Install-SecureOpsJeaEndpoint.ps1
```

## API Release Scripts

- `release/New-ApiDeploymentPackage.ps1` creates and validates a path-preserving deployment ZIP and payload manifest.
- `release/Test-ApiReleasePayload.ps1` rejects forbidden files and scans for credential-like and caller-supplied personal markers.
- `release/Validate-ApiAdRuntimeDependencies.ps1` validates hashes, runtime manifests, project consistency, and the AD dependency closure.
- `powershell/Test-ApiTestSwaggerReadiness.ps1` performs offline TEST Swagger artifact validation.
- `powershell/Test-ApiTestDeploymentReadOnly.ps1` performs only authenticated GET smoke checks after separately approved deployment.

## Rules

See `.cursor/rules/040-automation-ansible-powershell-rules.mdc` for the full ruleset. Highlights:

- Read-only cmdlets only (MVP).
- Full cmdlet names; no aliases.
- `[CmdletBinding()]` on every advanced function.
- `$ErrorActionPreference = 'Stop'` at script top.
- Output structured objects (`PSCustomObject` / `ConvertTo-Json`).
- No `Write-Host` in production scripts.

## JEA Whitelist

The canonical list of allowed cmdlets is in `docs/05-security-model.md`. The role capability file (`SecureOpsDiagnosticRole.psrc`) is generated from that list and reviewed in the same change set.

## Phase 8

A separate `scripts/powershell/jea/SecureOpsRemediationRole.psrc` and `Install-SecureOpsRemediationEndpoint.ps1` are added in Phase 8 for the bounded remediation catalog.
