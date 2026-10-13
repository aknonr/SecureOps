# scripts/

PowerShell artifacts. Production diagnostic and JEA scripts are not implemented yet; current API scripts provide offline release validation and an explicitly invoked read-only TEST smoke check.

## Structure

Present today: `powershell/` (local harnesses and TEST smoke/readiness checks), `diagnostics/` (read-only SQL and
evidence helpers) and `release/` (packaging and validation; see `release/README.md`).

Service account usage scan (ADR-0027), run by a person under their own authority, never by the product:
`powershell/Get-ServiceAccountUsage.ps1` (self-contained read-only collector for one server; prints one
`service-account-usage-v1` JSON line, writes nothing, opens no connection) and
`powershell/Invoke-ServiceAccountUsageScan.ps1 -CombinePath` (on the person's workstation: builds the upload file
`service-account-usage-scan-v1` from the collected documents and the planned server list). Operator guide:
`docs/service-accounts/USAGE-SCAN-TR.md`.

Proposed, **not deployable**: ADR-0024 revision 2 is accepted (owner, 2026-10-06) but Bilgi Güvenliği has not approved the
role capability and the endpoint registration: `jea/proposed/SecureOps.ServiceAccountUsage/` (one visible function, module, role capability and
session configuration; the collector above carries its functions verbatim) and the dormant `-UseJeaEndpoint` mode of
`Invoke-ServiceAccountUsageScan.ps1`.

Planned for Phase 1, **not yet present**:

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

- `powershell/Test-ResourceCatalogueSql.ps1` is an explicitly authorized local-only
  disposable database harness, not a production diagnostic. It requires the
  separately provisioned SecureOpsResourcesV1 LocalDB instance, creates a fresh
  prefixed database, refuses overwrite, runs migrations 001-013 with synthetic
  upgrade rows, and optionally runs guarded SQL integration tests. It retains the
  test database for inspection and never changes a shared SQL service.
  Announcement opt-in `-IncludeAnnouncementDrafts` adds 014-015 to that fresh DB;
  `-IncludeAnnouncementSources` includes drafts plus 016-017. Hangfire schema provisioning is separate.
  `-IncludeAnnouncementPreparations` includes sources plus 018; existing databases are refused.
  executable announcement contract commands are in `docs/contracts/planned-announcements-v1.md`.

- `release/New-ApiDeploymentPackage.ps1` creates and validates a path-preserving deployment ZIP and payload manifest.
- `release/Test-ApiReleasePayload.ps1` rejects forbidden files and scans for credential-like and caller-supplied personal markers.
- `release/Validate-ApiAdRuntimeDependencies.ps1` validates hashes, runtime manifests, project consistency, and the AD dependency closure.
- `powershell/Test-ApiTestSwaggerReadiness.ps1` performs offline TEST Swagger artifact validation.
- `powershell/Test-ApiTestDeploymentReadOnly.ps1` performs only authenticated GET smoke checks after separately approved deployment.

## Rules

See `docs/agent-guides/040-automation-ansible-powershell.md` for the full ruleset. Highlights:

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
